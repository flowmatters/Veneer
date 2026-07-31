# Veneer Menu Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make top-level menus declared in a `.veneer` file appear in Source's menu bar in the order they appear in the file, instead of alphabetically.

**Architecture:** The ordering rules move out of `VeneerMenu` into a new pure static class `Addons/MenuLayout.cs` with no RiverSystem or TIME dependency, so they are unit-testable without a loaded Source scenario. `VeneerMenu` keeps the I/O — loading the `.veneer` config and probing the project directory for HTML reports — and passes primitives in. Alongside this, `VeneerMenu` starts tracking the `ToolStripMenuItem`s it actually created, so `ClearMenu` removes exactly those instead of matching by menu title.

**Tech Stack:** C#, .NET 8 (`net8.0-windows`), WinForms `ToolStripMenuItem` for the menu bar, Newtonsoft.Json for `.veneer` deserialisation, NUnit 4.1.0 for tests.

**Spec:** [`docs/superpowers/specs/2026-07-30-veneer-menu-ordering-design.md`](../specs/2026-07-30-veneer-menu-ordering-design.md)

---

## Prerequisites — read before Task 0

### The build and test command are already known-good

Verified 2026-07-30 on this machine:

```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

**Baseline at commit `50c610e`: `Passed: 66`, 0 failed.** This plan adds 35 cases, so the expected end state is **101**. (An earlier figure of 13 in circulation predates the addon-launch-modes fixtures — `AddonCommandLineTests`, `AddonEnvironmentTests`, `AddonLauncherIntegrationTests`, `AddonScriptTests`, `AddonValidationTests`. 66 is correct; do not treat it as a broken environment.)

Append `--filter "FullyQualifiedName~MenuLayoutTests"` to scope to this plan's fixture. The run emits many `MSB3277` reference-version warnings — **pre-existing noise, not failures**.

If this command stops working, the recovery procedure is Task 0 of
`docs/superpowers/plans/2026-07-30-addon-launch-modes.md` (repopulate `..\Output` via `build.bat`). Do not invent a different one.

### `TreatWarningsAsErrors` is true in Debug

A warning fails the build. `NoWarn` covers only `1591` and `1587` (XML doc comments). Do not leave unused locals or unreachable code behind.

### Everything here must compile as C# 7.3

The `legacy_ci` branch targets .NET Framework 4.8 with C# 7.3, and Task 8 ports this work there. So **do not use** index-from-end (`list[^1]`), target-typed `new()`, switch expressions, or range operators anywhere in this plan's code. Write `list[list.Count - 1]`. String interpolation, `?.`, and expression-bodied members are fine.

### Sequencing gate: the overlapping work has landed

The spec sequences this work **after** the addon-launch-modes feature. That feature's Task 10 — the one that rewrites the `addon.type` dispatch inside `PopulateReportMenu` and adds `ClearLoggedProblems()` to `ClearMenu` — **has landed**, at commit `4be978d`. All "before" code quoted in Tasks 3-5 is the file at `50c610e` and matches.

**Do not trust line numbers in this plan.** That work is still landing commits, and each one shifts every line below it. Where a line number appears it is a *hint*, valid at `50c610e`. **Anchor every edit on the method name and the quoted snippet**, which are stable. Task 0 Step 3 checks whether drift has occurred.

Tasks 1 and 2 create new files and touch nothing else, so they are unaffected either way.

---

## File Structure

| File | Responsibility |
|---|---|
| `FlowMatters.Source.Veneer/Addons/MenuLayout.cs` | **New.** Pure menu-path and menu-ordering logic. Owns `DEFAULT_MENU`. No RiverSystem, TIME or WinForms dependency. |
| `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs` | **New.** NUnit fixture covering every rule-table row and every menu-path edge case. |
| `FlowMatters.Source.Veneer/VeneerMenu.cs` | **Modify.** Delegates ordering to `MenuLayout`; tracks created menus; restricts HTML reports to `Reporting`; puts addons before reports; moves the logo to the last menu. |
| `docs/veneer-file-format.md` | **Modify.** Five edits so the user-facing docs describe the new ordering. |
| `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj` (legacy branch only) | **Modify.** The non-SDK-style csproj enumerates sources and needs `<Compile Include>` entries. |

`MenuLayout` goes in `Addons/` next to `VeneerConfiguration.cs` because it operates on `VeneerAddon`. It gets its own file rather than joining `VeneerConfiguration.cs` specifically so it does not inherit that file's `using RiverSystem` — the no-RiverSystem property is what makes it testable.

**Two files this work deliberately does not touch.** `CLAUDE.md` and the project conventions say to bump `PROTOCOL_VERSION` in `ExchangeObjects/VeneerStatus.cs` when the REST API surface changes, and to keep `docs/api/` in sync. **Neither applies here.** A repo-wide search for `VeneerAddon` hits only `Addons/VeneerConfiguration.cs` and `Tests/AddonValidationTests.cs` — no endpoint exposes addons or menus, so nothing about the HTTP API changes. Do not bump the version and do not edit `docs/api/`. The only documentation affected is `docs/veneer-file-format.md`, in Task 6.

---

## Task 0: Confirm the starting state

**Files:** none modified.

- [ ] **Step 1: Confirm the build and tests are green before changing anything**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

Expected: `Passed!` with 0 failures, and **66 passed** if nothing has landed since `50c610e`. A higher number means more fixtures arrived — fine, just note the new baseline so you can check the +35 at the end. If it fails outright, **stop and report** — the problem is the build environment, not this plan.

- [ ] **Step 2: Confirm the working tree is clean enough to commit against**

Run: `git -C C:\src\projects\Veneer status --short`

Other work may be in progress in this tree. That is expected. What matters is that you will only ever `git add` the exact paths named in each task's commit step — **never `git add -A` or `git add .`**. Several files in this repo (`CLAUDE.md`, `branch-porting-guide.md`, `build.bat`, `specs/`) are deliberately untracked and must stay that way.

- [ ] **Step 3: Measure how far the line numbers have drifted**

Run:
```
git -C C:\src\projects\Veneer log --oneline -1
git -C C:\src\projects\Veneer grep -n "const string DEFAULT_MENU\|private bool HasMenuContent\|public void ClearMenu" -- FlowMatters.Source.Veneer/VeneerMenu.cs
```

At `50c610e` these sit at lines 22, 338 and 275. If they still do, every line-number hint in Tasks 3-5 is accurate. If they have moved, **the hints are stale but the quoted snippets are not** — locate each edit by method name and match the snippet.

- [ ] **Step 4: Read the two methods this plan reshapes**

Read `PopulateReportMenu` and `ClearMenu` in `FlowMatters.Source.Veneer/VeneerMenu.cs` in full before touching them. Both were modified by addon-launch-modes Task 10 and may have been modified again since. Tasks 4 and 5 tell you what to preserve.

---

## Task 1: `MenuLayout` menu-path parsing

Creates the new file with stubs, then drives `SplitMenuPath` and `TopLevelMenu` out with tests. This fixes the blank-segment defect: `RemoveEmptyEntries` currently runs *before* `Trim`, so `"   "` yields a blank-titled menu and `"Modelling| |Sub"` a blank-titled submenu.

**Files:**
- Create: `FlowMatters.Source.Veneer/Addons/MenuLayout.cs`
- Create: `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs`

- [ ] **Step 1: Create `MenuLayout` with stubs that throw**

In C# a missing class is a *build* failure, not a test failure, so there is no red-green cycle to observe. Stubbing first gives a genuine failing test run.

Create `FlowMatters.Source.Veneer/Addons/MenuLayout.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// Pure menu-path and menu-ordering logic for <c>.veneer</c> addon configuration.
    /// Deliberately free of RiverSystem, TIME and WinForms dependencies so that it is
    /// testable without a loaded Source scenario.
    /// </summary>
    public static class MenuLayout
    {
        public const string DEFAULT_MENU = "Reporting";

        public static string[] SplitMenuPath(string menuPath)
        {
            throw new NotImplementedException();
        }

        public static string TopLevelMenu(string menuPath)
        {
            throw new NotImplementedException();
        }

        public static List<string> TopLevelMenus(VeneerAddon[] addons, bool hasHtmlReports)
        {
            throw new NotImplementedException();
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs`:

```csharp
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class MenuLayoutTests
    {
        // Expected values are comma-joined rather than string[] literals: an array in a
        // [TestCase] attribute is easy to get wrong, and joining sidesteps it entirely.
        [TestCase(null,                       "Reporting")]
        [TestCase("",                         "Reporting")]
        [TestCase("   ",                      "Reporting")]
        [TestCase("|",                        "Reporting")]
        [TestCase(" | ",                      "Reporting")]
        [TestCase("Reporting",                "Reporting")]
        [TestCase("Models",                   "Models")]
        [TestCase("  Models  ",               "Models")]
        [TestCase("Models|",                  "Models")]
        [TestCase("Models|Calibration",       "Models,Calibration")]
        [TestCase("Models| |Calibration",     "Models,Calibration")]
        [TestCase("Models|Calibration|Daily", "Models,Calibration,Daily")]
        [TestCase("| |Calibration",           "Calibration")]
        public void SplitMenuPath_ProducesExpectedSegments(string input, string expected)
        {
            Assert.That(string.Join(",", MenuLayout.SplitMenuPath(input)), Is.EqualTo(expected));
        }

        [TestCase(null,                 "Reporting")]
        [TestCase("",                   "Reporting")]
        [TestCase("   ",                "Reporting")]
        [TestCase("|",                  "Reporting")]
        [TestCase("Models",             "Models")]
        [TestCase("Models|Calibration", "Models")]
        [TestCase("| |Calibration",     "Calibration")]
        public void TopLevelMenu_ReturnsFirstSegment(string input, string expected)
        {
            Assert.That(MenuLayout.TopLevelMenu(input), Is.EqualTo(expected));
        }
    }
}
```

Two cases carry the fix and are the reason this task exists:

- `"   "` → `Reporting`, where today it produces `""` and a blank-titled top-level menu.
- `"Models| |Calibration"` → two segments, where today it produces three and a blank-titled submenu wedged between them.

`"| |Calibration"` → `Calibration` records that a leading empty segment promotes the next one. That is a consequence of the rule, not a separate requirement, but worth pinning so a later refactor cannot change it silently.

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~MenuLayoutTests"
```

Expected: 20 failures, every one a `System.NotImplementedException`. If you instead get a *build* error, the stub file has a syntax problem — fix that before continuing.

- [ ] **Step 4: Implement `SplitMenuPath` and `TopLevelMenu`**

Replace the two stub bodies in `Addons/MenuLayout.cs`. Add `using System.Linq;` to the file's usings.

```csharp
        public static string[] SplitMenuPath(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return new[] { DEFAULT_MENU };

            // Trim *before* discarding empties. The original did the reverse, so a
            // whitespace-only segment survived as "" and became a blank-titled menu.
            var segments = menuPath.Split('|')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            return segments.Length > 0 ? segments : new[] { DEFAULT_MENU };
        }

        public static string TopLevelMenu(string menuPath)
        {
            return SplitMenuPath(menuPath)[0];
        }
```

`SplitMenuPath` never returns an empty array, so indexing `[0]` in `TopLevelMenu` is safe by construction rather than by luck.

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~MenuLayoutTests"
```

Expected: 20 passed, 0 failed. `TopLevelMenus` is still a stub and has no tests yet, so nothing should error.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/MenuLayout.cs FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs
git commit -m "feat: add MenuLayout menu-path parsing

Trims before discarding empty segments, so a whitespace-only menu path
resolves to Reporting instead of producing a blank-titled menu, and a
blank middle segment no longer creates a blank-titled submenu."
```

---

## Task 2: `MenuLayout.TopLevelMenus` — the ordering rule

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/MenuLayout.cs`
- Modify: `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs`

The rule, from the spec: **first appearance in `addons`, then `Reporting` if it has not already appeared.** An addon with no `menu` targets `Reporting` and claims its position.

- [ ] **Step 1: Write the failing tests**

Add `using System.Linq;` to the test file's usings, then append the following to the `MenuLayoutTests` class.

Note the block below **ends with the class and namespace closing braces**, so it replaces Task 1's trailing `    }\n}` rather than being inserted before them. Paste it after Task 1's last test method and delete the two braces it supersedes.

```csharp
        private static VeneerAddon[] Addons(params string[] menus)
        {
            return menus.Select(m => new VeneerAddon { name = "addon", menu = m }).ToArray();
        }

        private static string Bar(VeneerAddon[] addons, bool hasHtmlReports)
        {
            return string.Join(",", MenuLayout.TopLevelMenus(addons, hasHtmlReports));
        }

        // --- Rule table, row by row ---

        [Test] // row 1
        public void TopLevelMenus_PreservesFileOrder()
        {
            Assert.That(Bar(Addons("Modelling", "Analysis"), false),
                Is.EqualTo("Modelling,Analysis"));
        }

        [Test] // row 1, the reported bug: alphabetical order would put Analysis first
        public void TopLevelMenus_DoesNotSortAlphabetically()
        {
            Assert.That(Bar(Addons("Zebra", "Alpha"), false), Is.EqualTo("Zebra,Alpha"));
        }

        [Test] // row 2
        public void TopLevelMenus_AppendsReportingLastWhenOnlyHtmlReportsNeedIt()
        {
            Assert.That(Bar(Addons("Modelling", "Analysis"), true),
                Is.EqualTo("Modelling,Analysis,Reporting"));
        }

        [Test] // row 3
        public void TopLevelMenus_ReportingKeepsFilePositionWhenNamed()
        {
            Assert.That(Bar(Addons("Modelling", "Reporting", "Analysis"), false),
                Is.EqualTo("Modelling,Reporting,Analysis"));
            Assert.That(Bar(Addons("Modelling", "Reporting", "Analysis"), true),
                Is.EqualTo("Modelling,Reporting,Analysis"));
        }

        [Test] // row 4
        public void TopLevelMenus_CollapsesDuplicatesToFirstAppearance()
        {
            Assert.That(Bar(Addons("Analysis", "Modelling", "Analysis"), false),
                Is.EqualTo("Analysis,Modelling"));
        }

        [Test] // row 5 — the commonest .veneer shape
        public void TopLevelMenus_MenulessAddonClaimsReportingPosition()
        {
            Assert.That(Bar(Addons(null, "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons(null, "Modelling"), true), Is.EqualTo("Reporting,Modelling"));
        }

        [Test] // row 6
        public void TopLevelMenus_MenulessAddonLaterInFileClaimsLaterPosition()
        {
            Assert.That(Bar(Addons("Modelling", null), false), Is.EqualTo("Modelling,Reporting"));
        }

        [Test] // row 7
        public void TopLevelMenus_ReportingIsTheSoleFallback()
        {
            Assert.That(Bar(null, false), Is.EqualTo("Reporting"));
            Assert.That(Bar(null, true), Is.EqualTo("Reporting"));
            Assert.That(Bar(new VeneerAddon[0], false), Is.EqualTo("Reporting"));
            Assert.That(Bar(new VeneerAddon[0], true), Is.EqualTo("Reporting"));
        }

        // --- Beyond the table ---

        [Test]
        public void TopLevelMenus_OmitsReportingWhenNothingTargetsItAndThereAreNoReports()
        {
            Assert.That(Bar(Addons("Modelling"), false), Is.EqualTo("Modelling"));
        }

        [Test]
        public void TopLevelMenus_TreatsEmptyAndWhitespaceMenusAsReporting()
        {
            Assert.That(Bar(Addons("", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("   ", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("|", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
        }

        [Test]
        public void TopLevelMenus_MenulessAndExplicitReportingCollapseToEarlierPosition()
        {
            Assert.That(Bar(Addons(null, "Modelling", "Reporting"), false),
                Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("Modelling", "Reporting", null), false),
                Is.EqualTo("Modelling,Reporting"));
        }

        [Test]
        public void TopLevelMenus_CasingIsDistinct()
        {
            // Matches FindOrCreateReportMenu's ordinal `item.Text == mnu` comparison,
            // which already creates two separate menus for these.
            Assert.That(Bar(Addons("Analysis", "analysis"), false),
                Is.EqualTo("Analysis,analysis"));
        }

        [Test]
        public void TopLevelMenus_UsesOnlyTheFirstSegmentOfANestedPath()
        {
            Assert.That(Bar(Addons("Models|Calibration", "Models|Validation"), false),
                Is.EqualTo("Models"));
        }

        [Test]
        public void TopLevelMenus_IgnoresScenarioFilters()
        {
            // A filtered-out addon is greyed out in the menu but still claims its
            // menu's position, so the bar does not shift when the scenario changes.
            var filtered = new[]
            {
                new VeneerAddon { name = "a", menu = "Modelling", scenario = "SomeOtherScenario" },
                new VeneerAddon { name = "b", menu = "Analysis" }
            };
            Assert.That(Bar(filtered, false), Is.EqualTo("Modelling,Analysis"));
        }

        [Test]
        public void TopLevelMenus_ToleratesANullAddonEntry()
        {
            // Newtonsoft will happily deserialise `"addons": [null]`.
            var withNull = new VeneerAddon[] { null, new VeneerAddon { name = "b", menu = "Analysis" } };
            Assert.That(Bar(withNull, false), Is.EqualTo("Reporting,Analysis"));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~MenuLayoutTests"
```

Expected: the 20 from Task 1 still pass; the 15 new ones fail with `System.NotImplementedException`.

- [ ] **Step 3: Implement `TopLevelMenus`**

Replace the stub body in `Addons/MenuLayout.cs`:

```csharp
        public static List<string> TopLevelMenus(VeneerAddon[] addons, bool hasHtmlReports)
        {
            var result = new List<string>();

            if (addons != null)
            {
                foreach (var addon in addons)
                {
                    // A null or absent `menu` resolves to DEFAULT_MENU, so a menuless
                    // addon claims Reporting's file position like any other.
                    var menu = TopLevelMenu(addon?.menu);
                    if (!result.Contains(menu))
                        result.Add(menu);
                }
            }

            // Reporting is appended only if no addon already claimed a position for it:
            // either because HTML reports need somewhere to live, or because the bar
            // would otherwise be empty and the Veneer logo would have no home.
            if (!result.Contains(DEFAULT_MENU) && (hasHtmlReports || result.Count == 0))
                result.Add(DEFAULT_MENU);

            return result;
        }
```

`List<string>.Contains` uses ordinal string equality, which is what makes the casing test pass and matches `FindOrCreateReportMenu`'s `item.Text == mnu`. It is O(n) per lookup; `n` is the number of distinct menus in a `.veneer` file, so this is not worth a `HashSet` and a `HashSet` would lose the ordering this method exists to produce.

There is deliberately no scenario parameter. The method cannot evaluate a filter even though `addon.scenario` is visible on the POCO, because it has no access to the active scenario or to `config.targetScenario`.

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo --filter "FullyQualifiedName~MenuLayoutTests"
```

Expected: 35 passed, 0 failed.

- [ ] **Step 5: Run the whole suite to confirm nothing else broke**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

Expected: `Passed!`, 0 failures. Nothing calls `MenuLayout` yet, so this should be the previous total plus 35.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/MenuLayout.cs FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs
git commit -m "feat: add MenuLayout.TopLevelMenus ordering rule

Menus take the position of their first appearance in the .veneer addons
array; Reporting is appended last only if no addon already claimed a
position for it. An addon with no menu field targets Reporting and
claims its position. Scenario filters do not affect the result."
```

---

## Task 3: Point `VeneerMenu` at `MenuLayout`

Pure delegation. `RequiredMenus` stops sorting, and the three moved helpers are deleted.

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

- [ ] **Step 1: Delete the local `DEFAULT_MENU`**

`using FlowMatters.Source.Veneer.Addons;` is **already present** at the top of the file — do not add it again. A duplicate `using` raises `CS0105`, and `TreatWarningsAsErrors` turns that into a build failure.

Delete the class-level constant (line 22 at `50c610e`):

```csharp
        const string DEFAULT_MENU = "Reporting";
```

`MenuLayout.DEFAULT_MENU` is now the single definition. It is a `const`, so it still works as a default parameter value.

- [ ] **Step 2: Delete the three methods that moved**

Delete `SplitMenuPath`, `GetTopLevelMenu` and `HasMenuContent` in their entirety. Locate them **by name** — at `50c610e` they are at lines 164-172, 174-178 and 338-361, but that will drift. Their bodies are, for identification:

```csharp
        private string[] SplitMenuPath(string menuPath)      // starts with `if (string.IsNullOrEmpty(menuPath))`
        private string GetTopLevelMenu(string menuPath)      // `var parts = SplitMenuPath(menuPath);`
        private bool HasMenuContent(string menuName)         // starts with `if (Scenario == null)`
```

`HasMenuContent` did two jobs — "are there HTML files" and "does any addon target this menu". The first becomes `HtmlReportFiles()` in Step 4; the second lives inside `TopLevelMenus`.

- [ ] **Step 3: Update the three remaining call sites**

Find each by its snippet, not its line number:

| Where | Before | After |
|---|---|---|
| `FindOrCreateReportMenu` signature | `string mnu = DEFAULT_MENU` | `string mnu = MenuLayout.DEFAULT_MENU` |
| `PopulateReportMenu`, addon filter | `GetTopLevelMenu(a.menu) == mnu` | `MenuLayout.TopLevelMenu(a.menu) == mnu` |
| `PopulateReportMenu`, addon loop | `SplitMenuPath(addon.menu)` | `MenuLayout.SplitMenuPath(addon.menu)` |

`FindOrCreateNestedMenu` needs no change — it receives the already-split `menuPath` as a parameter and never mentions `DEFAULT_MENU`. Every other former use of `DEFAULT_MENU` sat inside the three methods deleted in Step 2 or the `RequiredMenus` body replaced in Step 4, so these three are all that remain — plus the new one Task 5 adds.

The build is the check: any `CS0103` for `DEFAULT_MENU`, `SplitMenuPath` or `GetTopLevelMenu` means a site was missed. That is the intended safety net, so do not try to enumerate them by eye.

- [ ] **Step 4: Rewrite `RequiredMenus` and add the HTML-report helpers**

Replace the whole of `RequiredMenus` — the method beginning `var result = new List<string>();` and containing the `addonMenus.OrderBy(m => m)` sort — with:

```csharp
        private List<string> RequiredMenus()
        {
            var config = VeneerConfiguration.Load(Scenario);
            return MenuLayout.TopLevelMenus(config?.addons, HtmlReportFiles().Any());
        }

        private IEnumerable<string> HtmlReportFiles()
        {
            var projectFolder = Scenario?.Project?.FileDirectory;
            if (projectFolder == null)
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(projectFolder, "*.htm*", SearchOption.TopDirectoryOnly);
        }
```

`HtmlReportFiles` is shared: `RequiredMenus` asks whether any exist, and Task 5's `AddHtmlReports` iterates the same enumerable. One definition of the glob.

Behaviour with `Scenario == null` is unchanged. `VeneerConfiguration.Load(null)` returns `null`, `HtmlReportFiles()` returns empty, so `TopLevelMenus(null, false)` gives `["Reporting"]` — exactly what the old code produced via `HasMenuContent` returning false and `hasOtherMenus` being false.

- [ ] **Step 5: Build and run the suite**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

Expected: `Passed!`, 0 failures, same count as at the end of Task 2 (101 if the baseline was 66).

Watch for `CS0103` (a call site you missed) and `CS0246` (a mistyped `MenuLayout` reference).

One thing **not** to worry about: after Task 2, `using System;` in `MenuLayout.cs` is no longer needed, since nothing throws `NotImplementedException` any more. An unused `using` is not a compiler warning, so it does not trip `TreatWarningsAsErrors`. Leave it or remove it; do not go hunting for a build error that will not appear.

- [ ] **Step 6: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "fix: order .veneer top-level menus by file position

RequiredMenus sorted menu names alphabetically and accumulated them in
an order-losing HashSet, so a .veneer file's second menu could appear
first. Ordering now comes from MenuLayout.TopLevelMenus, and the moved
helpers are deleted."
```

At this point **the reported bug is fixed.** Tasks 4 and 5 fix the adjacent defects the spec identified in the same code path.

---

## Task 4: Track created menus; move the logo to the last menu

Two changes that both need the same new state. `ClearMenu` currently removes menu items by matching their `Text` against the whole of Source's menu bar, so a `.veneer` file containing `"menu": "Tools"` makes Veneer delete *Source's own* `Tools` menu.

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

- [ ] **Step 1: Add the two fields**

After the `public WebServerStatusControl Control { get; set; }` property:

```csharp
        /// <summary>Top-level menus this project requires, in bar order.</summary>
        private List<string> _menuLayout = new List<string>();

        /// <summary>Menu items Veneer itself added to the main menu strip.</summary>
        private readonly List<ToolStripMenuItem> _createdMenus = new List<ToolStripMenuItem>();
```

- [ ] **Step 2: Record created menus in `FindOrCreateReportMenu`**

In `FindOrCreateReportMenu`'s `if (result == null)` branch, after `parent.MainMenuStrip.Items.Add(result);`:

```csharp
                _createdMenus.Add(result);
```

Only the create branch. A menu found by name already belonged to Source, and Veneer must not claim the right to remove it.

- [ ] **Step 3: Compute the layout once in `InitialiseRequiredMenus`**

Replace `InitialiseRequiredMenus` in full:

```csharp
        public void InitialiseRequiredMenus(Form parent, RiverSystemScenario scenario)
        {
            Scenario = scenario;
            _menuLayout = RequiredMenus();
            foreach (var mnu in _menuLayout)
            {
                FindOrCreateReportMenu(parent, mnu);
            }
        }
```

- [ ] **Step 4: Rewrite `ClearMenu` to remove only what Veneer created**

The current body is:

```csharp
        public void ClearMenu()
        {
            // A project change should re-report addon config problems.
            ClearLoggedProblems();

            Form parent = VeneerMenu.FindMainForm();
            foreach (var mnu in RequiredMenus())
            {
                ToolStripMenuItem reportMenu =
                    parent.MainMenuStrip.Items.Cast<ToolStripItem>().
                        Where(item => item.Text == mnu).Cast<ToolStripMenuItem>().FirstOrDefault();

                if (reportMenu != null)
                    parent.MainMenuStrip.Items.Remove(reportMenu);
            }
        }
```

That `Where(item => item.Text == mnu)` searching all of `MainMenuStrip.Items` is the destructive bug. Replace the method with:

```csharp
        public void ClearMenu()
        {
            // A project change should re-report addon config problems.
            ClearLoggedProblems();

            foreach (var menu in _createdMenus)
            {
                // Owner is the ToolStrip the item currently lives on, so this removes
                // the item from wherever it actually is rather than from a menu strip
                // we look up by hand.
                if (menu.Owner != null)
                    menu.Owner.Items.Remove(menu);
            }

            _createdMenus.Clear();
            _menuLayout.Clear();
        }
```

**The `ClearLoggedProblems()` call is load-bearing and must survive** — it was added by addon-launch-modes Task 10 so a project change re-reports addon config problems. Dropping it silently regresses that de-duplication and no test in this plan would notice. If the method has gained anything else since `50c610e`, carry that too.

`FindMainForm()` is no longer needed here, but it stays in use in `PopulateReportMenu` and by external callers — **do not delete it.**

- [ ] **Step 5: Move the logo to the last menu**

In `PopulateReportMenu`, replace:

```csharp
            // Only add Veneer logo to the first menu
            var requiredMenus = RequiredMenus();
            if (requiredMenus.Count > 0 && requiredMenus[0] == mnu)
```

with:

```csharp
            // Only add the Veneer logo to the last menu in the layout
            var layout = _menuLayout.Count > 0 ? _menuLayout : RequiredMenus();
            if (layout.Count > 0 && layout[layout.Count - 1] == mnu)
```

The `RequiredMenus()` fallback is defensive rather than reachable — `DropDownOpening` can only fire on a menu that exists, which implies `InitialiseRequiredMenus` ran — but `_menuLayout` is instance state on a singleton that `ClearMenu` empties, so this method must not assume it is populated.

`layout[layout.Count - 1]`, not `layout[^1]`: index-from-end is C# 8 and this file ports to a C# 7.3 branch.

- [ ] **Step 6: Build and run the suite**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

Expected: `Passed!`, 0 failures, same count as Task 3. These changes are WinForms-side and have no unit coverage — Task 7 verifies them by hand.

- [ ] **Step 7: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "fix: remove only the menus Veneer created, and move the logo last

ClearMenu matched menu items by Text across Source's whole menu bar, so
a .veneer file naming an existing Source menu made Veneer delete that
menu. It now removes tracked ToolStripMenuItem references. The Veneer
logo moves from the first menu in the layout to the last."
```

---

## Task 5: Restrict HTML reports to `Reporting`, and put addons first

`PopulateReportMenu` enumerates the project directory's `.htm*` files whatever menu it was called with, so **every** Veneer top-level menu gets the same report list. `docs/veneer-file-format.md:85` has always documented these as belonging to `Reporting`, so this brings the code in line with its documentation.

**Files:**
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`

- [ ] **Step 1: Extract the HTML block into its own method**

Add next to `NiceName`:

```csharp
        private void AddHtmlReports(ToolStripMenuItem reportMenu)
        {
            foreach (string reportFn in HtmlReportFiles())
            {
                string fn = Path.GetFileName(reportFn);
                ToolStripItem item = reportMenu.DropDownItems.Add(NiceName(fn));
                item.Click += (eventSender, eventArgs) => Launch(fn);
            }
        }
```

Two notes on fidelity to the original:

- `Path.GetFileName(reportFn)` replaces `reportFn.Replace(projectFolder + "\\", "")`. Equivalent for a `TopDirectoryOnly` enumeration, and it does not break if `FileDirectory` already ends in a separator — where the old `Replace` would have failed to match and left the full path in the menu.
- `fn` must stay declared **inside** the loop. Each `Click` closure captures its own copy; hoisting the declaration would make every report open the last file.

- [ ] **Step 2: Reorder `PopulateReportMenu` and gate the reports on `Reporting`**

This is a **move and a guard, not a rewrite.** The addon loop contains addon-launch-modes Task 10's `exe`/`script` dispatch, `VeneerAddon.Validate`, `LogOnce` and `default` case. **Keep all of it verbatim.** Only two things change: the HTML block moves from before the loop to after it, and it becomes conditional.

Delete this block, which currently opens the `if (Scenario != null)` body:

```csharp
                string projectFolder = Scenario.Project.FileDirectory;
                if (projectFolder != null)
                {
                    foreach (string reportFn in Directory.EnumerateFiles(projectFolder, "*.htm*",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string fn = reportFn.Replace(projectFolder + "\\", "");
                        ToolStripItem item = reportMenu.DropDownItems.Add(NiceName(fn));
                        item.Click += (eventSender, eventArgs) => Launch(fn);
                    }
                }
```

`HtmlReportFiles()` and `AddHtmlReports()` now own both the glob and the null guard.

Then insert, **between** the closing brace of the `if (config?.addons != null)` block and the `if (config?.options!= null)` block (note the missing space before `!=` — that is how the file reads, so grep for it verbatim or find it by eye):

```csharp
                if (mnu == MenuLayout.DEFAULT_MENU)
                {
                    AddHtmlReports(reportMenu);
                }
```

`var config` and `var currentScenario` need no moving — deleting the HTML block already leaves them as the first statements in the body.

The resulting order inside `if (Scenario != null)` is: `config`/`currentScenario`, then the addon loop, then the reports block, then the `options` block.

- [ ] **Step 3: Build and run the suite**

Run:
```
dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo
```

Expected: `Passed!`, 0 failures.

Check specifically that `System.IO.Path` resolves — `using System.IO;` is already present for `Directory`, so no new using is needed.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/VeneerMenu.cs
git commit -m "fix: put HTML reports only in Reporting, below the addons

PopulateReportMenu enumerated the project's .htm* files for whichever
menu it was populating, so every Veneer menu received a duplicate copy
of the report list. veneer-file-format.md has always documented these as
belonging to Reporting. Within Reporting, file-specified addons now
precede auto-discovered reports."
```

---

## Task 6: Update the user-facing documentation

**Files:**
- Modify: `docs/veneer-file-format.md`

The five "before" texts below were verified byte-for-byte against the file. Line numbers are hints; match on the text.

- [ ] **Step 1: Extend the `menu` table's absent/empty row (line 64)**

Before:
```
| absent / empty           | Item appears under the default `Reporting` menu. |
```
After:
```
| absent / empty / whitespace / `"\|"` | Item appears under the default `Reporting` menu. |
```

- [ ] **Step 2: Fix the `"Models"` row (line 66)**

Before:
```
| `"Models"`               | A new top-level `Models` menu is created next to `Reporting`; the item appears in it. |
```
After:
```
| `"Models"`               | A new top-level `Models` menu is created; the item appears in it. Its position in the menu bar follows the order menus first appear in the file — see below. |
```

- [ ] **Step 3: Rewrite line 70 to describe the ordering — keeping its existing promise**

Before:
```
Top-level menus are created up-front based on every `menu` value in the file, so menu-bar layout is stable regardless of which scenario is currently active.
```
After:
```
Top-level menus are created up-front based on every `menu` value in the file, so menu-bar layout is stable regardless of which scenario is currently active — an addon that is greyed out by a scenario filter still contributes its menu, in its usual position.

They appear in the menu bar **in the order they first appear in the file**. An addon with no `menu` counts as targeting `Reporting` for this purpose, so a menuless addon at the top of the file puts `Reporting` first. `Reporting` is appended after all file-specified menus when no addon targets it — which happens when it exists only to hold discovered HTML reports.

Naming a menu that Source itself already owns (`Tools`, `File`, and so on) is **not supported**: Veneer binds to the existing menu wherever Source placed it, and addons under that name are not populated.
```

The "stable regardless of which scenario is currently active" clause is **extended, not replaced** — it is still true, and it is still worth promising.

- [ ] **Step 4: Fix the logo statement (line 149)**

Before:
```
Veneer adds a clickable logo as the last item of the *first* top-level menu it owns. This is purely cosmetic and not configurable.
```
After:
```
Veneer adds a clickable logo as the last item of the *last* top-level menu it owns. This is purely cosmetic and not configurable.
```

- [ ] **Step 5: Confirm line 85 needs no change**

Re-read the HTML reports section. It should already say reports are "automatically added to the `Reporting` menu", which is now true. If it says anything else, fix it to match Task 5.

- [ ] **Step 6: Commit**

```bash
git add docs/veneer-file-format.md
git commit -m "docs: describe .veneer menu ordering

Menu-bar order follows file order; Reporting is appended when unnamed;
the logo sits on the last menu, not the first. Records that naming an
existing Source menu is unsupported."
```

---

## Task 7: Manual verification in Source

None of Tasks 3-5 has unit coverage — they are WinForms and RiverSystem code. This is where they get verified. **Do not skip this task and do not report the work complete without it.**

**Files:** none modified (unless a defect is found).

- [ ] **Step 1: Build the plugin and load a project**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj -nologo -v:m -clp:ErrorsOnly
```

Expected: exit code 0. Then start Source with the built plugin and open a test project.

- [ ] **Step 2: Verify bar order — the reported bug**

Write a `.veneer` sidecar whose menus are in deliberately non-alphabetical order:

```json
{
  "addons": [
    { "name": "Calibrate", "type": "exe", "path": "tools/noop.bat", "menu": "Zebra" },
    { "name": "Compare",   "type": "exe", "path": "tools/noop.bat", "menu": "Alpha" }
  ]
}
```

Expected: `Zebra` then `Alpha` in the menu bar. Before this work it was `Alpha`, `Zebra`.

- [ ] **Step 3: Verify `Reporting` placement and within-menu order**

Add `summary.html` and `gauges.html` to the project directory, reload.

Expected: bar reads `Zebra`, `Alpha`, `Reporting`. Open `Zebra` and `Alpha` — **neither** should list the HTML reports. Open `Reporting` — it should list `summary` and `gauges` and nothing else.

Then add `{ "name": "Publish", "type": "exe", "path": "tools/noop.bat", "menu": "Reporting" }` as the **first** entry and reload.

Expected: bar reads `Reporting`, `Zebra`, `Alpha`. `Reporting` lists `Publish` first, then `summary` and `gauges`.

- [ ] **Step 4: Verify the logo**

Expected: the Veneer logo is the last item of the **last** menu in the bar, and appears in no other menu.

- [ ] **Step 5: Verify no menus leak across a scenario change**

Switch to a different scenario, then back.

Expected: the menu bar is identical each time — no duplicated menus, no leftovers, same order. This exercises `ClearMenu`'s `_createdMenus` path.

- [ ] **Step 6: Verify Source's own menus survive**

Set one addon's menu to `"Tools"`, reload, then switch scenarios.

Expected: **Source's `Tools` menu is still present and still has its own items.** Before this work, `ClearMenu` removed it by name. The addon itself will not appear under `Tools` — that is the documented unsupported case from Task 6 Step 3, not a regression.

- [ ] **Step 7: Verify the blank-segment fix**

Set one addon's menu to `"   "` and another's to `"Models| |Calibration"`.

Expected: the first lands in `Reporting`. The second creates `Models → Calibration` with **no** blank-titled menu between them.

- [ ] **Step 8: Record the results**

Append a short "Verified" note to this plan under Task 7 stating what was checked and on which Source version, then commit:

```bash
git add docs/superpowers/plans/2026-07-30-veneer-menu-ordering.md
git commit -m "docs: record manual verification of menu ordering"
```

If any step fails, **stop and report** rather than patching blind — a failure here means one of Tasks 3-5 is wrong.

---

## Task 8: Port to `legacy_ci`

See [`branch-porting-guide.md`](../../../branch-porting-guide.md) for the general process.

**Files (on the `legacy_ci` branch):**
- Create: `FlowMatters.Source.Veneer/Addons/MenuLayout.cs`
- Create: `FlowMatters.Source.Veneer/Tests/MenuLayoutTests.cs` (only if that branch carries test fixtures — check)
- Modify: `FlowMatters.Source.Veneer/VeneerMenu.cs`
- Modify: `FlowMatters.Source.Veneer/FlowMatters.Source.Veneer.csproj`
- Modify: `docs/veneer-file-format.md`

- [ ] **Step 1: Confirm the branch's starting state**

The addon-launch-modes work ports to `legacy_ci` separately and must land there **first**, for the same reason it goes first on `master` — it shares `PopulateReportMenu` and `ClearMenu`. Confirm it has, and confirm the rename to `VeneerMenu.cs` has.

- [ ] **Step 2: Check whether that branch has test infrastructure**

Look for `FlowMatters.Source.Veneer/Tests/` and NUnit references in its `.csproj`. If absent, port the source change without the fixture and say so in the commit message — do **not** add a test framework to that branch as part of this work.

- [ ] **Step 3: Copy the source files across unchanged**

`MenuLayout.cs` needs no adaptation: it is plain C# with no CoreWCF, no async, and nothing newer than C# 7.3. The `VeneerMenu.cs` edits are WinForms and identical on both branches.

Double-check no C# 8+ syntax crept in — particularly `layout[layout.Count - 1]` rather than `layout[^1]`.

- [ ] **Step 4: Add the `<Compile Include>` entries**

The legacy `.csproj` is non-SDK-style and enumerates its sources, so each new file needs an explicit entry — `Addons\MenuLayout.cs`, and `Tests\MenuLayoutTests.cs` if Step 2 found fixtures. `master`'s SDK-style csproj needs none. The rename step in the addon-launch-modes plan hit this same difference.

- [ ] **Step 5: Build, test, and commit**

Build with the branch's own procedure. If tests were ported, run them. Then commit with the same message bodies used on `master`.

---

## Definition of done

- [ ] `dotnet test FlowMatters.Source.Veneer\FlowMatters.Source.Veneer.csproj --nologo` passes with 35 new `MenuLayoutTests` cases — **101 total** if the baseline was still 66.
- [ ] `MenuLayout` has no RiverSystem, TIME or WinForms dependency.
- [ ] `DEFAULT_MENU` is defined once, in `MenuLayout`.
- [ ] `SplitMenuPath`, `GetTopLevelMenu` and `HasMenuContent` no longer exist on `VeneerMenu`.
- [ ] Task 7's manual checks all pass and are recorded in this file.
- [ ] `docs/veneer-file-format.md` describes the ordering, the logo's new position, and the unsupported Source-menu-name case.
- [ ] `ClearLoggedProblems()` is still called from `ClearMenu`.
- [ ] No `git add -A`. `CLAUDE.md`, `branch-porting-guide.md`, `build.bat` and `specs/` are still untracked.
