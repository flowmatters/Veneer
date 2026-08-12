# Active Scenario Tracking in the Veneer GUI Plugin

**Date:** 2026-05-05
**Branch:** `legacy_ci` (and to be ported to `master`/CoreWCF per `branch-porting-guide.md`)
**Status:** Design — pending implementation plan

## Problem

When Veneer runs as a plugin inside the eWater Source GUI and the user has a project containing two or more scenarios, Veneer binds to the scenario that is active at the moment Veneer is started. If the user later activates a different scenario in Source, the running Veneer instance continues to serve the original scenario. External clients (e.g. Python scripts using `veneer-py`) silently receive data from a scenario that the user no longer thinks of as "current".

The Veneer command-line build does not have this problem: it loads a single scenario and never switches.

This is a fix for confusion, not for correctness — the data Veneer returns is correct for the scenario it is bound to, but the user's mental model expects "active scenario in Source" to equal "scenario served by Veneer".

## Goals

- Single Veneer GUI instance always serves the currently active scenario in Source.
- When the user changes the active scenario in the Source GUI, Veneer rebinds silently and logs the transition.
- Menus (`Reporting`, custom top-level menus declared in `.veneer` configs) reflect the active scenario's addons.
- The bound scenario's name is visible in the Veneer status panel.

## Non-Goals

- Multi-scenario concurrent serving. Explicitly deferred. The `WebServerStatusControl.Launch()` duplicate-panel guard remains in place.
- Notifying already-spawned addon child processes when the scenario changes. They keep `VENEER_PORT` and continue to function — they will silently target the new scenario.
- Automated GUI testing.
- Behaviour change in the command-line build (`VeneerCmd`).

## Decisions

- **Switching behaviour:** silent rebind. No prompts, no manual restart.
- **Detection mechanism:** poll `MainForm.Instance.CurrentScenario` on a 1-second timer. Source does not appear to expose an active-scenario-changed event; if one is discovered later, the polling can be replaced with event subscription inside the same component.
- **No-scenario state:** when `CurrentScenario` becomes `null`, stop the server and clear the menus. When a new scenario is activated, start the server on the prior port and rebuild menus.
- **Run-in-progress at switch time:** defer the rebind until the run finishes. The poller checks `SourceService._currentScenarioInvoker?.IsRunning` and, if true, logs a one-shot deferred-rebind warning and skips the tick.
- **Labelling:** a `Bound to: <name>` label inside the `WebServerStatusControl`, plus a log line on every transition.
- **No HTTP middleware** that checks current scenario per request. The lazy menu-on-open pattern handles submenu contents, but top-level menu items must be added/removed eagerly when scenarios change, which requires a GUI-side trigger regardless. A second mechanism would duplicate the polling guarantee.

## Architecture

### Components

#### `ProjectLoadListener` (extended)

Currently a singleton that subscribes to `ProjectManager.Instance.ProjectLoaded` and uses a one-shot `System.Timers.Timer` to wait for `MainForm.Instance.CurrentScenario` to become non-null after a project load.

After this change it becomes the persistent watcher for the active scenario. The existing one-shot timer is replaced with a continuous timer (`AutoReset = true`, 1000 ms). The continuous timer is **started in the singleton's constructor**, not from inside `_pm_ProjectLoaded`. This ensures the watcher observes scenario activations that happen without a fresh `ProjectLoaded` event (e.g. a project is already open when Veneer is plugged in, or scenarios swap in a way Source does not surface as a project load). The existing `_pm_ProjectLoaded` handler stays as-is and is now redundant for menu setup — it remains harmless because the first tick will detect the new scenario and run `ScenarioLoaded()` itself.

`_lastSeen` is initialized to `null`. If Veneer is loaded into a Source process that already has an active scenario, the first tick classifies as `FirstSighting` and runs the existing `ScenarioLoaded()` path — which is the desired behaviour.

On each tick:

