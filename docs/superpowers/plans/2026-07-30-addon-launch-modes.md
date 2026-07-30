# Addon Launch Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `.veneer` addon menu entries pass arguments, set environment variables, and run a series of commands inline without writing a temporary `.bat` file to disk.

**Architecture:** Pure logic (quoting, environment merging, `%VAR%` expansion, script generation, output filtering) lives in four small dependency-free classes under `DomainActions/`, each unit-tested. A thin `AddonLauncher` orchestrates them and owns the `Process`. `ReportingMenu` is renamed `VeneerMenu` and reduced to menu construction only.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`), NUnit 4.1.0 (already referenced), VS 2022 MSBuild, `cmd.exe` fed over redirected stdin.

**Spec:** [`docs/superpowers/specs/2026-07-30-addon-launch-modes-design.md`](../specs/2026-07-30-addon-launch-modes-design.md)

---

## READ THIS FIRST — never stage broadly, `master` is dirty

This work happens **directly on `master`**, at the user's explicit instruction, and
`master` has **unrelated uncommitted work in progress**: `SourceService.cs`,
`ISourceService.cs`, `ExchangeObjects/VeneerStatus.cs`, `UriTemplates.cs`, plus a
number of untracked files and directories.

**Never run `git add -A`, `git add .`, `git commit -a`, `git stash`, `git checkout .`
or `git reset --hard`.** Stage only the explicit paths named in each task's commit
step. Sweeping in the user's in-progress API work would be difficult to untangle
and is not yours to commit.

If a commit step's file list looks incomplete for what you changed, add the
specific paths you touched — never widen the pattern.

## READ THIS SECOND — the build is currently broken

**The working tree does not compile right now.** This is pre-existing and unrelated to this feature — the errors are all `CS0234` in files this plan does not touch (`ProjectLoadListener.cs`, `ScenarioInvoker.cs`, `GeoJSONFeature.cs`, and others), caused by five missing Source assemblies.

Verified state of this machine:

| Fact | Value |
|---|---|
| `C:\src\projects\Output` | exists, 282 DLLs, but **missing all five** of `RiverSystem.Forms.dll`, `RiverSystem.Forms.Core.dll`, `RiverSystem.Controls.UI.dll`, `TIME.UI.dll`, `TIME.Winforms.UI.dll` |
| `C:\src\projects\Veneer\References` | exists, 230 files, missing four of those five (it has `RiverSystem.Controls.UI.dll`). **Not a viable fallback** — it also lacks `RiverSystem.dll` and `TIME.dll` |
| `C:\Program Files\eWater\Source*` | **no installed Source at all** |
| `dotnet test` on the main csproj | **FAILS** — cannot resolve those five references |
| VS 2022 MSBuild on the main csproj | **FAILS** — same `CS0234` errors |
| `C:\Geospatial\Source\Source_6.10.0.14373` | exists and **does** contain all five |
| `vstest.console.exe` | `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe` |

So the supported build path is `build.bat` → `python compile_all.py --refpath ..\Output Veneer.sln`, which reads the uncommented lines of `Veneer.sln.include` and, for each version, **clears and repopulates `..\Output`** (i.e. `C:\src\projects\Output` — see `build_version` in `compile_all.py`, around lines 404-441), then builds into `Compiled/<version>/`.

`..\Output` is the important target: every `<Reference>` in
`FlowMatters.Source.Veneer.csproj` has a `HintPath` of `..\..\Output\*.dll`.
`Veneer\References` is only a secondary `ReferencePath` entry and is not what
fixes the build.

**Task 0 exists to get to a green build and a passing test run before any feature work.** Do not skip it, and do not start Task 1 until `dotnet test`-equivalent verification actually runs. Every "Run:" command in later tasks assumes Task 0 succeeded.

**Honest limitation:** because the build is broken as of writing, the exact test-runner invocation in Task 0 **Step 4** could **not** be verified end-to-end. Treat it as the first thing to establish, and if it needs adjusting, fix it in Task 0 and note the working command in the plan before continuing.

---

## File Structure

Four new pure-logic files, each with one responsibility and **no RiverSystem or TIME dependency** — this is what makes them unit-testable without a loaded scenario.

| File | Responsibility |
|---|---|
| `DomainActions/AddonContext.cs` | POCO carrying project dir, project file, port. Plus `IAddonLog`. Primitives only — never a `RiverSystemScenario`. |
| `DomainActions/AddonCommandLine.cs` | `QuoteArgument`, `QuoteArgumentForCmd`, `.exe` vs `.bat` composition |
| `DomainActions/AddonEnvironment.cs` | Effective-environment merge; single-pass `%VAR%` expansion |
| `DomainActions/AddonScript.cs` | Script line generation; `ScriptOutputFilter` |
| `DomainActions/AddonLauncher.cs` | Orchestration: builds `ProcessStartInfo`, owns the `Process`, pumps output. The only file here that touches `System.Diagnostics`. |

Modified:

| File | Change |
|---|---|
| `Addons/VeneerConfiguration.cs` | Four new `VeneerAddon` fields + validation |
| `ReportingMenu.cs` → `VeneerMenu.cs` | Renamed (Task 1); later, dispatch + `default` case, launching removed |
| `WebServerStatusControl.xaml.cs` | Hoist log sink out of `StartServer` |
| `CLAUDE.md`, `branch-porting-guide.md` | Rename references |

Tests go in `FlowMatters.Source.Veneer/Tests/`, namespace `FlowMatters.Source.Veneer.Tests`, following the existing `SchematicNameSanitiserTests.cs` convention (NUnit `[TestFixture]`, `Assert.That(..., Is.EqualTo(...))`).

---

## Task 0: Establish a green build and test baseline

**Files:** none modified — this task only verifies and records.

- [ ] **Step 1: Commit the spec**

It is currently untracked. `master` has unrelated in-progress work, so commit **only** the spec and this plan.

```bash
git add docs/superpowers/specs/2026-07-30-addon-launch-modes-design.md docs/superpowers/plans/2026-07-30-addon-launch-modes.md
git commit -m "docs: spec and plan for addon launch modes"
```

- [ ] **Step 2: Repopulate `..\Output` and confirm a green build**

Run: `build.bat`

Expected: a results summary listing each version from the uncommented lines of `Veneer.sln.include` with successful builds. This repopulates `C:\src\projects\Output` with a complete assembly set, including the five currently missing.

If it fails, stop and report — do not proceed. A likely cause is a Source path in `Veneer.sln.include` that no longer exists; comment out the offending line and retry. (All uncommented paths were present at the time of writing.)

**Recorded result (2026-07-30):** `build.bat` reported *11 successful builds out of 11 versions*. But it does **not** leave `..\Output` usable for a `master` build. `compile_all.py` splits the 11 versions into a COREWCF group (4 × Source 6.x, built on `master` in this repo) and a WCF group (7 × Source ≤5.6x, built on `legacy_ci` in `C:\src\projects\Veneer-legacy`), and it clears and restages `..\Output` per version. The WCF group runs **last**, so `..\Output` ends up holding the final legacy version's net48 reference set — still missing `RiverSystem.Forms.dll`, `RiverSystem.Forms.Core.dll`, `RiverSystem.Controls.UI.dll`, `TIME.UI.dll` and `TIME.Winforms.UI.dll`.

To get the fast inner loop working, restage a Source 6.x reference set over `..\Output` afterwards — the same two copies `compile_all.py` does per version (`copy_references`):

```powershell
$s='C:\Geospatial\Source\Source_6.10.0.14373'; $o='C:\src\projects\Output'
Get-ChildItem $s -File | Where-Object { $_.Extension -in '.dll','.exe' } |
  ForEach-Object { Copy-Item $_.FullName (Join-Path $o $_.Name) -Force }
