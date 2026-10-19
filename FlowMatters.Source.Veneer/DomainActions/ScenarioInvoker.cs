using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowMatters.Source.WebServer;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumer.Forms;
using RiverSystem.Controls.UI.ModelRun;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Forms;
//using RiverSystem.Tracking;
using TIME.DataTypes;
using TIME.ScenarioManagement.Execution;
using TIME.ScenarioManagement.RunManagement;
using TIME.Winforms.UI.Utils;
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.Veneer.ExchangeObjects;
using RiverSystem.Controls.UI;
using RiverSystem.ScenarioSets.RecorderSets;
using TIME.Management;
using TIME.Tools.Reflection;
#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || V4_2_4 || V4_2_5

#else
using RiverSystem.Options;
#endif

namespace FlowMatters.Source.Veneer
{
    class ScenarioInvoker
    {
        const string RUN_NAME_KEY = "_RunName";
        const string SELECTED_RECORDER_SETS_KEY = RecorderSetSelection.KEY;

        //private ScenarioRunWindow runControl;
        private object lockObj = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private Task _runningTask;

        // Published when RunScenario finishes while its task is still executing: completes once that
        // task has stopped and the run's recorder set selection (if it changed one) is restored.
        // TriggerRun and EvaluateFunctions refuse while it is incomplete.
        private static Task _pendingRestore;

        internal static Task PendingRestore
        {
            get { return Volatile.Read(ref _pendingRestore); }
            set { Volatile.Write(ref _pendingRestore, value); }
        }

        internal static bool IsRestorePending
        {
            get
            {
                var pending = PendingRestore;
                return pending != null && !pending.IsCompleted;
            }
        }

        /// <summary>
        /// Held while Veneer's apply or restore changes SelectedRecorderSets, and while GET
        /// /recorderSets reads it, so that read never sees the list mid-change by Veneer's apply or
        /// restore. Source's own code does not take it.
        /// </summary>
        internal static readonly object SelectionLock = new object();

        private readonly List<string> _restoreErrors = new List<string>();

        /// <summary>
        /// One message per failure to restore this run's recorder set selection. A deferred restore
        /// adds to it after RunScenario has returned.
        /// </summary>
        public string[] RestoreErrors
        {
            get
            {
                lock (_restoreErrors)
                {
                    return _restoreErrors.ToArray();
                }
            }
        }

        /// <summary>
        /// The selection collection a run changed, and a copy of its pre-run contents. Target stays
        /// null until the run changes the selection.
        /// </summary>
        private sealed class SelectionRestore
        {
            public IList<IRecorderSet> Target;
            public List<IRecorderSet> Snapshot;
        }

        //private bool running;
        public RiverSystemScenario Scenario { set; get; }

        private IRunManager JobRunner
        {
            get
            {
                if (Scenario == null)
                {
                    return ProjectManager.Instance.CurrentRiverSystemScenarioProxy.riverSystemScenario.RunManager;
                }
                return Scenario.RunManager;
            }
        }

        public bool IsRunning => _runningTask != null && !_runningTask.IsCompleted;

        public void CancelRun()
        {
            lock (lockObj)
            {
                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }
            }
        }

