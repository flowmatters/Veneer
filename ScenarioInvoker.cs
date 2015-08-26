using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumer.Forms;
using RiverSystem.Controls;
using RiverSystem.Controls.ModelRun;
using RiverSystem.Tracking;
using TIME.DataTypes;
using TIME.ScenarioManagement.Execution;
using TIME.ScenarioManagement.RunManagement;
using TIME.Winforms.Utils;

namespace FlowMatters.Source.Veneer
{
    class ScenarioInvoker
    {
        private ScenarioRunWindow runControl;
        private object lockObj = new object();
        private bool running;
        public RiverSystemScenario Scenario { set; get; }

        private IRunManager JobRunner
        {
            get
            {
                return ProjectManager.Instance.CurrentRiverSystemScenarioProxy.riverSystemScenario.RunManager;
            }
        }

        public void RunScenario()
        {
            if (Scenario == null)
            {
                MsgTools.ShowInfo(RiverSystemOptions.NEED_PROJECT_OPEN_MESSAGE);
                return;
            }

//            ProjectManager.Instance.SaveAuditLogMessage("Open run scenario window");
//            Scenario.outputManager = new Obsolete.Recording.OutputManager();

            if (IsRunnable())
            {
               
                //                JobRunner.BeforeRun += new BeforeTemporalRunHandler(JobRunner_BeforeRun);
                Scenario.RunManager.UpdateEvent = new EventHandler<JobRunEventArgs>(JobRunner_Update);

                var runWindow = new ScenarioRunWindow(Scenario);
                //runWindow.SetOwner(this);
                //runWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                //Enabled = false;
                runWindow.Show();

                var startOfRun = DateTime.Now;
                ProjectManager.Instance.SaveAuditLogMessage("Run started at " + DateTime.Now);

                Task x = Task.Factory.StartNew(() => Scenario.RunManager.Execute());
                while (!x.IsCompleted)
                {
                    Thread.Sleep(50);
                    Application.DoEvents();
                }

                RunTracker.RunCompleted((RiverSystemProject) Scenario.Project, startOfRun);
                ProjectManager.Instance.SaveAuditLogMessage("Run finished at " + DateTime.Now + " and took " + TimeTools.TimeSpanString(DateTime.Now - startOfRun));
                runWindow.Close();
                runWindow.Dispose();
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
            }

//            ProjectManager.Instance.SaveAuditLogMessage("Close run scenario window");
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
            running = false;
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
