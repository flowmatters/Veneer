using System;
using System.Reflection;
using System.Windows.Forms;
using FlowMatters.Source.WebServerPanel;
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
            var scenarios = e.Project.GetRSScenarios();
            if (scenarios.Length == 0)
            {
                return;
            }

            if (!scenarios[0].Loaded)
            {
                return;
            }

            var startOnLoad = Environment.GetEnvironmentVariable("VENEER_START_ON_LOAD");
            if (String.IsNullOrEmpty(startOnLoad))
            {
                return;
            }

            var veneerPort = 9876;
            var veneerPortVar = Environment.GetEnvironmentVariable("VENEER_PORT");
            if (!String.IsNullOrEmpty(veneerPortVar))
            {
                veneerPort = Int32.Parse(veneerPortVar);
            }

            var remoteConnections = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_REMOTE"));
            var allowScripts = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_SCRIPTS"));

            StartVeneer(veneerPort,remoteConnections,allowScripts);
        }

        private Timer _timer;


        private void StartVeneer(int port,bool remote, bool scripts)
        {
            WebServerStatusControl.DefaultPort = port;
            WebServerStatusControl.DefaultAllowRemote = remote;
            WebServerStatusControl.DefaultAllowScripts = scripts;

            _timer = new Timer(1000.0);
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
#if V4 && BEFORE_V4_3
#else
            MainForm.Instance.Invoke(new Action(() =>
            {
                var t = typeof(MenuPluginHelper);
                var invoker = t.GetMethod("ShowAnalysisWindow", BindingFlags.NonPublic | BindingFlags.Instance);
                invoker.Invoke(MainForm.Instance.MenuPluginHelper, new[]
                {
                    typeof(WebServerStatusPanel)
                    //null
                });
            }));
#endif
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