New-Item -ItemType Directory -Force (Join-Path $o 'Plugins') | Out-Null
Get-ChildItem "$s\Plugins" -File | Where-Object { $_.Extension -in '.dll','.exe' } |
  ForEach-Object { Copy-Item $_.FullName (Join-Path $o "Plugins\$($_.Name)") -Force }
```

If a later `master` build suddenly fails with `CS0234` on `RiverSystem.Forms` / `TIME.UI`, someone has re-run `build.bat`; re-run the snippet above.

- [ ] **Step 3: Confirm a plain single-version build now works**

With `..\Output` complete, the csproj's `HintPath` references resolve and a direct build should succeed. This is the fast inner loop for the rest of the plan — `build.bat` is far too slow to run per test.

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly
```
Expected: no `CS0234` errors, exit code 0.

- [ ] **Step 4: Confirm the existing tests run and pass**

The main project already references NUnit 4.1.0, NUnit3TestAdapter and Microsoft.NET.Test.Sdk, and already has two fixtures.

Try in order, and **record which one works**:

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~SchematicNameSanitiser" --nologo
```

If `dotnet test` still cannot resolve references, fall back to vstest against the MSBuild output:

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "..\..\Output\Plugins\CommunityPlugins\FlowMatters.Source.Veneer.dll" /TestCaseFilter:"FullyQualifiedName~SchematicNameSanitiser"
```

Expected: `Passed: 12` or similar, zero failures.

- [ ] **Step 5: Record the working command**

**Recorded result (verified 2026-07-30):** `dotnet test` works — 13 passed, 0 failed, exit code 0. The vstest fallback was not needed. Every later `Run:` step in this plan uses this base command:

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

To filter to a fixture, append `--filter "FullyQualifiedName~<FixtureName>"`. Note it emits many `MSB3277` reference-version warnings; these are pre-existing noise, not failures.

Commit.

```bash
git add docs/superpowers/plans/2026-07-30-addon-launch-modes.md
git commit -m "docs: record working test command in addon launch plan"
```

---

## Task 1: Rename `ReportingMenu` to `VeneerMenu`

Pure rename, zero behaviour change, its own commit — so the feature diff stays portable to `legacy_ci`. The class is `internal` (`ReportingMenu.cs:19`), so this is not an API change and is unreachable from veneer-py or IronPython.

**Files:**
- Rename: `FlowMatters.Source.Veneer/ReportingMenu.cs` → `FlowMatters.Source.Veneer/VeneerMenu.cs`
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs:165,168,209`
- Modify: `FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs:102,113,114`
- Modify: `CLAUDE.md:44`, `branch-porting-guide.md:207`

- [ ] **Step 1: Rename the file with git so history follows**

```bash
git mv FlowMatters.Source.Veneer/ReportingMenu.cs FlowMatters.Source.Veneer/VeneerMenu.cs
```

- [ ] **Step 2: Rename the class and its self-references**

In `VeneerMenu.cs`, change `internal class ReportingMenu` → `internal class VeneerMenu`, the private constructor `ReportingMenu()` → `VeneerMenu()`, the `_instance`/`Instance` field and property types, and the two internal `ReportingMenu.FindMainForm()` call sites (lines 67 and 214).

- [ ] **Step 3: Update the six external call sites**

`ProjectLoadListener.cs` lines 165, 168, 209 and `WebServerStatusControl.xaml.cs` lines 102, 113, 114 — all are `ReportingMenu.Instance...` or `ReportingMenu.FindMainForm()`. Note `ProjectLoadListener.cs:184,207` reference a *method* named `PopulateReportingMenu` — that is a local method name, **leave it alone** for now to keep this commit a pure rename.

- [ ] **Step 4: Verify nothing else references the old name**

Run: `git grep -n "\bReportingMenu\b" -- "*.cs"`
Expected: no output.

The word boundary matters. A plain `git grep -n "ReportingMenu"` still matches the
local method `PopulateReportingMenu` at `ProjectLoadListener.cs:184,207`, which
Step 3 deliberately leaves alone — so the unanchored form can never return clean
and would provoke an unplanned rename.

- [ ] **Step 5: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0, no errors.

- [ ] **Step 6: Update the two docs**

`CLAUDE.md:44` — change the bullet to `**`VeneerMenu`**`. `branch-porting-guide.md:207` — change `ReportingMenu.cs` to `VeneerMenu.cs`. Leave historical files under `docs/superpowers/specs/` and `plans/` untouched; they record what was true when written.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs \
        FlowMatters.Source.Veneer/ReportingMenu.cs \
        FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs \
        FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs \
        CLAUDE.md branch-porting-guide.md
git commit -m "refactor: rename ReportingMenu to VeneerMenu

The class handles Veneer's whole Tools-menu presence -- addons, HTML
report items, /doc/ links and the logo item -- not just reporting.
Pure rename, no behaviour change."
```

- [ ] **Step 8: Port the rename to `legacy_ci` before any feature work**

Renaming on one branch only would make every future menu change hand-mapped across a filename difference. Do this now, not later.

Follow `branch-porting-guide.md`. The legacy worktree is at `C:\src\projects\Veneer-legacy`. Apply the same rename there (`git mv`, class rename, same call sites — line numbers will differ), build with the legacy toolchain, and commit with the same message.

- [ ] **Step 9: Confirm both branches build**

