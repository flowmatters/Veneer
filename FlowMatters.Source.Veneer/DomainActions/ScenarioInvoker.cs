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
using RiverSystem.Controls;
using RiverSystem.Controls.ModelRun;
using RiverSystem.DataManagement.DataManager;
//using RiverSystem.Tracking;
using TIME.DataTypes;
using TIME.ScenarioManagement.Execution;
using TIME.ScenarioManagement.RunManagement;
using TIME.Winforms.Utils;
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

            if (parameters != null)
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
                        // Attempt to stop the run manager if it supports cancellation
                        TryStopRunManager();
                        break;
                    }

                    Thread.Sleep(50);
                    Application.DoEvents();
                }

                // Wait a short time for the task to complete after cancellation
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!_runningTask.Wait(5000)) // Wait up to 5 seconds
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

            if ((parameters != null) && parameters.Params.ContainsKey(RUN_NAME_KEY))
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
                // Try to find a Stop/Cancel method on the RunManager using reflection
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
                // Log but don't throw - cancellation might still work through task cancellation
                TIME.Management.Log.WriteError(this, $"Failed to stop run manager: {ex.Message}");
            }
        }
        private static void SetPrivateRunProperty(object run, string field, object value)
        {
            var mi = run.GetType().GetMember(field, BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var ri = ReflectedItem.NewItem(mi, run);
            ri.itemValue = value;
        }

        //private void LogRunEnd(DateTime startOfRun)
        //{
        //    Type t = typeof (RunTracker);
        //    MethodInfo method = t.GetMethod("RunCompleted", BindingFlags.Public | BindingFlags.Static);

        //    try
        //    {
        //        method.Invoke(null, new object[] {Scenario.Project, Scenario, startOfRun});
        //    }
        //    catch(Exception)
        //    {
        //        method.Invoke(null, new object[] { Scenario.Project, startOfRun });
        //    }
        //}

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

        //void JobRunner_BeforeRun(object sender, TemporalRunArgs args)
        //{
        //    Monitor.Enter(lockObj);
        //}

        void JobRunner_AfterRun(object sender, EventArgs e)
        {
//            running = false;
//            Monitor.PulseAll(lockObj);
//            Monitor.Exit(lockObj);
        }

        private bool IsRunnable()
        {
//            if(JobRunner==null)
//                ConfigureScenario();

            //Todo Need to do the cleanup tasks with applicationLayer
//            ProjectManager.Instance.RefreshScenario((RiverSystemScenario)JobRunner.Scenario);

            string message;
            bool runnable = JobRunner.IsRunnable(Scenario.Network,out message);

            // Update the Scenario Start and End for consistency, when saved this is used as a consistencey
            // check for running in the commandline as well (the extents of the scenaro are not well defined when there are no timeseries)
            //if (runnable)
            //{
            //    if (Scenario.Start > Scenario.CurrentConfiguration.StartDate)
            //        Scenario.Start = Scenario.CurrentConfiguration.StartDate;
            //    if (Scenario.End < Scenario.CurrentConfiguration.EndDate)
            //        Scenario.End = Scenario.CurrentConfiguration.EndDate;

            //    runnable = Scenario.CurrentConfiguration.IsRunnable(out message, JobRunner);
            //}

            return runnable;
        }

        //private void ConfigureScenario()
        //{
        //    try
        //    {
        //        if (Scenario.CurrentConfiguration == null )
        //            return;

        //        RunningConfiguration currentConfigPair = Scenario.CurrentConfiguration;//ConfigPair();
        //        ProjectManager.Instance.CurrentScenarioJobRunner = currentConfigPair.JobRunner;
        //        if (ProjectManager.Instance.CurrentScenarioJobRunner == null)
        //        {
        //            var srtc =
        //                new ScenarioRunTemporalCharacteristics(Scenario.TemporalSystemRunner,
        //                                                       SqlDateTime.MinValue.Value,
        //                                                       SqlDateTime.MaxValue.Value);
        //            //                                                                  DateTimePicker.MinDateTime.AddYears(1),
        //            //                                                                  DateTimePicker.MaxDateTime.AddYears(-1), true);
        //            ProjectManager.Instance.CurrentScenarioJobRunner = new ScenarioJobRunner(Scenario, srtc);
        //            currentConfigPair.JobRunner = ProjectManager.Instance.CurrentScenarioJobRunner;
        //        }

        //        ProjectManager.Instance.RefreshScenario(Scenario);
        //        ProjectManager.Instance.RefreshJobRunner(true);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.WriteError(this, "Error occured while trying to configure the scenario for running \n" + ex);
        //        throw;
        //    }
        //}

        //private static ConfigurationRunningPair ConfigPair()
        //{
        //    Type mf = MainForm.Instance.GetType();
        //    FieldInfo toolStripField = mf.GetField("toolStripAnalysisList", BindingFlags.NonPublic | BindingFlags.Instance);
        //    ToolStripComboBox list = (ToolStripComboBox) toolStripField.GetValue(MainForm.Instance );
        //    return (ConfigurationRunningPair)list.SelectedItem;
        //}
    }
}