1. If `MainForm.Instance == null`, return (defensive — should never happen at GUI runtime).
2. If a previous tick is still in progress (`_tickInProgress` flag), return.
3. Read `current = MainForm.Instance.CurrentScenario`.
4. If `current == _lastSeen` (reference equality), return.
5. If `SourceService._currentScenarioInvoker?.IsRunning == true`, log `"Scenario change detected (… → …) but a run is in progress; rebind deferred"` once per change and return without updating `_lastSeen`.
6. Otherwise, classify the transition:
   - `_lastSeen == null && current != null` → **first sighting**. Run the existing `ScenarioLoaded()` path (decides `VENEER_START_ON_LOAD` auto-start vs menu-only).
   - `_lastSeen != null && current == null` → **cleared**. Push `null` into `WebServerStatusControl.ActiveInstance.Scenario` if the panel exists, else call `ReportingMenu.Instance.ClearMenu()` directly.
   - both non-null and unequal → **rebind**. Push `current` into `WebServerStatusControl.ActiveInstance.Scenario` (which already does stop → clear-menu → restart → repopulate-menu). If the panel does not exist, call `ReportingMenu.Instance.ClearMenu()` followed by `ReportingMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, current)`.
7. Update `_lastSeen = current`.
8. Wrap the entire body in `try/catch`; log any exception via `TIME.Management.Log.WriteError` and continue.

All UI-affecting work (steps 5 onwards) is marshalled onto the UI thread via `MainForm.Instance.Invoke(...)` — matching the existing pattern in `WebServerStatusControl.Launch()`.

**First-time panel-open path is unaffected.** When the user opens the panel for the first time while a scenario is already active, the panel's own constructor sets `Scenario` and starts the server. The watcher sees no transition (`_lastSeen` was already set during the startup `FirstSighting` tick) and does nothing — correct.

A small pure helper is extracted for testability:

```csharp
internal enum ScenarioTransition
{
    None,           // unchanged
    FirstSighting,  // null → A
    Rebind,         // A → B
    Cleared,        // A → null
    DeferredDueToRun,
}

internal static ScenarioTransition Classify(
    RiverSystemScenario lastSeen,
    RiverSystemScenario current,
    bool runInProgress);
```

This keeps the timer body small and pushes the decision logic into a unit-testable function.

#### `WebServerStatusControl` (small additions)

- The existing `Scenario` setter logic is reused unchanged. It already does:
  - When the previous `_scenario` was non-null: `StopServer()` + `ReportingMenu.Instance.ClearMenu()` (using the OLD scenario for menu clearing).
  - Then assigns the new value.
  - When the new value is non-null: `StartServer()` + `PopulateMenu()` (using the NEW scenario).
  - Net effect: setting to `null` stops the server and clears the menu without restarting; setting from `null` to a scenario starts fresh; setting from A to B restarts on the same port and rebuilds menus. Setting `null` over `null` is a no-op.
- A new property `BoundScenarioName` (string) is added with `INotifyPropertyChanged` (or a manual `UpdateTarget()` call as already used for `Port`). Updated whenever the `Scenario` setter runs, set to `(none)` for null.
- The existing `ServerLogEvent` channel is used to emit `"Active scenario changed: <oldName> → <newName>"` (with `(none)` for null). The log call is made from `ProjectLoadListener` after pushing the new value into the setter, so the ordering reads naturally in the log.

#### `WebServerStatusControl.xaml`

A new row containing the `Bound to: <name>` label, bound to `BoundScenarioName`. The existing two-row top section (start/stop/restart + log level + clear; port + allow-remote + allow-scripts) becomes three rows. The log box (currently `Grid.Row="2"`) shifts to `Grid.Row="3"`.

#### `ReportingMenu`

No internal change. The watcher calls existing public methods (`ClearMenu`, `InitialiseRequiredMenus`). The existing `DropDownOpening` re-population (line 58) continues to handle submenu refresh.

#### `SourceService`

No change. The static state (`_sharedScenario`, `_currentScenarioInvoker`, `_runLock`) is already re-seeded by the existing restart path inside `WebServerStatusControl.StartServer()` via `InitializeStaticServiceState()`.

#### `VeneerCmd`

No change. `ProjectLoadListener` is GUI-only by virtue of its `MainForm.Instance` reliance, and the command-line build does not instantiate it.

### Data Flow

#### Project loads, scenario A becomes active

