# Active Scenario Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Veneer GUI plugin follow Source's active scenario — when the user switches scenarios in Source, Veneer silently rebinds the server, rebuilds the addon menus, and labels the status panel with the bound scenario name.

**Architecture:** Extend the existing `ProjectLoadListener` singleton to poll `MainForm.Instance.CurrentScenario` on a 1-second timer. On change, push the new scenario into the existing `WebServerStatusControl.Scenario` setter (which already does stop → clear-menu → restart → repopulate-menu). Defer rebinds while a Veneer-API run is in progress. Add a `Bound to: <name>` label inside the panel.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WPF (in-Source status control), `System.Timers.Timer`, `MainForm.Instance.Invoke` for UI marshalling. No new packages.

**Spec:** `docs/superpowers/specs/2026-05-05-active-scenario-tracking-design.md`

**Branch:** `legacy_ci`. Porting to `master`/CoreWCF is a separate exercise — see the spec's branch-porting section. The `master` `Scenario` setter has a pre-existing `async void` Stop/Start race that this work does not need to address.

**Testing reality:** This repository has no .NET test project; the only automated harness is `veneer-py` which drives the headless `VeneerCmd`. The watcher does not run in headless mode, so verification is through the manual GUI test matrix in the spec. Each task is build-verified by an MSBuild compile (the project has `TreatWarningsAsErrors=true` in Debug, so a clean build is meaningful).

---

## File Structure

**Modified:**
- `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs` — replaces the one-shot `_timer` with a continuous watcher; adds `_lastSeen`, `Classify`, `ScenarioTransition`, and the dispatch logic.
- `FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs` — adds `BoundScenarioName` property and the call site that updates it inside the `Scenario` setter.
- `FlowMatters.Source.Veneer/WebServerStatusControl.xaml` — adds a label row showing `Bound to: <name>`.

**No new files.** Both `ScenarioTransition` enum and `Classify` helper live as `internal` members of `ProjectLoadListener` — the spec explicitly chose this over a separate file because the helper is small and used in exactly one place.

---

## Build commands (referenced throughout)

Single-version build:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln /p:Configuration=Debug
```

The `legacy_ci` build expects Source assemblies in `References/`. If that directory is missing, run `build.bat` once first to populate it (it copies from an installed Source version).

A clean compile (zero warnings, zero errors) is the only automated check available.

---

## Task 1: Add `BoundScenarioName` property to `WebServerStatusControl`

**Why first:** This is a leaf change. The watcher (Task 4) will set this property; building it first means Task 4's dispatch code compiles against a real call site rather than a placeholder. No behaviour change yet — nothing reads the property.

**Files:**
- Modify: `FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs`

- [ ] **Step 1: Add the property and update it inside the existing `Scenario` setter**

In `WebServerStatusControl.xaml.cs`, locate the `Scenario` property at lines 82–100. Add a new field and property near the other state fields (around line 52, alongside `_scenario`), and update the setter to maintain `BoundScenarioName`:

```csharp
private string _boundScenarioName = "(none)";
public string BoundScenarioName
{
    get { return _boundScenarioName; }
    private set
    {
        _boundScenarioName = value;
        if (BoundScenarioLabel != null)
            BoundScenarioLabel.GetBindingExpression(System.Windows.Controls.Label.ContentProperty)?.UpdateTarget();
    }
}
```

`BoundScenarioLabel` is the XAML element added in Task 2; the null-guard is necessary because the setter can fire during XAML parse before the label exists. The pattern matches the existing manual-`UpdateTarget()` style used for `PortTxt` (line 159).

In the existing `Scenario` setter (line 82–100), update `BoundScenarioName` after the new value is assigned:

```csharp
public RiverSystemScenario Scenario
{
    get { return _scenario; }
    set
    {
        if (_scenario != null)
        {
            StopServer();
            ReportingMenu.Instance.ClearMenu();
        }
        _scenario = value;

        BoundScenarioName = _scenario != null ? _scenario.Name : "(none)";

        if (_scenario != null)
        {
            StartServer();
            PopulateMenu();
        }
    }
}
```

- [ ] **Step 2: Build and confirm clean compile**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln /p:Configuration=Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The label binding doesn't exist yet — that's fine because we null-guard.

- [ ] **Step 3: Commit**

```bash
git add FlowMatters.Source.Veneer/WebServerStatusControl.xaml.cs
git commit -m "Track BoundScenarioName on WebServerStatusControl"
```

---

## Task 2: Add the `Bound to:` label row to `WebServerStatusControl.xaml`

**Why second:** Now that the property exists, wire the XAML. Once this lands, opening the panel shows `Bound to: <name>` immediately.

**Files:**
- Modify: `FlowMatters.Source.Veneer/WebServerStatusControl.xaml`

- [ ] **Step 1: Add a row definition and the label**

The existing layout has 3 rows: row 0 (top buttons), row 1 (port + checkboxes), row 2 (log box). Insert a new row between row 1 and row 2 for the `Bound to:` label, shifting the log box to row 3.

Replace the `<Grid.RowDefinitions>` block (lines 19–23) with:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="30"/>
    <RowDefinition Height="30"/>
    <RowDefinition Height="24"/>
    <RowDefinition />
</Grid.RowDefinitions>
```

