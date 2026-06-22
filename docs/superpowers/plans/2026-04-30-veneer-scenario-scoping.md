# Veneer Scenario Scoping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow `.veneer` files to scope addon menu items to a named Source scenario, rendering non-matching items as disabled with an explanatory tooltip.

**Architecture:** Add two optional string fields (`targetScenario` at the config root, `scenario` per addon). At dropdown-open, evaluate the effective filter against the live `MainForm.Instance.CurrentScenario.Name` (case-insensitive). Non-matching addons are added to the menu but rendered disabled with `ToolTipText` naming the required scenario. Menu-bar layout and `RequiredMenus()` logic are unchanged. The captured `ReportingMenu.Scenario` field is still used to load the config (the addon set is fixed at project load); only enabled/disabled state is dynamic.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms (`ToolStripMenuItem`), Newtonsoft.Json, `TIME.Management.Log` for logging.

**Spec:** `docs/superpowers/specs/2026-04-30-veneer-scenario-scoping-design.md`

**Branch:** `legacy_ci` (this plan). Porting to `master` per `branch-porting-guide.md` is a follow-up.

**Test strategy:** Veneer has no automated test project. Verification is manual via a Source GUI session with a two-scenario `.rsproj` and a hand-edited `.veneer` file. See Task 4.

---

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` | Modify | Schema fields (`targetScenario`, `scenario`) + `AddonAppliesTo` helper |
| `FlowMatters.Source.Veneer/ReportingMenu.cs` | Modify | Disable-with-tooltip rendering + log line, in `PopulateReportMenu` |

No new files. No changes outside these two.

---

### Task 1: Add schema fields and `AddonAppliesTo` helper

**Files:**
- Modify: `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs`

- [ ] **Step 1: Add `targetScenario` field to `VeneerConfiguration`**

In `VeneerConfiguration.cs`, alongside the existing `addons` and `options` fields:

```csharp
public class VeneerConfiguration
{
    public VeneerAddon[] addons;
    public VeneerOptions options;
    public string targetScenario;   // NEW: optional default filter for all addons
    // ... existing static methods unchanged ...
}
```

- [ ] **Step 2: Add `scenario` property to `VeneerAddon`**

```csharp
public class VeneerAddon
{
    public string name { get; set; }
    public string type { get; set; }
    public string path { get; set; }
    public string menu { get; set; }
    public string scenario { get; set; }   // NEW: per-addon override
}
```

- [ ] **Step 3: Add the `AddonAppliesTo` static helper to `VeneerConfiguration`**

Add inside the `VeneerConfiguration` class (after the existing `Load` overloads). Note: `RiverSystemScenario` is already imported via `using RiverSystem;` at the top of the file.

```csharp
public static bool AddonAppliesTo(
    VeneerAddon addon,
    VeneerConfiguration config,
    RiverSystemScenario currentScenario)
{
    var filter = !string.IsNullOrEmpty(addon?.scenario)
        ? addon.scenario
        : config?.targetScenario;

    if (string.IsNullOrEmpty(filter)) return true;
    if (currentScenario == null) return false;

    return string.Equals(
        currentScenario.Name,
        filter,
        StringComparison.OrdinalIgnoreCase);
}

public static string EffectiveFilter(VeneerAddon addon, VeneerConfiguration config)
{
    return !string.IsNullOrEmpty(addon?.scenario)
        ? addon.scenario
        : config?.targetScenario;
}
```

`EffectiveFilter` is exposed so `ReportingMenu` can build the tooltip text without re-implementing the resolution rule.

`StringComparison` lives in `System`; the file already has `using System;` so no new using directive is required.

- [ ] **Step 4: Build the solution**

Run from a Developer Command Prompt or with the path in `CLAUDE.md`:

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```

