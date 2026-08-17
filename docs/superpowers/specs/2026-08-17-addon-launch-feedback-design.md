# Addon launch feedback

## Problem

Clicking a `script` or `exe` addon in Source's menu produces **no visible
indication that anything happened**. The operator clicks, nothing changes, and
the next signal — a browser tab opened by the addon itself — arrives 20–60
seconds later, or never if startup failed.

Before addons moved to inline `script` entries, the launched `.bat` opened a
console window. That window was ugly, but it acknowledged the click and showed
failures. `AddonLauncher.LaunchScript` sets `CreateNoWindow = true`
(`DomainActions/AddonLauncher.cs:130`) — correctly, since it redirects stdout
and stderr to route them into the Veneer panel — so that acknowledgement is
gone with nothing put in its place.

Veneer appears to have a replacement already, but three independent faults
render it inert:

1. **The panel is never opened or raised.** `VeneerMenu.LaunchAddon` calls
   `WebServerStatusControl.Launch()` only `if (Control == null)`
   (`VeneerMenu.cs:206-216`). `Control` is assigned in
   `WebServerStatusControl.PopulateMenu` (`WebServerStatusControl.xaml.cs:114`),
   which runs on **scenario load**, and is never nulled anywhere. By the time an
   operator can click an addon, `Control` is always non-null, so the guard never
   fires. The comment claiming the panel is force-opened describes an intent the
   code does not carry out.
2. **What is logged is filtered out.** Child stdout is written at
   `AddonLogLevel.Debug` (`AddonLauncher.cs:227-229`), and the panel's default
   minimum level is `LogLevel.Info`
   (`WebServerStatusControl.xaml.cs:45,223`). Every line is dropped unless the
   operator finds and changes the Log Level combo.
3. **There is nothing to log anyway.** `AddonScript.Generate` emits
   `@echo off` first (`AddonScript.cs:26`), so cmd echoes no commands, and
   a downstream deployment's own launch line redirects `> "run_<slug>-log.txt" 2>&1`
   so the app's output never reaches the pipe.

The compounding harm is a **double launch**. With no acknowledgement the
operator clicks again, and a second copy of a Dash app starts and fights the
first for its port.

## Goals

Ranked, from the operator's needs:

1. **Confirm the click registered** — immediate, unmissable, and structurally
   preventing the accidental second launch.
2. **Make failures visible** — a start that dies must be noticed in Source, not
   only in a log file nobody opens.
3. **Announce success** — low priority.

Explicitly **not a goal**: a live progress feed during startup. Veneer cannot
know when a Dash server has begun serving, and the operator did not ask to
watch it boot.

## Solution

Fix the three faults so the existing panel path works, then disable the menu
item while the addon is running so a second launch is impossible unless the
addon opts into it.

### Scope

In scope: `AddonLogLevel`, `AddonLauncher`'s completion reporting, the
`VeneerMenu` menu-population path, one new `VeneerAddon` field, and the
`docs/veneer-file-format.md` entry for it.

Out of scope:

- **The REST API.** `VeneerAddon` is not exposed by any endpoint, so there is
  **no `PROTOCOL_VERSION` bump** and no `docs/api/` change — the same
  reasoning as the addon launch modes and url addons designs.
- **Downstream launchers.** A launcher's `launch_commands` redirect means
  the app's own startup chatter still will not reach the panel. Teeing it back
  was considered and deliberately left out: the goals above are met by Veneer's
  own lifecycle lines, and the app's output is verbose enough that routing it
  into the panel would bury them. The downstream app benefits from this design
  with no change on its side, once the plugin is rebuilt.
- **Detecting readiness.** See Goals.

## Schema

`VeneerAddon` (`Addons/VeneerConfiguration.cs:79-99`) gains one optional field:

```csharp
public bool allowMultiple { get; set; }
```

| Field           | Type | Default | Purpose |
|-----------------|------|---------|---------|
| `allowMultiple` | bool | `false` | Permit more than one instance of this addon to run at once. When `false`, the menu item is disabled while an instance is running. |

`allowMultiple` follows the `allowScripts` naming precedent in `options`.
Defaulting to `false` means every `.veneer` file already deployed keeps the
safe single-instance behaviour without being edited — the default is the
restrictive case, so an addon that genuinely supports concurrent instances
declares it rather than inheriting it by accident.

Multiple instances make sense for some addons and not others, which is why this
is per-addon policy rather than a global rule. A Dash app bound to a fixed port
must be single-instance; a report generator need not be.