        public void RunScenario(RunParameters parameters, bool showWindow, ServerLogListener logger)
        {
            lock (lockObj)
            {
                if (IsRunning)
                {
                    throw new InvalidOperationException("A simulation is already running. Cancel the current run before starting a new one.");
                }

                _cancellationTokenSource = new CancellationTokenSource();
            }

            if (Scenario == null)
            {
                MsgTools.ShowInfo(RiverSystemOptions.NEED_PROJECT_OPEN_MESSAGE);
                return;
            }

            ScenarioRunWindow runWindow = null;
            var startOfRun = DateTime.Now;

            // Records each configuration property overridden by this run's parameters, keyed by
            // parameter name, so the pre-run values can be restored once the run finishes.
            var originalConfiguration = new Dictionary<string, object>();
            var selection = new SelectionRestore();

            // The run's task, kept for the finally after it has nulled _runningTask.
            Task runTask = null;

            try
            {
                if (parameters != null)
                {
                    // Validated before anything is changed: an invalid SelectedRecorderSets throws
                    // InvalidRunParameterException with the configuration untouched.
                    var requestedSets = ResolveRecorderSetSelection(parameters);
                    ApplyRunParameters(parameters, originalConfiguration);
                    if (requestedSets != null)
                        ApplyRecorderSetSelection(requestedSets, showWindow, selection);
                }

                if (!IsRunnable()) throw new Exception("Scenario not runnable");

                Scenario.RunManager.UpdateEvent = new EventHandler<JobRunEventArgs>(JobRunner_Update);

                if (showWindow)
                {
                    MainForm.Instance.Invoke(new Action(() =>
                    {
                        runWindow = new ScenarioRunWindow(Scenario);
                        runWindow.Show();
                    }));
                    ProjectManager.Instance.SaveAuditLogMessage("Run started at " + DateTime.Now);
                }

                _runningTask = Task.Factory.StartNew(() => Scenario.RunManager.Execute(), _cancellationTokenSource.Token);
                runTask = _runningTask;

                while (!_runningTask.IsCompleted)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        TryStopRunManager();
                        break;
                    }

                    Thread.Sleep(50);
                    Application.DoEvents();
                }

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!_runningTask.Wait(5000))
                    {
                        logger?.Invoke(this, "Warning: Simulation task did not respond to cancellation within timeout period.", LogLevel.Warning);
                    }

                    throw new OperationCanceledException("Simulation run was cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke(this, "Simulation run was cancelled.", LogLevel.Warning);
                throw;
            }
            finally
            {
                lock (lockObj)
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    _runningTask = null;
                }

                // Run parameters apply to this run only. Restore the scenario configuration to its
                // pre-run state (on success, failure, or cancellation) so a subsequent run that
                // omits parameters is unaffected, and so a run never silently mutates — or, if the
                // project is later saved, persists — the scenario's configuration.
                RevertRunParameters(originalConfiguration, logger);

                if (runTask != null && !runTask.IsCompleted)
                {
                    // Execute is still running: after a cancel whose wait timed out, or after any
                    // exception that left the task executing. The selection's restore (a no-op when
                    // this run changed nothing) runs once the task stops, so the selection never
                    // changes under a running Execute. It is published whether or not there is
                    // anything to restore, so POST /runs and evaluate refuse until the task stops.
                    PendingRestore = runTask.ContinueWith(
                        _ => RestoreRecorderSetSelection(selection, showWindow), TaskScheduler.Default);
                    logger?.Invoke(this, "Warning: the simulation task is still stopping; new runs are refused until it has.", LogLevel.Warning);
                }
                else
                {
                    RestoreRecorderSetSelection(selection, showWindow);
                }

                if (showWindow && runWindow != null)
                {
                    ProjectManager.Instance.SaveAuditLogMessage("Run finished at " + DateTime.Now + " and took " + TimeTools.TimeSpanString(DateTime.Now - startOfRun));
                    MainForm.Instance.Invoke(new Action(() =>
                    {
                        runWindow.Close();
                        runWindow.Dispose();
                    }));
                }
            }

            if((parameters!=null)&&parameters.Params.ContainsKey(RUN_NAME_KEY))
            {
                try
                {
                    var latestRun = Scenario.Project.ResultManager.AllRuns().Last();
                    var runName = parameters.Params[RUN_NAME_KEY];
                    SetPrivateRunProperty(latestRun, "_runName", runName);
                    SetPrivateRunProperty(latestRun, "_runFullName", runName);
                }
                catch
                {
                    // Ignore. Not supported in all versions of Source
                    if (logger != null)
                        logger(this, "Cannot set custom run name. Not supported by this version of Source", LogLevel.Warning);
                }
            }
        }

        private void TryStopRunManager()
        {
            try
            {
                var runManager = JobRunner;
                var stopMethod = runManager.GetType().GetMethod("Stop") ??
                               runManager.GetType().GetMethod("Cancel") ??
                               runManager.GetType().GetMethod("Abort");

                if (stopMethod != null)
                {
                    stopMethod.Invoke(runManager, null);
                }
            }
            catch (Exception ex)
            {
                TIME.Management.Log.WriteError(this, $"Failed to stop run manager: {ex.Message}");
            }
        }

        private static void SetPrivateRunProperty(object run, string field, object value)
        {
            var mi = run.GetType().GetMember(field, BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var ri = ReflectedItem.NewItem(mi, run);
            ri.itemValue = value;
        }

        private void ApplyRunParameters(RunParameters parameters, Dictionary<string, object> originalValues)
        {
            // SelectedRecorderSets is get-only: ReflectedItem would capture a reference to the live
            // collection and assign nothing. ApplyRecorderSetSelection handles it instead.
            HashSet<string> skipKeys = new HashSet<string>(new [] { RUN_NAME_KEY, SELECTED_RECORDER_SETS_KEY });

            RunningConfiguration configuration = Scenario.CurrentConfiguration;
            foreach (var entry in parameters.Params.Where(kvp=>!skipKeys.Contains(kvp.Key)))
            {
                var ri = ReflectedItem.NewItem(entry.Key, configuration);

                // Capture the pre-run value (in its native type) before overwriting it, so the
                // configuration can be restored once the run completes. Guard against duplicate
                // keys so the first-seen (true pre-run) value is the one preserved.
                if (!originalValues.ContainsKey(entry.Key))
                    originalValues[entry.Key] = ri.itemValue;

                var val = entry.Value;

                if (ri.itemType == typeof (DateTime))
                    val = DateTime.ParseExact(entry.Value.ToString(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
                else if (ri.itemType == typeof (InputSet))
                    val = Scenario.Network.InputSets.FirstOrDefault(ipSet => ipSet.Name == (string)entry.Value);
                else if (ri.itemType == typeof(TimeStep))
                    val = TimeStep.FromName((string) entry.Value);

                ri.itemValue = val;
            }
        }

        private void RevertRunParameters(Dictionary<string, object> originalValues, ServerLogListener logger)
        {
            if (originalValues == null || originalValues.Count == 0)
                return;

            RunningConfiguration configuration = Scenario.CurrentConfiguration;
            foreach (var entry in originalValues)
            {
                try
                {
                    // The captured value is already the property's native type, so it can be
                    // assigned straight back without the conversions applied on the way in.
                    var ri = ReflectedItem.NewItem(entry.Key, configuration);
                    ri.itemValue = entry.Value;
                }
                catch (Exception ex)
                {
                    logger?.Invoke(this, $"Warning: could not restore run parameter '{entry.Key}' to its pre-run value: {ex.Message}", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// The recorder sets the SelectedRecorderSets parameter asks for, as instances from the
        /// manager's Sets, in the caller's order without repeats, and never empty: an empty request
        /// gives [CurrentTree]. Null when the parameter is absent, or when it is empty and there is
        /// no RecorderSetManager or no RiverSystemConfiguration (nothing is changed then). Throws
        /// InvalidRunParameterException for an invalid value, before anything is changed.
        /// </summary>
        private List<IRecorderSet> ResolveRecorderSetSelection(RunParameters parameters)
        {
            object value;
            if (!parameters.Params.TryGetValue(SELECTED_RECORDER_SETS_KEY, out value))
                return null;

            var requested = RecorderSetSelection.ParseNames(value);

            // GetPluginModel never creates the manager. SelectedRecorderSets is not read here: its
            // getter creates the manager when it is absent.
            var manager = Scenario.GetPluginModel<RecorderSetManager>();
            if (manager == null)
            {
                RecorderSetSelection.Resolve(null, -1, requested);
                return null;
            }
            if (Scenario.CurrentRiverSystemConfiguration == null)
            {
                RecorderSetSelection.RequireEmpty(requested, "the current configuration has no recorder set selection");
                return null;
            }

            var available = manager.Sets.ToList();
            var currentTree = manager.CurrentTree;
            var treeIndex = available.FindIndex(set => ReferenceEquals(set, currentTree));

            var indices = RecorderSetSelection.Resolve(available.Select(set => set.Name).ToList(), treeIndex, requested);

            // Each instance comes from mgr.Sets, so the tree is the manager's CurrentTree object
            // itself. The selection is never left empty: Source's getter would then add the tree on
            // the run's own thread, outside SelectionLock.
            var sets = indices.Select(i => available[i]).ToList();
            if (sets.Count == 0)
                sets.Add(currentTree);
            return sets;
        }

        /// <summary>
        /// Replaces the selection in place with <paramref name="sets"/>, in order, after recording
        /// the collection and a copy of its contents in <paramref name="restore"/>.
        /// </summary>
        private void ApplyRecorderSetSelection(List<IRecorderSet> sets, bool runningInGui, SelectionRestore restore)
        {
            OnGuiThread(runningInGui, () =>
            {
                lock (SelectionLock)
                {
                    // Reading SelectedRecorderSets while it is empty adds the Current Recorder Tree
                    // to it, so an empty selection is snapshotted, and restored, as [CurrentTree].
                    var selected = Scenario.CurrentRiverSystemConfiguration.SelectedRecorderSets;
                    restore.Snapshot = new List<IRecorderSet>(selected);
                    restore.Target = selected;

                    selected.Clear();
                    foreach (var set in sets)
                        selected.Add(set);
                }
            });
        }

        /// <summary>
        /// Puts the pre-run contents back into the collection the run changed; does nothing when the
        /// run changed no selection. A failure is added to RestoreErrors and logged at Error through
        /// TIME.Management.Log; it is never thrown.
        /// </summary>
        private void RestoreRecorderSetSelection(SelectionRestore restore, bool runningInGui)
        {
            if (restore.Target == null)
                return;

            try
            {
                OnGuiThread(runningInGui, () =>
                {
                    lock (SelectionLock)
                    {
                        restore.Target.Clear();
                        foreach (var set in restore.Snapshot)
                            restore.Target.Add(set);
                    }
                });
            }
            catch (Exception ex)
            {
                var message = $"Could not restore the recorder set selection to its pre-run value: {ex.Message}";
                lock (_restoreErrors)
                {
                    _restoreErrors.Add(message);
                }
                TIME.Management.Log.WriteError(this, message);
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the Source GUI thread when hosted in the GUI, and on
        /// the calling thread otherwise.
        /// </summary>
        /// <remarks>
        /// Apply and GET /recorderSets run on a service thread and a deferred restore on a
        /// thread-pool thread, while the Source GUI reads the selection on its own thread (its
        /// recorder set controls, a project save). SelectedRecorderSets is a
        /// List&lt;IRecorderSet&gt;, which is not thread-safe, so in the GUI each Clear/Add sequence,
        /// and GET /recorderSets's read, goes through MainForm.Instance.Invoke and cannot interleave
        /// with those reads, as RunScenario already does for the run window. VeneerCmd has no GUI
        /// thread, so the action runs directly there.
        /// </remarks>
        internal static void OnGuiThread(bool runningInGui, Action action)
        {
            var form = runningInGui ? MainForm.Instance : null;
            if (form != null && form.InvokeRequired)
                form.Invoke(action);
            else
                action();
        }

        private void JobRunner_Update(object sender, JobRunEventArgs e)
        {
            if (e.State==JobRunningState.Finished)
                JobRunner_AfterRun(sender,e);
            CurrentSimulationDate = e.CurrentSimulationDate;
        }

        public DateTime CurrentSimulationDate { get; set; }

        public double GetPercentComplete()
        {
            if (JobRunner?.Scenario?.CurrentConfiguration == null)
                return 0;

            var currentConfig = JobRunner.Scenario.CurrentConfiguration;
            var totalDuration = currentConfig.EndDate - currentConfig.StartDate;
            if (totalDuration.TotalSeconds <= 0)
                return 100;

            var elapsedDuration = CurrentSimulationDate - currentConfig.StartDate;
            return Math.Max(0, 100.0 * Math.Min(1, elapsedDuration.TotalSeconds / totalDuration.TotalSeconds));
        }

        void JobRunner_AfterRun(object sender, EventArgs e)
        {
        }

        private bool IsRunnable()
        {
            string message;
            bool runnable = JobRunner.IsRunnable(Scenario.Network,out message);
            return runnable;
        }
    }
}
