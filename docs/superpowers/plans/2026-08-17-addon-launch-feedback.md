# Addon Launch Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make clicking a `script` or `exe` addon visibly acknowledge itself, surface failures in Source, and disable the menu item while an instance runs — unless the addon opts into concurrency with a new `allowMultiple` field.

**Architecture:** Three new pure types do the thinking — `RunningAddons` (per-addon counts keyed on the *normalised* menu path), `AddonMenuItemState` (the whole menu-item policy as one function), and `OneShotLifecycle` (an `Interlocked`-guarded `IAddonLifecycle`). `AddonLauncher` gains a lifecycle parameter threaded down to `Run`'s completion watcher, which fires it in a `finally`. `VeneerMenu.LaunchAddon` owns the one-shot so its own catch cannot double-fire, marks running before launching, and raises the Veneer panel unconditionally through a guarded wrapper. `AddonLogLevel` gains `Info` so lifecycle lines clear the panel's default filter.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`) on `master`; .NET Framework 4.8 / C# 7.3 on `legacy_ci`. WinForms menu-bar integration, WPF log panel. NUnit 4.1.0 on `master`, down to 2.6.4 on `legacy_ci`.

**Spec:** [`docs/superpowers/specs/2026-08-17-addon-launch-feedback-design.md`](../specs/2026-08-17-addon-launch-feedback-design.md)

---

## Prerequisites

### Build and test command

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

**Baseline verified at commit `9e21629` (`master`), immediately before writing this plan: `Passed: 134, Failed: 0`.** Scope with `--filter "FullyQualifiedName~RunningAddonsTests"` while iterating. `MSB3245`/`MSB3277` reference warnings are pre-existing noise.

### If the build fails with `CS0234` on `RiverSystem.Forms` / `TIME.UI`

This **already happened once while writing this plan** and is the normal state after anyone runs `build.bat`: `compile_all.py` clears and restages `..\Output` per Source version, and the legacy (net48) group runs last, leaving `..\Output` missing `RiverSystem.Forms.dll`, `RiverSystem.Forms.Core.dll`, `RiverSystem.Controls.UI.dll`, `TIME.UI.dll` and `TIME.Winforms.UI.dll`.

Restage a Source 6.x reference set (verified working — this is what produced the 134 baseline):

```powershell
$s='C:\Geospatial\Source\Source_6.10.0.14373'; $o='C:\src\projects\Output'
Get-ChildItem $s -File | Where-Object { $_.Extension -in '.dll','.exe' } |
  ForEach-Object { Copy-Item $_.FullName (Join-Path $o $_.Name) -Force }
New-Item -ItemType Directory -Force (Join-Path $o 'Plugins') | Out-Null
Get-ChildItem "$s\Plugins" -File | Where-Object { $_.Extension -in '.dll','.exe' } |
  ForEach-Object { Copy-Item $_.FullName (Join-Path $o "Plugins\$($_.Name)") -Force }
```

Full recovery procedure: Task 0 of `docs/superpowers/plans/2026-07-30-addon-launch-modes.md`.

### `TreatWarningsAsErrors` is true in Debug

`NoWarn` covers only `1591` and `1587`. Do not leave unused locals or unused `catch (Exception ex)` variables behind — use a bare `catch { }` when the exception is deliberately ignored. An unused `using` is **not** a compiler warning.

### C# 7.3 only

Task 10 ports to `legacy_ci` (.NET Framework 4.8, C# 7.3). **Do not use** target-typed `new()`, switch expressions, index-from-end, or ranges. Object initialisers, `?.`, `??`, `$"…"`, `out var` and expression-bodied members are all fine.

### ASCII only in string literals

The spec's prose uses en/em dashes in tooltips. **Use ASCII hyphens in the code** — these literals must compile identically on both branches, and `legacy_ci` builds under a different toolchain. Tests assert on substrings via `AddonAssert.Contains`, so wording stays flexible.

### NUnit portability

Use `Assert.That(x, Is.EqualTo(y))` / `Is.True` / `Is.False` and the existing `AddonAssert.Contains(actual, expected, because)` from `Tests/AddonAssert.cs`. `Does.Contain` and `StringAssert` are **not** portable across the NUnit versions `legacy_ci` builds against.

### The dirty working tree

`master` has ~20 untracked files unrelated to this work (`BUILD.md`, `CLAUDE.md`, `specs/`, `*.lscache`, …). **Stage named files only — never `git add .` or `git add <dir>`.**

---

## File Structure

| File | Responsibility |
|---|---|
| `Addons/RunningAddons.cs` | **New.** Per-addon running counts, keyed on normalised menu path + name. |
| `Addons/AddonMenuItemState.cs` | **New.** The whole menu-item text/enabled/tooltip policy, pure. |
| `DomainActions/OneShotLifecycle.cs` | **New.** `IAddonLifecycle` that fires at most once. |
| `DomainActions/AddonContext.cs` | `AddonLogLevel.Info`; `IAddonLifecycle`. |
| `DomainActions/AddonLauncher.cs` | Lifecycle threaded to the watcher; watcher `try/catch/finally`; the finished line. |
| `VeneerMenu.cs` | `LaunchAddon` owns the one-shot; guarded panel raise; `PopulateReportMenu` applies the state; log-level mapping. |
| `WebServerStatusControl.xaml.cs` | Private `Append(msg, level, forceScroll)`. |
| `Tests/RunningAddonsTests.cs`, `Tests/AddonMenuItemStateTests.cs`, `Tests/OneShotLifecycleTests.cs` | **New.** |
| `Tests/AddonLauncherIntegrationTests.cs` | `FakeLog` records levels; `FakeLifecycle`; five call sites updated; lifecycle tests. |
| `docs/veneer-file-format.md`, `Samples/addons/README.md` | Documentation. |

---

## Task 0: Record the baseline

**Files:** none modified.

- [ ] **Step 1: Confirm a green build and record the count**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed! - Failed: 0, Passed: 134`. If it fails with `CS0234`, run the restage snippet under **Prerequisites** and retry. If it fails any other way, **stop and report** — do not start implementing on a red baseline.

- [ ] **Step 2: Confirm the anchors this plan edits still read as expected**

These line numbers are quoted throughout. Confirm before relying on them:

```bash
sed -n '15,25p' FlowMatters.Source.Veneer/DomainActions/AddonContext.cs   # AddonLogLevel, IAddonLog
sed -n '206,216p' FlowMatters.Source.Veneer/VeneerMenu.cs                 # LaunchAddon
sed -n '94,138p'  FlowMatters.Source.Veneer/VeneerMenu.cs                 # the item-state block
sed -n '286,318p' FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs # the watcher
sed -n '219,249p' FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs # ServerLogEvent, LogAddonMessage
```

If any has drifted, locate by signature rather than by line and note the drift in the task commit.

---

## Task 1: `RunningAddons`

The counts. Pure, so it goes first and everything else can assume it.

**Files:**
- Create: `FlowMatters.Source.Veneer/Addons/RunningAddons.cs`
- Test: `FlowMatters.Source.Veneer/Tests/RunningAddonsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/RunningAddonsTests.cs`. The key-normalisation cases are the load-bearing ones — they are why this is a class and not a `HashSet<string>` inline in `VeneerMenu`.

