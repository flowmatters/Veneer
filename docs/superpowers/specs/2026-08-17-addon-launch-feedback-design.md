# Addon launch feedback

## Problem

Clicking a `script` or `exe` addon in Source's menu usually produces **no
visible indication that anything happened**. The operator clicks, nothing
changes, and the next signal — a browser tab opened by the addon itself —
arrives 20–60 seconds later, or never if startup failed.

Before addons moved to inline `script` entries, the launched `.bat` opened a
console window. That window was ugly, but it acknowledged the click and showed
failures. `AddonLauncher.LaunchScript` sets `CreateNoWindow = true`
(`DomainActions/AddonLauncher.cs:130`) — correctly, since it redirects stdout
and stderr to route them into the Veneer panel — so that acknowledgement is
gone with nothing put in its place.

Veneer has a replacement, but three independent faults render it inert.

### 1. The panel is raised on the first click of a session only

`VeneerMenu.LaunchAddon` opens the panel `if (Control == null)`
(`VeneerMenu.cs:206-216`). `Control` is assigned **only** in
`WebServerStatusControl.PopulateMenu` (`WebServerStatusControl.xaml.cs:114`),
which requires the panel to exist — and it is never nulled anywhere.

The addon menus, however, are also built by a **panel-free** path:
`ProjectLoadListener.ScenarioLoaded` → `PopulateReportingMenu` →
`InitialiseRequiredMenus` when `VENEER_START_ON_LOAD` is unset
(`ProjectLoadListener.cs:173-186,207-210`), and `ApplyScenarioChange` does the
same when `ActiveInstance == null` (`ProjectLoadListener.cs:156-171`). Neither
sets `Control`.

So the guard is not dead code — it is the documented first-click behaviour
(`VeneerMenu.cs:223-235`, `docs/veneer-file-format.md:99`). The defect is that
`Control` **latches non-null for the rest of the session**. The panel is
`HideOnClose` (`WebServerStatusPanel.cs:50-53`), so an operator who closes it
merely hides it — `Control` stays set, and **every subsequent click silently
fails to re-raise it**. Closing the panel once permanently disables the only
acknowledgement Veneer has.

### 2. What is logged is filtered out

Child stdout is written at `AddonLogLevel.Debug` (`AddonLauncher.cs:227-229`),
mapped to `LogLevel.Debug` (`VeneerMenu.cs:277-279`), while the panel's default
minimum is `LogLevel.Info` (`WebServerStatusControl.xaml.cs:45,223`). Every
line is dropped unless the operator finds and changes the Log Level combo.

### 3. There is nothing to log anyway

`AddonScript.Generate` emits `@echo off` first (`AddonScript.cs:26`), so cmd
echoes no commands. In one downstream deployment the launch line also
redirects `> "run_<slug>-log.txt" 2>&1`, so
the app's output never reaches the pipe at all.

### The compounding harm

With no acknowledgement the operator clicks again, and a second copy of a Dash
app starts and fights the first for its port.

## Goals

Ranked, from the operator's needs:

1. **Confirm the click registered** — and structurally prevent the accidental
   second launch.
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

In scope: `AddonLogLevel`, the lifecycle reporting threaded through
`AddonLauncher`, the `VeneerMenu` menu-population and launch path, one new
`VeneerAddon` field, one scroll behaviour change in `WebServerStatusControl`,
and the documentation listed under **Documentation**.

Out of scope:

- **The REST API.** `VeneerAddon` is referenced only by `Addons/`,
  `AddonLauncher`, `VeneerMenu` and the tests — no endpoint exposes it.
  (`ProjectLoadListener` uses `VeneerConfiguration`, not `VeneerAddon`.) So
  **no `PROTOCOL_VERSION` bump**
  (`ExchangeObjects/VeneerStatus.cs:18`) and no `docs/api/` change, the same
  reasoning as the addon launch modes and url addons designs.
- **Downstream launchers.** A launcher's `launch_commands` redirect (in a
  *different repository*) means the app's own startup chatter still will not
  reach the panel. Teeing it back was considered and deliberately left out:
  the goals above are met by Veneer's own lifecycle lines, and the app's output
  is verbose enough that routing it into the panel would bury them. The
  downstream app benefits from this design with no change on its side, once the
  plugin is rebuilt.
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

## Structure

