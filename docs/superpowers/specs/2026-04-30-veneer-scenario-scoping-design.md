# Scenario-scoped `.veneer` configuration

**Date:** 2026-04-30
**Branch:** legacy_ci (must be ported to master per `branch-porting-guide.md`)

## Background

A Source `.rsproj` project may contain multiple scenarios. Veneer requires an active scenario to be meaningful, so `.veneer`-driven menu items are populated when a scenario is current. The trigger today is the *first* scenario to become active after project load (`ProjectLoadListener` → `ReportingMenu.InitialiseRequiredMenus`).

For one user group, the project contains two scenarios and the tools configured via `.veneer` operate against the second scenario the user typically activates. There is currently no way to express "these menu entries are only meaningful when scenario X is active". The user group needs the ability to scope the configuration — at the file level and per menu item — to a named scenario, with appropriate UX when the named scenario is not active.

GUI scenario-switching after Veneer is initialised is a known rough edge for Veneer-in-Source and is out of scope here. This design only concerns how addon menu items present themselves relative to whatever the active scenario happens to be at the moment a menu opens.

## Goals

- Allow a `.veneer` file to specify a target scenario by name, applying to all addons by default.
- Allow individual addons to override the default with their own scenario constraint.
- Present non-matching addons as disabled menu items (not hidden), with a tooltip naming the required scenario, so the user can discover the tool and understand how to enable it.
- Maintain a stable menu bar regardless of which scenario is active.
- Preserve full backwards compatibility with existing `.veneer` files.

## Non-goals

- Fixing GUI scenario-switching for Veneer broadly (separate concern).
- Scenario-gating of `VeneerOptions` (server-wide settings; out of scope).
- Wildcard or pattern matching of scenario names (YAGNI for the driving use case).
- Multi-scenario filters (an addon belonging to *several* named scenarios). YAGNI.

## Schema additions to `.veneer`

Two new optional string fields, both nullable:

```jsonc
{
  "targetScenario": "Operations",        // OPTIONAL, top-level
  "addons": [
    { "name": "Tool A", "type": "exe", "path": "...", "menu": "Models" },
    { "name": "Tool B", "type": "exe", "path": "...", "menu": "Models",
      "scenario": "Calibration" }       // OPTIONAL, per-addon override
  ],
  "options": { ... }                     // unchanged; never scenario-gated
}
```

`VeneerConfiguration` gains:

```csharp
public string targetScenario;
```

`VeneerAddon` gains:

```csharp
public string scenario { get; set; }
```

Existing `.veneer` files without these fields deserialize unchanged with both fields `null`.

### Resolution rule

For each addon:

```
effectiveFilter = addon.scenario ?? config.targetScenario
```

- If `effectiveFilter` is null/empty → addon is unconditional (current behavior).
- Otherwise the addon applies only when `currentScenario.Name` equals `effectiveFilter`, case-insensitive.

## Filter evaluation

A small helper, e.g. on `VeneerConfiguration` or a static helper class:

```csharp
public static bool AddonAppliesTo(
    VeneerAddon addon,
    VeneerConfiguration config,
    RiverSystemScenario currentScenario)
{
    var filter = addon.scenario ?? config.targetScenario;
    if (string.IsNullOrEmpty(filter)) return true;
    if (currentScenario == null) return false;
    return string.Equals(
        currentScenario.Name,
        filter,
        StringComparison.OrdinalIgnoreCase);
}
```

This is invoked from `ReportingMenu.PopulateReportMenu`, which already runs on `DropDownOpening`. The filter check reads `MainForm.Instance.CurrentScenario` (the live current scenario), **not** the captured `ReportingMenu.Scenario` field.

`ReportingMenu.Scenario` continues to act as the "anchor" used for non-filter purposes (project folder lookup for HTML reports, addon path resolution). This matches the existing assumption that the captured scenario doesn't change for the lifetime of the GUI session.