```csharp
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class RunningAddonsTests
    {
        private static VeneerAddon Addon(string menu, string name)
        {
            return new VeneerAddon { name = name, type = "script", menu = menu };
        }

        // The six spellings below all render in the SAME menu (MenuLayout.SplitMenuPath
        // maps null/whitespace to "Reporting", trims segments and drops empties). Keying
        // on the raw string would give five independent counts for one menu item, and the
        // double launch this class exists to prevent would come straight back.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Reporting")]
        [TestCase(" Reporting ")]
        [TestCase("Reporting|")]
        public void MenuSpellingsThatRenderTogetherShareOneCount(string menu)
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Reporting", "Tool"));

            Assert.That(running.RunningCount(Addon(menu, "Tool")), Is.EqualTo(1));
        }

        [Test]
        public void DifferentMenusDoNotCollide()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Models", "Tool"));

            Assert.That(running.RunningCount(Addon("Reporting", "Tool")), Is.EqualTo(0));
        }

        [Test]
        public void NestedMenuPathsAreDistinguished()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Models|Calibration", "Tool"));

            Assert.That(running.RunningCount(Addon("Models", "Tool")), Is.EqualTo(0));
            Assert.That(running.RunningCount(Addon("Models|Calibration", "Tool")), Is.EqualTo(1));
        }

        [Test]
        public void DifferentNamesDoNotCollide()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Reporting", "Tool"));

            Assert.That(running.RunningCount(Addon("Reporting", "Other")), Is.EqualTo(0));
        }

        [Test]
        public void UnknownAddonReportsZero()
        {
            Assert.That(new RunningAddons().RunningCount(Addon("Reporting", "Tool")), Is.EqualTo(0));
        }

        [Test]
        public void CountsAccumulateAndDrainToZero()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            running.MarkRunning(addon);
            running.MarkRunning(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(2));

            running.Finished(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(1));

            running.Finished(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        // Finished() is called from a threadpool watcher and must never throw there --
        // an escaping exception becomes an unobserved task exception, silently swallowed.
        [Test]
        public void FinishedBelowZeroIsANoOpNotAnException()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Assert.DoesNotThrow(() => running.Finished(addon));
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));

            running.MarkRunning(addon);
            running.Finished(addon);
            Assert.DoesNotThrow(() => running.Finished(addon));
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        [Test]
        public void ConcurrentMarkAndFinishBalanceOut()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Parallel.For(0, 500, i =>
            {
                running.MarkRunning(addon);
                running.Finished(addon);
            });

            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        [Test]
        public void ConcurrentMarksAreAllCounted()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Parallel.For(0, 500, i => running.MarkRunning(addon));

            Assert.That(running.RunningCount(addon), Is.EqualTo(500));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~RunningAddonsTests" --nologo`