Run: `build.bat`
Expected: successful builds for both the CoreWCF (`master`) and WCF (`legacy_ci`) version groups.

---

## Task 2: `AddonContext` and `IAddonLog`

The seam that keeps everything downstream testable. Primitives only.

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/AddonContext.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Everything an addon launch needs from the host, as primitives.
    /// Deliberately carries no RiverSystem or TIME types so that the launch
    /// logic can be unit tested without a loaded scenario.
    /// </summary>
    internal class AddonContext
    {
        public string ProjectDirectory { get; set; }
        public string ProjectFile { get; set; }
        public int Port { get; set; }
    }

    internal enum AddonLogLevel
    {
        Debug,
        Warning,
        Error
    }

    internal interface IAddonLog
    {
        void Write(string message, AddonLogLevel level);
    }
}
```

- [ ] **Step 2: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonContext.cs
git commit -m "feat: add AddonContext and IAddonLog seam for addon launching"
```

---

## Task 3: Argument quoting

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/AddonCommandLine.cs`
- Test: `FlowMatters.Source.Veneer/Tests/AddonCommandLineTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/AddonCommandLineTests.cs`. The metacharacter cases are the point of this fixture — every whitespace case passes against a narrow trigger while `&`, `|` and `^` ship broken.

```csharp
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonCommandLineTests
    {
        [TestCase("plain",        "plain")]
        [TestCase("has space",    "\"has space\"")]
        [TestCase("",             "\"\"")]
        [TestCase("a&b",          "a&b")]
        [TestCase("a|b",          "a|b")]
        public void QuoteArgument_QuotesOnWhitespaceOnly(string input, string expected)
        {
            Assert.That(AddonCommandLine.QuoteArgument(input), Is.EqualTo(expected));
        }

        [TestCase("plain",     "plain")]
        [TestCase("has space", "\"has space\"")]
        [TestCase("",          "\"\"")]
        [TestCase("a&b",       "\"a&b\"")]
        [TestCase("a|b",       "\"a|b\"")]
        [TestCase("a<b",       "\"a<b\"")]
        [TestCase("a>b",       "\"a>b\"")]
        [TestCase("a^b",       "\"a^b\"")]
        [TestCase("a(b)c",     "\"a(b)c\"")]
        public void QuoteArgumentForCmd_AlsoQuotesOnMetacharacters(string input, string expected)
        {
            Assert.That(AddonCommandLine.QuoteArgumentForCmd(input), Is.EqualTo(expected));
        }

        [Test]
        public void QuoteArgument_EscapesEmbeddedQuote()
        {
            Assert.That(AddonCommandLine.QuoteArgument("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
        }

        [Test]
        public void QuoteArgument_DoublesTrailingBackslashesBeforeClosingQuote()
        {
            Assert.That(AddonCommandLine.QuoteArgument("C:\\dir with space\\"), Is.EqualTo("\"C:\\dir with space\\\\\""));
        }

        [Test]
        public void Compose_Exe_UsesPathAsFileName()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\tools\calib.exe", new[] { "--out", "results 2026" },
                                     out fileName, out arguments);
            Assert.That(fileName, Is.EqualTo(@"C:\tools\calib.exe"));
            Assert.That(arguments, Is.EqualTo("--out \"results 2026\""));
        }

        [Test]
        public void Compose_Bat_WrapsInCmdWithDoubledOuterQuotes()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\dir with space\x.bat", new[] { "arg with space" },
                                     out fileName, out arguments);
            Assert.That(fileName, Is.EqualTo("cmd.exe"));
            Assert.That(arguments, Is.EqualTo(
                "/D /V:OFF /C \"\"C:\\dir with space\\x.bat\" \"arg with space\"\""));
        }

        [Test]
        public void Compose_Bat_QuotesMetacharacterArgument()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\t\x.bat", new[] { "a&b" }, out fileName, out arguments);
            Assert.That(arguments, Is.EqualTo("/D /V:OFF /C \"\"C:\\t\\x.bat\" \"a&b\"\""));
        }

        [Test]
        public void Compose_Bat_NoArgs_StillDoublesQuotes()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\dir with space\x.bat", null, out fileName, out arguments);
            Assert.That(arguments, Is.EqualTo("/D /V:OFF /C \"\"C:\\dir with space\\x.bat\"\""));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonCommandLineTests`
Expected: build failure — `AddonCommandLine` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonCommandLine
    {
        private static readonly char[] WhitespaceOrQuote = { ' ', '\t', '"' };
        private static readonly char[] CmdMetacharacters = { '&', '|', '<', '>', '^', '(', ')' };

        public static bool IsBatch(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        public static string QuoteArgument(string arg)
        {
            arg = arg ?? string.Empty;
            if (arg.Length > 0 && arg.IndexOfAny(WhitespaceOrQuote) < 0)
                return arg;
            return Quote(arg);
        }

        /// <summary>
        /// As QuoteArgument, but also quotes shell metacharacters. Required on the
        /// cmd /C path, where an unquoted &amp; or | truncates the argument and
        /// executes the tail as a command.
        /// </summary>
        public static string QuoteArgumentForCmd(string arg)
        {
            arg = arg ?? string.Empty;
            if (arg.Length > 0
                && arg.IndexOfAny(WhitespaceOrQuote) < 0
                && arg.IndexOfAny(CmdMetacharacters) < 0)
                return arg;
            return Quote(arg);
        }

        public static void Compose(string path, IEnumerable<string> args,
                                   out string fileName, out string arguments)
        {
            var list = (args ?? Enumerable.Empty<string>()).ToList();

            if (IsBatch(path))
            {
                fileName = "cmd.exe";
                // The path is ALWAYS quoted, even when it contains no whitespace.
                // The doubled-outer-quote trick relies on there being three or more
                // quotes present, and an unquoted path would make the composed line
                // depend on whether the path happened to contain a space.
                var parts = new List<string> { Quote(path) };
                parts.AddRange(list.Select(QuoteArgumentForCmd));
                arguments = "/D /V:OFF /C \"" + string.Join(" ", parts) + "\"";
            }
            else
            {
                fileName = path;
                arguments = string.Join(" ", list.Select(QuoteArgument));
            }
        }

        private static string Quote(string arg)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    backslashes = 0;
                    sb.Append('"');
                    continue;
                }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonCommandLineTests`
Expected: all pass, zero failures.

- [ ] **Step 5: Verify the composed `.bat` line actually works against real cmd**

The unit test asserts the *string*. This step asserts the string is *correct*, which no unit test can. Create a throwaway `.bat` under a path containing a space that echoes `%~1`, and invoke it with the exact composed arguments.