| File | Change |
|---|---|
| `DomainActions/AddonContext.cs` | `AddonLogLevel` gains `Info`; new `IAddonLifecycle`. |
| `DomainActions/OneShotLifecycle.cs` | **New.** `IAddonLifecycle` wrapper over an `Action<VeneerAddon>`, `Interlocked`-guarded to fire at most once. |
| `DomainActions/AddonLauncher.cs` | `Launch`, `LaunchScript`, `LaunchExe` and `Run` take an `IAddonLifecycle`; watcher gains `try/catch/finally`. |
| `Addons/RunningAddons.cs` | **New.** Per-addon running counts. In `Addons/` because it is free of WinForms and RiverSystem types and references `VeneerAddon` — the dependency direction argued for `AddonUrl` in the url-addons design. |
| `Addons/AddonMenuItemState.cs` | **New.** Pure menu-item state policy. |
| `VeneerMenu.cs` | `LaunchAddon` raises the panel (guarded), marks running and owns the `OneShotLifecycle`; `PopulateReportMenu` applies `AddonMenuItemState`; `ControlAddonLog` maps `Info`; `SourceAddonLog` passes `Info` through. |
| `WebServerStatusControl.xaml.cs` | New private `Append(msg, level, forceScroll)`; `ServerLogEvent` and `LogAddonMessage` both call it. |
| `Tests/RunningAddonsTests.cs`, `Tests/AddonMenuItemStateTests.cs`, `Tests/OneShotLifecycleTests.cs` | **New.** |
| `Tests/AddonLauncherIntegrationTests.cs` | Five existing `Launch` call sites updated (`:103,129,162,180,193`); `FakeLog` records levels; new lifecycle tests. |

Three new source files and three new test fixtures — see
**Porting to `legacy_ci`**.

`RunningAddons` is an **instance field on the `VeneerMenu` singleton**
(`VeneerMenu.cs:22-37`), constructed with it. That is what makes "`ClearMenu()`
does not clear the counts" (see **Behaviour**) a statement about a live object
rather than about static state.

`RunningAddons` and `AddonMenuItemState` are `public`, matching the other types
in `Addons/` (`MenuLayout`, `VeneerConfiguration`). That is a second reason
`RunningAddons` must not implement `IAddonLifecycle`: the interface is
`internal` (`AddonContext.cs:22-25`), so a public type could not implement it
without widening it.

## Components

### `AddonLogLevel.Info`

`DomainActions/AddonContext.cs:15-20` gains `Info` between `Debug` and
`Warning`. `VeneerMenu.ControlAddonLog.Write` maps it to `LogLevel.Info`, which
already exists (`AbstractSourceServer.cs:8-14`) and clears the panel's default
filter.

Child stdout **stays at `Debug`**. Promoting it would let one chatty addon
drown the lifecycle lines this design exists to make visible; an operator who
wants the detail can still lower the panel's Log Level.

**`SourceAddonLog` must pass `Info` through** to `TIME.Management.Log.WriteInfo`,
rather than staying Error-only. It is the fallback for *both* the url path and
the process path whenever `EffectiveControl` is null (`VeneerMenu.cs:256-260`),
and that is reachable on exactly the path where feedback matters most: if
`TryRaisePanel` fails, `ActiveInstance` is null, `AddonLog()` returns
`SourceAddonLog`, and an Error-only sink would discard `Launching 'X'...` too —
leaving the operator with no panel, no line, and a disabled menu item. Its
docstring (`VeneerMenu.cs:288-293`) claims "The URL path emits only errors, so
there is no Debug or Warning traffic to lose here", which this change makes
false; update it in the same edit.

### `IAddonLifecycle` and the once-and-only-once guarantee

New, beside `IAddonLog` in `AddonContext.cs`:

```csharp
internal interface IAddonLifecycle
{
    void Finished(VeneerAddon addon);
}
```

**`Finished` fires when the process ends, not when `Launch` returns.** This is
the point most easily got wrong: `Launch` returns as soon as the process is
started and the watcher task is queued, and it cannot even distinguish a failed
`Start()` from a running process — `Start()` failure is caught inside `Run`
(`AddonLauncher.cs:241-247`), which logs, disposes and returns normally. Firing
`Finished` at `Launch`'s boundaries would decrement the count immediately on
every successful launch, the item would never disable, and the feature would
ship doing nothing.

So the lifecycle object is threaded through `Launch` → `LaunchScript` /
`LaunchExe` → `Run`, and the terminal call sites are:

| Site | Fires |
|---|---|
| `Launch` addon-validation return (`:28`) | synchronously, UI thread |
| `Launch` empty-project-directory return (`:41`) | synchronously, UI thread |
| `Launch` catch (`:53-58`) | synchronously — catches `BuildEffective`, `ResolveWorkingDirectory`, `Expand`, `AddonCommandLine.Compose`, `ApplyEnvironment`, and anything thrown by `Run` before `Task.Run` is reached |
| `Run` `Start()`-failure return (`:241-247`) | synchronously |
| `Run` watcher `finally` (`:290-318`) | on the threadpool |