Add the label after the existing row-1 controls (after line 36, before the log box), spanning all columns:

```xml
<Label Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="7"
       Name="BoundScenarioLabel"
       HorizontalAlignment="Left" VerticalAlignment="Center"
       Content="{Binding Path=BoundScenarioName, StringFormat='Bound to: {0}'}"/>
```

Update the log box's `Grid.Row` from `"2"` to `"3"` (line 38):

```xml
<TextBox Grid.Row="3" Grid.Column="0" Grid.ColumnSpan="7" Name="LogBox" ...
```

- [ ] **Step 2: Build and confirm clean compile**

Run:
```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MsBuild.exe" Veneer.sln /p:Configuration=Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The XAML compiler will generate the `BoundScenarioLabel` field, which the property setter from Task 1 references.

- [ ] **Step 3: Manual smoke check (optional but recommended)**

Launch Source with the freshly built plugin, open a project with one scenario, open the Veneer panel. Confirm:
- The panel layout still looks right — buttons on top, port row below, then a `Bound to: <scenario name>` line, then the log box filling the rest.
- The bound name matches the project's active scenario.
- The server still starts and serves requests as before.

If the layout looks wrong or the label doesn't update, fix before continuing.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/WebServerStatusControl.xaml
git commit -m "Show bound scenario name in Veneer status panel"
```

---

## Task 3: Add `ScenarioTransition` enum and `Classify` helper

**Why third:** Pure function, no Source dependencies, can be implemented and reasoned about in isolation. Task 4 will call it from the timer body. Implementing it standalone keeps Task 4's diff focused on the wiring.

**Files:**
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs`

- [ ] **Step 1: Add the enum and helper to `ProjectLoadListener`**

Inside the `ProjectLoadListener` class (between the existing fields and the constructor), add:

```csharp
internal enum ScenarioTransition
{
    None,
    FirstSighting,
    Rebind,
    Cleared,
    DeferredDueToRun,
}

internal static ScenarioTransition Classify(
    RiverSystem.RiverSystemScenario lastSeen,
    RiverSystem.RiverSystemScenario current,
    bool runInProgress)
{
    if (ReferenceEquals(lastSeen, current))
        return ScenarioTransition.None;

    if (runInProgress)
        return ScenarioTransition.DeferredDueToRun;

    if (lastSeen == null)
        return ScenarioTransition.FirstSighting;

    if (current == null)
        return ScenarioTransition.Cleared;

    return ScenarioTransition.Rebind;
}
```

`ReferenceEquals` is deliberate — see the spec's "Reference vs name equality" section. `using RiverSystem;` is already at the top of the file.

- [ ] **Step 2: Build and confirm clean compile**

Run the build command. Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The new types are unused, but `internal` so no `CS0414` warnings.

- [ ] **Step 3: Eyeball-verify the truth table**

Walk the function by hand against these cases (this is the substitute for the unit tests we don't have):

| `lastSeen` | `current` | `runInProgress` | Expected |
|---|---|---|---|
| null | null | false | None |
| null | A | false | FirstSighting |
| A | A | false | None |
| A | B | false | Rebind |
| A | null | false | Cleared |
| A | B | true | DeferredDueToRun |
| null | A | true | DeferredDueToRun |
| A | A | true | None (ReferenceEquals short-circuits) |

The order of the conditions in `Classify` matters for the last row — `ReferenceEquals` must come before the `runInProgress` check so an unchanged scenario never triggers a deferral.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs
git commit -m "Extract ScenarioTransition classification helper"
```

---

## Task 4: Convert `ProjectLoadListener` to a continuous active-scenario watcher

**Why fourth:** The biggest change. With Tasks 1–3 in place, this task wires the timer, dispatches transitions to the existing setter, and updates the menu when the panel is closed.

This task does **not** yet emit log lines or handle deferred-rebind one-shot logging — those land in Task 5 to keep the diff reviewable.