Expected: clean build. `TreatWarningsAsErrors` is true in Debug — any new XML-doc or unused-field warning will fail the build, so the new fields must compile without warning. (The schema fields are public and serialised by Newtonsoft.Json, so the compiler won't warn about them being unused.)

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs
git commit -m "Add targetScenario and per-addon scenario fields to .veneer config"
```

---

### Task 2: Render non-matching addons as disabled in `PopulateReportMenu`

**Files:**
- Modify: `FlowMatters.Source.Veneer/ReportingMenu.cs` (in `PopulateReportMenu`, around the addon-add loop currently at lines 87-106)

Existing relevant code (for context):

```csharp
ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);
switch (addon.type)
{
    case "exe":
        item.Click += (o, args) => LaunchExeAddon(addon.path);
        break;
}
```

- [ ] **Step 1: Add the `using` directive for `MainForm`**

`MainForm` lives in `RiverSystem.Forms` (per the existing `using` in `ProjectLoadListener.cs`). Confirm `ReportingMenu.cs` has `using RiverSystem.Forms;`. If absent, add it. (Currently the file has `using RiverSystem;` and `using RiverSystem.Api;` — `MainForm` requires `RiverSystem.Forms`.)

- [ ] **Step 2: Capture the live current scenario before the addon loop**

In `PopulateReportMenu`, immediately after `var config = VeneerConfiguration.Load(Scenario);` and before the `if (config?.addons != null)` block, add:

```csharp
var currentScenario = MainForm.Instance.CurrentScenario;
```

This reads the *live* current scenario (as opposed to the captured `Scenario` field which anchors the addon set itself).

- [ ] **Step 3: Disable non-matching items and set the tooltip**

Inside the existing `foreach (var addon in addonsForMenu)` loop in `PopulateReportMenu`, modify the per-addon body so each created `item` is checked against the filter. Replace this block:

```csharp
ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);
switch (addon.type)
{
    case "exe":
        item.Click += (o, args) => LaunchExeAddon(addon.path);
        break;
}
```

with:

```csharp
ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);
switch (addon.type)
{
    case "exe":
        item.Click += (o, args) => LaunchExeAddon(addon.path);
        break;
}

if (!VeneerConfiguration.AddonAppliesTo(addon, config, currentScenario))
{
    var filter = VeneerConfiguration.EffectiveFilter(addon, config);
    item.Enabled = false;
    item.ToolTipText = $"Requires scenario '{filter}' to be active";
    TIME.Management.Log.WriteMessage(
        this,
        $"Veneer addon '{addon.name}' disabled: requires scenario '{filter}', current is '{currentScenario?.Name ?? "none"}'");
}
```

Notes:
- `ToolStripItem.Enabled` and `ToolStripItem.ToolTipText` exist on the base `ToolStripItem` type, so no cast is needed even though `Add` returns `ToolStripItem`.
- The click handler is wired up regardless; disabled items don't fire it, so this is safe.
- If `TIME.Management.Log` does not expose a `WriteMessage(object, string)` overload, fall back to the next-most-appropriate non-error level method (likely `WriteInformation` or similar). Verify by inspecting the type in the IDE or via `Go to Definition` on the existing call at `ScenarioInvoker.cs:192` (`TIME.Management.Log.WriteError(this, ...)`). The only call shape confirmed in this codebase is `WriteError(this, string)`. As a last-resort fallback if no informational method is available across all Source versions, use `WriteError` — the spec's "log one line" requirement is still satisfied even if the level is semantically slightly off.

- [ ] **Step 4: Build the solution**

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln
```

Expected: clean build with no warnings. If the chosen log method name is wrong, the build will fail with a clear error — fix by selecting the correct `TIME.Management.Log` method.

- [ ] **Step 5: Commit**

```bash
git add FlowMatters.Source.Veneer/ReportingMenu.cs
git commit -m "Disable .veneer menu items when scenario filter does not match active scenario"
```

---

### Task 3: Build against all installed Source versions

**Files:** none modified — this is a multi-version build sanity check.

- [ ] **Step 1: Run the multi-version build**

```bash
build.bat
```

This invokes `python compile_all.py --refpath ..\Output Veneer.sln` and compiles against every Source installation discovered under `C:\Program Files\eWater\Source*`. Conditional symbols (`V3`, `V4_0`, `V4_1`, ...) are applied per build.

Expected: clean compile across all versions. The change does not touch any `#if`-guarded API, so all versions should succeed.

- [ ] **Step 2: If any version fails, investigate**

Most likely cause of a per-version failure is a `RiverSystem.Forms.MainForm.CurrentScenario` API difference, which we did not introduce — the existing `ProjectLoadListener.cs:72` already uses it under the same `#if V4 && BEFORE_V4_3` guard. The new `MainForm.Instance.CurrentScenario` access in `ReportingMenu.cs` will be compiled for every Source version, so it can fail on versions where `ProjectLoadListener` currently guards the call.

If a guard is needed, wrap the live-scenario read and the disable-block in the same `#if` pattern. Around the new lines in `PopulateReportMenu`:

```csharp
#if V4 && BEFORE_V4_3
            // CurrentScenario API not available; addons remain unconditionally enabled
            // on these older Source versions.
#else
            var currentScenario = MainForm.Instance.CurrentScenario;
#endif
```

And around the disable check:

```csharp
#if V4 && BEFORE_V4_3
#else
            if (!VeneerConfiguration.AddonAppliesTo(addon, config, currentScenario))
            {
                // ... disable + tooltip + log as in Task 2 Step 3 ...
            }
#endif
```