**The one-shot is owned by `VeneerMenu.LaunchAddon`, not by `Launch`.**
`OneShotLifecycle` wraps an `Action<VeneerAddon>` behind an
`Interlocked.Exchange` flag — first call wins, later calls are no-ops.
`LaunchAddon` constructs it over `runningAddons.Finished`, passes it to
`Launch`, **and uses that same object in its own catch**. Putting the wrapper
inside `Launch` instead would leave `LaunchAddon`'s catch outside the guard, and
a double decrement stays constructible: `Run`'s `Start()`-failure branch reports
→ the next `log.Write` throws (`:243-244`) → `Launch`'s catch reports (a no-op)
→ *its* `log.Write` throws (`:55-57`) → the exception escapes `Launch` →
`LaunchAddon`'s catch decrements a second time. With `allowMultiple: false` the
decrement floor hides it; with two instances live the count goes 3 → 2 → 1 and
the label lies, which is precisely what the count exists to prevent.

One object spanning both layers makes the guarantee structural rather than an
audit of six call sites that will grow to seven.

**The watcher's `try/catch/finally` is required, not stylistic.** `Task.Run(...)`
at `:290` has no continuation and no `await`, so anything thrown inside becomes
an *unobserved* task exception and is silently swallowed. `WaitForExit()`,
`ExitCode` and `log.Write` can all throw there — `ControlAddonLog.Write` reaches
`_originalContext.Post` (`xaml.cs:221`), and `_originalContext` is whatever
`SynchronizationContext.Current` was at construction (`xaml.cs:67`), which can
be null; `TIME.Management.Log.WriteError` (`VeneerMenu.cs:284`) is a second
candidate, and a dispatcher shut down during Source exit a third. (`Post` itself
is fire-and-forget — an exception *inside* the posted delegate surfaces on the
UI thread, not here.)
Without the `finally` the count is never decremented and the item is disabled
until Source restarts. The watcher becomes:

```
try     { WaitForExit; Flush; if (ExitCode != 0) Error(...) else Info("Addon 'X' finished"); }
catch   { best-effort Error("Addon 'X' could not be monitored: ..."), itself guarded }
finally { try { lifecycle.Finished(addon); } finally { process.Dispose(); } }
```

`process.Dispose()` moves into the `finally` for the same reason it is there at
`:317` today — it must not be skipped by a throw above it.

**The two statements are nested rather than sequential.** `lifecycle.Finished`
invokes a caller-supplied `Action`, so a plain `Finished(); Dispose();` lets a
throwing callback skip the `Dispose` and leak the handle — while the exception
becomes an unobserved task exception, which is precisely what this
`try/catch/finally` exists to prevent. `Finished` still goes first, so the count
is released as early as possible; the inner `finally` guarantees the `Dispose`
either way. In the wiring this design specifies the callback is
`RunningAddons.Finished`, which is documented no-throw — the nesting is what
keeps that a property of the *contract* rather than a coincidence of the current
caller.

`LaunchUrl` does **not** take a lifecycle. It starts no process.

### `RunningAddons`

New, in `Addons/`. A lock-protected **count** per addon key, in a
`Dictionary<string,int>` with `StringComparer.Ordinal` — matching the ordinal
comparisons the menu code already uses for menu names (`VeneerMenu.cs:83,177`).
It does **not** implement `IAddonLifecycle`; `OneShotLifecycle` adapts it, so
the only path that can decrement a count is the guarded one. Not "pure" in the
strict sense — it holds mutable state — but free of WinForms and RiverSystem
types, and therefore unit-testable.

```csharp
static string Key(VeneerAddon addon)    // see below
void   MarkRunning(VeneerAddon addon)   // count++
void   Finished(VeneerAddon addon)      // count--, floored at zero
int    RunningCount(VeneerAddon addon)
```

A **count, not a set**, because `allowMultiple` permits concurrent instances:
with a set, the first instance exiting would clear the state while the second
was still running, and the label would lie. The count also feeds the label.

Decrement below zero is a no-op rather than an exception — `Finished` is called
from a threadpool watcher and must never throw there.

**The key is the normalised menu path, not the raw string:**

```csharp
string.Join("|", MenuLayout.SplitMenuPath(addon.menu)) + '\0' + addon.name
```