Expected: the argument arrives intact for `arg with space` and for `a&b`, and no stray `'b' is not recognized` appears. If it does not, the composition rule is wrong — fix it before continuing, because everything downstream inherits it.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonCommandLine.cs FlowMatters.Source.Veneer/Tests/AddonCommandLineTests.cs
git commit -m "feat: add addon argument quoting and command-line composition"
```

---

## Task 4: Environment merge and variable expansion

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs`
- Test: `FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonEnvironmentTests
    {
        private static AddonContext Ctx()
        {
            return new AddonContext
            {
                ProjectDirectory = @"C:\models\catchment",
                ProjectFile = @"C:\models\catchment\m.rsproj",
                Port = 9876
            };
        }

        [Test]
        public void BuildEffective_InjectsVeneerVariables()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("9876"));
            Assert.That(env["VENEER_PROJECT_DIR"], Is.EqualTo(@"C:\models\catchment"));
            Assert.That(env["VENEER_PROJECT_FILE"], Is.EqualTo(@"C:\models\catchment\m.rsproj"));
        }

        [Test]
        public void BuildEffective_AddonEnvOverridesInjected()
        {
            var addonEnv = new Dictionary<string, string> { { "VENEER_PORT", "1234" } };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["VENEER_PORT"], Is.EqualTo("1234"));
        }

        [Test]
        public void BuildEffective_ExpandsEnvValues()
        {
            var addonEnv = new Dictionary<string, string> { { "OUT", @"%VENEER_PROJECT_DIR%\out" } };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["OUT"], Is.EqualTo(@"C:\models\catchment\out"));
        }

        [Test]
        public void BuildEffective_EnvEntriesDoNotCrossReference()
        {
            var addonEnv = new Dictionary<string, string>
            {
                { "A", "first" },
                { "B", "%A%" }
            };
            var env = AddonEnvironment.BuildEffective(Ctx(), addonEnv);
            Assert.That(env["B"], Is.EqualTo("%A%"), "env entries must not resolve against each other");
        }

        [Test]
        public void Expand_LeavesUnknownVariablesIntact()
        {
            var env = new Dictionary<string, string> { { "KNOWN", "yes" } };
            Assert.That(AddonEnvironment.Expand("%KNOWN% %NOPE%", env), Is.EqualTo("yes %NOPE%"));
        }

        [Test]
        public void Expand_IsSinglePass()
        {
            var env = new Dictionary<string, string>
            {
                { "OUTER", "%INNER%" },
                { "INNER", "resolved" }
            };
            Assert.That(AddonEnvironment.Expand("%OUTER%", env), Is.EqualTo("%INNER%"));
        }

        [Test]
        public void Expand_IsCaseInsensitive()
        {
            var env = AddonEnvironment.BuildEffective(Ctx(), null);
            Assert.That(AddonEnvironment.Expand("%veneer_port%", env), Is.EqualTo("9876"));
        }

        [Test]
        public void Expand_HandlesNullAndEmpty()
        {
            var env = new Dictionary<string, string>();
            Assert.That(AddonEnvironment.Expand(null, env), Is.Null);
            Assert.That(AddonEnvironment.Expand("", env), Is.EqualTo(""));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonEnvironmentTests`
Expected: build failure — `AddonEnvironment` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonEnvironment
    {
        private static readonly Regex VariablePattern =
            new Regex("%([^%]+)%", RegexOptions.Compiled);

        /// <summary>
        /// process environment + Veneer's injected variables + the addon's own env
        /// (which wins). Values in the addon's env are expanded against everything
        /// above them, but NOT against each other -- that would make the result
        /// depend on JSON key order.
        /// </summary>
        public static Dictionary<string, string> BuildEffective(
            AddonContext context, IDictionary<string, string> addonEnv)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                env[(string)entry.Key] = entry.Value as string ?? string.Empty;

            env["VENEER_PORT"] = context.Port.ToString();
            env["VENEER_PROJECT_DIR"] = context.ProjectDirectory ?? string.Empty;
            env["VENEER_PROJECT_FILE"] = context.ProjectFile ?? string.Empty;

            if (addonEnv != null)
            {
                // Snapshot first so addon entries cannot resolve against each other.
                var baseline = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in addonEnv)
                    env[kv.Key] = Expand(kv.Value, baseline);
            }

            return env;
        }

        /// <summary>
        /// Replaces %NAME% where NAME is present. Unknown variables are left
        /// intact so a typo is visible rather than silently blanking an argument.
        /// Single pass: substituted values are not themselves rescanned.
        /// </summary>
        public static string Expand(string input, IDictionary<string, string> env)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return VariablePattern.Replace(input, match =>
            {
                string value;
                return env.TryGetValue(match.Groups[1].Value, out value) ? value : match.Value;
            });
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonEnvironmentTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonEnvironment.cs FlowMatters.Source.Veneer/Tests/AddonEnvironmentTests.cs
git commit -m "feat: add addon environment merge and single-pass variable expansion"
```

---

## Task 5: Script generation and output filtering

The trickiest pure logic. The stream shape below was established empirically — see the spec's "What the captured stream actually contains".

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/AddonScript.cs`
- Test: `FlowMatters.Source.Veneer/Tests/AddonScriptTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonScriptTests
    {
        private const string Nonce = "abc-123";

        [Test]
        public void Generate_EmitsEchoOffMarkersGuardsAndTerminator()
        {
            var lines = AddonScript.Generate(new[] { "one", "two" }, Nonce).ToList();
            Assert.That(lines, Is.EqualTo(new[]
            {
                "@echo off",
                "echo ##VENEER:abc-123:1",
                "one",
                "if errorlevel 1 exit %errorlevel%",
                "echo ##VENEER:abc-123:2",
                "two",
                "if errorlevel 1 exit %errorlevel%",
                "exit 0"
            }));
        }

        private static List<string> Run(ScriptOutputFilter filter, IEnumerable<string> raw)
        {
            var kept = new List<string>();
            foreach (var line in raw) kept.AddRange(filter.Accept(line));
            kept.AddRange(filter.Flush());
            return kept;
        }

        [Test]
        public void Filter_DropsBannerPromptScaffolding_KeepsEchoAndOutput()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[]
            {
                "Microsoft Windows [Version 10.0.26200.8875]",
                "(c) Microsoft Corporation. All rights reserved.",
                "",
                @"C:\models\catchment>@echo off",
                "echo ##VENEER:abc-123:1",
                "##VENEER:abc-123:1",
                "python calibrate.py",
                "calibration complete",
                "if errorlevel 1 exit %errorlevel%",
                "exit 0"
            });

            Assert.That(kept, Is.EqualTo(new[] { "python calibrate.py", "calibration complete" }));
        }

        [Test]
        public void Filter_RecordsCurrentStep()
        {
            var filter = new ScriptOutputFilter(Nonce);
            Run(filter, new[]
            {
                @"C:\x>@echo off",
                "echo ##VENEER:abc-123:1",
                "##VENEER:abc-123:1",
                "first",
                "echo ##VENEER:abc-123:3",
                "##VENEER:abc-123:3",
                "third"
            });
            Assert.That(filter.CurrentStep, Is.EqualTo(3));
        }

        [Test]
        public void Filter_TrimsLineEndingsBeforeMatching()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[]
            {
                "C:\\x>@echo off\r",
                "if errorlevel 1 exit %errorlevel%\r",
                "real output\r"
            });
            Assert.That(kept, Is.EqualTo(new[] { "real output" }));
        }

        [Test]
        public void Filter_FlushesWhenSentinelNeverArrives()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[] { "catastrophic cmd failure", "second line" });
            Assert.That(kept, Is.EqualTo(new[] { "catastrophic cmd failure", "second line" }),
                        "a stream with no sentinel must not be silently discarded");
        }

        [Test]
        public void Filter_DoesNotDropOutputResemblingAMarkerWithADifferentNonce()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[] { @"C:\x>@echo off", "##VENEER:other:7" });
            Assert.That(kept, Is.EqualTo(new[] { "##VENEER:other:7" }));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonScriptTests`
