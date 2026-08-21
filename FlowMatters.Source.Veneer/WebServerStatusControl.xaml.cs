using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FlowMatters.Source.Veneer;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer;
using Newtonsoft.Json;
using RiverSystem;
using RiverSystem.Controls.ManagedExtensions;
using RiverSystem.Forms;
using RiverSystem.TaskDefinitions;
using TIME.ManagedExtensions;
using Application = System.Windows.Forms.Application;
using Button = System.Windows.Controls.Button;
using Path = System.IO.Path;
using TextBox = System.Windows.Controls.TextBox;
using Timer = System.Timers.Timer;
using UserControl = System.Windows.Controls.UserControl;

namespace FlowMatters.Source.WebServerPanel
{
    /// <summary>
    /// Interaction logic for WebServerStatusControl.xaml
    /// </summary>
    public partial class WebServerStatusControl : UserControl, IRiverSystemPlugin, IDisposable
    {
        public static int DefaultPort = SourceRESTfulService.DEFAULT_PORT;
        public static bool DefaultAllowRemote = false;
        public static bool DefaultAllowScripts = false;

        private static WebServerStatusControl _activeInstance;
        public static WebServerStatusControl ActiveInstance => _activeInstance;

        private RiverSystemScenario _scenario;
        private SynchronizationContext _originalContext;
        private Timer _timer;
        private LogLevel _minimumLogLevel = LogLevel.Info;

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

        public WebServerStatusControl()
        {
            _activeInstance = this;
            Port = DefaultPort;
            AllowRemoteConnections = DefaultAllowRemote;
            AllowScripts = DefaultAllowScripts;
            InitializeComponent();
            _originalContext = SynchronizationContext.Current;
            this.DataContext = this;

            LogLevelCombo.ItemsSource = Enum.GetValues(typeof(LogLevel));
            LogLevelCombo.SelectedItem = _minimumLogLevel;

            _timer = new Timer(1000.0);
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (Scenario == null)
                ServerLogEvent(this, "No active scenario. Load a project file before opening Web Server Monitoring", LogLevel.Warning);
        }

        public RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                if (_scenario != null)
                {
                    StopServer();
                    VeneerMenu.Instance.ClearMenu();
                }
                _scenario = value;

                BoundScenarioName = _scenario != null ? _scenario.Name : "(none)";