Keying on the raw `addon.menu` would be wrong. `SplitMenuPath`
(`Addons/MenuLayout.cs:16-29`) maps null/whitespace to `"Reporting"`, trims each
segment and drops empties, so `null`, `""`, `"Reporting"`, `" Reporting "` and
`"Reporting|"` all render **the same menu item location** while producing five
different raw keys. Two identical-looking items with independent counts would
restore exactly the double launch this design prevents. Normalising means "same
key" is "same rendered location". `addon.menu` may be null and concatenates
cleanly in C#; `SplitMenuPath` handles null itself.

The consequence is that two entries in one `.veneer` sharing a rendered menu
location and `name` — even with different `path` values — are treated as one
addon, so launching either disables both. That is the correct reading of two
menu items the operator cannot tell apart.

The converse is **accepted, not solved**: the same app declared twice under
different menus (say `"Reporting"` and `"Models"`) gets two keys, both enabled,
and can still be double-launched into port contention. The harm is a property of
the *app*, not of the menu item, and Veneer has no way to know two entries
invoke the same thing. Declaring the app once is the fix.

### `AddonMenuItemState`

New, in `Addons/`, pure:

```csharp
static AddonMenuItemState For(VeneerAddon addon, string invalid,
                              bool appliesToScenario, string effectiveFilter,
                              int runningCount)
// -> Text, Enabled, ToolTipText
```

`effectiveFilter` is a **separate parameter** and not derivable from `addon`:
`VeneerConfiguration.EffectiveFilter` falls back to `config.targetScenario` when
the addon carries no `scenario` (`VeneerConfiguration.cs:71-76`), and the
function has no `config`. `PopulateReportMenu` computes it as it does today
(`VeneerMenu.cs:131`) and passes it in.

| Condition | Text | Enabled | Tooltip |
|---|---|---|---|
| `invalid != null` | `name` | no | `Invalid addon: {invalid}` |
| unknown `type` | `name` | no | `Unknown addon type '{type}'` |
| `!appliesToScenario` | `name` | no | `Requires scenario '{effectiveFilter}' to be active` |
| running, `allowMultiple: false` | `name (running)` | **no** | `Already running — close the app to launch it again.` |
| running, `allowMultiple: true`, count 1 | `name (running)` | **yes** | `1 instance already running — launching again will start another.` |
| running, `allowMultiple: true`, count N>1 | `name (N running)` | **yes** | `N instances already running — launching again will start another.` |
| otherwise | `name` | yes | none |

`ToolTipText` on the final row is `null`, not `""` — WinForms shows no tooltip
for either, but `null` is what an item that was never assigned one carries.

Precedence is top to bottom. The single `allowMultiple: false` row covers any
count ≥ 1 — reachable at N > 1 by editing `allowMultiple` true→false between
launches, since `VeneerConfiguration.Load` re-reads the file on every dropdown
open (`VeneerMenu.cs:79`). The label stays `(running)` there rather than
exposing a count the addon has declared it does not support.

The unknown-`type` test **must match the dispatch `switch`** at
`VeneerMenu.cs:105-123`, which is case-sensitive while `Validate` is not — the
asymmetry documented in the url-addons design. Use the same ordinal comparison,
so a state saying "unknown type" and a `switch` attaching a handler can never
disagree.

This replaces the current inline sequence, which sets item state three times in
a row and lets the scenario filter overwrite an invalid addon's tooltip — a
defect the existing code documents in a comment at `VeneerMenu.cs:126-128` and
this design retires with a regression test rather than a comment.

The existing logging side effects stay in `PopulateReportMenu`, not in the pure
function: `LogOnce` for invalid (`:101`) and unknown type (`:121`), and the
per-dropdown `WriteError` for scenario-filtered (`:134-136`, documented at
`docs/veneer-file-format.md:141`). **Running adds no log line** — it is not a
problem, and `PopulateReportMenu` runs on every dropdown open.

The dispatch `switch` (`VeneerMenu.cs:105-123`) also stays, since it attaches
the Click handler — but its `Enabled`/`ToolTipText` assignments in the `default`
arm are **removed**, leaving `AddonMenuItemState` the single writer. Otherwise
the two disagree the moment the table changes, which is the class of defect the
pure function exists to end.

A `type: "url"` entry sharing a rendered menu location and `name` with a running
`script`/`exe` entry gets the same key and is therefore rendered `(running)` and
disabled, even though it launches nothing. Accepted for the same reason as the
collapse itself (see `RunningAddons`): two menu items the operator cannot tell
apart should not behave differently. Editing an addon's `menu` or `name` while
an instance runs orphans its count under the old key and re-enables the item —
`VeneerConfiguration.Load` re-reads on every dropdown (`:79`) — same family as
the `allowMultiple` flip above.

