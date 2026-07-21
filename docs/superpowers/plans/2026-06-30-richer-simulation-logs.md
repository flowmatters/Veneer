# Richer Simulation Logs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich the `RunLog` returned by `GET /runs/{id}` with each entry's log level and simulation timestep, and expose the last captured stack trace via a new `RunSummary.LastStackTrace` field — without changing the existing API shape.

**Architecture:** A new isolated static helper `RunLogFormatter` turns a TIME `LogEntry` into a single formatted string (`[Level] timestep <sim-time>: message`). `SourceService.TriggerRun` captures these formatted lines plus the last non-null `StackTrace` into a new `CapturedRunLog` container, stored per run. `GetRunResults` surfaces both. The same change is then ported verbatim to the `legacy_ci` (classic WCF) branch.

**Tech Stack:** C# / .NET 8 (net8.0-windows), CoreWCF, Newtonsoft.Json, TIME/RiverSystem (eWater Source) assemblies. Design spec: `docs/superpowers/specs/2026-06-30-richer-simulation-logs-design.md`.

---

## Testing note (read first)

This repository has **no C# unit-test project**. The established testing approach is pytest
integration via the separate **veneer-py** repo, launching real Source+Veneer instances
(see `veneer-testing.md`). `RunLogFormatter` depends on the external TIME `LogEntry` type, so a
standalone C# unit-test project is not practical here.

**Verification gate for each code task:** a successful single-version build:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```
`TreatWarningsAsErrors` is **true in Debug**, so warnings fail the build — this is a real gate.
Before the final PR, run the multi-version build `build.bat` (compiles against all installed
Source versions / conditional symbols).

The pure formatting logic is deliberately split into an internal `FormatLine(level, timeStep, message)`
method over primitives, so it is ready to unit-test if a C# harness is ever added, and so its
branching can be reasoned about directly from this plan.

A final **manual integration check** (Task 7) confirms end-to-end behaviour against a running instance.

---

## File Structure

- **Create** `FlowMatters.Source.Veneer/Formatting/RunLogFormatter.cs` — the `RunLogFormatter`
  static helper **and** the `CapturedRunLog` container. One responsibility: turning captured
  simulation log entries into the formatted form Veneer stores/returns. Self-contained so it
  copies verbatim to `legacy_ci`.
- **Modify** `FlowMatters.Source.Veneer/SourceService.cs` — retype `_runLogs`/`RunLogs`; enrich
  the `TriggerRun` logger; surface `LastStackTrace` in `GetRunResults`.
- **Modify** `FlowMatters.Source.Veneer/ExchangeObjects/RunSummary.cs` — add the `LastStackTrace`
  `[DataMember]`.
- **Modify** `FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs` — bump `PROTOCOL_VERSION`.
- **Modify** `docs/api/schemas.md` and `docs/api/runs-and-results.md` — document the change.
- **Port** all of the above to the `legacy_ci` branch (Task 8).

---

## Task 0: Feature branch

**Files:** none (git only).

- [ ] **Step 1: Create a feature branch off master**

Run:
```
git checkout -b feature/richer-simulation-logs
```
Expected: `Switched to a new branch 'feature/richer-simulation-logs'`.

---

## Task 1: `RunLogFormatter` + `CapturedRunLog`

**Files:**
- Create: `FlowMatters.Source.Veneer/Formatting/RunLogFormatter.cs`

- [ ] **Step 1: Create the formatter and container**

Create `FlowMatters.Source.Veneer/Formatting/RunLogFormatter.cs` with exactly:

```csharp
using System;
using System.Globalization;
using TIME.Management;

namespace FlowMatters.Source.Veneer.Formatting
{
    /// <summary>
    /// Captured simulation log for a single Veneer-triggered run: the formatted log lines plus
    /// the last non-null stack trace seen during the run (null when no entry carried one).
    /// </summary>
    public class CapturedRunLog
    {
        public string[] Messages;
        public string LastStackTrace;
    }

    /// <summary>
    /// Formats a TIME <see cref="LogEntry"/> into a single human-readable log line carrying the
    /// log level and, when present, the simulation timestep.
    /// </summary>
    public static class RunLogFormatter
    {
        /// <summary>
        /// Format a log entry as "[Level] timestep &lt;sim-time&gt;: message". The
        /// "timestep &lt;sim-time&gt;" segment is omitted entirely when the entry has no timestep.
        /// The word "timestep" labels the value as simulation (model) time so it is not mistaken
        /// for a wall-clock log timestamp.
        /// </summary>
        public static string Format(LogEntry entry)
        {
            return FormatLine(entry?.Type.ToString(), entry?.TimeStep, entry?.Message);
        }