**Files:**
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs`

- [ ] **Step 1: Replace the timer fields and constructor body**

The current class has a one-shot `_timer` that gets created inside `_pm_ProjectLoaded` and reschedules itself in `_timer_Elapsed` until `CurrentScenario != null`. We're replacing it with a continuous watcher started in the constructor.

Add these fields (alongside `_pm`):

```csharp
private RiverSystem.RiverSystemScenario _lastSeen;
private bool _tickInProgress;
```

Update the constructor to start the continuous timer immediately:

```csharp
protected ProjectLoadListener()
{
    _pm = ProjectManager.Instance;
    if (_pm != null)
    {
        _pm.ProjectLoaded += _pm_ProjectLoaded;
    }

    _timer = new Timer(1000.0);
    _timer.AutoReset = true;
    _timer.Elapsed += _timer_Elapsed;
    _timer.Start();
}
```

`_timer` was previously declared as a field (line 66) and reassigned. Keep the field declaration; it will only be assigned once now, in the constructor.

- [ ] **Step 2: Replace `_pm_ProjectLoaded` body**

The existing handler does three things:
1. File-path normalisation (lines 43–47).
2. Early-return if `e.Project.GetRSScenarios().Length == 0` (lines 49–53).
3. Early-return if `scenarios[0].Loaded` is false (lines 55–58).
4. Sets up the one-shot timer that waits for `MainForm.Instance.CurrentScenario`.

We keep (1). We **deliberately drop (2), (3), and (4)** — they exist solely to gate the one-shot timer's lifecycle, which no longer exists. The continuous watcher independently observes `MainForm.Instance.CurrentScenario` and only acts when it transitions. If a project loads without scenarios, the watcher sees `null` and does nothing; if a scenario later becomes loaded, the watcher picks it up. This is a strict superset of the existing gating behaviour.

```csharp
private void _pm_ProjectLoaded(object sender,
    TIME.ScenarioManagement.EditorState.ProjectActionWithPathEventArgs<RiverSystem.RiverSystemProject> e)
{
    var combined = Path.Combine(Directory.GetCurrentDirectory(), e.Project.FullFilename);
    if (File.Exists(combined))
    {
        e.Project.SetFullFilename(combined);
    }
}
```

- [ ] **Step 3: Replace `_timer_Elapsed` with the watcher loop**

```csharp
private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
{
#if V4 && BEFORE_V4_3
    return;
#else
    if (_tickInProgress) return;
    _tickInProgress = true;
    try
    {
        if (MainForm.Instance == null) return;

        var current = MainForm.Instance.CurrentScenario;
        var runInProgress = SourceService._currentScenarioInvoker != null
                            && SourceService._currentScenarioInvoker.IsRunning;

        var transition = Classify(_lastSeen, current, runInProgress);

        switch (transition)
        {
            case ScenarioTransition.None:
                return;

            case ScenarioTransition.DeferredDueToRun:
                // Task 5 adds one-shot deferred-rebind logging here.
                return;

            case ScenarioTransition.FirstSighting:
                MainForm.Instance.Invoke(new Action(() => ScenarioLoaded()));
                _lastSeen = current;
                return;

            case ScenarioTransition.Rebind:
            case ScenarioTransition.Cleared:
                MainForm.Instance.Invoke(new Action(() => ApplyScenarioChange(current)));
                _lastSeen = current;
                return;
        }
    }
    catch (Exception ex)
    {
        try { TIME.Management.Log.WriteError(this, "Veneer scenario watcher tick failed: " + ex.Message); }
        catch { /* never let logging kill the watcher */ }
    }
    finally
    {
        _tickInProgress = false;
    }
#endif
}
```

The reference to `SourceService._currentScenarioInvoker` requires it to be `internal` (it currently is `private static`). The next sub-step makes it `internal`.

- [ ] **Step 4: Make `_currentScenarioInvoker` accessible**

In `FlowMatters.Source.Veneer/SourceService.cs` (around line 59), change:

```csharp
private static ScenarioInvoker _currentScenarioInvoker;
```

to:

```csharp
internal static ScenarioInvoker _currentScenarioInvoker;
```

This is the only access escalation needed. Both files live in the same assembly, so `internal` is the minimal expansion.

- [ ] **Step 5: Add `ApplyScenarioChange` to `ProjectLoadListener`**

```csharp
private void ApplyScenarioChange(RiverSystem.RiverSystemScenario newScenario)
{
    var control = WebServerStatusControl.ActiveInstance;
    if (control != null)
    {
        control.Scenario = newScenario;
    }
    else
    {
        ReportingMenu.Instance.ClearMenu();
        if (newScenario != null)
        {
            ReportingMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, newScenario);
        }
    }
}
```

`ApplyScenarioChange` runs on the UI thread (the watcher invokes it via `MainForm.Instance.Invoke`). The existing `Scenario` setter does the heavy lifting; the menu-only branch covers the case where no panel is open.

A `using` directive for `FlowMatters.Source.WebServerPanel` is needed to resolve `WebServerStatusControl`. Add it to the top of the file alongside the existing usings.

- [ ] **Step 6: Adjust `ScenarioLoaded` for the new entry path**

The existing `ScenarioLoaded()` (line 85) is unchanged — it still decides between Auto-Start and menu-only. It now runs from inside `_timer_Elapsed` for the FirstSighting case rather than from the old one-shot timer. The behaviour is identical: it inspects `VENEER_START_ON_LOAD` and calls `StartVeneer()` or `PopulateReportingMenu()`.

No code change. Just confirming it survives the refactor.

- [ ] **Step 7: Build and confirm clean compile**

Run the build command. Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Common breakage points to check:
- The `using` for `WebServerStatusControl` is in place.
- `_currentScenarioInvoker` is `internal` not `private`.
- `_timer` is declared once as a field (no shadowed local `_timer = new Timer(...)` inside the constructor declaration list).

- [ ] **Step 8: Commit**

```bash
git add FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs FlowMatters.Source.Veneer/SourceService.cs
git commit -m "Watch active scenario and rebind Veneer on change"
```

---

## Task 5: Emit log lines on transitions and one-shot deferred-rebind notice

**Why fifth:** Now that the dispatch works, surface the transitions in the panel log so the user can see what happened. Also implement the once-per-change throttle for deferred-rebind warnings so the log doesn't fill up while a long run is in progress.

**Files:**
- Modify: `FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs`

- [ ] **Step 1: Add a deferred-rebind throttle field**

Add to `ProjectLoadListener`:

```csharp
private RiverSystem.RiverSystemScenario _deferredRebindTarget;
```

When a deferral happens, store the target scenario reference. Only log if the target reference differs from what was last logged. Reset to `null` on any non-deferred transition.

- [ ] **Step 2: Wire log emission into the transition switch**

Update the switch in `_timer_Elapsed`:

```csharp
case ScenarioTransition.None:
    _deferredRebindTarget = null;
    return;