### Ordering of `appliesToScenario` and running

`AddonAppliesTo` is re-evaluated on every dropdown (`VeneerMenu.cs:129`), so an
addon **can** be both running and scenario-filtered: launch it under scenario
`Ops`, then switch scenarios. The filtered row wins and the running state is not
shown. That is deliberate — the operator cannot launch it in either case, and
the scenario reason is the actionable one. The count is untouched and the label
returns when the scenario does.

### `VeneerMenu.LaunchAddon`

```
var once = new OneShotLifecycle(runningAddons.Finished);
runningAddons.MarkRunning(addon);             // FIRST, and outside the try
try {
    TryRaisePanel();                          // guarded, see below
    var log = AddonLog();
    log.Write("Launching '<name>'...", Info);
    AddonLauncher.Launch(addon, BuildAddonContext(), log, once);
} catch (Exception ex) {                      // must not escape a Click handler
    once.Finished(addon);                     // the one-shot, NOT runningAddons
    TIME.Management.Log.WriteError(this, ...);
}
```

**`MarkRunning` is the first statement and sits outside the `try`**, so the
increment and the catch's decrement are trivially balanced. Order matters: with
`MarkRunning` inside the try and anything fallible before it — `TryRaisePanel`,
`AddonLog()`, `BuildAddonContext()` dereferencing `Scenario?.Project` — a throw
would reach the catch and decrement a count that was never incremented. The
floor masks that only at zero; with a genuine second instance live under
`allowMultiple: true` the count would go 2 → 1 and the label would lie.
`MarkRunning` is a dictionary increment under a lock and does not throw.

**The panel raise becomes unconditional** — the `Control == null` guard is
deleted, so a closed (hidden) panel is re-shown on every click.
`WebServerStatusControl.Launch()` is already idempotent: it reuses
`WebServerStatusPanel.ActivePanel` and calls `ActivateWindow()`, which handles
the `HideOnClose` hidden case (`WebServerStatusPanel.cs:50-53,72-88`).

**But it must be guarded**, which it is not today. `Launch()` ends in unguarded
reflection — `GetMethod` can return null and `Invoke` can throw
`TargetInvocationException` (`xaml.cs:345-350`) — all inside
`MainForm.Instance.Invoke`, which rethrows on the calling thread, and
`MainForm.Instance` may itself be null. Today the `Control == null` guard limits
this to at most one call per session; unconditional means every click, at the
top of a `Click` handler where an escaping exception becomes an
unhandled-exception dialog in Source. `TryRaisePanel` catches and logs, matching
the reasoning behind `ShellLink.TryOpen` in the url-addons design.

The outer `try/catch` is belt and braces for the same reason: `Launch` is
*documented* as never throwing (`AddonLauncher.cs:13-17`), but that promise
rests on the caller-supplied `IAddonLog` never throwing, which
`ControlAddonLog.Write` does not guarantee (`VeneerMenu.cs:281-284`). Without
it, a throw between `MarkRunning` and the watcher strands the count incremented
and disables the item permanently.

`MarkRunning` happens on the UI thread before the launch, so the acknowledgement
does not wait on process start. An early-return path then fires `Finished`
synchronously on the same thread — 1 → 0, no hazard.

### Scroll behaviour

`ServerLogEvent` (`xaml.cs:219-235`) appends but scrolls only if the user was
already at the bottom (`:226-233`). Lifecycle lines must be brought into view
regardless, while `Debug` child stdout keeps respecting the operator's
scrollback.

**`ServerLogEvent`'s signature must not change.** It is subscribed as a delegate
— `server.LogGenerator += ServerLogEvent` (`xaml.cs:184`) against
`ServerLogListener(object, string, LogLevel)` (`AbstractSourceServer.cs:42`) —
and C# delegate compatibility requires matching arity. A trailing
`bool forceScroll = false` does **not** satisfy it; on `master`
(`net8.0-windows`) that is
a `CS0123` build break, and the existing `LogLevel level = LogLevel.Info` default
gets away with it only because the arity still matches.

So the body moves to a new private `Append(string msg, LogLevel level, bool
forceScroll)`. `ServerLogEvent` keeps its exact three-parameter signature and
calls `Append(msg, level, forceScroll: false)`; `LogAddonMessage` (`:246-249`)
calls `Append(msg, level, forceScroll: level == LogLevel.Info || level ==
LogLevel.Error)`.