No validation change. `allowMultiple` on a `type: "url"` addon is meaningless
but harmless, and rejecting it would add a failure mode without preventing one.

## Components

### `AddonLogLevel.Info`

`DomainActions/AddonContext.cs:15-20` gains `Info` between `Debug` and
`Warning`. `VeneerMenu.ControlAddonLog.Write` maps it to `LogLevel.Info`, which
clears the panel's default filter. `SourceAddonLog` stays Error-only — it is
used only on the url path, which emits no lifecycle lines.

Child stdout **stays at `Debug`**. Promoting it would let one chatty addon
drown the lifecycle lines this design exists to make visible; an operator who
wants the detail can still lower the panel's Log Level.

### `IAddonLifecycle`

New, beside `IAddonLog` in `AddonContext.cs`, one method:

```csharp
internal interface IAddonLifecycle
{
    void Finished(VeneerAddon addon);
}
```

`AddonLauncher.Launch` takes one and **guarantees exactly one `Finished` per
call, on every path**: addon validation failure, empty project directory, an
exception from `AddonEnvironment.BuildEffective`, `Process.Start()` throwing,
and normal exit from the completion watcher.

That single guarantee is what makes a stranded disabled menu item impossible.
It is stated as a property of `Launch` rather than left implicit at each call
site because `Launch` has five exit paths today and will grow more; a path that
forgets to report would leave the operator unable to relaunch until Source
restarts, with no error to explain why.

`LaunchUrl` does **not** take one. It starts no process, so there is no
lifetime to track.

### `RunningAddons`

New, owned by `VeneerMenu`. A lock-protected **count** per addon key:

```csharp
string Key(VeneerAddon addon)   // addon.menu + '\0' + addon.name
void   MarkRunning(VeneerAddon addon)   // count++
void   Finished(VeneerAddon addon)      // count--, floored at zero
int    RunningCount(VeneerAddon addon)
```

A **count, not a set**, because `allowMultiple` permits concurrent instances:
with a set, the first instance exiting would clear the state while the second
was still running, and the label would lie. The count also feeds the label
directly (see below).

Decrement below zero is a no-op rather than an exception. `Finished` is called
from a threadpool watcher and must never throw there.

The class holds no WinForms types, so it is unit-testable. It lives in
`VeneerMenu` rather than `AddonLauncher` so the launcher stays free of both UI
and cross-launch state.

**Key collision:** two addons sharing a `menu` and `name` — in different
projects, or across a project change — are treated as one addon. For the
downstream-launcher case this is correct (same app, same port). Elsewhere it is
harmless.

### `AddonMenuItemState`

New, pure:

```csharp
static AddonMenuItemState For(VeneerAddon addon, string invalid,
                              bool appliesToScenario, int runningCount)
// -> Text, Enabled, ToolTipText
```

The whole menu-item policy in one testable function. `PopulateReportMenu`
computes it and assigns three properties.

| Condition | Text | Enabled | Tooltip |
|---|---|---|---|
| `invalid != null` | `name` | no | `Invalid addon: {invalid}` |
| unknown `type` | `name` | no | `Unknown addon type '{type}'` |
| scenario-filtered | `name` | no | `Requires scenario '{filter}' to be active` |
| running, `allowMultiple: false`, count 1 | `name (running)` | **no** | `Already running — close the app to launch it again.` |
| running, `allowMultiple: true`, count 1 | `name (running)` | **yes** | `1 instance already running — launching again will start another.` |
| running, `allowMultiple: true`, count N>1 | `name (N running)` | **yes** | `N instances already running — launching again will start another.` |
| otherwise | `name` | yes | none |

Precedence is top to bottom: an invalid or filtered addon cannot be running, so
the running rows are evaluated last.

This replaces the current inline sequence, which sets state three times in a row
and lets the scenario filter overwrite an invalid addon's tooltip — a defect the
existing code documents in a comment at `VeneerMenu.cs:126-128` and this design
retires with a regression test rather than a comment.

### `VeneerMenu.LaunchAddon`

```
WebServerStatusControl.Launch();          // unconditional
runningAddons.MarkRunning(addon);
log.Write("Launching '<name>'...", Info);
AddonLauncher.Launch(addon, context, log, lifecycle);
```

**`Launch()` becomes unconditional** — the `Control == null` guard is deleted.
It is already idempotent: it reuses `WebServerStatusPanel.ActivePanel` and calls
`ActivateWindow()`, which handles the `HideOnClose` hidden case by re-showing
the panel (`WebServerStatusPanel.cs:50-53,72-88`). A panel appearing or coming
to front **is** the click acknowledgement; the Info line is what it then says.