case ScenarioTransition.DeferredDueToRun:
    if (!ReferenceEquals(_deferredRebindTarget, current))
    {
        _deferredRebindTarget = current;
        var oldName = _lastSeen != null ? _lastSeen.Name : "none";
        var newName = current != null ? current.Name : "none";
        TIME.Management.Log.WriteInfo(this, string.Format(
            "Veneer scenario change detected ({0} → {1}) but a run is in progress; rebind deferred",
            oldName, newName));
    }
    return;

case ScenarioTransition.FirstSighting:
    _deferredRebindTarget = null;
    MainForm.Instance.Invoke(new Action(() => ScenarioLoaded()));
    _lastSeen = current;
    return;

case ScenarioTransition.Rebind:
case ScenarioTransition.Cleared:
    _deferredRebindTarget = null;
    var fromName = _lastSeen != null ? _lastSeen.Name : "none";
    var toName = current != null ? current.Name : "none";
    TIME.Management.Log.WriteInfo(this,
        string.Format("Veneer active scenario changed: {0} → {1}", fromName, toName));
    MainForm.Instance.Invoke(new Action(() => ApplyScenarioChange(current)));
    _lastSeen = current;
    return;
```

The log line is emitted **before** `ApplyScenarioChange` so it appears above the stop/start log spam from `StartServer`.

**Note on log routing:** `WebServerStatusControl.ServerLogEvent` is private and is hooked to the running server's `LogGenerator` event. During a rebind the server is being stopped/restarted, and during deferral or "panel never opened" cases there is no server-bound log channel at all. Routing transition logs through `TIME.Management.Log.WriteInfo(this, ...)` uses Source's application-wide logger, which is consistent with how `ReportingMenu` already logs (e.g. `ReportingMenu.cs:149`). When the panel is open and a server is running, the rebind sequence appears interleaved: the transition line in Source's main log, followed by the server's own stop/start lines in the panel's log box.

- [ ] **Step 3: Build and confirm clean compile**

Run the build command. Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add FlowMatters.Source.Veneer/AutoStart/ProjectLoadListener.cs
git commit -m "Log active-scenario transitions and deferred rebinds"
```

---

## Task 6: Manual GUI verification

**Why last:** All code is in place; this task walks the spec's manual test matrix. Each row that fails sends the worker back to the relevant earlier task with a specific failure mode.