**Not `level >= LogLevel.Info`.** Child **stderr** is logged at
`AddonLogLevel.Warning` (`AddonLauncher.cs:232-235`) → `LogLevel.Warning`, and
`Warning >= Info` — so that predicate would force-scroll every stderr line. A
Dash app writes its entire startup to stderr, which would yank the LogBox to the
bottom on every line and destroy exactly the scrollback this design says it
preserves.

Testing `Info` and `Error` explicitly is precise rather than merely close:
across the addon path those two levels are used **only** by Veneer's own
lines — `Launching 'X'...` and `Addon 'X' finished` at Info, and `could not
start` / `failed with exit code N` at Error — while both child streams are
`Debug` (stdout) and `Warning` (stderr). Failures force-scroll, which is Goal 2.

## Threading

**`Finished` needs no UI marshalling.** It fires on the threadpool `Task` in
`AddonLauncher.Run`, but only mutates `RunningAddons`. Because
`PopulateReportMenu` is wired to `DropDownOpening` and begins with
`DropDownItems.Clear()` (`VeneerMenu.cs:63,75`), **every menu item is rebuilt
from scratch each time the menu opens** — nested submenus included, since they
are children of the cleared collection — so the enabled state is re-derived from
the count on the next open and self-corrects.

This is the load-bearing simplification: no `Invoke`, no cross-thread WinForms
access, and no state stored on `ToolStripItem` objects that are discarded on
every dropdown. Storing it on the item would silently do nothing.

Two boundaries, neither new:

- A dropdown held open while a process exits does not update live. A menu click
  closes the dropdown, so this is unobservable in practice.
- The `DropDownOpening` handler is attached only to menus **Veneer created**
  (`VeneerMenu.cs:60-66`). A `.veneer` naming a menu Source already owns is
  never populated at all — pre-existing, documented at
  `docs/veneer-file-format.md:89`.

## Behaviour

| Event | Panel | Menu item |
|---|---|---|
| Click | opens/raises; `Launching 'X'...` at Info, scrolled into view | disabled on next open (unless `allowMultiple`) |
| Exit code 0 | `Addon 'X' finished` at Info | re-enabled |
| Non-zero exit | existing Error line, `at line N` intact; **no** finished line | re-enabled |
| `Start()` throws | existing Error line; **no** finished line | re-enabled immediately, so the operator can fix and retry |
| Validation failure / no project directory | existing Error line | `Finished` fires synchronously on the UI thread; never appears disabled |

**Project change.** `ClearMenu()` does **not** clear `RunningAddons`. The counts
track live OS processes, not menu state: a Dash app survives a project switch,
and a second copy would still contend for its port. Clearing would re-enable the
item and permit exactly the double launch this design prevents.

**Source exits while an addon runs.** The counts die with the process. The child
`cmd.exe` is orphaned — already true today, and unchanged here.

**`exe` addons** get identical treatment; `LaunchExe` runs through the same
`Run`.

**`url` addons** are untouched.

## Documentation

