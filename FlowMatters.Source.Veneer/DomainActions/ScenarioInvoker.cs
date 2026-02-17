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
//using RiverSystem.Tracking;
using TIME.DataTypes;
using TIME.ScenarioManagement.Execution;
using TIME.ScenarioManagement.RunManagement;
using TIME.Winforms.UI.Utils;
using FlowMatters.Source.Veneer.ExchangeObjects;
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

        //private ScenarioRunWindow runControl;
        private object lockObj = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private Task _runningTask;

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

            if(parameters!=null)
                ApplyRunParameters(parameters);

            if (!IsRunnable()) throw new Exception("Scenario not runnable");

            Scenario.RunManager.UpdateEvent = new EventHandler<JobRunEventArgs>(JobRunner_Update);

            ScenarioRunWindow runWindow = null;
            var startOfRun = DateTime.Now;

            if (showWindow)
            {
                runWindow = new ScenarioRunWindow(Scenario);
                runWindow.Show();
                ProjectManager.Instance.SaveAuditLogMessage("Run started at " + DateTime.Now);
            }

            try
            {
                _runningTask = Task.Factory.StartNew(() => Scenario.RunManager.Execute(), _cancellationTokenSource.Token);

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
                        logger?.Invoke(this, "Warning: Simulation task did not respond to cancellation within timeout period.");
                    }

                    throw new OperationCanceledException("Simulation run was cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke(this, "Simulation run was cancelled.");
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

                if (showWindow && runWindow != null)
                {
                    ProjectManager.Instance.SaveAuditLogMessage("Run finished at " + DateTime.Now + " and took " + TimeTools.TimeSpanString(DateTime.Now - startOfRun));
                    runWindow.Close();
                    runWindow.Dispose();
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
                        logger(this, "Cannot set custom run name. Not supported by this version of Source");
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

        private void ApplyRunParameters(RunParameters parameters)
        {
            HashSet<string> skipKeys = new HashSet<string>(new [] { RUN_NAME_KEY});

            RunningConfiguration configuration = Scenario.CurrentConfiguration;
            Type configType = configuration.GetType();
            foreach (var entry in parameters.Params.Where(kvp=>!skipKeys.Contains(kvp.Key)))
            {
                var ri = ReflectedItem.NewItem(entry.Key, configuration);
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
