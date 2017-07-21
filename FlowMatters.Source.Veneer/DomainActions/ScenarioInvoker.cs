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
#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5 || GBRSource

#else
using RiverSystem.Options;
#endif

namespace FlowMatters.Source.Veneer
{
    class ScenarioInvoker
    {
        //private ScenarioRunWindow runControl;
        private object lockObj = new object();
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

        public void RunScenario(RunParameters parameters,bool showWindow)
        {
            if (Scenario == null)
            {
                MsgTools.ShowInfo(RiverSystemOptions.NEED_PROJECT_OPEN_MESSAGE);
                return;
            }

//            ProjectManager.Instance.SaveAuditLogMessage("Open run scenario window");
//            Scenario.outputManager = new Obsolete.Recording.OutputManager();

            if(parameters!=null)
                ApplyRunParameters(parameters);

            if (!IsRunnable()) throw new Exception("Scenario not runnable");

            //                JobRunner.BeforeRun += new BeforeTemporalRunHandler(JobRunner_BeforeRun);
            Scenario.RunManager.UpdateEvent = new EventHandler<JobRunEventArgs>(JobRunner_Update);

            ScenarioRunWindow runWindow = null;
            var startOfRun = DateTime.Now;

            if (showWindow)
            {
                runWindow = new ScenarioRunWindow(Scenario);
                //runWindow.SetOwner(this);
                //runWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                //Enabled = false;
                runWindow.Show();
                ProjectManager.Instance.SaveAuditLogMessage("Run started at " + DateTime.Now);
            }


            Task x = Task.Factory.StartNew(() => Scenario.RunManager.Execute());
            while (!x.IsCompleted)
            {
                Thread.Sleep(50);
                Application.DoEvents();
            }

//                LogRunEnd(startOfRun);

            if (showWindow)
            {
                ProjectManager.Instance.SaveAuditLogMessage("Run finished at " + DateTime.Now + " and took " + TimeTools.TimeSpanString(DateTime.Now - startOfRun));
                runWindow.Close();
                runWindow.Dispose();
            }
            //if so then run
            //running = true;

            //runControl = new ScenarioRunWindow(Scenario);
            //runControl.SetOwner(MainForm.Instance);
            //runControl.Show();

            //Scenario.RunManager.Execute();
            //                lock (lockObj)
            //                {
            //while (running)
            //{
            //    Thread.Sleep(50);
            //    Application.DoEvents();
            //    //Monitor.Wait(lockObj);
            //}
            //                }

            //runControl.Close();
            //runControl.Dispose();

//            ProjectManager.Instance.SaveAuditLogMessage("Close run scenario window");
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
            RunningConfiguration configuration = Scenario.CurrentConfiguration;
            Type configType = configuration.GetType();
            foreach (var entry in parameters.Params)
            {
                var prop = configType.GetProperty(entry.Key, BindingFlags.Instance | BindingFlags.Public);
                var val = entry.Value;
                if (prop == null)
                {
                    throw new NotImplementedException(String.Format(
                        "Running configuration doesn't have a property: {0}", entry.Key));
                }
                if (prop.PropertyType == typeof (DateTime))
                    val = DateTime.ParseExact(entry.Value.ToString(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
                else if (prop.PropertyType == typeof (InputSet))
                    val = Scenario.Network.InputSets.FirstOrDefault(ipSet => ipSet.Name == (string)entry.Value);
                else if (prop.PropertyType == typeof(TimeStep))
                    val = TimeStep.FromName((string) entry.Value);

                prop.SetValue(configuration,val);
            }
        }

        private void JobRunner_Update(object sender, JobRunEventArgs e)
        {
            if (e.State==JobRunningState.Finished)
                JobRunner_AfterRun(sender,e);
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
