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
        protected ProjectLoadListener()
        {
            _pm = ProjectManager.Instance;
            if (_pm != null)
            {
                _pm.ProjectLoaded += _pm_ProjectLoaded;
            }
        }

        private void _pm_ProjectLoaded(object sender,
            TIME.ScenarioManagement.EditorState.ProjectActionWithPathEventArgs<RiverSystem.RiverSystemProject> e)
        {
            var combined = Path.Combine(Directory.GetCurrentDirectory(), e.Project.FullFilename);
            if (File.Exists(combined))
            {
                e.Project.SetFullFilename(combined);
            }

            var scenarios = e.Project.GetRSScenarios();
            if (scenarios.Length == 0)
            {
                return;
            }

            if (!scenarios[0].Loaded)
            {
                return;
            }

            _timer = new Timer(1000.0);
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private Timer _timer;

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
#if V4 && BEFORE_V4_3
#else
            if (MainForm.Instance.CurrentScenario == null)
            {
                _timer = new Timer(1000.0);
                _timer.AutoReset = false;
                _timer.Elapsed += _timer_Elapsed;
                _timer.Start();
                return;
            }

            ScenarioLoaded();
#endif
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
            ReportingMenu.Instance.FindOrCreateReportMenu(MainForm.Instance, MainForm.Instance.CurrentScenario);
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