This preserves the spec's behavior on every version where it can be implemented, and falls back gracefully (all addons enabled) on versions where the API isn't available — matching the pre-feature behavior on those versions. Do not commit anything that breaks an existing-version build.

- [ ] **Step 3: Commit only if guards were needed**

If a version-specific guard had to be added, commit it:

```bash
git add FlowMatters.Source.Veneer/ReportingMenu.cs
git commit -m "Guard CurrentScenario access for versions where API is not available"
```

Otherwise this task produces no commit.

---

### Task 4: Manual verification in Source GUI

**Files:** none modified.

This task is the verification gate. Veneer has no automated GUI tests; the spec's "Verification (manual)" section enumerates the cases.

- [ ] **Step 1: Prepare a test project**

Create or copy a Source `.rsproj` containing at least two scenarios. For concreteness, use scenarios named `Operations` and `Calibration`. Place a `.rsproj.veneer` next to it with the following content:

```jsonc
{
  "targetScenario": "Operations",
  "addons": [
    { "name": "Default Tool",       "type": "exe", "path": "tools/default.bat", "menu": "Reporting" },
    { "name": "Top-level Gated",    "type": "exe", "path": "tools/op.bat",      "menu": "Models" },
    { "name": "Per-addon Gated",    "type": "exe", "path": "tools/cal.bat",     "menu": "Models",
      "scenario": "Calibration" }
  ]
}
```

(Provide the referenced `.bat` files as no-ops; they are not invoked during verification of menu state.)

- [ ] **Step 2: Backwards compatibility — existing `.veneer` files**

Temporarily remove `targetScenario` and any per-addon `scenario` from the file. Open the project in Source. In each scenario, confirm every addon is enabled with no tooltip. Restore the fields.

- [ ] **Step 3: Top-level `targetScenario` only**

In the `.veneer` above, ensure the `Per-addon Gated` entry has its `scenario` field removed. Open the project, activate `Operations`, open the `Models` menu — both addons enabled. Activate `Calibration`, reopen `Models` — both addons disabled with tooltip `Requires scenario 'Operations' to be active`.

- [ ] **Step 4: Per-addon `scenario` only**

Remove `targetScenario` from the file, keep `Per-addon Gated`'s `scenario: "Calibration"`. With `Operations` active, `Per-addon Gated` is disabled with tooltip `Requires scenario 'Calibration' to be active`; the other addons are enabled. With `Calibration` active, `Per-addon Gated` is enabled and the others remain enabled.

- [ ] **Step 5: Both — per-addon overrides top-level**

Use the original config (both fields present). With `Operations` active: `Top-level Gated` enabled, `Per-addon Gated` disabled (tooltip names `Calibration`). With `Calibration` active: `Top-level Gated` disabled (tooltip names `Operations`), `Per-addon Gated` enabled.

- [ ] **Step 6: Case-insensitive match**

Change `targetScenario` to `"operations"` (lowercase). Activate the `Operations` scenario — `Top-level Gated` is enabled. The match must be case-insensitive.

- [ ] **Step 7: Menu-bar stability**

With the original config, switch back and forth between `Operations` and `Calibration`. The set of top-level menus in the menu bar (`Reporting`, `Models`, etc.) must not change — only the enabled/disabled state of items inside each dropdown.

- [ ] **Step 8: Tooltip content**

Hover over each disabled item and confirm the tooltip shows `Requires scenario '<name>' to be active` with the configured filter scenario name (the one the user must activate, not the one currently active).

- [ ] **Step 9: Log output**

Inspect Source's log output (the same destination used for `TIME.Management.Log.WriteError` calls — typically a log panel or file in the Source install). Each dropdown-open of a menu containing a disabled addon should emit one line per disabled addon with the addon name, required scenario, and current scenario.

- [ ] **Step 10: Document any deviations**

If any verification step fails, do NOT mark the task complete. Capture the failing case and return to the relevant earlier task to fix.

- [ ] **Step 11: Commit verification notes (optional)**

If you took notes during verification that future maintainers should see, append them to `veneer-testing.md` and commit:

```bash
git add veneer-testing.md
git commit -m "Document scenario-scoping manual verification cases"
```

---

## Out of scope

- Porting to `master` (CoreWCF / .NET 8) — separate plan, follows `branch-porting-guide.md`.
- Wildcard or multi-scenario filters.
- Scenario-gating of `VeneerOptions` (autoStart / allowScripts / defaultPort).
- Reacting to scenario-change events from Source (GUI scenario-switching for Veneer is a known rough edge, out of scope).
