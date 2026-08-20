using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using FlowMatters.Source.Veneer.Addons;
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
        private RiverSystem.RiverSystemScenario _deferredRebindTarget;

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
            // Diagnostics only. Option defaults stay a load-time concern, so this
            // deliberately does not re-apply them.
            //
            // This Load duplicates the one the menu rebuild below performs. Both
            // are cheap -- two Exists, two small reads -- and this path runs at
            // human speed, so sharing one result is not worth having the log line
            // and the menu disagree about what was on disk.
            LogConfigurationChain(VeneerConfiguration.Load(newScenario), newScenario);

            var control = WebServerStatusControl.ActiveInstance;
            if (control != null)
            {
                control.Scenario = newScenario;
            }
            else
            {
                VeneerMenu.Instance.ClearMenu();
                if (newScenario != null)
                {
                    VeneerMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, newScenario);
                }
            }
        }

        private void ScenarioLoaded()
        {
            ApplyDefaultsFromEnvironmentAndConfig();

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

        private void ApplyDefaultsFromEnvironmentAndConfig()
        {
            var port = WebServerStatusControl.DefaultPort;

            var config = VeneerConfiguration.Load(MainForm.Instance.CurrentScenario);
            if (config?.options != null && config.options.defaultPort.GetValueOrDefault() > 0)
            {
                port = config.options.defaultPort.Value;
            }

            var envPort = Environment.GetEnvironmentVariable("VENEER_PORT");
            if (!String.IsNullOrEmpty(envPort) && Int32.TryParse(envPort, out var parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            WebServerStatusControl.DefaultPort = port;
            LogConfigurationChain(config, MainForm.Instance.CurrentScenario);
        }

        private static string _lastLoggedChain;

        /// <summary>
        /// With two files feeding one menu, "where did this item come from?" and
        /// "why is my sidecar being ignored?" are questions a user will ask.
        ///
        /// Deduplicated rather than fired once per load, because the Rebind
        /// transition also covers a scenario change within one project, where the
        /// resolved files are identical and a second line would be noise.
        ///
        /// The project is part of the key, not just the chain: two projects that
        /// both resolve to nothing both produce "none", and suppressing the second
        /// would read as "Veneer did not look" rather than "Veneer found nothing".
        ///
        /// Call on the UI thread only -- _lastLoggedChain is unsynchronised. Both
        /// call sites reach here inside MainForm.Instance.Invoke.
        /// </summary>
        private void LogConfigurationChain(
            ResolvedVeneerConfiguration config, RiverSystem.RiverSystemScenario scenario)
        {
            var chain = config.SourceFiles.Length == 0
                ? "none"
                : String.Join(", ", config.SourceFiles);

            // Which file is doing the superseding is not obvious from a bare
            // "superseding X" -- name the loser and say it is being ignored.
            if (config.SupersededSidecar != null)
                chain += " (ignoring the sidecar " + config.SupersededSidecar + ")";

            var key = scenario?.RiverSystemProject?.FullFilename + " -> " + chain;
            if (key == _lastLoggedChain) return;
            _lastLoggedChain = key;

            TIME.Management.Log.WriteInfo(this, "Veneer configuration: " + chain);
        }

        private void PopulateReportingMenu()
        {
            VeneerMenu.Instance.InitialiseRequiredMenus(MainForm.Instance, MainForm.Instance.CurrentScenario);
        }

        private void StartVeneer()
        {
            WebServerStatusControl.DefaultAllowRemote =
                !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_REMOTE"));
            WebServerStatusControl.DefaultAllowScripts =
                !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VENEER_ALLOW_SCRIPTS"));

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