`MarkRunning` happens on the UI thread before the launch, so the acknowledgement
does not wait on process start.

### `PopulateReportMenu`

Reads `RunningCount` and applies `AddonMenuItemState`. No other change.

## Threading

**`Finished` needs no UI marshalling.** It fires on the threadpool `Task` in
`AddonLauncher.Run`, but only mutates `RunningAddons`. Because
`PopulateReportMenu` is wired to `DropDownOpening` and begins with
`DropDownItems.Clear()` (`VeneerMenu.cs:63,75`), **every menu item is rebuilt
from scratch each time the menu opens** — so the enabled state is re-derived
from the count on the next open and self-corrects.

This is the load-bearing simplification: no `Invoke`, no cross-thread WinForms
access, and no state stored on `ToolStripItem` objects that are discarded on
every dropdown. Storing it on the item would silently do nothing.

The cost is that a dropdown held open while a process exits does not update
live. A menu click closes the dropdown, so this is unobservable in practice.

## Behaviour

| Event | Panel | Menu item |
|---|---|---|
| Click | opens/raises; `Launching 'X'...` at Info | disabled on next open (unless `allowMultiple`) |
| Exit code 0 | `Addon 'X' finished` at Info | re-enabled |
| Non-zero exit | existing Error line, `at line N` intact | re-enabled |
| `Start()` throws | existing Error line | re-enabled immediately, so the operator can fix and retry |
| Validation failure / no project directory | existing Error line | `Finished` fires synchronously on the UI thread; never appears disabled |

**Project change.** `ClearMenu()` does **not** clear `RunningAddons`. The counts
track live OS processes, not menu state: a Dash app survives a project switch,
and a second copy would still contend for its port. Clearing would re-enable the
item and permit exactly the double launch this design prevents.

**Source exits while an addon runs.** The counts die with the process. The child
`cmd.exe` is orphaned — already true today, and unchanged here.

**`exe` addons** get identical treatment; `LaunchExe` runs through the same
`Run`, so the same lifecycle reporting applies.

**`url` addons** are untouched.

## Risks

**A wedged process leaves the item disabled until Source restarts.** Accepted,
and the reason `allowMultiple` exists as an escape hatch for addons where a
second instance is tolerable. The tooltip states why the item is disabled, so
the operator is not left guessing. Mitigating this further — a timeout, or a
force-launch modifier — would reintroduce the double launch for the case the
design exists to prevent.

**A deliberate second instance requires a config change** for addons that did
not previously need one. This is intentional: `allowMultiple` makes
concurrency a declared property of the addon rather than a click away.

**`AddonLogLevel` values shift** when `Info` is inserted after `Debug`. The
enum is `internal` and never persisted or serialised, so no stored data or
external caller depends on the ordinals.

## Testing

**Pure, unit-tested — the bulk of the behaviour:**

- `RunningAddons`: key derivation from `menu` + `name`; increment and decrement
  to zero; decrement below zero is a no-op rather than an exception; concurrent
  mark/finish across threads; an unknown key reports zero.
- `AddonMenuItemState`: the full matrix — valid/invalid × in-scenario/filtered ×
  count 0/1/2 × `allowMultiple` true/false. Includes the regression test for the
  tooltip-overwrite defect at `VeneerMenu.cs:126-128`: an addon that is both
  invalid and scenario-filtered must keep the *invalid* tooltip.

**Integration — `Tests/AddonLauncherIntegrationTests.cs`, which already spawns
real processes:**

- `Finished` called **exactly once** on each of: clean exit, non-zero exit,
  `Start()` failure (bad path), validation failure, empty project directory.
  Each path gets its own test, because once-and-only-once is the load-bearing
  claim of this design.
- Lifecycle lines are emitted at `AddonLogLevel.Info`; child stdout is still
  emitted at `AddonLogLevel.Debug`.

**Not covered, stated plainly rather than papered over:** applying the state in
`PopulateReportMenu`, and the panel raise via `WebServerStatusControl.Launch()`.
Both are reflection into WeifenLuo docking against a live Source instance. They
are kept to the thinnest possible layer — read a pure value, assign three
properties, call one idempotent method — precisely because they can only be
checked by hand.

**Manual verification:** with the Veneer panel closed, click an addon; the panel
must re-show and `Launching 'X'...` must be visible **without touching the Log
Level combo**. Reopen the menu; the item must read `(running)` and be greyed.
Close the app; reopen the menu; the item must be enabled again.
