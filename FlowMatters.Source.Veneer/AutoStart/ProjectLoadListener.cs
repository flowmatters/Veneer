using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using FlowMatters.Source.WebServerPanel;
using RiverSystem;
using RiverSystem.ApplicationLayer.Consumer.Forms;
using RiverSystem.Forms;
using Timer = System.Timers.Timer;

namespace FlowMatters.Source.Veneer.AutoStart
{
    public class ProjectLoadListener
    {
        private static ProjectLoadListener _instance;

        public static ProjectLoadListener Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProjectLoadListener();
                }

                return _instance;
            }
        }

        private ProjectManager _pm;
        private RiverSystem.RiverSystemScenario _lastSeen;
        private bool _tickInProgress;

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

        private void _pm_ProjectLoaded(object sender,
            TIME.ScenarioManagement.EditorState.ProjectActionWithPathEventArgs<RiverSystem.RiverSystemProject> e)
        {
            var combined = Path.Combine(Directory.GetCurrentDirectory(), e.Project.FullFilename);
            if (File.Exists(combined))
            {
                e.Project.SetFullFilename(combined);
            }
        }

        private Timer _timer;

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

        private void ScenarioLoaded()
        {
            var startOnLoad = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_START_ON_LOAD"));
            if (startOnLoad)
            {
                StartVeneer();
            }
            else
            {
                PopulateReportingMenu();
            }
        }

        private void PopulateReportingMenu()
        {
            ReportingMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, MainForm.Instance.CurrentScenario);
        }

        private void StartVeneer()
        {
            var veneerPort = 9876;
            var veneerPortVar = Environment.GetEnvironmentVariable("VENEER_PORT");
            if (!String.IsNullOrEmpty(veneerPortVar))
            {
                veneerPort = Int32.Parse(veneerPortVar);
            }

            var remoteConnections = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_REMOTE"));
            var allowScripts = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_SCRIPTS"));

            //StartVeneer(veneerPort, remoteConnections, allowScripts);
            WebServerStatusControl.DefaultPort = veneerPort;
            WebServerStatusControl.DefaultAllowRemote = remoteConnections;
            WebServerStatusControl.DefaultAllowScripts = allowScripts;

            WebServerStatusControl.Launch();
        }

        private void Project_ScenarioAdded(object sender, TIME.ScenarioManagement.ScenarioArgs e)
        {
            MessageBox.Show($"Scenario added: {e.container.ScenarioName}");
        }

        private void Container_ScenarioLoaded(object sender, TIME.ScenarioManagement.ScenarioContainerEventArgs e)
        {
            MessageBox.Show("Loaded scenario: " + e.ScenarioContainer.Scenario.Name);
        }
    }
}