## Menu bar and item rendering

### At project load (`InitialiseRequiredMenus`)

Unchanged. `RequiredMenus()` continues to enumerate all top-level menus referenced by any addon plus the default `Reporting` menu where appropriate. No scenario awareness is needed at this point — the menu bar is created based purely on the static config, which guarantees stable menu-bar layout regardless of which scenario is active.

`HasMenuContent` is also unchanged: an addon counts as content regardless of its scenario filter, so a menu containing only scenario-gated addons still triggers menu creation.

### At dropdown-open (`PopulateReportMenu`)

The addon set itself is fixed at project load: `config = VeneerConfiguration.Load(Scenario)` continues to use the captured `ReportingMenu.Scenario` field as today. Only the *enabled/disabled* state of each item is dynamic.

For every addon belonging to this menu, the menu item is added unconditionally — addons are never hidden. The click handler is wired up as today.

After adding the item:

```csharp
var filter = addon.scenario ?? config.targetScenario;
var current = MainForm.Instance.CurrentScenario;
if (!VeneerConfiguration.AddonAppliesTo(addon, config, current))
{
    item.Enabled = false;
    item.ToolTipText = $"Requires scenario '{filter}' to be active";
    // log debug line — see Logging
}
```

Disabled `ToolStripMenuItem` instances do not fire their click handlers, so wiring them up unconditionally introduces no behavioural risk.

The Veneer logo addition is unchanged (still attached only to the first menu in `RequiredMenus()`).

## Logging

When an addon is rendered as disabled due to scenario mismatch, log one line via `TIME.Management.Log` — the same logger used by `ScenarioInvoker.cs:192` (`TIME.Management.Log.WriteError(this, ...)`). Use the appropriate non-error level (e.g. `WriteMessage` / `WriteInformation`) for this informational case. Suggested format:

```
Veneer addon '{addon.name}' disabled: requires scenario '{filter}', current is '{currentScenario?.Name ?? "none"}'
```

Logging fires only while the user opens a menu, so volume is bounded by user interaction.

## Backwards compatibility

- `.veneer` files without `targetScenario` or `scenario` deserialize identically and behave identically to today (all addons enabled).
- The `VeneerOptions` block is untouched.
- The captured `ReportingMenu.Scenario` field and its uses are unchanged.
- `RequiredMenus()` returns the same set as today for any pre-existing config.

## Branch porting

This change targets `legacy_ci` (.NET Framework 4.8, classic WCF). It must be ported to `master` (CoreWCF, .NET 8) following `branch-porting-guide.md`. The change is confined to:

- `FlowMatters.Source.Veneer/Addons/VeneerConfiguration.cs` (schema additions, helper)
- `FlowMatters.Source.Veneer/ReportingMenu.cs` (filter evaluation, disabled-item rendering, logging)

No CoreWCF-specific patterns are touched, so the port should be near-mechanical.

## Verification (manual)

Veneer has no automated GUI test harness. Verification uses a Source project with at least two scenarios (e.g. `Operations`, `Calibration`):

1. **Existing `.veneer` files (no new fields)** — addons enabled in every scenario; no regression.
2. **Top-level `targetScenario` only** — addons enabled only when the named scenario is active; disabled with tooltip otherwise.
3. **Per-addon `scenario` only** — the targeted addon respects its filter; other addons unaffected.
4. **Both top-level and per-addon** — per-addon `scenario` overrides `targetScenario`; addons without `scenario` fall back to `targetScenario`.
5. **Case-insensitive match** — `"operations"` in config matches scenario named `"Operations"`.
6. **Tooltip content** — hovering a disabled item shows `Requires scenario '<name>' to be active` with the configured filter name.
7. **Menu bar stability** — switching the active scenario in Source does not add or remove top-level menu strips.
8. **Log output** — disabled addons produce one log line per dropdown-open with the addon name, required scenario, and current scenario.