Expected: **build error** `CS0246: The type or namespace name 'RunningAddons' could not be found`. That is the correct red for a type that does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Addons/RunningAddons.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How many instances of each addon are currently running, so the menu can
    /// disable an item while its process is alive.
    ///
    /// A count rather than a set: `allowMultiple` permits concurrent instances, and
    /// with a set the first one exiting would clear the state while the others were
    /// still running -- the label would lie. The count also feeds the label directly.
    ///
    /// Not "pure" (it holds mutable state), but free of WinForms and RiverSystem
    /// types, so it is unit-testable without a loaded scenario.
    ///
    /// Deliberately does NOT implement IAddonLifecycle: that interface is internal,
    /// this type is public like its neighbours in Addons/, and routing every
    /// decrement through OneShotLifecycle is what makes double-fire impossible.
    /// </summary>
    public class RunningAddons
    {
        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// The NORMALISED menu path plus the name. Keying on addon.menu raw would be
        /// wrong: SplitMenuPath maps null/whitespace to "Reporting", trims segments and
        /// drops empties, so null, "", "   ", "Reporting", " Reporting " and "Reporting|"
        /// all render one menu item while producing six different raw keys. "Same key"
        /// must mean "same rendered location".
        /// </summary>
        public static string Key(VeneerAddon addon)
        {
            var menu = string.Join("|", MenuLayout.SplitMenuPath(addon == null ? null : addon.menu));
            return menu + "\0" + (addon == null ? null : addon.name);
        }

        public void MarkRunning(VeneerAddon addon)
        {
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                _counts.TryGetValue(key, out n);
                _counts[key] = n + 1;
            }
        }

        /// <summary>
        /// Floored at zero rather than throwing. Called from AddonLauncher's threadpool
        /// watcher, where an escaping exception would be swallowed as an unobserved task
        /// exception and the count would strand.
        /// </summary>
        public void Finished(VeneerAddon addon)
        {
            var key = Key(addon);
            lock (_counts)
            {
                int n;
                if (!_counts.TryGetValue(key, out n) || n <= 0) return;
                if (n == 1) _counts.Remove(key);
                else _counts[key] = n - 1;
            }
        }

        public int RunningCount(VeneerAddon addon)
        {
            lock (_counts)
            {
                int n;
                _counts.TryGetValue(Key(addon), out n);
                return n;
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~RunningAddonsTests" --nologo`

Expected: `Passed: 14, Failed: 0` — eight `[Test]` methods plus six from the one `[TestCase]` set.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 155, Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/RunningAddons.cs FlowMatters.Source.Veneer/Tests/RunningAddonsTests.cs
git commit -m "feat: track running addon instances, keyed on the normalised menu path"
```

---

## Task 2: `allowMultiple` and `AddonMenuItemState`

The menu-item policy. `allowMultiple` lands here because this is its first consumer.

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs:99` (after `url`)
- Create: `FlowMatters.Source.Veneer/Addons/AddonMenuItemState.cs`
- Test: `FlowMatters.Source.Veneer/Tests/AddonMenuItemStateTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/AddonMenuItemStateTests.cs`:

```csharp
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonMenuItemStateTests
    {
        private static VeneerAddon Addon(string type = "script", bool allowMultiple = false)
        {
            return new VeneerAddon
            {
                name = "Example Tool", type = type, script = new[] { "echo hi" },
                allowMultiple = allowMultiple
            };
        }

        private static AddonMenuItemState State(VeneerAddon addon, string invalid = null,
                                                bool applies = true, string filter = null,
                                                int running = 0)
        {
            return AddonMenuItemState.For(addon, invalid, applies, filter, running);
        }

        [Test]
        public void NotRunningAndValidIsPlainAndEnabled()
        {
            var s = State(Addon());
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
            Assert.That(s.Enabled, Is.True);
            Assert.That(s.ToolTipText, Is.Null);
        }

        [Test]
        public void InvalidIsDisabledWithTheReason()
        {
            var s = State(Addon(), invalid: "has no 'script' lines");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
        }

        [Test]
        public void UnknownTypeIsDisabledAndNamesTheType()
        {
            var s = State(Addon(type: "wibble"));
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "wibble");
        }

        // The dispatch switch in VeneerMenu.PopulateReportMenu is case-SENSITIVE while
        // VeneerAddon.Validate is not. If this function disagreed, a "URL" addon could be
        // rendered enabled while the switch attached no Click handler -- a menu item that
        // silently does nothing, which is the defect that switch's default arm was added
        // to fix. Same ordinal comparison, deliberately.
        [TestCase("exe")]
        [TestCase("script")]
        [TestCase("url")]
        public void KnownTypesAreEnabled(string type)
        {
            Assert.That(State(Addon(type: type)).Enabled, Is.True);
        }

        [TestCase("Script")]
        [TestCase("EXE")]
        [TestCase("Url")]
        public void TypeMatchingIsCaseSensitiveLikeTheDispatchSwitch(string type)
        {
            Assert.That(State(Addon(type: type)).Enabled, Is.False);
        }

        [Test]
        public void ScenarioFilteredIsDisabledAndNamesTheFilter()
        {
            var s = State(Addon(), applies: false, filter: "Ops");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "Ops");
        }

        // The filter comes from VeneerConfiguration.EffectiveFilter, which falls back to
        // config.targetScenario when the addon carries no `scenario` -- so it must be
        // passed in. This test fails if someone "simplifies" the signature to read
        // addon.scenario directly.
        [Test]
        public void TheFilterComesFromTheParameterNotTheAddon()
        {
            var addon = Addon();
            addon.scenario = null;
            var s = State(addon, applies: false, filter: "FromTargetScenario");
            AddonAssert.Contains(s.ToolTipText, "FromTargetScenario");
        }

        // Regression for the defect documented at VeneerMenu.cs:126-128, where the
        // scenario-filter block overwrote an invalid addon's tooltip. Precedence is
        // top to bottom and invalid wins.
        [Test]
        public void InvalidBeatsScenarioFilterForTheTooltip()
        {
            var s = State(Addon(), invalid: "has no 'script' lines", applies: false, filter: "Ops");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
            Assert.That(s.ToolTipText.Contains("Ops"), Is.False,
                        "the scenario filter overwrote the invalid reason");
        }

        [Test]
        public void InvalidBeatsRunning()
        {
            var s = State(Addon(), invalid: "has no 'script' lines", running: 1);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
        }

        [Test]
        public void ScenarioFilterBeatsRunning()
        {
            var s = State(Addon(), applies: false, filter: "Ops", running: 1);
            AddonAssert.Contains(s.ToolTipText, "Ops");
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
        }

        [Test]
        public void RunningSingleInstanceIsLabelledAndDisabled()
        {
            var s = State(Addon(), running: 1);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "Already running");
        }

        // Reachable by flipping allowMultiple true->false in the .veneer between
        // launches: VeneerConfiguration.Load re-reads on every dropdown open. The label
        // stays "(running)" rather than exposing a count the addon has declared it does
        // not support.
        [Test]
        public void RunningManyWithoutAllowMultipleStaysDisabledAndUncounted()
        {
            var s = State(Addon(), running: 3);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.False);
        }

        [Test]
        public void AllowMultipleStaysEnabledWhileRunning()
        {
            var s = State(Addon(allowMultiple: true), running: 1);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.True);
            AddonAssert.Contains(s.ToolTipText, "1 instance already running");
        }

        [Test]
        public void AllowMultipleShowsTheCountAboveOne()
        {
            var s = State(Addon(allowMultiple: true), running: 3);
            Assert.That(s.Text, Is.EqualTo("Example Tool (3 running)"));
            Assert.That(s.Enabled, Is.True);
            AddonAssert.Contains(s.ToolTipText, "3 instances already running");
        }

        [Test]
        public void AllowMultipleNotRunningIsPlain()
        {
            var s = State(Addon(allowMultiple: true));
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
            Assert.That(s.Enabled, Is.True);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~AddonMenuItemStateTests" --nologo`

Expected: build errors — `CS0246` for `AddonMenuItemState` and `CS0117`/`CS1061` for `allowMultiple`.

- [ ] **Step 3: Add the `allowMultiple` field**

In `Addons/VeneerConfiguration.cs`, immediately after the `url` property (`:99`):

```csharp
        /// <summary>
        /// Permit more than one instance of this addon to run at once. Default false:
        /// the restrictive case is the default, so an addon that genuinely supports
        /// concurrency declares it rather than inheriting it by accident, and every
        /// .veneer file already deployed keeps single-instance behaviour untouched.
        /// Meaningless for type "url", which launches no process; harmless there.
        /// </summary>
        public bool allowMultiple { get; set; }
```

- [ ] **Step 4: Write the implementation**

Create `Addons/AddonMenuItemState.cs`:

```csharp
namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How one addon's menu item should look: its text, whether it is clickable, and
    /// the tooltip explaining why not.
    ///
    /// Pure, and the SINGLE writer of those three properties. It replaces a sequence in
    /// PopulateReportMenu that assigned them three times in a row, where the
    /// scenario-filter block overwrote an invalid addon's tooltip -- a defect the code
    /// carried as a comment rather than a fix.
    /// </summary>
    public class AddonMenuItemState
    {
        public string Text { get; private set; }
        public bool Enabled { get; private set; }
        public string ToolTipText { get; private set; }

        private AddonMenuItemState(string text, bool enabled, string toolTipText)
        {
            Text = text;
            Enabled = enabled;
            ToolTipText = toolTipText;
        }

        /// <summary>
        /// effectiveFilter is a separate parameter and NOT derivable from the addon:
        /// VeneerConfiguration.EffectiveFilter falls back to config.targetScenario when
        /// the addon carries no `scenario`, and this function has no config.
        /// Precedence is top to bottom.
        /// </summary>
        public static AddonMenuItemState For(VeneerAddon addon, string invalid,
                                             bool appliesToScenario, string effectiveFilter,
                                             int runningCount)
        {
            var name = addon.name;

            if (invalid != null)
                return new AddonMenuItemState(name, false, "Invalid addon: " + invalid);

            if (!IsKnownType(addon.type))
                return new AddonMenuItemState(name, false,
                    "Unknown addon type '" + addon.type + "'");

            if (!appliesToScenario)
                return new AddonMenuItemState(name, false,
                    "Requires scenario '" + effectiveFilter + "' to be active");

            if (runningCount > 0)
            {
                if (!addon.allowMultiple)
                    return new AddonMenuItemState(name + " (running)", false,
                        "Already running - close the app to launch it again.");

                if (runningCount == 1)
                    return new AddonMenuItemState(name + " (running)", true,
                        "1 instance already running - launching again will start another.");

                return new AddonMenuItemState(
                    name + " (" + runningCount + " running)", true,
                    runningCount + " instances already running - launching again will start another.");
            }

            // null rather than "": both render no tooltip, and null is what an item that
            // was never assigned one carries.
            return new AddonMenuItemState(name, true, null);
        }

        /// <summary>
        /// Ordinal, matching the case-SENSITIVE dispatch switch in
        /// VeneerMenu.PopulateReportMenu. VeneerAddon.Validate is case-insensitive, so
        /// the two can disagree about e.g. "URL" -- if this function were the lenient
        /// one, such an addon would render enabled with no Click handler attached.
        /// </summary>
        private static bool IsKnownType(string type)
        {
            return type == "exe" || type == "script" || type == "url";
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~AddonMenuItemStateTests" --nologo`

Expected: `Passed: 22, Failed: 0` — sixteen `[Test]` methods plus six from the two `[TestCase]` sets. (Thirteen as first written; code review added three pinning the unknown-type branch, which no test could distinguish.)

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 177, Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/AddonMenuItemState.cs FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs FlowMatters.Source.Veneer/Tests/AddonMenuItemStateTests.cs
git commit -m "feat: add allowMultiple and the addon menu-item state policy"
```

---

## Task 3: `IAddonLifecycle` and `OneShotLifecycle`

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonContext.cs`
- Create: `FlowMatters.Source.Veneer/DomainActions/OneShotLifecycle.cs`
- Test: `FlowMatters.Source.Veneer/Tests/OneShotLifecycleTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/OneShotLifecycleTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class OneShotLifecycleTests
    {
        private static VeneerAddon Addon()
        {
            return new VeneerAddon { name = "Tool", type = "script" };
        }

        [Test]
        public void FirstFinishedCallsThrough()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => calls++);

            once.Finished(Addon());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void LaterFinishedCallsAreNoOps()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => calls++);

            once.Finished(Addon());
            once.Finished(Addon());
            once.Finished(Addon());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ThePassedAddonReachesTheCallback()
        {
            VeneerAddon seen = null;
            var addon = Addon();

            new OneShotLifecycle(a => seen = a).Finished(addon);

            Assert.That(ReferenceEquals(seen, addon), Is.True);
        }

        // Finished can be raced by the threadpool watcher and the UI thread's catch
        // block. Exactly one must win.
        [Test]
        public void ConcurrentFinishedCallsFireExactlyOnce()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => Interlocked.Increment(ref calls));
            var addon = Addon();

            Parallel.For(0, 200, i => once.Finished(addon));

            Assert.That(calls, Is.EqualTo(1));
        }

        // A null callback is a wiring bug supplied on the UI thread, not a runtime
        // condition. Tolerating it means a mis-wired LaunchAddon never decrements and
        // the menu item stays greyed out until Source restarts, with no diagnostic --
        // exactly the failure this feature exists to prevent. Fail at construction,
        // where the stack still names the caller.
        [Test]
        public void ANullCallbackThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new OneShotLifecycle(null));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~OneShotLifecycleTests" --nologo`

Expected: build error `CS0246: The type or namespace name 'OneShotLifecycle' could not be found`.

- [ ] **Step 3: Add `Info` and `IAddonLifecycle`**

In `DomainActions/AddonContext.cs`, add `using FlowMatters.Source.Veneer.Addons;` at the top, then replace the enum and add the interface:

```csharp
    internal enum AddonLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    internal interface IAddonLog
    {
        void Write(string message, AddonLogLevel level);
    }

    /// <summary>
    /// Reports that an addon launch has finished -- the process exited, or never
    /// started. NOT that Launch() returned: Launch returns as soon as the completion
    /// watcher is queued, and cannot even distinguish a failed Start() from a running
    /// process, so firing on its return would clear the running state immediately on
    /// every successful launch.
    /// </summary>
    internal interface IAddonLifecycle
    {
        void Finished(VeneerAddon addon);
    }
```

`Info` is inserted after `Debug`, shifting the ordinals. Safe: the enum is `internal`, never serialised, and every use is `==` rather than a comparison.

- [ ] **Step 4: Write `OneShotLifecycle`**

Create `DomainActions/OneShotLifecycle.cs`:

```csharp
using System;
using System.Threading;
using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Fires its callback at most once, however many times Finished is called and from
    /// however many threads.
    ///
    /// Owned by VeneerMenu.LaunchAddon and threaded down into AddonLauncher, so that ONE
    /// object spans both layers. That placement is the point: LaunchAddon's own catch
    /// also has to report, and a wrapper created inside Launch would leave it outside the
    /// guard. The escaping path is real -- Run's Start()-failure branch reports, its next
    /// log.Write throws, Launch's catch reports (a no-op), ITS log.Write throws, and the
    /// exception reaches LaunchAddon's catch, which would decrement a second time. At
    /// count 1 the floor in RunningAddons hides that; with two instances live the count
    /// would go 3 -> 2 -> 1 and the label would lie, which is exactly what counting
    /// instead of set-membership was meant to prevent.
    /// </summary>
    internal sealed class OneShotLifecycle : IAddonLifecycle
    {
        private readonly Action<VeneerAddon> _onFinished;
        private int _fired;

        public OneShotLifecycle(Action<VeneerAddon> onFinished)
        {
            _onFinished = onFinished;
        }

        public void Finished(VeneerAddon addon)
        {
            if (Interlocked.Exchange(ref _fired, 1) != 0) return;
            if (_onFinished != null) _onFinished(addon);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~OneShotLifecycleTests" --nologo`

Expected: `Passed: 6, Failed: 0` — five as first written, plus `FinishedIsAtomicUnderContention` added during code review.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 183, Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonContext.cs FlowMatters.Source.Veneer/DomainActions/OneShotLifecycle.cs FlowMatters.Source.Veneer/Tests/OneShotLifecycleTests.cs
git commit -m "feat: add AddonLogLevel.Info and a one-shot addon lifecycle"
```

---

## Task 4: Thread the lifecycle through `AddonLauncher`

The biggest task. `Launch` gains a required parameter, the watcher gains `try/catch/finally`, and the finished line appears.

**Files:**
- Modify: `FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs`
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs:215` — the `_runningAddons` field and the real call-site wiring, pulled forward from Task 6 Step 1 (Step 4e). `Launch` gains a **required** parameter, so the tree does not compile until its only production caller is updated.
- Test: `FlowMatters.Source.Veneer/Tests/AddonLauncherIntegrationTests.cs`

- [ ] **Step 1: Extend the fixture with level recording and a lifecycle stub**

In `Tests/AddonLauncherIntegrationTests.cs`, replace the `FakeLog` class (`:26-56`) with one that keeps levels — the existing `Lines`/`WaitFor`/`Dump` members keep their exact behaviour so no existing test changes:

```csharp
        private sealed class FakeLog : IAddonLog
        {
            private readonly List<KeyValuePair<string, AddonLogLevel>> _entries =
                new List<KeyValuePair<string, AddonLogLevel>>();

            public void Write(string message, AddonLogLevel level)
            {
                lock (_entries) _entries.Add(new KeyValuePair<string, AddonLogLevel>(message, level));
            }

            public string[] Lines
            {
                get { lock (_entries) return _entries.Select(e => e.Key).ToArray(); }
            }

            /// <summary>The level of the first line containing `substring`, or null.</summary>
            public AddonLogLevel? LevelOf(string substring)
            {
                lock (_entries)
                {
                    foreach (var e in _entries)
                        if (e.Key != null && e.Key.Contains(substring)) return e.Value;
                    return null;
                }
            }

            /// <summary>Polls until the predicate holds, so tests need no sleeps.</summary>
            public bool WaitFor(Func<string[], bool> predicate, int timeoutMs = 30000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (predicate(Lines)) return true;
                    System.Threading.Thread.Sleep(25);
                }
                return false;
            }

            public string Dump()
            {
                return string.Join(Environment.NewLine, Lines);
            }
        }

        private sealed class FakeLifecycle : IAddonLifecycle
        {
            private int _count;

            public int Count { get { return System.Threading.Volatile.Read(ref _count); } }

            public void Finished(VeneerAddon addon)
            {
                System.Threading.Interlocked.Increment(ref _count);
            }

            public bool WaitForFinish(int timeoutMs = 30000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (Count > 0) return true;
                    System.Threading.Thread.Sleep(25);
                }
                return false;
            }
        }
```

- [ ] **Step 2: Update the five existing call sites and add the lifecycle tests**

Change each of `AddonLauncherIntegrationTests.cs:103, 129, 162, 180, 193` from
`AddonLauncher.Launch(addon, Context(), log);` to
`AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());`.

Then append these tests to the fixture. The once-and-only-once cases are the load-bearing ones — each terminal path in `Launch`/`Run` gets its own, because that guarantee is what stops a stranded disabled menu item.

```csharp
        [Test]
        public void Lifecycle_FiresOnceOnCleanExit()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            System.Threading.Thread.Sleep(250);   // catch a late second fire
            Assert.That(life.Count, Is.EqualTo(1));
        }

        // The failure mode this whole design turns on: Launch returns as soon as the
        // watcher is queued. If Finished were fired at Launch's return, the count would
        // clear before the child had even run and the menu item would never disable.
        [Test]
        public void Lifecycle_FiresAfterTheProcessExitsNotWhenLaunchReturns()
        {
            WriteBat("slow.bat", "ping -n 3 127.0.0.1 > nul\r\necho done");
            var addon = new VeneerAddon { name = "slow", type = "exe", path = "slow.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.Count, Is.EqualTo(0), "Finished fired before the child exited");
            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
        }
        // ^ The one mildly timing-dependent test here: it leans on `ping -n 3` giving
        //   ~2s of headroom. If it fails, check the log first -- if cmd.exe is blocked in
        //   the environment, Start() throws and Finished fires synchronously, which is a
        //   broken environment rather than a broken guarantee.

        [Test]
        public void Lifecycle_FiresOnceOnNonZeroExit()
        {
            WriteBat("bad.bat", "exit /b 3");
            var addon = new VeneerAddon { name = "bad", type = "exe", path = "bad.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            System.Threading.Thread.Sleep(250);
            Assert.That(life.Count, Is.EqualTo(1));
        }

        // Must be a NON-batch bogus path. AddonCommandLine.Compose routes .bat/.cmd
        // through cmd.exe, which always starts, and script mode always launches cmd.exe --
        // so WriteBat cannot produce a Start() failure.
        [Test]
        public void Lifecycle_FiresOnceWhenStartFails()
        {
            var addon = new VeneerAddon { name = "nope", type = "exe", path = "nosuch.exe" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(5000), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            Assert.That(life.Count, Is.EqualTo(1));
        }

        [Test]
        public void Lifecycle_FiresOnceOnValidationFailure()
        {
            var addon = new VeneerAddon { name = "broken", type = "script" };   // no script lines

            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), new FakeLog(), life);

            Assert.That(life.Count, Is.EqualTo(1));   // synchronous, no wait needed
        }

        [Test]
        public void Lifecycle_FiresOnceWhenThereIsNoProjectDirectory()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };
            var context = new AddonContext { ProjectDirectory = null, Port = 9876 };

            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, context, new FakeLog(), life);

            Assert.That(life.Count, Is.EqualTo(1));
        }

        [Test]
        public void CleanExitLogsAFinishedLineAtInfo()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("finished"))), Is.True,
                        "no finished line. Log was:\n" + log.Dump());
            Assert.That(log.LevelOf("finished"), Is.EqualTo(AddonLogLevel.Info));
        }

        [Test]
        public void ChildOutputStaysAtDebugSoAChattyAddonCannotDrownTheLifecycleLines()
        {
            WriteBat("noisy.bat", "echo CHATTER");
            var addon = new VeneerAddon { name = "noisy", type = "exe", path = "noisy.bat" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("CHATTER"))), Is.True,
                        "no child output. Log was:\n" + log.Dump());
            Assert.That(log.LevelOf("CHATTER"), Is.EqualTo(AddonLogLevel.Debug));
        }

        [Test]
        public void AFailedRunLogsNoFinishedLine()
        {
            WriteBat("bad.bat", "exit /b 3");
            var addon = new VeneerAddon { name = "bad", type = "exe", path = "bad.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            Assert.That(log.Lines.Any(x => x.Contains("finished")), Is.False,
                        "a failure was reported as finished. Log was:\n" + log.Dump());
        }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~AddonLauncherIntegrationTests" --nologo`

Expected: build error `CS1501: No overload for method 'Launch' takes 4 arguments`.

- [ ] **Step 4: Thread the lifecycle through `AddonLauncher`**

In `DomainActions/AddonLauncher.cs`:

**(a)** `Launch` takes the lifecycle and reports on every exit path. Change the signature to
`public static void Launch(VeneerAddon addon, AddonContext context, IAddonLog log, IAddonLifecycle lifecycle)`
and add `lifecycle.Finished(addon);` immediately before the `return;` at `:28` and the `return;` at `:41`, and as the last statement of the `catch` block at `:53-58`. Pass `lifecycle` on to `LaunchScript(...)` and `LaunchExe(...)`.

Add to the method's doc comment:

```csharp
        /// Reports exactly one lifecycle.Finished per call, on every path: addon
        /// validation, missing project directory, this catch, a failed Start(), and the
        /// completion watcher. The caller supplies a OneShotLifecycle, so a path that
        /// reports twice is harmless -- but a path that reports NONE leaves the menu item
        /// disabled until Source restarts, with no error to explain it.
```

Five terminal paths result, and four are covered by the tests above. **`Launch`'s own
`catch` is the uncovered one**: there is no seam to make `AddonEnvironment.BuildEffective`
or `ProcessStartInfo` construction throw from a test, and inventing one is not worth the
coupling. Recorded here so a later reader knows it is a deliberate gap rather than an
oversight. `LaunchScript` and `LaunchExe` have no early `return` — both build a
`ProcessStartInfo` and call `Run` unconditionally — so an exception is the only way out of
that `try` other than `Run`, which is what makes four-of-five acceptable.

**(b)** `LaunchScript` and `LaunchExe` each take `IAddonLifecycle lifecycle` as a new **last** parameter, and pass it to `Run`.

**(c)** `Run` takes `IAddonLifecycle lifecycle` as a new **last** parameter, after `feedStdin`. Its `Start()`-failure branch (`:241-247`) reports before returning:

```csharp
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not start: {1}", addon.name, ex.Message),
                          AddonLogLevel.Error);
                process.Dispose();
                lifecycle.Finished(addon);
                return;
            }
```

Note the order: `Dispose` first, then report. A throwing `log.Write` would otherwise skip the dispose.

**(d)** Replace the watcher body (`:290-318`) with:

```csharp
            Task.Run(() =>
            {
                // MUST stay the parameterless overload. It waits for EOF on both
                // redirected streams as well as process exit, which is the only
                // reason Flush() and CurrentStep can be read here without locking:
                // every Accept() on the pump thread has already returned. In .NET 5+
                // WaitForExit(int) does NOT wait for the output pumps, so adding a
                // timeout here would silently introduce a data race on the filter's
                // state and could report the wrong step number. Use
                // WaitForExitAsync() if a timeout is ever needed -- it preserves the
                // guarantee and frees this threadpool thread.
                try
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
                    else
                    {
                        log.Write(string.Format("Addon '{0}' finished", addon.name),
                                  AddonLogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    // Task.Run has no continuation and nothing awaits it, so anything
                    // escaping here would become an unobserved task exception and be
                    // swallowed -- the count would never decrement and the menu item
                    // would stay disabled until Source restarts, unexplained.
                    // WaitForExit, ExitCode and log.Write can all throw: ControlAddonLog
                    // reaches a SynchronizationContext captured at construction, which
                    // may be null, and Source's own log during shutdown.
                    try
                    {
                        log.Write(string.Format("Addon '{0}' could not be monitored: {1}",
                                                addon.name, ex.Message),
                                  AddonLogLevel.Error);
                    }
                    catch { /* the log is what failed; there is nowhere left to report */ }
                }
                finally
                {
                    // Nested, not sequential. lifecycle.Finished invokes a caller-supplied
                    // Action; if it throws, a plain `Finished(); Dispose();` skips the
                    // Dispose and leaks the handle -- while the exception becomes an
                    // unobserved task exception, which is the exact failure this whole
                    // try/catch/finally exists to prevent. Finished goes first so the count
                    // is released as early as possible; the inner finally guarantees Dispose
                    // runs either way.
                    try { lifecycle.Finished(addon); }
                    finally { process.Dispose(); }
                }
            });
```

**(e) Wire the one production caller so the tree builds.** `VeneerMenu.LaunchAddon` (`VeneerMenu.cs:215`) still calls the three-argument `Launch`, which is now a **certain** `CS7036` on the whole project — not a maybe.

Do this as the *real* wiring one commit early, rather than a placeholder. Add the counts field next to `_createdMenus` (`:47`) — this is Task 6 Step 1, moved forward:

```csharp
        /// <summary>
        /// Live instance counts, an instance field on this singleton. Deliberately NOT
        /// cleared by ClearMenu: the counts track live OS processes, not menu state. A
        /// Dash app survives a project switch, and clearing would re-enable the item
        /// while the process still holds its port.
        /// </summary>
        private readonly RunningAddons _runningAddons = new RunningAddons();
```

and change the call at `:215` to:

```csharp
            AddonLauncher.Launch(addon, BuildAddonContext(), AddonLog(), new OneShotLifecycle(_runningAddons.Finished));
```

This is behaviourally inert at this commit — nothing calls `MarkRunning` until Task 6, and `RunningAddons.Finished` returns cleanly on an absent key — but it leaves no broken intermediate state.

**Do not** pass `new OneShotLifecycle(null)`. Task 3 made that constructor throw `ArgumentNullException`, and `LaunchAddon` is a WinForms `Click` handler with no automated coverage: the suite would stay green through Tasks 4 and 5 while every addon click raised an unhandled-exception dialog in Source.

**Do not** instead give `lifecycle` a `= null` default. The parameter is required precisely so a future call site cannot silently opt out of the guarantee. `OneShotLifecycle` is `internal` in `FlowMatters.Source.Veneer.DomainActions` and `VeneerMenu.cs:11` already has that `using`; `RunningAddons` is in `FlowMatters.Source.Veneer.Addons`, whose `using` is at `:10`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --filter "FullyQualifiedName~AddonLauncherIntegrationTests" --nologo`

Expected: `Passed: 14, Failed: 0` (5 existing + 9 new).

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 192, Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/DomainActions/AddonLauncher.cs FlowMatters.Source.Veneer/Tests/AddonLauncherIntegrationTests.cs FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "feat: report addon completion through a lifecycle callback"
```

---

## Task 5: Force lifecycle lines into view

**Files:**
- Modify: `FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs:219-249`

No test: this is WPF against a live control. Kept to the thinnest possible layer for that reason.

- [ ] **Step 1: Extract `Append` and force-scroll the lifecycle levels**

Replace `ServerLogEvent` and `LogAddonMessage` with:

```csharp
        /// <summary>
        /// Signature is FIXED: this is subscribed as a ServerLogListener delegate in
        /// StartServer, and C# delegate compatibility requires matching arity. A trailing
        /// `bool forceScroll = false` is a CS0123 build break, not a convenience -- the
        /// existing `LogLevel level = LogLevel.Info` default gets away with it only
        /// because the arity still matches.
        /// </summary>
        void ServerLogEvent(object sender, string msg, LogLevel level = LogLevel.Info)
        {
            Append(msg, level, false);
        }

        private void Append(string msg, LogLevel level, bool forceScroll)
        {
            _originalContext.Post(delegate
            {
                if (level < _minimumLogLevel)
                    return;

                var scrollViewer = GetScrollViewer(LogBox);
                bool wasAtBottom = scrollViewer == null ||
                    scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 10;

                LogBox.AppendText(msg + "\n");

                if (forceScroll || wasAtBottom)
                    LogBox.ScrollToEnd();
            }, null);
        }

        /// <summary>
        /// Log entry point for addon output, usable regardless of server state.
        /// The LogBox sink is otherwise only wired up inside StartServer
        /// (server.LogGenerator += ServerLogEvent), but addons can be launched with
        /// the server stopped, so their output needs a path that does not depend on
        /// it. Append marshals to the UI thread via _originalContext -- which is what
        /// OutputDataReceived, raised on a threadpool thread, requires.
        ///
        /// Info and Error are tested explicitly, NOT `level >= LogLevel.Info`. Child
        /// stderr is logged at Warning, and Warning >= Info -- a Dash app writes its whole
        /// startup to stderr, so that predicate would yank the log to the bottom on every
        /// line and destroy the scrollback this is meant to preserve. Across the addon
        /// path Info and Error are used only by Veneer's own lifecycle and failure lines,
        /// while both child streams are Debug (stdout) and Warning (stderr).
        /// </summary>
        internal void LogAddonMessage(string msg, LogLevel level)
        {
            Append(msg, level, level == LogLevel.Info || level == LogLevel.Error);
        }
```

Note the level filter still applies first: an operator who raises the panel's minimum to Warning will not see `Launching '...'`. That is their explicit choice and is left alone.

- [ ] **Step 2: Build**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 192, Failed: 0` — no behaviour change reaches the tests; this step is checking it compiles and nothing regressed.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs
git commit -m "feat: scroll addon lifecycle lines into view"
```

---

## Task 6: Wire up `VeneerMenu`

Where it all becomes visible.

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

No automated test: WinForms and reflection into WeifenLuo docking. Task 9 verifies it by hand.

- [ ] **Step 1: Confirm the counts field is present**

Already added by Task 4 Step 4e, which needed it to wire the call site rather than
leave a placeholder that throws. Verify it is there next to `_createdMenus` (`:47`):

```csharp
        private readonly RunningAddons _runningAddons = new RunningAddons();
```

Nothing calls `MarkRunning` yet — Step 3 below is what makes it live.

- [ ] **Step 2: Replace the item-state block in `PopulateReportMenu`**

Replace everything from `ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);` (`:94`) through the end of the scenario-filter block (`:137`) with:

```csharp
                        ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);

                        string invalid = VeneerAddon.Validate(addon);
                        if (invalid != null)
                            LogOnce($"Veneer addon '{addon.name}' {invalid}");

                        // Dispatch only -- the Enabled/ToolTipText assignments that used to
                        // live in the default arm have moved to AddonMenuItemState, so there
                        // is exactly one writer of the item's appearance.
                        //
                        // The arms below and AddonMenuItemState.IsKnownType are ONE LIST IN
                        // TWO PLACES. A type added here but not there renders disabled, which
                        // is loud. A type added there but not here renders ENABLED with no
                        // Click handler -- a menu item that silently does nothing, which is
                        // the exact defect the default arm was added to fix. A drift test is
                        // not cheap for a switch inside WinForms, so this comment is the guard.
                        if (invalid == null)
                        {
                            switch (addon.type)
                            {
                                case "exe":
                                case "script":
                                    item.Click += (o, args) => LaunchAddon(addon);
                                    break;

                                case "url":
                                    item.Click += (o, args) => LaunchUrlAddon(addon);
                                    break;

                                default:
                                    LogOnce($"Veneer addon '{addon.name}' has unknown type '{addon.type}'");
                                    break;
                            }
                        }

                        var applies = VeneerConfiguration.AddonAppliesTo(addon, config, currentScenario);
                        var filter = VeneerConfiguration.EffectiveFilter(addon, config);

                        if (!applies)
                            TIME.Management.Log.WriteError(
                                this,
                                $"Veneer addon '{addon.name}' disabled: requires scenario '{filter}', current is '{currentScenario?.Name ?? "none"}'");

                        // Running deliberately adds NO log line: it is not a problem, and
                        // this runs on every dropdown open.
                        var state = AddonMenuItemState.For(
                            addon, invalid, applies, filter, _runningAddons.RunningCount(addon));

                        item.Text = state.Text;
                        item.Enabled = state.Enabled;
                        item.ToolTipText = state.ToolTipText;
```

- [ ] **Step 3: Rewrite `LaunchAddon` and add `TryRaisePanel`**

> **`MarkRunning` goes in `LaunchAddon` ONLY — never in `LaunchUrlAddon`.**
> `AddonLauncher.LaunchUrl` takes no lifecycle and has none to give: it opens a
> URL and returns, with no process to wait on. A url addon that marked itself
> running would therefore never be matched by a `Finished`, and its menu item
> would stay disabled until Source restarts — precisely the failure this feature
> exists to prevent. `AddonMenuItemState.For` is still called for url addons, but
> their count is permanently zero, which is correct.

Replace `LaunchAddon` (`:206-216`):

```csharp
        private void LaunchAddon(VeneerAddon addon)
        {
            // The one-shot is constructed HERE, not inside AddonLauncher.Launch, because
            // the catch below is a second place that has to report. One object spanning
            // both layers is what makes a double decrement impossible; see
            // OneShotLifecycle.
            //
            // Outside the try, and safe there despite the constructor throwing on a null
            // callback: _runningAddons is readonly with an inline initialiser, so the
            // method group cannot be null and the ArgumentNullException is unreachable
            // from this call site. Were it reachable, it would escape a Click handler as
            // an unhandled-exception dialog.
            var lifecycle = new OneShotLifecycle(_runningAddons.Finished);

            // FIRST, and outside the try, so the increment and the catch's decrement are
            // trivially balanced. With this inside the try, a throw from TryRaisePanel,
            // AddonLog() or BuildAddonContext() would decrement a count that was never
            // incremented -- masked at zero by the floor in RunningAddons, but with a
            // second instance genuinely live the count would go 2 -> 1 and the label
            // would lie.
            _runningAddons.MarkRunning(addon);

            try
            {
                TryRaisePanel();

                var log = AddonLog();
                log.Write(string.Format("Launching '{0}'...", addon.name), AddonLogLevel.Info);

                AddonLauncher.Launch(addon, BuildAddonContext(), log, lifecycle);
            }
            catch (Exception ex)
            {
                // Launch is documented as never throwing, but that promise rests on the
                // supplied IAddonLog never throwing, which ControlAddonLog does not
                // guarantee. Without this, a throw between MarkRunning and the watcher
                // strands the count and disables the item permanently -- and this is a
                // Click handler, so it would also raise an unhandled-exception dialog.
                lifecycle.Finished(addon);
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer addon '{0}' could not be launched: {1}",
                                        addon.name, ex.Message));
            }
        }

        /// <summary>
        /// Raise the Veneer panel, unconditionally -- the old `if (Control == null)` guard
        /// meant this ran on the FIRST click of a session only. Control is assigned by
        /// WebServerStatusControl.PopulateMenu and never nulled, so once the operator
        /// closes the panel (HideOnClose merely hides it) every later click silently failed
        /// to bring it back, leaving the click with no acknowledgement at all.
        ///
        /// Guarded because Launch() ends in unguarded reflection (GetMethod can return
        /// null, Invoke can throw) inside MainForm.Instance.Invoke, which rethrows on this
        /// thread. At most once per session that was survivable; on every click, at the top
        /// of a Click handler, it is not.
        /// </summary>
        private void TryRaisePanel()
        {
            try
            {
                WebServerStatusControl.Launch();
            }
            catch (Exception ex)
            {
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer could not open the monitoring panel: {0}",
                                        ex.Message));
            }
        }
```

- [ ] **Step 4: Map `Info` in both log sinks**

In `ControlAddonLog.Write` (`:275-285`), add the `Info` arm:

```csharp
                var mapped = level == AddonLogLevel.Error   ? LogLevel.Error
                           : level == AddonLogLevel.Warning ? LogLevel.Warning
                           : level == AddonLogLevel.Info    ? LogLevel.Info
                           : LogLevel.Debug;
```

Replace `SourceAddonLog` (`:288-301`) — including its now-false docstring:

```csharp
        /// <summary>
        /// Used when no Veneer panel is available: the URL path, which deliberately does
        /// not open one, and the process path when TryRaisePanel failed.
        ///
        /// Info is passed through, not dropped. That second case is exactly where feedback
        /// matters most -- if the panel could not be raised, an Error-only sink would
        /// discard 'Launching ...' too, leaving the operator with no panel, no line, and a
        /// disabled menu item.
        /// </summary>
        private sealed class SourceAddonLog : IAddonLog
        {
            public void Write(string message, AddonLogLevel level)
            {
                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
                else if (level == AddonLogLevel.Info)
                    TIME.Management.Log.WriteInfo(this, message);
            }
        }
```

- [ ] **Step 5: Delete the comments this task falsifies**

Tasks 3 and 4 wrote comments scoped to "at this commit", describing a half-wired
state. This is the commit that finishes the wiring, so they become confidently
wrong documentation the moment it lands. Nothing else removes them.

- `DomainActions/OneShotLifecycle.cs` (~`:18-19`) — "Will be owned by ... (Task 6) ...
  at this commit nothing constructs or wires it in". `VeneerMenu.LaunchAddon` now
  constructs it; say what owns it, in the present tense, and drop the task number.
- `DomainActions/AddonContext.cs` (~`:21-31`) — "At this commit `Info` has no consumer
  that maps it correctly -- Task 6 is expected to add one". Step 4 above *is* that
  consumer. Keep the substance (an `Info` line is invisible to an operator whose panel
  threshold is raised — that trap is real and worth documenting) and drop the
  "expected to" framing.
- While there, drop the hardcoded `VeneerMenu.cs:277-279` line reference in that same
  block. Step 2 rewrote `PopulateReportMenu` above it, so the number is already stale.
  Name `ControlAddonLog.Write` instead; it does not drift.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 192, Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs FlowMatters.Source.Veneer/DomainActions/OneShotLifecycle.cs FlowMatters.Source.Veneer/DomainActions/AddonContext.cs
git commit -m "feat: acknowledge addon launches and disable the item while running"
```

---

## Task 7: Documentation

**Files:**
- Modify: `docs/veneer-file-format.md`
- Modify: `Samples/addons/README.md`

- [ ] **Step 1: Add `allowMultiple` to the field table**

In `docs/veneer-file-format.md`, add a row to the addon field table (`:50-61`), after `scenario`, following the existing ordering and `Required` conventions:

```markdown
| `allowMultiple`    | bool             | no             | Permit more than one instance at once. When `false` (the default) the menu item is disabled, and labelled `(running)`, while an instance is running. Ignored for `"url"`. |
```

- [ ] **Step 2: Reconcile the panel sentence**

`:99` currently reads "Opens the Veneer panel if it is closed, because that is where addon output is written." That sentence was **aspirational** — the code did it on the first click of a session only, so an operator who closed the panel never saw it again. It is now true as written.

Keep the sentence and extend it to say the panel is raised on **every** launch, and that Veneer's own `Launching …` / `… finished` lines appear there at `Info` without changing the Log Level. Then check the surrounding paragraph for any remaining description of the first-click-only behaviour.

- [ ] **Step 3: Update the sample README**

In `Samples/addons/README.md`:

At `:131-132`, the claim that addon output goes to the log at `Debug` and the operator must lower the Log Level is now only half true. Amend to say that Veneer's own lifecycle lines (`Launching …`, `… finished`) and failures appear at `Info`/`Error` and are visible by default, while the addon's own stdout remains at `Debug`.

In "Diagnosing a menu item that does nothing" (`:139-151`), add *already running* to the list of causes for a greyed-out item, noting `allowMultiple` as the opt-out.

**Also amend the list preamble at `:141-142`**, which currently promises "the same reason is written to Source's log once". That is false for the new entry, which deliberately logs nothing.

- [ ] **Step 4: Fix the tooltip quoted at `docs/veneer-file-format.md:140`**

That line quotes a live string verbatim:

```
- Hovering shows the tooltip `Requires scenario '<filter>' to be active`.
```

It is correct against `VeneerMenu.cs:133` today, but Task 6 hands that string to `AddonMenuItemState`, where it ends with a period. Update the quote to match.

This was found by grepping the whole tree for all six tooltip strings — it is the **only** site outside the source that quotes one. If you change a tooltip in a later task, repeat that grep rather than assuming.

- [ ] **Step 5: Verify the field table against the code, both directions**

Every property on `VeneerAddon` (`Addons/VeneerConfiguration.cs:79-100`) appears in the table, and every table row names a real property. This check caught a drift in the url-addons work.

- [ ] **Step 6: Commit**

```bash
git add docs/veneer-file-format.md Samples/addons/README.md
git commit -m "docs: document allowMultiple and the addon launch feedback"
```

---

## Task 8: Sample addon

**Files:**
- Modify: `Samples/addons/inline-script.rsproj.veneer`

- [ ] **Step 1: Add an `allowMultiple` entry**

Add a second addon to the sample that sets `"allowMultiple": true`, with a comment-free JSON entry matching the file's existing style, so the field appears in at least one shipped example. Keep it short — a `script` that echoes and exits.

- [ ] **Step 2: Verify it parses**

Run: `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo`

Expected: `Passed: 192, Failed: 0`. (The samples are not parsed by tests; this step only confirms nothing else broke. Validate the JSON with `python -m json.tool Samples/addons/inline-script.rsproj.veneer`.)

- [ ] **Step 3: Commit**

```bash
git add Samples/addons/inline-script.rsproj.veneer
git commit -m "docs: show allowMultiple in the inline-script sample"
```

---

## Task 9: Manual verification

**Files:** none.

**This task cannot be skipped or inferred.** Tasks 5 and 6 have *no* automated coverage — the panel raise, the scroll, and the whole menu-item application are WinForms and reflection against a live Source instance. The behaviour this feature exists for has been reasoned about, not observed. Requires the Source GUI with a project whose `.veneer` declares at least one `script` addon.

- [ ] **Step 1: Confirm the defect first, on the pre-change plugin**

Before installing the new build: click a `script` addon with the Veneer panel **closed**. Expect **nothing visible** — no panel, no line. This is the regression baseline; if the panel appears here, the diagnosis is wrong and the rest of this task means nothing.

- [ ] **Step 2: Panel raise and acknowledgement**

With the new plugin and the Veneer panel closed, click the addon.
Expect: the panel appears, and `Launching '<name>'...` is visible **without touching the Log Level combo**.

- [ ] **Step 3: The item disables**

Reopen the menu. Expect: the item reads `<name> (running)`, is greyed, and its tooltip says it is already running.

- [ ] **Step 4: The item re-enables**

Close the app so its `cmd.exe` exits. Reopen the menu.
Expect: the item is enabled and plainly named again, and `Addon '<name>' finished` appeared in the panel.

- [ ] **Step 5: Failure is visible**

Point an addon at a bad path or make its script `exit /b 3`. Click it.
Expect: an `Error` line in the panel naming the failure, and the item **enabled** again immediately.

- [ ] **Step 6: `allowMultiple`**

Set `"allowMultiple": true` on an addon and launch it twice.
Expect: after the first launch the item stays **enabled** and reads `(running)`; after the second it reads `(2 running)`.

- [ ] **Step 7: Scrollback is not hijacked**

With a chatty addon running (one that writes to stderr), scroll up in the panel.
Expect: child output does **not** yank the view to the bottom; the `finished` line does.

- [ ] **Step 8: Already-open panel**

Repeat Step 2 with the panel already open and docked. Expect the line to appear and scroll into view. Note honestly whether the raise is perceptible — the spec's Risks section predicts it is **not**, and records that as an accepted limitation. If it is worse than that in practice, say so rather than passing the step.

- [x] **Step 9: Record the results in this plan**

Add an execution-status table (see the url-addons plan for the format) recording what passed, what did not, and anything surprising.

### Execution status

Run by the operator in the Source GUI on **2026-08-21**, against the build at
`c8d912a`. Reported outcome: **good — all steps passed.**

Recorded at the granularity actually reported. The operator confirmed the run
as a whole rather than step by step, so this entry deliberately does not claim
per-step observations that were not stated — in particular Step 1 (the
pre-change regression baseline) and Step 8 (whether the panel raise is
perceptible when the panel is already docked, which the spec predicts it is
not) are covered by the overall pass, not by separate recorded findings.

This is the first and only observation of Tasks 5 and 6 behaving as designed.
Neither has automated coverage — the panel raise, the force-scroll, and the
whole menu-item application are WinForms and reflection against a live Source
instance — so this step is what moved them from reasoned-about to seen.

---

## Task 10: Port to `legacy_ci`

**Files:** the same source files, on the `legacy_ci` branch, plus its csproj.

- [ ] **Step 1: Port the commits**

Cherry-pick or re-apply Tasks 1-8 onto `legacy_ci`.

- [ ] **Step 2: Add the `<Compile Include>` entries**

`legacy_ci`'s csproj is non-SDK with explicit file lists. Add **six** entries — three source files and three test fixtures:

```xml
    <Compile Include="Addons\RunningAddons.cs" />
    <Compile Include="Addons\AddonMenuItemState.cs" />
    <Compile Include="DomainActions\OneShotLifecycle.cs" />
    <Compile Include="Tests\RunningAddonsTests.cs" />
    <Compile Include="Tests\AddonMenuItemStateTests.cs" />
    <Compile Include="Tests\OneShotLifecycleTests.cs" />
```

A file missing here compiles fine on `master` and **silently vanishes** on `legacy_ci`; for a test fixture that means the guarantee it protects goes unchecked there, with no failure to notice. Check each of the six against the csproj's existing `<Compile Include>` block rather than trusting this list.

- [ ] **Step 3: Check C# 7.3 compatibility**

`Parallel.For` needs `using System.Threading.Tasks;`. `Volatile.Read`/`Interlocked` need `using System.Threading;`. Neither the lambda syntax nor anything else in these files requires C# 8+. If the branch will not build (it did not during the url-addons work — a pre-existing `MC1000` WPF markup-compiler error), verify the new files compile standalone:

```
csc /langversion:7.3 /warnaserror /t:library Addons\RunningAddons.cs Addons\AddonMenuItemState.cs DomainActions\OneShotLifecycle.cs
```

and **say plainly in the commit message that the tests were not run there**, rather than implying they passed.

- [x] **Step 4: Commit**

```bash
git commit -m "feat: port addon launch feedback to legacy_ci"
```

### Execution status

Ported at `948ec7f` on `legacy_ci` (parent `9be5df0`), 2026-08-21, as one squashed
commit: `git cherry-pick 9bb7792^..c8d912a` then `reset --soft` and re-commit.

**Only two files had diverged** between the feature's merge-base and `legacy_ci`
— `VeneerMenu.cs` and `WebServerStatusControl.xaml.cs`. Fifteen of sixteen commits
applied clean.

**The one textual conflict**, in `3c19b27`: `legacy_ci` declares `SourceAddonLog`
*before* `ControlAddonLog`, master *after*, so the pick tried to insert master's
rewritten class at master's position and leave legacy's copy standing — two
`SourceAddonLog` classes. Resolved by keeping `legacy_ci`'s ordering and editing
the existing class in place.

**`EffectiveControl` was deliberately NOT back-ported.** Master has
`Control ?? WebServerStatusControl.ActiveInstance`, which is master-only work
outside these sixteen commits. It exists because master's `ChangeScenarioAsync`
assigns `Control` on an async continuation after an awaited `StartServer`,
leaving a window where it is null on the first click. **`legacy_ci`'s `Scenario`
setter is synchronous** (`StartServer(); PopulateMenu();` inline), so `Control` is
assigned before the setter returns and the fallback would cover nothing.

**`MC1000` depends on what is staged in `..\Output`, and Step 3 was right after
all.** The port built clean at the time — `MSBuild -t:Rebuild -p:Configuration=Debug`,
0 warnings 0 errors, under `/warnaserror+` and `/langversion:7.3` — and this
entry first recorded that the markup-compiler failure was "gone". **That was
wrong, and it was disproved within the hour.** Re-running the Source 6.x restage
from Prerequisites (needed to repair `master` after the legacy build clobbered
`..\Output`) and then rebuilding `legacy_ci` reproduces it exactly:

```
error MC1000: Unknown build error, 'Could not load type
'System.Runtime.Versioning.TargetPlatformAttribute' from assembly
'System.Runtime, Version=4.1.2.0, ...'
```

So `MC1000` is not a property of the branch — it is what a **net48 markup
compile against the Source 6.x (net8-era) reference set** produces. The port
succeeded because `..\Output` still held legacy-compatible assemblies at that
moment. The two branches need different reference sets in the same shared
directory, and whichever was staged last decides which branch can build.

**Tests ran on this branch**: 192 total, **190 passed, 2 failed**. Both failures
are pre-existing and unrelated — `ScriptMode_PersistsStateAcrossLinesAndStripsScaffolding`
and `ScriptMode_StopsAtFirstFailureAndAttributesTheLine`. Confirmed by building a
throwaway detached worktree at the parent commit and running the same command
there: **134 total, 132 passed, the same 2 failing.** Root cause is this machine's
console encoding prefixing the batch text written to `cmd`'s stdin with a UTF-8
BOM, so `cmd` rejects `@echo off` and exits 9009. Nothing in this feature touches
stdin generation. The port therefore adds 58 tests, all passing.

Two caveats, both carried in the commit message: the NUnit that resolved for the
build was **3.13.2**, not the 2.6.4 floor, so 2.6.4 portability is established by
inspection rather than execution; and **nothing was verified in a running Source**
on this branch — the menu behaviour has GUI coverage on `master` only.

> **`..\Output` hosts one branch's reference set at a time, and this is the single
> biggest trap in this repository.** Building `legacy_ci` (net48) leaves `master`
> failing with the `CS0234` above until the Source 6.x restage in Prerequisites is
> re-run; re-running that restage then leaves `legacy_ci` failing with the
> `MC1000` above. There is no state in which both branches build. Both directions
> were hit during this task, in that order, and the second one produced a
> confidently wrong plan entry before it was caught.
>
> Decide which branch you need working, stage for it, and expect the other to be
> broken until you stage back. A build failure on either branch is far more likely
> to be the wrong reference set than a real defect — check that first.

**Follow-up ported 2026-08-21**: `d5f2123` (strengthening
`SeparatorCannotBeConfusedWithMenuPipes`) is cherry-picked here as `df1bf11`.
**Not built or tested on this branch** — `..\Output` was staged for `master` by
then, so a legacy build could only produce the `MC1000` above. The change is two
string literals in one assertion plus comments, with no C# 7.3 or NUnit
portability surface, but it is unverified here and should be built the next time
`legacy_ci` is staged.

---

## Expected end state

| | |
|---|---|
| Baseline | 134 passing |
| Task 1 | +21 → 155 |
| Task 2 | +22 → 177 |
| Task 3 | +6 → 183 |
| Task 4 | +9 → 192 |
| **Total** | **192 passing, 0 failing** |

Counts are `[Test]` methods plus one per `[TestCase]` attribute.

**Both implemented tasks came in above their first prediction, and the gates below have been shifted to match.** Task 1 was written as +14 and landed at +21; Task 2 was written as +19 and landed at +22. In both cases the extra tests came from mutation testing during code review, and in both cases they closed a real hole:

- **Task 1** — the null guards in `RunningAddons.Key` could be deleted with all 14 tests still green, in a class whose `Finished` runs inside a `Task.Run` that swallows exceptions. Four null-input tests, a null/missing-name key-collision test, a separator collision test, and a distinct-keys concurrency test.
- **Task 2** — the unknown-type branch of `AddonMenuItemState` had neither its precedence nor its `Text` pinned. Moving the running check above it made a typo'd `type` render **enabled with no Click handler**, the exact outcome that branch exists to prevent.
- **Task 3** — nothing distinguished `Interlocked.Exchange` from a plain `if (_fired == 1) return; _fired = 1;`. `Parallel.For` never contends the guard, because its workers reach it in sequence. A barrier-released, 300-round test catches the non-atomic variant 9 times in 10 (measured, not assumed); `Parallel.For` alone caught it 0 times in 21. The regression it guards is the silent one: a double decrement is invisible at count 1 because `RunningAddons` floors at zero, and above 1 it makes the menu label lie.

If a later task's review adds tests, shift the remaining rows the same way rather than letting the gates drift. An executor who cannot trust these numbers cannot tell a silently unregistered test from a stale plan — which is the failure this table was already corrected for once.

Tasks 5-8 add no tests. Task 9 is manual and Task 10 is a branch port.