- **`docs/veneer-file-format.md`** — add the `allowMultiple` row to the addon
  field table (`:50-61`), following its ordering and `Required` conventions.
  `:99` ("Opens the Veneer panel if it is closed, because that is where addon
  output is written") is **currently wrong** — the code does it on the first
  click of a session only — and this design makes it true. Leave the sentence,
  and note it now describes shipped behaviour rather than intent.
- **`Samples/addons/README.md`** — `:131-132` says addon output goes to the log
  at `Debug` and the operator must lower the Log Level; that becomes half-wrong
  once lifecycle lines are at `Info`. `:139-151` ("Diagnosing a menu item that
  does nothing") enumerates eight causes of a greyed-out item and must gain
  *already running*; it is where an operator will look, and the url-addons
  design designates this README as the how-to guide `veneer-file-format.md`
  cross-links to. **Its preamble at `:141-142` must be amended too** — "the same
  reason is written to Source's log once" is false for the new entry, which
  deliberately logs nothing.

## Porting to `legacy_ci`

`master`'s csproj is SDK-style with default globbing, so the new files need no
project change. `legacy_ci` is non-SDK (`<Project ToolsVersion="15.0" …>`, with
explicit `<Compile Include>` lines for `Addons\MenuLayout.cs`,
`Addons\VeneerConfiguration.cs`, `Tests\AddonLauncherIntegrationTests.cs` and
the rest) and needs five new entries: `Addons\RunningAddons.cs`,
`Addons\AddonMenuItemState.cs`, `DomainActions\OneShotLifecycle.cs`,
`Tests\RunningAddonsTests.cs`, `Tests\AddonMenuItemStateTests.cs` and
`Tests\OneShotLifecycleTests.cs`. A file missing from that list compiles on
`master` and silently vanishes on `legacy_ci` — which for a test fixture means
the guarantee it protects goes unchecked there.

The five `Launch` call-site line numbers cited throughout are `master`'s;
`legacy_ci`'s copy of `Tests/AddonLauncherIntegrationTests.cs` may differ, so
locate them by signature rather than by line when porting.

## Testing

**Pure, unit-tested — the bulk of the behaviour:**

- `RunningAddons`: key normalisation — `null`, `""`, `"Reporting"`,
  `" Reporting "` and `"Reporting|"` with the same `name` must all produce **one
  key**, while `"Models"` must not collide with `"Reporting"`; increment and
  decrement to zero; decrement below zero is a no-op rather than an exception;
  concurrent mark/finish across threads; an unknown key reports zero.
- `AddonMenuItemState`: the matrix — valid / invalid / unknown-type ×
  in-scenario / filtered × count 0/1/2 × `allowMultiple` true/false. Includes
  the regression test for the tooltip-overwrite defect at
  `VeneerMenu.cs:126-128` (an addon both invalid and scenario-filtered keeps the
  *invalid* tooltip), the `(false, N>1)` case, and case-sensitivity agreement
  with the dispatch `switch`.

**Integration — `Tests/AddonLauncherIntegrationTests.cs`, which already spawns
real processes.** Its five existing `Launch` call sites (`:103,129,162,180,193`)
gain the new argument; `lifecycle` is **required, not nullable**, so a future
call site cannot silently opt out of the guarantee — the fixture supplies a
recording stub. `FakeLog.Write` (`:30-33`) currently records only `message` and
discards `level`; it must record both, or the level assertions below cannot be
written.

- `Finished` called **exactly once** on each of: clean exit, non-zero exit,
  `Start()` failure, addon-validation failure, empty project directory. Each
  path gets its own test, because once-and-only-once is the load-bearing claim.
- `Finished` fires **after** the process exits, not when `Launch` returns — the
  test that would have caught the failure mode described under
  `IAddonLifecycle`.
- The `Start()`-failure case must use a **non-batch bogus path** (e.g.
  `"nosuch.exe"`). `AddonCommandLine.Compose` routes `.bat`/`.cmd` through
  `cmd.exe`, which always starts (`AddonCommandLine.cs:48-58`), and script mode
  always launches `cmd.exe` (`AddonLauncher.cs:124`) — so the fixture's
  `WriteBat` helper cannot produce this case.
- `OneShotLifecycle` gets its own unit test: the second and subsequent
  `Finished` calls are no-ops, including under concurrent callers.
- `Addon 'X' finished` is emitted at `AddonLogLevel.Info` on clean exit and
  **not** on failure; child stdout is still `AddonLogLevel.Debug`.

`Launching 'X'...` is emitted by `VeneerMenu`, not `AddonLauncher`, so it is
**not** covered by these tests — it falls under manual verification below.

**Not covered, stated plainly rather than papered over:**

- Applying the state in `PopulateReportMenu`, the panel raise, and the scroll
  change. All are WinForms or reflection into WeifenLuo docking against a live
  Source instance, kept to the thinnest possible layer — read a pure value,
  assign three properties, call one guarded method — precisely because they can
  only be checked by hand.
- **The `LaunchAddon` ↔ `Launch` one-shot wiring.** `OneShotLifecycle` is tested
  in isolation and `Launch`'s five terminal sites are tested against an
  unguarded recording stub, but the composite path the design exists for — `Run`
  reports → `log.Write` throws (`:243-244`) → `Launch`'s catch reports → *its*
  `log.Write` throws (`:55-57`) → escapes → `LaunchAddon`'s catch — crosses a
  `VeneerMenu` seam the integration fixture cannot reach. It is guaranteed by
  construction (one object, `Interlocked`-guarded, spanning both layers) rather
  than by test. Anyone moving the one-shot back inside `Launch` will break it
  silently.

**Manual verification:** with the Veneer panel closed, click an addon; the panel
must re-show and `Launching 'X'...` must be visible **without touching the Log
Level combo**. Reopen the menu; the item must read `(running)` and be greyed.
Close the app; reopen the menu; the item must be enabled again. Repeat with the
panel already open and docked, and with `allowMultiple: true`.

## Decisions taken

| Decision | Chosen | Rejected |
|---|---|---|
| Where feedback lives | The existing Veneer panel, un-muted | A visible console window (`CreateNoWindow = false` with redirected streams shows an *empty* window; dropping the redirect loses exit-code reporting and the panel log — trading goal 2 for goal 1) |
| Preventing the double launch | Disable the menu item while running | Label only (does not prevent it); a fixed ~10s disable (a click at t=30s still starts a second copy) |
| Concurrency policy | Per-addon `allowMultiple`, default `false` | A global rule (multiple instances are right for some addons and fatal for others) |
| Running state | Count per addon | Set (the first of several instances exiting would clear the label while others run) |
| Key | Normalised menu path + name | Raw `addon.menu` + name (five spellings of one location produce five keys) |
| `Finished` timing | In `Run`'s watcher `finally` | At `Launch`'s return (fires immediately on success; the item would never disable) |
| Double-fire protection | One-shot owned by `LaunchAddon`, threaded into `Launch` | A one-shot *inside* `Launch` (leaves `LaunchAddon`'s own catch outside the guard — the double decrement stays constructible); relying on the decrement floor (masks it at count 1, corrupts the label above it) |
| Thread safety | None needed — menus rebuild on `DropDownOpening` | Marshalling `Finished` to the UI thread |
| `ClearMenu` on project change | Leave counts alone | Clear them (re-enables the item while the process still holds the port) |
| `RunningAddons` owner | `VeneerMenu` | `AddonLauncher` (would give the launcher cross-launch state and UI concerns) |

## Risks

**A wedged process leaves the item disabled until Source restarts.** Accepted,
and the reason `allowMultiple` exists as an escape hatch for addons where a
second instance is tolerable. The tooltip states why the item is disabled, so
the operator is not left guessing. Mitigating further — a timeout, or a
force-launch modifier — would reintroduce the double launch for the case the
design exists to prevent.

**Goal 1 is weaker than "unmissable" for an already-docked panel.** If the panel
is visible and docked, `ActivateWindow()` is `Activate()` + `BringToFront()` on
an already-visible window and nothing moves; the acknowledgement is the new log
line, which the forced scroll at least guarantees is on screen. The greyed menu
item — the other half — is only observable on the *next* dropdown open, i.e.
after the operator has already decided whether to click again. So the
**structural** protection against the double launch is complete, while the
**visible** acknowledgement is a line of text. Accepted: the alternatives (a
toast window, a status-bar flash) add a WinForms surface and a new failure mode
for a goal the disable already covers.

**A pathological logging failure can re-enable the item early, and leaks a
`Process`.** If `log.Write` throws inside `Run`'s `BeginOutputReadLine` or
`feedStdin` catch blocks, the exception escapes `Run` **before `Task.Run` at
`:290`**, so the one-shot fires from `Launch`'s catch while the process is still
alive: premature re-enable, and a child that is never waited on and never
`Dispose()`d. Both pre-existing; the early re-enable is deliberate, since it is
recoverable where a stranded disable is not. Closing the leak means restructuring
those two catch blocks, which is a wider change than this design justifies.

**`VeneerMenu.Instance.Control` is never nulled.**
`WebServerStatusPanel.Dispose` clears `_activePanel`
(`WebServerStatusPanel.Designer.cs:14-24`), so
`WebServerStatusControl.Launch()` correctly re-creates a genuinely disposed
panel. `WebServerStatusControl.Dispose` clears `_activeInstance`
(`xaml.cs:289-295`) but has **no caller** — `ElementHost` does not dispose its
WPF `Child` — so `_activeInstance` self-corrects only when the next control's
constructor reassigns it (`xaml.cs:61`). `Control` has no clearing at all.

The likely residual harm is not a throw but a **silently invisible line**:
`EffectiveControl` (`VeneerMenu.cs:236-239`) hands back the orphaned control,
`TryRaisePanel` raises the *new* panel, and `Launching 'X'...` lands in a LogBox
nobody can see — defeating Goal 1 on a path this design does not guard. Not
fixed here; nulling `Control` on dispose is a correct, separable change.

**The watcher's own `catch` can re-enable the item while the child still runs.**
If `WaitForExit()` or `ExitCode` throws, the `catch` logs "could not be
monitored", the `finally` fires `Finished` and disposes, and the process carries
on unmonitored with the menu item enabled. Deliberate, and the same trade as the
early re-enable above: an item wrongly enabled is recoverable, one wrongly
disabled is not. Symmetrically, `Run`'s `Start()`-failure branch logs at
`:243-244` *before* `process.Dispose()` at `:245`, so a throwing `log.Write`
there skips the dispose — pre-existing, and low harm since `Start()` failed.

**`AddonLogLevel` values shift** when `Info` is inserted after `Debug`. The enum
is `internal` and never persisted or serialised, so nothing depends on the
ordinals.