Expected: build failure — `AddonScript` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonScript
    {
        public const string Guard = "if errorlevel 1 exit %errorlevel%";
        public const string Terminator = "exit 0";
        public const string EchoOff = "@echo off";

        public static string Marker(string nonce, int step)
        {
            return "##VENEER:" + nonce + ":" + step;
        }

        /// <summary>
        /// @echo off suppresses the prompt (setting PROMPT empty does not, and /Q
        /// is inert for a stdin-fed session). The guard after every line gives
        /// stop-on-first-failure inside cmd, with no round-tripping to .NET.
        /// </summary>
        public static IEnumerable<string> Generate(IEnumerable<string> lines, string nonce)
        {
            yield return EchoOff;
            int step = 0;
            foreach (var line in lines)
            {
                step++;
                yield return "echo " + Marker(nonce, step);
                yield return line;
                yield return Guard;
            }
            yield return Terminator;
        }
    }

    /// <summary>
    /// Strips Veneer's own scaffolding from cmd's stdout. The command echo is
    /// deliberately kept -- it cannot be reliably stripped (an echoed input line
    /// is byte-identical to a program printing the same text) and reads usefully
    /// as a transcript. Note the echo is pre-expansion: it shows
    /// "cd %VENEER_PROJECT_DIR%", not the resolved path.
    /// </summary>
    internal sealed class ScriptOutputFilter
    {
        private readonly Regex _markerEcho;
        private readonly Regex _markerOutput;
        private readonly List<string> _preSentinel = new List<string>();
        private bool _sentinelSeen;

        public int CurrentStep { get; private set; }

        public ScriptOutputFilter(string nonce)
        {
            var n = Regex.Escape(nonce);
            _markerEcho = new Regex(@"^echo ##VENEER:" + n + @":(\d+)$");
            _markerOutput = new Regex(@"^##VENEER:" + n + @":(\d+)$");
        }

        public IEnumerable<string> Accept(string rawLine)
        {
            var line = (rawLine ?? string.Empty).TrimEnd('\r', '\n');

            if (!_sentinelSeen)
            {
                if (line.Contains(">" + AddonScript.EchoOff))
                {
                    _sentinelSeen = true;
                    _preSentinel.Clear();
                }
                else
                {
                    _preSentinel.Add(line);
                }
                yield break;
            }

            if (line == AddonScript.Guard) yield break;
            if (line == AddonScript.Terminator) yield break;
            if (_markerEcho.IsMatch(line)) yield break;

            var output = _markerOutput.Match(line);
            if (output.Success)
            {
                CurrentStep = int.Parse(output.Groups[1].Value);
                yield break;
            }

            yield return line;
        }

        /// <summary>
        /// If the sentinel never arrived, cmd failed pathologically -- surface
        /// what it did say rather than discarding the whole stream.
        /// </summary>
        public IEnumerable<string> Flush()
        {
            if (_sentinelSeen) return new string[0];
            var buffered = _preSentinel.ToArray();
            _preSentinel.Clear();
            return buffered;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonScriptTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonScript.cs FlowMatters.Source.Veneer/Tests/AddonScriptTests.cs
git commit -m "feat: add addon script generation and cmd output filtering"
```

---

## Task 6: Schema fields and validation

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs:79-91`
- Test: `FlowMatters.Source.Veneer/Tests/AddonValidationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonValidationTests
    {
        [Test]
        public void ExeWithPathOnly_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "exe", path = "tools/x.bat" };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void ScriptWithLines_IsValid()
        {
            var addon = new VeneerAddon { name = "a", type = "script", script = new[] { "echo hi" } };
            Assert.That(VeneerAddon.Validate(addon), Is.Null);
        }

        [Test]
        public void BothPathAndScript_IsInvalid()
        {
            var addon = new VeneerAddon
            {
                name = "a", type = "script",
                path = "tools/x.bat", script = new[] { "echo hi" }
            };
            Assert.That(VeneerAddon.Validate(addon), Does.Contain("both"));
        }

        [Test]
        public void ScriptTypeWithNoLines_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "script" };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }

        [Test]
        public void ScriptTypeWithEmptyLines_IsInvalid()
        {
            var addon = new VeneerAddon { name = "a", type = "script", script = new string[0] };
            Assert.That(VeneerAddon.Validate(addon), Is.Not.Null);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonValidationTests`
Expected: build failure — the new members do not exist.

- [ ] **Step 3: Add the fields and validation**

Replace the `VeneerAddon` class in `Addons/VeneerConfiguration.cs` (currently lines 79-91):

```csharp
    public class VeneerAddon
    {
        public string name { get; set; }

        public string type { get; set; }

        public string path { get; set; }

        public string menu { get; set; }

        public string scenario { get; set; }

        public string[] args { get; set; }

        public Dictionary<string, string> env { get; set; }

        public string workingDirectory { get; set; }

        public string[] script { get; set; }

        /// <summary>
        /// Returns null when valid, otherwise a human-readable reason. Used to
        /// render a disabled menu item with a tooltip rather than silently
        /// omitting the entry.
        /// </summary>
        public static string Validate(VeneerAddon addon)
        {
            bool hasScript = addon.script != null && addon.script.Length > 0;

            if (!string.IsNullOrEmpty(addon.path) && addon.script != null)
                return "specifies both 'path' and 'script'; they are mutually exclusive";

            if (string.Equals(addon.type, "script", StringComparison.OrdinalIgnoreCase) && !hasScript)
                return "is type 'script' but has no 'script' lines";

            return null;
        }
    }
```

`System` and `System.Collections.Generic` are already imported at the top of the file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` filtered to `AddonValidationTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs FlowMatters.Source.Veneer/Tests/AddonValidationTests.cs
git commit -m "feat: add args/env/workingDirectory/script to VeneerAddon with validation"
```

---

## Task 7: Hoist the Veneer log sink out of server lifetime

Addons can be launched with the server stopped, but the `LogBox` sink is only wired inside `StartServer` (`WebServerStatusControl.xaml.cs:184`). Without this, addon output goes nowhere.

**Files:**
- Modify: `FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs`

- [ ] **Step 1: Add a public log entry point**

`ServerLogEvent` (line 219) is private. Add an internal method beside it that `VeneerMenu` can call regardless of server state, reusing the same `_originalContext.Post` marshalling and `_minimumLogLevel` filtering:

```csharp
        internal void LogAddonMessage(string msg, LogLevel level)
        {
            ServerLogEvent(this, msg, level);
        }
```

- [ ] **Step 2: Verify the sink is live when the panel is open**

`ServerLogEvent` writes directly to `LogBox` and does not depend on `_server`, so calling it with the server stopped is already safe. Confirm by reading lines 219-235 — there must be no `_server` dereference. If there is, hoist that part out.

- [ ] **Step 3: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs
git commit -m "feat: allow addon output to reach the Veneer log with the server stopped"
```

---

## Task 8: `AddonLauncher` — exe mode

**Files:**
- Create: `FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs`

- [ ] **Step 1: Write the launcher, exe path only**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonLauncher
    {
        public static void Launch(VeneerAddon addon, AddonContext context, IAddonLog log)
        {
            var env = AddonEnvironment.BuildEffective(context, addon.env);

            if (addon.script != null && addon.script.Length > 0)
            {
                LaunchScript(addon, context, env, log);
                return;
            }

            LaunchExe(addon, context, env, log);
        }

        private static string ResolveWorkingDirectory(VeneerAddon addon, AddonContext context,
                                                      IDictionary<string, string> env)
        {
            if (string.IsNullOrEmpty(addon.workingDirectory))
                return context.ProjectDirectory;

            var expanded = AddonEnvironment.Expand(addon.workingDirectory, env);
            return Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(context.ProjectDirectory, expanded);
        }

        private static void LaunchExe(VeneerAddon addon, AddonContext context,
                                      IDictionary<string, string> env, IAddonLog log)
        {
            var path = AddonEnvironment.Expand(addon.path, env);
            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(context.ProjectDirectory, path);
            var args = (addon.args ?? new string[0])
                .Select(a => AddonEnvironment.Expand(a, env))
                .ToList();

            string fileName, arguments;
            AddonCommandLine.Compose(fullPath, args, out fileName, out arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ResolveWorkingDirectory(addon, context, env)
            };
            ApplyEnvironment(startInfo, env);

            // filter is null: exe mode has no banner and no @echo off sentinel, so
            // applying the script-mode rules would buffer the whole stream.
            // feedStdin is null: nothing is written to an exe's stdin.
            Run(startInfo, addon, log, null, null);
        }

        private static void ApplyEnvironment(ProcessStartInfo startInfo,
                                             IDictionary<string, string> env)
        {
            foreach (var kv in env)
                startInfo.Environment[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Starts the process, pumps its output, and reports completion. In exe mode
        /// filter is null and output passes through unchanged -- the script-mode
        /// filter rules assume a banner and an @echo off sentinel that an exe launch
        /// never produces, and applying them would buffer a long-running addon's
        /// entire output.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT use the Exited event. Exited can fire before the
        /// async output pump has delivered its final lines, which would drop
        /// trailing output, run filter.Flush() before the last Accept(), and read
        /// filter.CurrentStep on one thread while a threadpool thread writes it --
        /// so "failed at line N" could name the wrong line. The parameterless
        /// WaitForExit() also waits for the output handlers to finish, which is
        /// exactly the guarantee needed here.
        ///
        /// feedStdin runs before the completion watcher starts, so a fast-exiting
        /// process cannot be disposed while script lines are still being written.
        /// Both output pumps are already running by then, so the child can never
        /// block on a full stdout buffer while we are blocked writing its stdin.
        ///
        /// The one case that does stall: a script line running something that itself
        /// consumes stdin (the footgun the spec documents). cmd stops draining, so
        /// the write blocks once the ~4KB stdin buffer fills -- and since this runs
        /// on the WinForms Click thread, that freezes Source's UI. If that command
        /// then waits for input that never arrives, cmd never exits either, so
        /// WaitForExit (no timeout) blocks a threadpool thread indefinitely and the
        /// addon never reports. Moving Launch off the UI thread would fix the first
        /// half; neither is addressed here.
        /// </remarks>
        private static void Run(ProcessStartInfo startInfo, VeneerAddon addon,
                                IAddonLog log, ScriptOutputFilter filter,
                                Action<StreamWriter> feedStdin)
        {
            var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                if (filter == null) log.Write(e.Data, AddonLogLevel.Debug);
                else foreach (var line in filter.Accept(e.Data))
                    log.Write(line, AddonLogLevel.Debug);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) log.Write(e.Data, AddonLogLevel.Warning);
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not start: {1}", addon.name, ex.Message),
                          AddonLogLevel.Error);
                process.Dispose();
                return;
            }

            // Must be guarded. If cmd exits before every line is written -- a script
            // whose first line calls exit, or cmd being killed by EDR -- WriteLine or
            // the flush inside Dispose throws IOException ("pipe has been ended").
            // Launch runs on the WinForms Click handler, so an unguarded throw would
            // surface as an unhandled-exception dialog in Source AND skip the watcher
            // below, leaking the process. Report it and still let the watcher run.
            if (feedStdin != null)
            {
                try
                {
                    using (var stdin = process.StandardInput)
                        feedStdin(stdin);
                }
                catch (Exception ex)
                {
                    // IOException is the only expected case (broken pipe), but this
                    // catches broadly for symmetry with the Start() guard above --
                    // anything escaping here becomes a dialog in Source.
                    log.Write(string.Format("Addon '{0}' script was cut short: {1}",
                                            addon.name, ex.Message),
                              AddonLogLevel.Error);
                }
            }

            Task.Run(() =>
            {
                process.WaitForExit();

                if (filter != null)
                    foreach (var line in filter.Flush())
                        log.Write(line, AddonLogLevel.Debug);

                if (process.ExitCode != 0)
                {
                    var where = filter != null && filter.CurrentStep > 0
                        ? string.Format(" at line {0}", filter.CurrentStep)
                        : string.Empty;
                    log.Write(string.Format("Addon '{0}' failed{1} with exit code {2}",
                                            addon.name, where, process.ExitCode),
                              AddonLogLevel.Error);
                }

                process.Dispose();
            });
        }
    }
}
```

- [ ] **Step 2: Add the script branch stub so it compiles**

```csharp
        private static void LaunchScript(VeneerAddon addon, AddonContext context,
                                         IDictionary<string, string> env, IAddonLog log)
        {
            throw new NotImplementedException("Task 9");
        }