        /// <summary>
        /// Pure formatting logic over primitives, isolated from the TIME types so the branching
        /// (level brackets, optional timestep, smart date/datetime granularity) is easy to reason
        /// about and to test.
        /// </summary>
        internal static string FormatLine(string level, DateTime? timeStep, string message)
        {
            var levelPart = string.IsNullOrEmpty(level) ? "" : "[" + level + "] ";
            var timeStepPart = timeStep.HasValue ? "timestep " + FormatTimeStep(timeStep.Value) + ": " : "";
            return levelPart + timeStepPart + (message ?? "");
        }

        // Smart granularity: date only at midnight, full datetime otherwise (preserves sub-daily steps).
        private static string FormatTimeStep(DateTime timeStep)
        {
            return timeStep.TimeOfDay == TimeSpan.Zero
                ? timeStep.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : timeStep.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
```

Reference expected outputs (these are the behaviours the integration check in Task 7 confirms):
- `FormatLine("Error", 1990-06-01 00:00:00, "Data file not found")` → `[Error] timestep 1990-06-01: Data file not found`
- `FormatLine("Warning", 1990-06-01 06:30:00, "Storage spilled")` → `[Warning] timestep 1990-06-01 06:30:00: Storage spilled`
- `FormatLine("Information", null, "Run started")` → `[Information] Run started`
- `FormatLine("Error", null, null)` → `[Error] `

- [ ] **Step 2: Build**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```
Expected: `Build succeeded`, 0 errors. (New file compiles; nothing references it yet.)

- [ ] **Step 3: Commit**

```
git add FlowMatters.Source.Veneer/Formatting/RunLogFormatter.cs
git commit -m "feat: add RunLogFormatter for level/timestep-aware run log lines"
```

---

## Task 2: Capture richer log in `TriggerRun`

**Files:**
- Modify: `FlowMatters.Source.Veneer/SourceService.cs` (field/property ~51-66; `TriggerRun` ~363-414)

- [ ] **Step 1: Retype the backing field and property**

Replace (SourceService.cs ~line 51):
```csharp
        private static Dictionary<int,string[]> _runLogs = new Dictionary<int,string[]>();
```
with:
```csharp
        private static Dictionary<int,CapturedRunLog> _runLogs = new Dictionary<int,CapturedRunLog>();
```

Replace (SourceService.cs ~lines 62-66):
```csharp
        public Dictionary<int,string[]> RunLogs
        {
            get { return _runLogs; }
            set { _runLogs = value; }
        }
```
with:
```csharp
        public Dictionary<int,CapturedRunLog> RunLogs
        {
            get { return _runLogs; }
            set { _runLogs = value; }
        }
```

(`using FlowMatters.Source.Veneer.Formatting;` is already present at the top of the file, so
`CapturedRunLog` resolves.)

- [ ] **Step 2: Enrich the run logger closure**

Replace (SourceService.cs ~lines 363-368):
```csharp
            ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
            LogAction runLogger = (sender, args) =>
            {
                messages.Enqueue(args.Entry.Message);
            };
            TIME.Management.Log.MessageRecieved += runLogger;
```
with:
```csharp
            ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
            object stackTraceLock = new object();
            string lastStackTrace = null;
            LogAction runLogger = (sender, args) =>
            {
                messages.Enqueue(RunLogFormatter.Format(args.Entry));
                var stackTrace = args.Entry.StackTrace;
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    // MessageRecieved can fire from multiple threads; guard so "last" is well-defined.
                    lock (stackTraceLock)
                    {
                        lastStackTrace = stackTrace;
                    }
                }
            };
            TIME.Management.Log.MessageRecieved += runLogger;
```

- [ ] **Step 3: Store the captured log on success**

Replace (SourceService.cs ~line 414):
```csharp
            RunLogs[newRun.RunNumber] = messages.ToArray();
```
with:
```csharp
            RunLogs[newRun.RunNumber] = new CapturedRunLog
            {
                Messages = messages.ToArray(),
                LastStackTrace = lastStackTrace
            };
```

Note: the two error paths (the `catch` block ~line 387 and the "completed without producing a
result" path ~line 410) keep `messages.ToArray()` in `SimulationFault.Log`. Those lines are now
the richer formatted strings automatically — **do not change them**.

- [ ] **Step 4: Build**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```
Expected: `Build succeeded`. (`GetRunResults` still references `RunLogs[...]` as a value — it is
fixed in Task 3; until then the compiler will error there, so build **after** Task 3 if you batch.
If building now, expect the only error to be in `GetRunResults` assigning `CapturedRunLog` to
`string[]` — proceed to Task 3 to resolve.)

- [ ] **Step 5: Commit (after Task 3 builds clean, or commit Tasks 2+3 together)**

```
git add FlowMatters.Source.Veneer/SourceService.cs
git commit -m "feat: capture log level, timestep and stack trace per run in TriggerRun"
```

---

## Task 3: Surface `LastStackTrace` (RunSummary + GetRunResults)

**Files:**
- Modify: `FlowMatters.Source.Veneer/ExchangeObjects/RunSummary.cs` (constructor ~27; fields ~135)
- Modify: `FlowMatters.Source.Veneer/SourceService.cs` (`GetRunResults` ~499-537)

- [ ] **Step 1: Add the `LastStackTrace` DataMember**

In `RunSummary.cs`, after the existing `RunLog` field (~line 135):
```csharp
        [DataMember] public string[] RunLog;
```
add:
```csharp
        [DataMember] public string LastStackTrace;
```

In the `RunSummary(Run r)` constructor, after `RunLog = new string[0];` (~line 27) add:
```csharp
            LastStackTrace = null;
```

- [ ] **Step 2: Read the captured log in `GetRunResults`**

In `SourceService.GetRunResults` (~line 502), after `string[] log;` add a sibling local:
```csharp
            string lastStackTrace = null;
```

Replace the lookup block (~lines 514-525):
```csharp
            if (RunLogs.ContainsKey(run.RunNumber))
            {
                log = RunLogs[run.RunNumber];
            }
            else
            {
                log = new []
                {
                    "Run log not available",
                    "Run log only available through Veneer for runs triggered in Veneer"
                };
            }
```
with:
```csharp
            if (RunLogs.ContainsKey(run.RunNumber))
            {
                var captured = RunLogs[run.RunNumber];
                log = captured.Messages;
                lastStackTrace = captured.LastStackTrace;
            }
            else
            {
                log = new []
                {
                    "Run log not available",
                    "Run log only available through Veneer for runs triggered in Veneer"
                };
            }
```

Replace the result assembly (~lines 534-536):
```csharp
            var result = new RunSummary(run);
            result.RunLog = log;
            return result;
```
with:
```csharp
            var result = new RunSummary(run);
            result.RunLog = log;
            result.LastStackTrace = lastStackTrace;
            return result;
```

- [ ] **Step 3: Build**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```
git add FlowMatters.Source.Veneer/ExchangeObjects/RunSummary.cs FlowMatters.Source.Veneer/SourceService.cs
git commit -m "feat: expose LastStackTrace on RunSummary"
```
(If you deferred the Task 2 commit, include `SourceService.cs` here under a combined message.)

---

## Task 4: Bump protocol version + update API docs

**Files:**
- Modify: `FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs` (line 18)
- Modify: `docs/api/schemas.md` (RunSummary table ~72-84)
- Modify: `docs/api/runs-and-results.md` (example payload ~94-116; capture note ~40)

- [ ] **Step 1: Bump `PROTOCOL_VERSION`**

In `VeneerStatus.cs` line 18, change:
```csharp
        public const int PROTOCOL_VERSION = 20260626;
```
to:
```csharp
        public const int PROTOCOL_VERSION = 20260630;
```

- [ ] **Step 2: Update `docs/api/schemas.md`**

In the `RunSummary` table, change the `RunLog` row and add a `LastStackTrace` row:
```markdown
| `RunLog` | string[] | Captured log; each line is `[Level] timestep <sim-time>: message` (the `timestep <sim-time>` part is present only when the entry has a simulation timestep). Placeholder text for non-Veneer runs. |
| `LastStackTrace` | string \| null | Stack trace of the last log entry that carried one during the run; `null` if none (e.g. a clean run, or a non-Veneer run). |
```

- [ ] **Step 3: Update `docs/api/runs-and-results.md`**

In the example payload (~line 104), change the `RunLog` line and add `LastStackTrace`:
```json
  "RunLog": ["[Information] Run started", "[Error] timestep 1990-06-01: Data file not found"],
  "LastStackTrace": null,
```
(Keep it consistent with the surrounding JSON — `LastStackTrace` as a sibling member of `RunLog`.)

- [ ] **Step 4: Build (sanity — only VeneerStatus.cs is code)**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```
git add FlowMatters.Source.Veneer/ExchangeObjects/VeneerStatus.cs docs/api/schemas.md docs/api/runs-and-results.md
git commit -m "docs: bump PROTOCOL_VERSION and document richer RunLog + LastStackTrace"
```

---

## Task 5: Multi-version build

**Files:** none (build only).

- [ ] **Step 1: Compile against all installed Source versions**

Run:
```
build.bat
```
Expected: each discovered Source version under `Compiled/<version>/` builds with 0 errors. If a
version fails, inspect the captured build output (the multi-version build replays failed-version
errors in its summary) and fix any version-specific (`#if`) issues before continuing.

- [ ] **Step 2: Commit only if fixes were needed**

```
git add -A
git commit -m "fix: resolve version-specific build issues for richer run logs"
```

---

## Task 6: Verification before completion (master/CoreWCF)

**Files:** none.

- [ ] **Step 1:** Use the superpowers:verification-before-completion skill before claiming done.
- [ ] **Step 2:** Confirm: single-version build clean, `build.bat` clean, all edits committed,
      `git status` clean. Record the actual build output, do not assert success without it.

---

## Task 7: Manual integration check (recommended)

**Files:** none. Requires a Source install + a project file (or VeneerCmd).

- [ ] **Step 1: Launch a Veneer instance**

Either run inside Source (Plugin Manager) or launch `FlowMatters.Source.VeneerCmd` against a
project file (see its CommandLine options: port, project file).

- [ ] **Step 2: Trigger a failing run and inspect the log**

Trigger a run that emits an error (e.g. a model with a missing/unreadable input data file).
Then `GET /runs/latest` and confirm:
- `RunLog` lines are prefixed with `[Level]` and, where the entry had a simulation timestep,
  `timestep <sim-time>:`.
- `LastStackTrace` is populated (non-null) for the error.

- [ ] **Step 3: Trigger a clean run and inspect the log**

Trigger a run that completes cleanly. Confirm `GET /runs/latest` returns formatted `RunLog`
lines and `LastStackTrace: null`.

- [ ] **Step 4:** Note the observed output in the PR description / commit message.

---

## Task 8: Port to `legacy_ci` (classic WCF) branch

**Files (on `legacy_ci`):** the same four source files + two docs, adapted per
`branch-porting-guide.md`.

- [ ] **Step 1: Switch to the legacy branch**

Run:
```
git checkout legacy_ci
```
(Stash/commit any worktree state first. Consider a sub-branch `feature/richer-simulation-logs-legacy`.)

- [ ] **Step 2: Inspect the legacy equivalents**

Read the legacy `SourceService.cs` (`TriggerRun`, `GetRunResults`, `_runLogs`/`RunLogs`),
`ExchangeObjects/RunSummary.cs`, `ExchangeObjects/VeneerStatus.cs`, and the `Formatting/`
directory. Note differences: classic WCF uses `System.ServiceModel.Web` (`WebOperationContext`,
`WebFaultException`) and **synchronous** Start/Stop, but the `TIME.Management.Log.MessageRecieved`
logger pattern and `LogEntry` type are identical.

- [ ] **Step 3: Add `RunLogFormatter.cs` verbatim**

Copy `Formatting/RunLogFormatter.cs` from this plan/Task 1 unchanged. `LogEntry`, `DateTime`,
`CultureInfo` are all available the same way on .NET Framework 4.8.

- [ ] **Step 4: Mirror the `SourceService` changes**

Apply the same three edits as Task 2 + the `GetRunResults` edit from Task 3, against the legacy
`SourceService.cs`. The `_runLogs`/`RunLogs` retype, the logger closure, the `CapturedRunLog`
storage, and the `GetRunResults` read are WCF-flavour-agnostic — only surrounding context lines
differ. Ensure the legacy file has a `using` for the `Formatting` namespace (add if missing).

- [ ] **Step 5: Mirror `RunSummary` + `VeneerStatus`**

Add `LastStackTrace` to legacy `RunSummary.cs` and bump `PROTOCOL_VERSION` to `20260630` in legacy
`VeneerStatus.cs` (match master's value so both branches advertise the same protocol).

- [ ] **Step 6: Mirror the docs** (if `docs/api/` exists on `legacy_ci`)

Apply the same `schemas.md` / `runs-and-results.md` edits. If the docs live only on master, skip.

- [ ] **Step 7: Build the legacy branch**

Build with the legacy toolchain per `BUILD.md` / `branch-porting-guide.md` (legacy MSBuild,
.NET Framework 4.8). Expected: 0 errors.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "feat: richer simulation logs (level, timestep, stack trace) — port to legacy WCF"
```

- [ ] **Step 9: Return to the feature branch**

```
git checkout feature/richer-simulation-logs
```

---

## Done criteria

- `RunLog` lines carry `[Level]` and optional `timestep <sim-time>:`.
- `RunSummary.LastStackTrace` returns the last non-null stack trace, else `null`.
- `RunSummary` shape otherwise unchanged (`RunLog` still `string[]`).
- `PROTOCOL_VERSION` bumped on both branches; `docs/api/` updated.
- Single-version and multi-version (`build.bat`) builds clean; manual run-trigger check passes.
- Change ported to `legacy_ci`.
