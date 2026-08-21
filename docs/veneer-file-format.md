# `.veneer` File Format

A `.veneer` file is an optional JSON sidecar to a Source `.rsproj` project. It customises how Veneer presents itself inside the Source GUI: which menus appear, what addon tools are launchable from those menus, and which scenarios those tools apply to. It also carries a few server-level defaults.

## Filename and discovery

Veneer resolves configuration from **three layers**, merged into one effective
configuration. Every file uses the format described below.

| # | Layer | Where |
|---|-------|-------|
| 1 | Home project | `<configDir>/<project>.rsproj.veneer` |
| 2 | Sidecar | `<projectDir>/<project>.rsproj.veneer` |
| 3 | Global | `<configDir>/global.veneer` |

All three are **additive** — none replaces another. A file in your configuration
directory that supplies only `env` leaves the sidecar's addons untouched.

`<configDir>` is the `VENEER_CONFIG_DIR` environment variable if set, otherwise
`%USERPROFILE%\.veneer`. Veneer never creates it; a missing directory simply
contributes no layers.

```
C:\models\ExampleProject.rsproj              the project
%USERPROFILE%\.veneer\ExampleProject.rsproj.veneer           (layer 1 — home project)
C:\models\ExampleProject.rsproj.veneer       sidecar         (layer 2)
%USERPROFILE%\.veneer\global.veneer                          (layer 3)
```

The match is exact: a `.veneer` file's name is the `.rsproj` filename with
`.veneer` appended. If no file exists anywhere, Veneer behaves with built-in
defaults — no addons, default port, scripts disabled, single `Reporting` menu.

Files are loaded from disk every time a relevant menu opens, so edits take effect
on the next dropdown without restarting Source. A malformed file is logged and
skipped; the other layers still apply.

## Global configuration

This is what lets a model be shared over git while each modeller keeps their own
tools: commit the `.rsproj`, and put your own addons in
`%USERPROFILE%\.veneer\`, where a fresh clone cannot disturb them.

Matching is by **file name only**. Two projects with the same file name in
different directories share the same `<configDir>/<name>.rsproj.veneer`.

### How the layers combine

| Field | Rule |
|---|---|
| `addons` | Concatenated in layer order, most specific first. No de-duplication — two addons with the same name produce two menu items. |
| `env` | Merged **per key**, most specific layer winning. Overriding one variable does not discard the rest of a layer's block. |
| `targetScenario` | Applies only to the addons **in its own file**. A `targetScenario` in `global.veneer` never gates the project's addons. |
| `options` | Merged field by field, most specific layer winning; a field no layer sets keeps Veneer's own default. |

Layers are listed most specific first, and that one ordering decides both which
layer wins a contested field and the order addons appear in a menu. So a home
project file's addons appear above the sidecar's, and `global.veneer`'s appear
last.

**Relative paths still resolve against the project directory**, in every layer.
A `path` in the home project file resolves exactly as it would in the sidecar.
For a tool that lives with your configuration rather than with the model, use
`%VENEER_CONFIG_DIR%` (see **Injected variables**).

**In a project that has never been saved**, only the global layer applies, and
only `type: "url"` addons can actually be launched — `exe` and `script` addons
report `no project directory is available`, because Veneer refuses to resolve a
program name with no directory to resolve it against.

### Finding out what actually loaded

Three layers resolving to one menu means "which file did this come from?" is a
real question. Veneer answers it in Source's log when a project loads:

```
Veneer configuration: C:\models\ExampleProject.rsproj.veneer, C:\Users\joel\.veneer\global.veneer
```

That line lists the files that contributed, most specific first, and is the only
place `<configDir>` is reported as a resolved path rather than a rule — so it is
also how you confirm where Veneer is looking on a given machine.

`Veneer configuration: none` means no file was found in any layer. A file that
was found but could not be read or parsed is reported separately, on its own line,
and does not stop the other layers from applying.

The line is logged once per project rather than on every scenario change, so
switching scenarios inside one project will not repeat it.

## Shared variables

Any `.veneer` file may carry a top-level `env` block. Its entries apply to **every
addon in every layer**, not just the file's own, which is what lets a committed
sidecar name a variable that a personal file defines:

```jsonc
// ~/.veneer/ExampleProject.rsproj.veneer   (personal, never committed)
{ "env": { "TOOLS_ROOT": "D:\\my\\tools" } }

// ExampleProject.rsproj.veneer             (committed alongside the model)
{ "addons": [ { "name": "Calibrate", "type": "exe",
                "path": "%TOOLS_ROOT%/calibrate.bat" } ] }