```
ProjectManager.ProjectLoaded
  → ProjectLoadListener._pm_ProjectLoaded (existing)
  → start continuous 1s Timer
  → first tick where CurrentScenario != null:
      _lastSeen = A
      ScenarioLoaded()   // existing path: PopulateReportingMenu() OR StartVeneer()
```

#### User opens panel, starts server

Unchanged: `WebServerStatusControl` ctor → `Scenario` setter → `StartServer()` + `PopulateMenu()`.

#### User switches active scenario A → B

```
Timer tick: current (B) != _lastSeen (A), no run in progress
  → MainForm.Invoke:
      if WebServerStatusControl.ActiveInstance != null:
          ServerLogEvent("Active scenario changed: A → B")
          ActiveInstance.Scenario = B    // existing setter does the work
                                         //   StopServer
                                         //   ReportingMenu.ClearMenu
                                         //   StartServer (same port; static state re-seeded)
                                         //   PopulateMenu
          ActiveInstance.BoundScenarioName = "B"
      else:
          ReportingMenu.Instance.ClearMenu
          ReportingMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, B)
  → _lastSeen = B
```

#### User closes the active scenario (CurrentScenario → null)

```
Timer tick: current == null, _lastSeen == A
  → MainForm.Invoke:
      ServerLogEvent("Active scenario changed: A → none")
      if ActiveInstance != null:
          ActiveInstance.Scenario = null   // existing setter: StopServer + ClearMenu, no restart
          ActiveInstance.BoundScenarioName = "(none)"
      else:
          ReportingMenu.Instance.ClearMenu
  → _lastSeen = null
```

#### Run in progress at switch time

```
Timer tick: current (B) != _lastSeen (A), SourceService._currentScenarioInvoker.IsRunning == true
  → log "Scenario change detected (A → B) but a run is in progress; rebind deferred" (once)
  → _lastSeen unchanged
  ...
  next tick after run completes: classified as Rebind, runs as above
```

### Threading

- `System.Timers.Timer` callbacks run on a thread-pool thread.
- The classification (`Classify`) is pure and runs on the timer thread.
- Any mutation of UI/scenario state (`WebServerStatusControl.Scenario`, `ReportingMenu.Instance.*`, `BoundScenarioName`) is marshalled to the UI thread via `MainForm.Instance.Invoke(...)`, matching `WebServerStatusControl.Launch()` (line 260).
- A `_tickInProgress` flag (or `lock`) guards against reentrant ticks if the timer fires while a rebind is still running.
- `_lastSeen` is only read and written from the timer thread, so no synchronisation is required there.

### Reference vs name equality

Comparison is done on the `RiverSystemScenario` reference, not its `.Name`. A rename does not trigger a spurious rebind, and a fresh scenario object with the same name (e.g. after reload) does trigger one.

## Edge Cases

| Case | Behaviour |
|---|---|
| Timer callback throws | Caught and logged via `TIME.Management.Log.WriteError`. Timer keeps running. |
| Port unavailable on restart | `StartServer` throws. Caught on rebind path; logged; server left stopped; user can manually `Start`. No retry loop. |
| Reentrant ticks | `_tickInProgress` flag skips overlapping ticks. |
| `MainForm.Instance == null` | Tick returns early. Defensive guard for cross-version safety. |
| `VENEER_START_ON_LOAD` set | Auto-start fires only on `FirstSighting`. Subsequent rebinds reuse the existing panel. |
| Repeated `ProjectLoaded` events | Singleton subscription means no double-firing. The watcher continues to observe and rebinds on the next tick. |
| Spawned addon child processes | Continue running on the same port. Now silently target the new scenario. **Documented as expected.** |
| Veneer panel never opened | Watcher routes menu mutations directly through `ReportingMenu.Instance`. No server runs. |
| Rapid A → B → A switching | Each tick classifies independently; final state matches `_lastSeen`. Reentrancy guard prevents nested rebinds. |
| In-flight HTTP request at rebind | Connection drops on `StopServer`. Acceptable — same as today's manual restart. |

## Verification

Automated GUI testing of the Source plugin is not currently supported by the existing pytest harness in `veneer-py` (which drives `VeneerCmd`).

