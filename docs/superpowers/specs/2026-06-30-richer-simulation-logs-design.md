# Richer simulation logs in RunSummary

**Date:** 2026-06-30
**Status:** Approved (design)
**Branches affected:** `master` (CoreWCF) and `legacy_ci` (classic WCF)

## Problem

When Veneer captures simulation logs in `SourceService.TriggerRun`, it records only
`LogEntry.Message`. The other diagnostic fields on `TIME.Management.LogEntry` — the log
level (`Type`), the simulation timestep (`TimeStep`), and the `StackTrace` — are discarded.
As a result the `RunLog` returned in `RunSummary` is a flat list of bare messages with no
indication of severity, no indication of *when* (in model time) each message was emitted,
and no stack trace even when an error carries one.

## Goal

Enrich the captured log without changing the shape of the existing API:

- `RunSummary.RunLog` stays a `string[]`, but each string is pre-formatted to include the
  log level and (when present) the simulation timestep.
- Surface the most relevant stack trace via a single new additive field.

## Non-goals

- No change to `RunLog`'s type (`string[]`).
- No structured/per-field log object in the API.
- No change to how non-Veneer runs are handled (they still return the placeholder log).

## Current behaviour (baseline)

- `SourceService.TriggerRun` (`FlowMatters.Source.Veneer/SourceService.cs`):
  - Subscribes a `runLogger` to `TIME.Management.Log.MessageRecieved` that enqueues only
    `args.Entry.Message` into a `ConcurrentQueue<string>`.
  - Stores `messages.ToArray()` in `RunLogs` (`Dictionary<int,string[]>`), keyed by run number.
  - Two error paths return `messages.ToArray()` in `SimulationFault.Log`:
    the `catch` around `RunScenario`, and the "run completed without producing a result" path.
- `SourceService.GetRunResults` reads `RunLogs[run.RunNumber]` into `RunSummary.RunLog`.
- `RunSummary` (`ExchangeObjects/RunSummary.cs`) exposes `[DataMember] public string[] RunLog`.
- Veneer's own `LogLevel` enum (`AbstractSourceServer.cs`) is **distinct** from TIME's
  `LogType` — the simulation entries carry `LogType`, not Veneer's `LogLevel`.

## Design

### 1. Log line format

Each captured simulation log line is formatted as:

```
[<Level>] timestep <sim-time>: <message>
```

- **Level** — `LogEntry.Type.ToString()` (TIME `LogType`, e.g. `Information` / `Warning` /
  `Error`), in square brackets.
- **timestep** — the literal word `timestep` followed by the simulation time. The label is
  deliberate: it prevents users from mistaking the model time for a wall-clock log timestamp
  (a real risk when a model's run period is on or around the present day). The whole
  `timestep <sim-time>` segment is **omitted entirely** when `LogEntry.TimeStep` is null.
- **sim-time** — "smart" granularity: `yyyy-MM-dd` when the time component is midnight,
  otherwise `yyyy-MM-dd HH:mm:ss` (preserves sub-daily timesteps).
- **message** — `LogEntry.Message`.

Examples:

```
[Error] timestep 1990-06-01: Data file not found
[Warning] timestep 1990-06-01 06:30:00: Storage spilled
[Information] Run started
```

### 2. `RunLogFormatter` (new, isolated helper)

New file `FlowMatters.Source.Veneer/Formatting/RunLogFormatter.cs`:

```csharp
public static class RunLogFormatter
{
    public static string Format(LogEntry entry); // LogEntry => formatted line per §1
}
```

- Pure function, no dependencies on Veneer state — trivially unit-testable.
- Self-contained so the legacy WCF branch can take the **identical file** with no
  adaptation (`LogEntry` is the same TIME type on both branches).
- Defensive on nulls (null `Message`, null `TimeStep`).

### 3. Capturing level + timestep + last stack trace (`TriggerRun`)

- `RunLogs` changes from `Dictionary<int,string[]>` to `Dictionary<int,CapturedRunLog>`.
- New small container (alongside `RunLogFormatter` or as a nested/standalone type):

  ```csharp
  public class CapturedRunLog
  {
      public string[] Messages;
      public string LastStackTrace; // last non-null StackTrace seen, else null
  }
  ```

- The `runLogger` closure:
  - `messages.Enqueue(RunLogFormatter.Format(args.Entry));`
  - Tracks the **last non-null** `args.Entry.StackTrace`. Because `MessageRecieved` may fire
    from multiple threads, the update uses a lock (or equivalent holder) so "last" is
    well-defined — consistent with the existing use of `ConcurrentQueue` for messages.
- On success: `RunLogs[newRun.RunNumber] = new CapturedRunLog { Messages = messages.ToArray(), LastStackTrace = lastStackTrace };`
- The two error paths keep returning `messages.ToArray()` in `SimulationFault.Log`; those
  lines are now the richer formatted strings, for free.

### 4. Surfacing it (`GetRunResults` + `RunSummary`)

- `RunSummary` gains one additive member:

  ```csharp
  [DataMember] public string LastStackTrace; // null when no stack trace was captured
  ```

  Additive — existing clients are unaffected. Default null (set in constructor).
- `GetRunResults` reads the `CapturedRunLog`:
  - `result.RunLog = captured.Messages;`
  - `result.LastStackTrace = captured.LastStackTrace;`
  - The "run log not available" placeholder path leaves `LastStackTrace` null.

### 5. Cross-cutting

- **Protocol version** — bump `PROTOCOL_VERSION` in
  `ExchangeObjects/VeneerStatus.cs` (current `20260626`) because the REST surface changed.
- **API docs**:
  - `docs/api/schemas.md` — add a `LastStackTrace` row to the `RunSummary` table; note that
    `RunLog` lines now carry level and (optional) simulation timestep.
  - `docs/api/runs-and-results.md` — update the example `RunSummary` payload (formatted
    `RunLog` lines and a `LastStackTrace` field).
- **Legacy WCF branch (`legacy_ci`)** — port the same change per `branch-porting-guide.md`:
  - `RunLogFormatter.cs` copies verbatim.
  - `TriggerRun`, `RunSummary`, `GetRunResults`, the `RunLogs` dictionary type, docs, and the
    protocol-version bump mirrored.

## Testing

- Unit tests for `RunLogFormatter.Format`:
  - Error with date-only timestep → `[Error] timestep 1990-06-01: ...`
  - Warning with sub-daily timestep → `[Warning] timestep 1990-06-01 06:30:00: ...`
  - Entry with null `TimeStep` → no `timestep` segment.
  - Null `Message` handled without throwing.
- Manual / integration: trigger a run that errors (e.g. missing input data), confirm
  `GET /runs/{id}` returns formatted `RunLog` lines and a populated `LastStackTrace`; trigger
  a clean run and confirm `LastStackTrace` is null.

## Risks / notes

- `LogType` enum member names are surfaced verbatim as the level string; acceptable since
  they are human-readable.
- The last-stack-trace selection is "last non-null wins"; under concurrent logging this is
  inherently order-dependent, but matched to the lock-guarded update it is deterministic for
  a given log ordering.