```

- [ ] **Step 3: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0. Note `TreatWarningsAsErrors` is true in Debug, so an unused variable will fail the build.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs
git commit -m "feat: add AddonLauncher with exe-mode launching and output capture"
```

---

## Task 9: `AddonLauncher` — script mode

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs`

- [ ] **Step 1: Replace the stub**

```csharp
        private static void LaunchScript(VeneerAddon addon, AddonContext context,
                                         IDictionary<string, string> env, IAddonLog log)
        {
            var nonce = Guid.NewGuid().ToString();   // "D" -- no regex metacharacters
            var filter = new ScriptOutputFilter(nonce);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/D /V:OFF",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ResolveWorkingDirectory(addon, context, env)
            };
            ApplyEnvironment(startInfo, env);

            Run(startInfo, addon, log, filter, stdin =>
            {
                foreach (var line in AddonScript.Generate(addon.script, nonce))
                    stdin.WriteLine(line);
            });
        }
```

Note the script lines are fed through `Run`'s `feedStdin` callback rather than
after it returns. That ordering is deliberate — `Run` starts the completion
watcher only after `feedStdin` finishes, so a script whose first line fails
immediately cannot have its `Process` disposed while later lines are still being
written.

- [ ] **Step 2: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs
git commit -m "feat: add script-mode addon launching via cmd stdin"
```