### Manual GUI test matrix

| # | Setup | Action | Expected |
|---|---|---|---|
| 1 | Project with scenarios A, B. Veneer panel never opened. | Switch A → B. | Top-level menus rebuild for B's addons. No errors. |
| 2 | Panel open, server running on A. | Switch to B. | Log line `Active scenario changed: A → B`. Server restarts on same port. `Bound to: B` updates. `GET /` returns B's network. |
| 3 | Panel open, server running on A. | Close active scenario. | Log `… → none`. Server stops. `Bound to: (none)`. Menus cleared. |
| 4 | After (3). | Activate scenario again. | Server auto-restarts on prior port. Label updates. Menus rebuilt. |
| 5 | Panel open, server on A. | Trigger long API run on A. Switch to B mid-run. | Log warning "rebind deferred". Run completes. Next tick rebinds to B. |
| 6 | `VENEER_START_ON_LOAD=1`. | Open project. Then switch scenarios. | Auto-Start fires once on initial sighting only. Subsequent switches rebind without re-launching the panel. |
| 7 | Panel open. | Switch A → B → A rapidly. | No crash. Final state bound to A. |
| 8 | Single-scenario project. | Open and close it. | No spurious rebinds. |
| 9 | `veneer-py` test harness against `VeneerCmd` build. | Run existing tests. | Unchanged behaviour — polling not active in headless mode. |
| 10 | Project with scenario A active. Open Veneer panel for the first time. | (no scenario switch) | `Bound to: A` shown immediately. Server starts on default port. Menus populated. |
| 11 | Veneer plugin loaded into a Source process that already has a project + scenario open. | (no user action) | First tick classifies as `FirstSighting`, runs `ScenarioLoaded()` exactly once. Behaviour matches the post-`ProjectLoaded` path. |

### Unit-testable seam

`ProjectLoadListener.Classify(RiverSystemScenario lastSeen, RiverSystemScenario current, bool runInProgress)` is pure, has no Source/WPF dependencies, and exhaustively covers the transitions in a single function. If a test project is added later, it is a one-table-driven test.

## Branch porting

This change lives entirely in the `legacy_ci` branch's `ProjectLoadListener` and `WebServerStatusControl`, both of which exist on the `master` (CoreWCF) branch with similar shapes. Per `branch-porting-guide.md`, the port should be straightforward:
- The `Scenario` setter is structurally the same on both branches.
- `MainForm.Invoke(...)` works the same way.
- `System.Timers.Timer` is unchanged across .NET Framework 4.8 and .NET 8.
- The `Bound to` XAML row is identical.

The main difference is that on `master`, `AbstractSourceServer` exposes `async Task Start()` / `async Task Stop()`, and the `master` `WebServerStatusControl.Scenario` setter calls `StartServer()` / `StopServer()` as fire-and-forget `async void` without awaiting. This means a rebind on `master` can race: `StopServer` is dispatched, then `StartServer` is dispatched immediately, and the new server may attempt to bind the port before the old one has released it. This race exists on `master` today even without active-scenario tracking — manual restart hits it too — but rebinds will exercise it more often. The "Port unavailable on restart" edge case (caught and logged, server left stopped) covers the failure mode.

If the porting work wants to harden this, the fix is to make the `master` `Scenario` setter `await` the stop before starting the new server. That is a separate change from this design and not blocked by it.

## Open Questions

- Does Source expose an `ActiveScenarioChanged` (or similar) event somewhere in `RiverSystem.Forms` / `MainForm` / `ProjectManager` / a view-model? If so, the polling timer can be replaced with an event subscription inside `ProjectLoadListener`. This is a follow-up; polling is sufficient for v1.
- Confirm `SourceService._currentScenarioInvoker` is the correct (and only) signal for "API run in progress". If there are run paths that bypass this static (e.g. `SourceService` instances created outside the standard path), the deferred-rebind logic may need to consult an additional gate.
- Should the 1 s polling continue to fire when no project is loaded (empty Source GUI)? Cost is trivial, but it means the timer keeps logging exceptions if anything inside the tick body misbehaves during early or late lifecycle. Current design says yes; revisit if it generates noise.
