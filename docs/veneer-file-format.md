# `.veneer` File Format

A `.veneer` file is an optional JSON sidecar to a Source `.rsproj` project. It customises how Veneer presents itself inside the Source GUI: which menus appear, what addon tools are launchable from those menus, and which scenarios those tools apply to. It also carries a few server-level defaults.

## Filename and discovery

Veneer looks for the configuration alongside the Source project file:

```
my-model.rsproj
my-model.rsproj.veneer
```

The match is exact: the `.veneer` file's name is the `.rsproj` filename with `.veneer` appended. If no such file exists, Veneer behaves with built-in defaults — no addons, default port, scripts disabled, single `Reporting` menu.

The file is loaded from disk every time a relevant menu opens, so edits take effect on the next dropdown without restarting Source.

## Top-level structure

```jsonc
{
  "targetScenario": "Operations",
  "addons": [ /* see Addons */ ],
  "options":  { /* see Options */ }
}
```

All three top-level fields are optional. An empty object `{}` is valid and equivalent to no file.

| Field            | Type             | Required | Purpose |
|------------------|------------------|----------|---------|
| `targetScenario` | string           | no       | Default scenario filter applied to every addon (per-addon `scenario` overrides this). |
| `addons`         | array of objects | no       | Tools to expose in Source's menu bar. |
| `options`        | object           | no       | Server-level defaults applied to the Veneer hosting control. |

## Addons

Each entry in `addons` describes one launchable tool that appears as a menu item under Source's main menu bar.

```jsonc
{
  "name": "Run Calibration Scripts",
  "type": "exe",
  "path": "tools/calibrate.bat",
  "menu": "Models|Calibration",
  "scenario": "Calibration"
}
```

| Field      | Type   | Required | Purpose |
|------------|--------|----------|---------|
| `name`     | string | yes      | Text shown on the menu item. |
| `type`     | string | yes      | Addon kind. Currently only `"exe"` is implemented; other values are silently ignored. |
| `path`     | string | yes      | Path to the executable or batch file, relative to the directory containing the `.rsproj`. |
| `menu`     | string | no       | Where the item appears in the menu bar. Defaults to `Reporting`. See **Menu paths** below. |
| `scenario` | string | no       | Per-addon scenario filter. Overrides `targetScenario`. See **Scenario scoping** below. |

### Menu paths

The `menu` field is a pipe-delimited path. The first segment names a top-level entry in Source's menu bar; subsequent segments name nested sub-menus, created on demand.

| `menu` value             | Result |
|--------------------------|--------|
| absent / empty           | Item appears under the default `Reporting` menu. |
| `"Reporting"`            | Same as default. |
| `"Models"`               | A new top-level `Models` menu is created next to `Reporting`; the item appears in it. |
| `"Models|Calibration"`   | Item appears under `Models → Calibration`. |
| `"Models|Calibration|Daily"` | Item appears under `Models → Calibration → Daily`. Arbitrary nesting depth is supported. |

Top-level menus are created up-front based on every `menu` value in the file, so menu-bar layout is stable regardless of which scenario is currently active.

### Launching exe addons

When the user clicks an enabled addon, Veneer:

1. Resolves the addon's `path` against the project directory.
2. If the path ends in `.bat`, launches it via `cmd.exe /C <path>`. Otherwise, launches the executable directly.
3. Sets the `VENEER_PORT` environment variable on the child process to the port the Veneer HTTP server is listening on, so the addon can call back into Veneer's REST API.
4. Starts the Veneer HTTP server first if it isn't already running.

Click handlers are wired regardless of whether the item is enabled — disabled menu items never fire them, so this is safe.

### HTML reports (separate from addons)

Independent of the `addons` block, any `*.htm` / `*.html` file in the project directory is automatically added to the `Reporting` menu. Clicking opens it via Veneer's `/doc/<filename>` HTTP endpoint. Filenames are prettified for display by replacing underscores with spaces and stripping the extension. This requires no `.veneer` file at all.

## Scenario scoping

A Source project can contain multiple scenarios, but only one is *active* in the GUI at a time. Scenario scoping lets a `.veneer` file declare which scenario its tools are designed for.

**Resolution rule** (per addon):

```
effective filter = addon.scenario   (if present and non-empty)
                 ?? targetScenario  (top-level field)
                 ?? <unconditional>
```

Matching is **case-insensitive** against the active scenario's name (`"operations"` matches `Operations`). If the effective filter is empty or absent, the addon is unconditional and behaves as in earlier versions of Veneer.

**Behavior when filter does not match the active scenario:**

- The menu item is still added to its menu, but rendered **disabled** (greyed out).
- Hovering shows the tooltip `Requires scenario '<filter>' to be active`.
- One log line is written via `TIME.Management.Log` per disabled addon per dropdown-open, naming the addon, the required scenario, and the active scenario.

This makes scenario-specific tools discoverable (the user sees the menu item exists) and self-explanatory (the tooltip tells the user how to enable it) without polluting the menu with broken-looking "missing" items.

**Example — two-scenario project, one user group's tools**

```jsonc
{
  "targetScenario": "Operations",
  "addons": [
    { "name": "Daily Inflows",   "type": "exe", "path": "tools/inflows.bat",   "menu": "Operations" },
    { "name": "Releases Report", "type": "exe", "path": "tools/releases.bat", "menu": "Operations" },
    { "name": "Run Calibration", "type": "exe", "path": "tools/calibrate.bat","menu": "Models",
      "scenario": "Calibration" }
  ]
}
```

When `Operations` is the active scenario: `Daily Inflows` and `Releases Report` are enabled; `Run Calibration` is disabled with a tooltip pointing at `Calibration`.

When `Calibration` is active: only `Run Calibration` is enabled; the two `Operations`-bound items are disabled.

## Options

Server-level defaults applied to Veneer's in-Source web-server control. These are **never** scenario-gated — they take effect whenever the project is loaded.

```jsonc
{
  "options": {
    "allowScripts": true,
    "defaultPort": 9876,
    "autoStart": false
  }
}
```

| Field          | Type | Default | Purpose |
|----------------|------|---------|---------|
| `allowScripts` | bool | `false` | Pre-checks the "Allow scripts" toggle on the Veneer control, enabling Python script execution endpoints. Override at runtime via the GUI or the `VENEER_ALLOW_SCRIPTS` environment variable. |
| `defaultPort`  | int  | `9876`  | Pre-fills the port number on the Veneer control. Values `≤ 0` are ignored. Override at runtime via the GUI or the `VENEER_PORT` environment variable. |
| `autoStart`    | bool | `false` | **Currently defined in the schema but not consumed by Veneer.** To start Veneer automatically on project load, use the `VENEER_START_ON_LOAD` environment variable. |

## Veneer logo entry

Veneer adds a clickable logo as the last item of the *first* top-level menu it owns. This is purely cosmetic and not configurable.

## Compatibility

The schema is JSON-additive: any unknown field is ignored, and any older `.veneer` file (no `targetScenario`, no per-addon `scenario`) behaves exactly as it did before scenario scoping was added — every addon is unconditionally enabled.