                if(_scenario != null)
                {
                    StartServer();
                    PopulateMenu();
                }
            }
        }

        private void PopulateMenu()
        {
            Form parent = VeneerMenu.FindMainForm();
            VeneerMenu.Instance.Control = this;
            VeneerMenu.Instance.InitialiseRequiredMenus(parent,_scenario);
        }

        private AbstractSourceServer _server;

        private int _port;

        private bool _allowScripts;
        public bool AllowScripts
        {
            get { return _allowScripts; }
            set
            {
                _allowScripts = value;
                if (_server != null) _server.AllowScript = value;
            }
        }
        public int Port
        {
            get { return _port; }
            set
            {
                _port = value;
                RestartIfRunning();
            }
        }

        private bool _allowRemoteConnections;

        public bool AllowRemoteConnections
        {
            get { return _allowRemoteConnections; }
            set
            {
                _allowRemoteConnections = value;
                RestartIfRunning();
            }
        }
        private void RestartIfRunning()
        {
            if (Running)
            {
                RestartServer();
            }
        }

        private void StartServer()
        {
            _server = new SourceRESTfulService(Port) {AllowRemoteConnections = AllowRemoteConnections};
            _server.Scenario = Scenario;
            _server.LogGenerator += ServerLogEvent;
            _server.Start();
            _port = _server.Port;
            PortTxt.GetBindingExpression(TextBox.TextProperty).UpdateTarget();
            _server.AllowScript = AllowScripts;
            UpdateButtons();
        }

        public bool NotRunning { get { return !Running; } }
        public bool Running
        {
            get { return (_server != null) && _server.Running; }
        }

        /// <summary>
        /// Signature is FIXED: this is subscribed as a ServerLogListener delegate in
        /// StartServer, and C# delegate compatibility requires matching arity. A trailing
        /// `bool forceScroll = false` is a CS0123 build break, not a convenience -- the
        /// existing `LogLevel level = LogLevel.Info` default gets away with it only
        /// because the arity still matches.
        /// </summary>
        void ServerLogEvent(object sender, string msg, LogLevel level = LogLevel.Info)
        {
            Append(msg, level, false);
        }

        private void Append(string msg, LogLevel level, bool forceScroll)
        {
            _originalContext.Post(delegate
            {
                if (level < _minimumLogLevel)
                    return;

                var scrollViewer = GetScrollViewer(LogBox);
                bool wasAtBottom = scrollViewer == null ||
                    scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 10;

                LogBox.AppendText(msg + "\n");

                if (forceScroll || wasAtBottom)
                    LogBox.ScrollToEnd();
            }, null);
        }

        /// <summary>
        /// Log entry point for addon output, usable regardless of server state.
        /// The LogBox sink is otherwise only wired up inside StartServer
        /// (server.LogGenerator += ServerLogEvent), but addons can be launched with
        /// the server stopped, so their output needs a path that does not depend on
        /// it. Append marshals to the UI thread via _originalContext -- which is what
        /// OutputDataReceived, raised on a threadpool thread, requires.
        ///
        /// Info and Error are tested explicitly, NOT `level >= LogLevel.Info`. Child
        /// stderr is logged at Warning, and Warning >= Info -- a Dash app writes its whole
        /// startup to stderr, so that predicate would yank the log to the bottom on every
        /// line and destroy the scrollback this is meant to preserve. Across the addon
        /// path Info and Error are used only by Veneer's own lifecycle and failure lines,
        /// while both child streams are Debug (stdout) and Warning (stderr).
        /// </summary>
        internal void LogAddonMessage(string msg, LogLevel level)
        {
            Append(msg, level, level == LogLevel.Info || level == LogLevel.Error);
        }

        private static System.Windows.Controls.ScrollViewer GetScrollViewer(System.Windows.DependencyObject depObj)
        {
            if (depObj is System.Windows.Controls.ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void LogLevelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LogLevelCombo.SelectedItem is LogLevel selected)
                _minimumLogLevel = selected;
        }

        private void StopServer()
        {
            if(Running)
                _server.Stop();
            _server = null;
            UpdateButtons();
        }

        public void Dispose()
        {
            StopServer();
            UpdateButtons();
            if (_activeInstance == this)
                _activeInstance = null;
        }

        private void ClearBtn_OnClick(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }

        private void StartBtn_OnClick(object sender, RoutedEventArgs e)
        {
            StartServer();
        }

        private void StopBtn_OnClick(object sender, RoutedEventArgs e)
        {
            StopServer();
        }

        private void RestartBtn_OnClick(object sender, RoutedEventArgs e)
        {
            RestartServer();
        }

        private void RestartServer()
        {
            StopServer();
            StartServer();
        }

        private void UpdateButtons()
        {
            StartBtn.GetBindingExpression(Button.IsEnabledProperty).UpdateTarget();
            StopBtn.GetBindingExpression(Button.IsEnabledProperty).UpdateTarget();
            RestartBtn.GetBindingExpression(Button.IsEnabledProperty).UpdateTarget();
        }

        public static void Launch()
        {
#if V4 && BEFORE_V4_3
#else

            MainForm.Instance.Invoke(new Action(() =>
            {
                // If a Veneer panel already exists, bring it to front instead of creating a duplicate
                var existingPanel = WebServerStatusPanel.ActivePanel;
                if (existingPanel != null)
                {
                    existingPanel.ActivateWindow();
                    return;
                }

                var t = typeof(MenuPluginHelper);
                var invoker = t.GetMethod("ShowAnalysisWindow", BindingFlags.NonPublic | BindingFlags.Instance);
                invoker.Invoke(MainForm.Instance.MenuPluginHelper, new[]
                {
                    typeof(WebServerStatusPanel)
                });
            }));
#endif
        }
    }
}