```

These become real environment variables for the launched process, so a script can
read `%TOOLS_ROOT%` without it being spliced into `args`.

A value may itself use process variables and Veneer's own — for example
`"TOOLS_ROOT": "%VENEER_CONFIG_DIR%\\tools"`. It may **not** use another `env`
entry: `"SUB": "%TOOLS_ROOT%\\sub"` is left as written, so a cross-reference is
visible rather than dependent on file or key order. An addon's own `env` may use
these values, and wins where both set the same name.

Nothing is reserved, so an `env` block can override `%VENEER_PORT%` and its
siblings. There is rarely a reason to.

## Top-level structure

```jsonc
{
  "targetScenario": "Operations",
  "addons": [ /* see Addons */ ],
  "options":  { /* see Options */ },
  "env":      { /* see Shared variables */ }
}
```

All four top-level fields are optional. An empty object `{}` is valid and equivalent to no file.

| Field            | Type             | Required | Purpose |
|------------------|------------------|----------|---------|
| `targetScenario` | string           | no       | Default scenario filter applied to every addon (per-addon `scenario` overrides this). |
| `addons`         | array of objects | no       | Tools to expose in Source's menu bar. |
| `options`        | object           | no       | Server-level defaults applied to the Veneer hosting control. |
| `env`            | object           | no       | Variables supplied to every addon in every layer. See **Shared variables** above. |

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

| Field              | Type             | Required       | Purpose |
|--------------------|------------------|----------------|---------|
| `name`             | string           | yes            | Text shown on the menu item. |
| `type`             | string           | yes            | `"exe"`, `"script"` or `"url"`. An unrecognised value renders a **disabled** menu item with a tooltip. |
| `path`             | string           | for `"exe"`    | Program or batch file, relative to the directory containing the `.rsproj` unless rooted. |
| `script`           | array of strings | for `"script"` | Command lines, run in one `cmd.exe` session. |
| `url`              | string           | for `"url"`    | Link to open. Must begin with `http://`, `https://` or `mailto:`. |
| `args`             | array of strings | no             | Arguments for an `"exe"`. Veneer quotes each element — do not add your own quotes. |
| `env`              | object           | no             | Environment variables for the launched program. Overrides the injected variables below. |
| `workingDirectory` | string           | no             | Relative to the project directory, and defaults to it. |
| `menu`             | string           | no             | Where the item appears in the menu bar. Defaults to `Reporting`. See **Menu paths** below. |
| `scenario`         | string           | no             | Per-addon scenario filter. Overrides `targetScenario`. See **Scenario scoping** below. |
| `allowMultiple`    | bool             | no             | Permit more than one instance at once. When `false` (the default) the menu item is disabled, and labelled `(running)`, while an instance is running. Ignored for `"url"`. |

`path`, `script` and `url` are three ways of saying what an entry does, and an entry must use **exactly one**. Specifying two renders a disabled item with a tooltip naming the pair that conflicted.

### Injected variables

`%VENEER_PORT%`, `%VENEER_PROJECT_DIR%`, `%VENEER_PROJECT_FILE%` and
`%VENEER_CONFIG_DIR%` expand inside `path`, `args`, `workingDirectory`, `url`,
`env` values and script lines.

`VENEER_CONFIG_DIR` is the resolved configuration directory — useful for a global
addon whose tool lives beside the configuration rather than in the model
directory: `"path": "%VENEER_CONFIG_DIR%/tools/calibrate.bat"`.

An unknown `%VAR%` is left as literal text rather than blanked, so a typo is visible rather than silently producing a truncated argument — or, for a `url`, a malformed address in the browser.

`VENEER_PORT` is the *configured* port, not a promise that the server is listening.

### Menu paths

The `menu` field is a pipe-delimited path. The first segment names a top-level entry in Source's menu bar; subsequent segments name nested sub-menus, created on demand.

| `menu` value             | Result |
|--------------------------|--------|
| absent / empty / whitespace / `"\|"` | Item appears under the default `Reporting` menu. |
| `"Reporting"`            | Same as default. |
| `"Models"`               | A new top-level `Models` menu is created; the item appears in it. Its position in the menu bar follows the order menus first appear in the file — see below. |
| `"Models|Calibration"`   | Item appears under `Models → Calibration`. |
| `"Models|Calibration|Daily"` | Item appears under `Models → Calibration → Daily`. Arbitrary nesting depth is supported. |

Top-level menus are created up-front based on every `menu` value in the file, so menu-bar layout is stable regardless of which scenario is currently active — an addon that is greyed out by a scenario filter still contributes its menu, in its usual position.

They appear in the menu bar **in the order they first appear in the file**. An addon with no `menu` counts as targeting `Reporting` for this purpose, so a menuless addon at the top of the file puts `Reporting` first. `Reporting` is appended after all file-specified menus when no addon targets it — which happens when it exists only to hold discovered HTML reports.

Naming a menu that Source itself already owns (`Tools`, `File`, and so on) is **not supported**: Veneer binds to the existing menu wherever Source placed it, and addons under that name are not populated.

### Launching exe addons

