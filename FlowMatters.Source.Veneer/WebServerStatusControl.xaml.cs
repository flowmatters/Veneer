using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.WebServer;
using FlowMatters.Source.WebServerPanel;
using Newtonsoft.Json;
using RiverSystem;
using RiverSystem.Forms;
using RiverSystem.TaskDefinitions;
using TIME.Core.Metadata;
using Application = System.Windows.Forms.Application;
using Button = System.Windows.Controls.Button;
using Path = System.IO.Path;
using TextBox = System.Windows.Controls.TextBox;
using Timer = System.Timers.Timer;
using UserControl = System.Windows.Controls.UserControl;

namespace FlowMatters.Source.Veneer
{
    /// <summary>
    /// Interaction logic for WebServerStatusControl.xaml
    /// </summary>
    [IgnoreMenuItem] // We only want this in Tools > Veneer Server
    public partial class WebServerStatusControl : UserControl, IRiverSystemPlugin, IDisposable
    {
        public static int DefaultPort = SourceRESTfulService.DEFAULT_PORT;
        public static bool DefaultAllowRemote = false;
        public static bool DefaultAllowScripts = false;
        public static bool DefaultAllowSsl = false;

        private RiverSystemScenario _scenario;
        private SynchronizationContext _originalContext;
        private Timer _timer;

        public WebServerStatusControl()
        {
            Port = DefaultPort;
            AllowRemoteConnections = DefaultAllowRemote;
            AllowSsl = DefaultAllowSsl;
            AllowScripts = DefaultAllowScripts;
            InitializeComponent();
            _originalContext = SynchronizationContext.Current;
            this.DataContext = this;

            _timer = new Timer(1000.0);
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (Scenario == null)
                ServerLogEvent(this, "No active scenario. Load a project file before opening Web Server Monitoring");
        }

        public RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                if (_scenario != null)
                {
                    StopServer();
                    ReportingMenu.ClearMenu();
                }
                _scenario = value;
              
                if(_scenario != null)
                {
                    StartServer();
                    PopulateMenu();
                }
            }
        }

        private void PopulateMenu()
        {
            Form parent = ReportingMenu.FindMainForm();
            ReportingMenu.Instance.Control = this;
            ToolStripMenuItem reportMenu = ReportingMenu.Instance.FindOrCreateReportMenu(parent,_scenario);
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
                if (_server != null) _server.Service.AllowScript = value;
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

        private bool _allowSsl;
        public bool AllowSsl
        {
            get { return _allowSsl; }
            set
            {
                _allowSsl = value;
                if (value)
                    ServerLogEvent(this, "Note: Enabling SSL requires a valid SSL certificate.");
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
            _server = new SourceRESTfulService(Port) {AllowRemoteConnections = AllowRemoteConnections, AllowSsl = AllowSsl};
            _server.Scenario = Scenario;
            _server.LogGenerator += ServerLogEvent;
            _server.Start();
            _port = _server.Port;
            PortTxt.GetBindingExpression(TextBox.TextProperty).UpdateTarget();
            _server.Service.AllowScript = AllowScripts;
            UpdateButtons();
        }

        public bool NotRunning { get { return !Running; } }
        public bool Running
        {
            get { return (_server != null) && _server.Running; }
        }

        void ServerLogEvent(object sender, string msg)
        {
            _originalContext.Post( delegate
                {
                    LogBox.Text = msg + "\n" + LogBox.Text;                    
                },null);
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
    }
}