**Setup:**
1. Build with `build.bat` (multi-version) or the single-version MSBuild command above.
2. Copy the built plugin to the target Source installation's `Plugins\CommunityPlugins\` directory.
3. Launch Source.

**Test matrix (from spec §Verification):**

- [ ] **Case 1 — Menu-only tracking, panel never opened**
  - Open a project with two scenarios A and B. Don't open the Veneer panel.
  - Switch active scenario from A to B in Source.
  - Expected: top-level Veneer-managed menus rebuild for B's addons (visible in the main menu strip). No exceptions in Source's log.

- [ ] **Case 2 — Server rebinds on switch**
  - Open the same project. Open the Veneer panel. Confirm `Bound to: A` is shown and the server is running.
  - Switch to scenario B in Source.
  - Expected: log entry `Active scenario changed: A → B`. Server restarts on the same port. `Bound to: B` updates. `curl http://localhost:<port>/` returns B's network rather than A's.

- [ ] **Case 3 — Cleared on scenario close**
  - From Case 2's state (panel open, bound to B), close the active scenario in Source.
  - Expected: log `Active scenario changed: B → none`. Server stops. `Bound to: (none)`. Top-level Veneer menus removed from the menu strip.

- [ ] **Case 4 — Re-bind after a null gap**
  - From Case 3's state, activate a scenario again.
  - Expected: server auto-restarts on the prior port. Label updates. Menus rebuilt.

- [ ] **Case 5 — Deferred rebind during API run**
  - Panel open, server running on A.
  - Trigger a long API run on A (`POST /runs` from `veneer-py` with a slow scenario, or any run that takes several seconds).
  - While the run is in progress, switch to scenario B in Source.
  - Expected: log warning `Scenario change detected (A → B) but a run is in progress; rebind deferred` (logged once, not spammed). Run completes against A. After completion, the next 1-second tick rebinds to B and emits `Active scenario changed: A → B`.

- [ ] **Case 6 — `VENEER_START_ON_LOAD` semantics**
  - Set environment variable `VENEER_START_ON_LOAD=1`. Launch Source. Open a project.
  - Expected: panel auto-launches. Confirm `Bound to: <scenario>`.
  - Switch scenarios.
  - Expected: panel does **not** re-launch. Existing panel rebinds (Auto-Start fires only on `FirstSighting`).

- [ ] **Case 7 — Rapid switching**
  - Panel open, server on A. Switch A → B → A as quickly as Source's UI permits.
  - Expected: no crash. `_lastSeen` settles on A. Server bound to A. Menus reflect A.

- [ ] **Case 8 — Single-scenario project**
  - Open a project with only one scenario, then close it.
  - Expected: no spurious rebinds in the log. Watcher observes one `FirstSighting` and one `Cleared`.

- [ ] **Case 9 — `VeneerCmd` regression check**
  - In the `veneer-py` repo, run the existing pytest harness against a `VeneerCmd` build of this branch.
  - Expected: identical behaviour to before this work. Polling not active in headless mode (`MainForm.Instance == null`).

- [ ] **Case 10 — Initial-bind label**
  - Open a project with scenario A active. Open the Veneer panel for the first time.
  - Expected: `Bound to: A` shown immediately. Server starts on default port. Menus populated.

- [ ] **Case 11 — Plugin-load-into-running-Source (already-active scenario)**
  - Build the plugin, then while Source is already running with a project + scenario open, drop the plugin into the plugins folder. (This may require closing and reopening the project depending on Source's plugin discovery behaviour — fall back to that if hot-discovery doesn't apply.)
  - Expected: first watcher tick classifies `FirstSighting`. Behaviour matches Case 1/Case 10.

- [ ] **Step 12: Commit any tweaks**

If the matrix surfaced cosmetic fixes (label alignment, log wording), commit them as small follow-ups:

```bash
git add <files>
git commit -m "<short imperative subject>"
```

If a test case fails for a non-cosmetic reason, return to the relevant earlier task — do not commit fixes that paper over a real bug.

---

## Out of scope (per spec)

- Multi-instance support / labelling for multiple panels.
- HTTP request middleware that checks current scenario.
- Notifying spawned addon child processes when scenario changes.
- Source-event-based detection (deferred until/unless an event is found).
- Master/CoreWCF port. The `master` `Scenario` setter has a pre-existing async race that this plan does not address.

## Worked-around in this plan

- No .NET test project exists; the `Classify` truth table is verified by inspection. If a test project is added in the future, `Classify` is the natural one-table-test seam.
- `ServerLogEvent` is private to the control; cross-component transition logging routes through `TIME.Management.Log.WriteInfo` instead. This is consistent with how `ReportingMenu` already logs (e.g. line 149 of `ReportingMenu.cs`).