---

## Task 10: Wire `VeneerMenu` to the launcher

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs` (the `switch` at former lines 101-107; `LaunchExeAddon` at former 176-198)

- [ ] **Step 1: Replace the dispatch switch**

Handle `"exe"` and `"script"`, and add the `default` case that fixes today's silent dead menu item. Validate first, mirroring the existing scenario-filter disable treatment at former lines 112-116.

```csharp
                        string invalid = VeneerAddon.Validate(addon);
                        if (invalid != null)
                        {
                            item.Enabled = false;
                            item.ToolTipText = "Invalid addon: " + invalid;
                            LogOnce(string.Format("Veneer addon '{0}' {1}", addon.name, invalid));
                        }
                        else
                        {
                            switch (addon.type)
                            {
                                case "exe":
                                case "script":
                                    item.Click += (o, args) => LaunchAddon(addon);
                                    break;
                                default:
                                    item.Enabled = false;
                                    item.ToolTipText = string.Format("Unknown addon type '{0}'", addon.type);
                                    LogOnce(string.Format(
                                        "Veneer addon '{0}' has unknown type '{1}'", addon.name, addon.type));
                                    break;
                            }
                        }
```

- [ ] **Step 2: Add once-only logging**

`VeneerConfiguration.Load` is called from four places (`VeneerMenu` former lines 85, 239, 287 and `ProjectLoadListener.cs:192`) and the config is re-read every time the menu opens, so a malformed entry would log on every drop-down.

```csharp
        private static readonly HashSet<string> _loggedProblems = new HashSet<string>();

        private void LogOnce(string message)
        {
            if (_loggedProblems.Add(message))
                TIME.Management.Log.WriteError(this, message);
        }

        internal void ClearLoggedProblems()
        {
            _loggedProblems.Clear();
        }
```

Call `ClearLoggedProblems()` from the existing `ClearMenu()` so a project change re-reports.

- [ ] **Step 3: Replace `LaunchExeAddon` with `LaunchAddon`**

```csharp
        private void LaunchAddon(VeneerAddon addon)
        {
            if (Control == null)
                WebServerStatusControl.Launch();

            var context = new AddonContext
            {
                ProjectDirectory = Scenario.Project.FileDirectory,
                ProjectFile = Scenario.Project.FullFilename,
                Port = Control.Port
            };

            AddonLauncher.Launch(addon, context, new ControlAddonLog(Control));
        }
```

- [ ] **Step 4: Add the log adapter**

Bridges `IAddonLog` to the control, and sends errors to the Source log as well so they survive the panel closing.

```csharp
        private sealed class ControlAddonLog : IAddonLog
        {
            private readonly WebServerStatusControl _control;

            public ControlAddonLog(WebServerStatusControl control) { _control = control; }

            public void Write(string message, AddonLogLevel level)
            {
                var mapped = level == AddonLogLevel.Error   ? LogLevel.Error
                           : level == AddonLogLevel.Warning ? LogLevel.Warning
                           : LogLevel.Debug;

                _control.LogAddonMessage(message, mapped);

                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
            }
        }
```

- [ ] **Step 5: Add the missing import**

`VeneerMenu.cs` does **not** currently import `DomainActions` — it has `Addons`,
`WebServer`, `WebServerPanel` and the `RiverSystem*` namespaces. Steps 3 and 4
introduce `AddonContext`, `AddonLauncher`, `IAddonLog` and `AddonLogLevel`, so
without this the build fails with four `CS0246`s.

```csharp
using FlowMatters.Source.Veneer.DomainActions;
```

Also add `using System.Collections.Generic;` if absent, for the `HashSet<string>`
in Step 2.

- [ ] **Step 6: Build**

Run: `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly`
Expected: exit 0. `Scenario.Project.FullFilename` and `Scenario.Project.FileDirectory` both exist — see `ExchangeObjects/VeneerStatus.cs:24-26`, which uses `s.Project.FullFilename` on a `RiverSystemScenario`.

Note Step 1 inserts the validation and dispatch block *before* the existing
scenario-filter block (current lines 109-117). An addon that is both invalid and
scenario-filtered will have its tooltip overwritten by the filter message. This
is benign — the filter block only ever disables — and is called out here so it is
not mistaken for a bug.

- [ ] **Step 7: Run the whole test suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` with no filter
Expected: all pass, including the two pre-existing Schematic fixtures.