When the user clicks an enabled addon, Veneer:

1. Expands `%VAR%` references in `path`, `args` and `workingDirectory`.
2. Resolves the addon's `path` against the project directory unless it is already rooted.
3. If the path ends in `.bat` or `.cmd`, launches it via `cmd.exe /D /V:OFF /C`, quoting the path and each argument. Otherwise, launches the executable directly with no shell involved.
4. Sets `VENEER_PORT`, `VENEER_PROJECT_DIR` and `VENEER_PROJECT_FILE` on the child process, plus anything in `env`.
5. Opens the Veneer panel if it is closed, because that is where addon output is written. This happens on **every** launch, not merely the first of a session, so a panel the operator has closed comes back with the next click. `type: "url"` addons do not, since they produce no output.
6. Writes `Launching '<name>'...` to that panel, then `Addon '<name>' finished`, or a failure line, when the child process ends. Veneer's own lifecycle lines are `Info` and its failures `Error`, both of which the panel shows at its default minimum Log Level, so they appear without the operator touching the **Log Level** control. The addon's own standard output stays at `Debug` (hidden until the Log Level is lowered) and its standard error at `Warning`.

While an instance is running, the addon's menu item is disabled and labelled `<name> (running)`, unless the addon sets `allowMultiple: true`. The menu is rebuilt each time its dropdown opens, so that label appears and clears as instances start and exit, but only refreshes when the menu is next opened.

Click handlers are wired regardless of whether the item is enabled — disabled menu items never fire them, so this is safe.

### Linking to a page — `type: "url"`

```json
{ "name": "Model wiki", "type": "url", "url": "https://wiki.example.org/catchment", "menu": "Help" }
```

The URL must begin with `http://`, `https://` or `mailto:`. Anything else renders a disabled menu item with a tooltip.

`file://` is **deliberately excluded**. It would admit `file://server/share/tool.exe`, and without it `type: "url"` cannot launch a local program by any spelling. For a document on a network share, serve it over HTTP or use an `exe` addon.

**The scheme must be written literally.** `"url": "https://%HOST%/help"` is fine — the variable may appear anywhere after the scheme — but `"url": "%HELP_URL%"` is rejected, because the entry is validated when the menu is built, before any variable is expanded.

The link opens in whatever application the machine has registered for it. A `mailto:` link on a machine with no mail client will fail, and the failure is logged.

### HTML reports (separate from addons)

Independent of the `addons` block, any `*.htm` / `*.html` file in the project directory is automatically added to the `Reporting` menu — and to that menu only, below any addons the file placed there. Clicking opens it via Veneer's `/doc/<filename>` HTTP endpoint on the configured port. Filenames are prettified for display by replacing underscores with spaces and stripping the extension. This requires no `.veneer` file at all.

Worked examples, the limitations of `type: "script"`, where addon output goes, and how to diagnose a greyed-out item are in [`Samples/addons/README.md`](../Samples/addons/README.md).

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
- Hovering shows the tooltip `Requires scenario '<filter>' to be active.`
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

Server-level defaults applied to Veneer's in-Source web-server control. These are
**never** scenario-gated — no `targetScenario` or per-addon `scenario` affects
them.

They are applied at different moments, which matters if you are watching one
change: `defaultPort` is read when the project loads, `allowScripts` when a Veneer
menu is first opened.

```jsonc
{
  "options": {
    "allowScripts": true,
    "defaultPort": 9876,
    "autoStart": false
  }
}
```

Each field is independent. A field a layer omits falls through to the next
layer, and eventually to Veneer's own default — omitting `allowScripts` no
longer forces it to `false`.

| Field          | Type | Default | Purpose |
|----------------|------|---------|---------|
| `allowScripts` | bool | unset → `false` | Pre-checks the "Allow scripts" toggle on the Veneer control, enabling Python script execution endpoints. Override at runtime via the GUI. `VENEER_ALLOW_SCRIPTS` also sets it, but **only when `VENEER_START_ON_LOAD` is set** — it is read on the auto-start path only, where it overrides this field in either direction. |
| `defaultPort`  | int  | unset → `9876`  | Pre-fills the port number on the Veneer control. Values `≤ 0` are ignored. Override at runtime via the GUI or the `VENEER_PORT` environment variable. |
| `autoStart`    | bool | unset → `false` | **Currently defined in the schema but not consumed by Veneer.** To start Veneer automatically on project load, use the `VENEER_START_ON_LOAD` environment variable. |

## Veneer logo entry

Veneer adds a clickable logo as the last item of the *last* top-level menu it owns. This is purely cosmetic and not configurable.

## Compatibility

The schema is JSON-additive: any unknown field is ignored, and any older `.veneer` file (no `targetScenario`, no per-addon `scenario`) behaves exactly as it did before scenario scoping was added — every addon is unconditionally enabled.