- [ ] **Step 8: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "feat: dispatch exe and script addons, disable invalid and unknown types"
```

---

## Task 11: Sample `.veneer` files and documentation

**Files:**
- Create: `Samples/addons/exe-with-args.rsproj.veneer`
- Create: `Samples/addons/inline-script.rsproj.veneer`
- Modify: `CLAUDE.md` (Addons bullet)

- [ ] **Step 1: Write the samples**

```json
{
  "addons": [
    {
      "name": "Calibrate",
      "type": "exe",
      "path": "tools/calib.exe",
      "args": ["--port", "%VENEER_PORT%", "--out", "results 2026"],
      "env": { "RUN_LABEL": "nightly" },
      "workingDirectory": "tools",
      "menu": "Veneer|Calibration"
    }
  ]
}
```

```json
{
  "addons": [
    {
      "name": "Nightly run",
      "type": "script",
      "env": { "RUN_LABEL": "nightly" },
      "script": [
        "cd %VENEER_PROJECT_DIR%",
        "python calibrate.py --run %RUN_LABEL%",
        "python report.py"
      ],
      "menu": "Veneer|Calibration"
    }
  ]
}
```

- [ ] **Step 2: Document the limitations for addon authors**

Add a short section to the samples directory or `CLAUDE.md` covering the three stdin-mode limitations from the spec: a command that reads stdin eats the rest of the script; `goto` fails silently with no diagnostic; `for` needs single `%`. State the escape hatch (ship a `.bat`, use `type: "exe"`) and that it is itself shell-dependent.

- [ ] **Step 3: Commit**

```bash
git add Samples/addons CLAUDE.md
git commit -m "docs: add addon launch mode samples and author-facing limitations"
```

---

## Task 12: End-to-end verification

Unit tests cannot prove any of this actually runs. Do these by hand.

- [ ] **Step 1: Multi-version build**

Run: `build.bat`
Expected: all versions in `Veneer.sln.include` build successfully.

- [ ] **Step 2: Verify in Source**

Load a project with a `.veneer` file using the samples from Task 11. Confirm, for `type: "exe"` with `args`: no console window appears, output lands in the Veneer log at Debug (lower the Log Level combo to see it), and `%VENEER_PORT%` arrives expanded.

- [ ] **Step 3: Verify script mode**

Confirm the transcript appears in the Veneer log, that `set`/`cd` persist across lines, and that no banner or `C:\...>` prompt noise leaks through.

- [ ] **Step 4: Verify stop-on-first-failure and attribution**

Make the middle line of a three-line script exit non-zero. Confirm the third line does not run, an Error appears naming the failing line number, and the error is also in Source's log (close the Veneer panel and check).

- [ ] **Step 5: Verify the spaced-path bug is fixed**

Point an `exe` addon at a `.bat` under a directory containing a space. Confirm it runs — this is broken on `master` today.

- [ ] **Step 6: Verify with the server stopped**

Stop the Veneer server, then launch an addon. Confirm output still reaches the log (this is what Task 7 exists for).

---

## Task 13: Port the feature to `legacy_ci`

**Files:** the legacy worktree at `C:\src\projects\Veneer-legacy`

- [ ] **Step 1: Read the porting guide**

Read `branch-porting-guide.md` fully before starting.

- [ ] **Step 2: Port**

The four pure-logic files should port **verbatim** — they use nothing newer than .NET Framework 4.8. That was the reason for hand-rolling `QuoteArgument` instead of using `ProcessStartInfo.ArgumentList`, which does not exist there. `AddonLauncher`, `VeneerMenu` and `WebServerStatusControl` need the usual adaptation.

Confirm the C# language version accepts the code — the legacy branch is C# 7.3, so no target-typed `new`, no switch expressions, no `using` declarations.

- [ ] **Step 3: Port the tests**

Check whether the legacy project has NUnit available. If not, note it and skip rather than adding new packaging to that branch.

- [ ] **Step 4: Build and commit**

```bash
git -C C:/src/projects/Veneer-legacy add -A
git -C C:/src/projects/Veneer-legacy commit -m "feat: addon launch modes (exe+args, inline script, env vars) -- port to legacy WCF"
```

- [ ] **Step 5: Final multi-version build**

Run: `build.bat`
Expected: both branch groups build successfully across all versions.

---

## Notes

- **No `PROTOCOL_VERSION` bump and no `docs/api/` change.** Verified: `VeneerAddon` is referenced only by `ProjectLoadListener`, `VeneerMenu` and its own definition — no REST endpoint exposes it. The two standing rules about bumping `VeneerStatus.PROTOCOL_VERSION` and keeping `docs/api/` in sync do not apply here.
- **`TreatWarningsAsErrors` is true in Debug.** Unused *variables* will fail the build. Unused *usings* will not — `CS8019` is IDE-only unless `EnforceCodeStyleInBuild` is set, which it is not; `ReportingMenu.cs` currently carries several unused imports and builds fine.
- **Three spec Testing rows have no unit test here, deliberately.** `exe`-mode output passing through unfiltered, `/D /V:OFF` on the script-mode invocation, and once-only validation logging all live inside `AddonLauncher`/`VeneerMenu`, which own a real `Process` and a WPF control and are not unit-testable without introducing seams this feature does not justify. Task 12 covers the first two by hand. This gap is intentional, not an oversight.
- **`%` cannot be protected by quoting.** Do not "fix" this by adding `%` to the metacharacter set; see the spec section on why the design is already safe.
- **A stdin-consuming script line can stall two things, not one.** Documented in `Run`'s `<remarks>`: the write blocks on a full stdin buffer (freezing the UI thread), and if the command waits for input that never comes, `WaitForExit()` has no timeout so the watcher blocks a threadpool thread and the addon never reports. Note the threshold is lower than it looks — `AddonScript.Generate` emits three lines per user line plus scaffolding, so the generated byte count is roughly 2-3× the user's script.
- **Pre-existing, but newly more reachable:** `log.Write` from the watcher thread reaches `LogBox.AppendText` via `_originalContext.Post`, which would throw if the panel has been disposed by the time a long-running addon finishes. The existing server-log path has the same exposure after `StopServer`, so this is not introduced here — but addons can now outlive the panel by minutes, so it is worth knowing if a stray exception appears after closing the Veneer window.
