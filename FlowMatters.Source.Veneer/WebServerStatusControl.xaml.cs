using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FlowMatters.Source.WebServer;
using RiverSystem;
using RiverSystem.TaskDefinitions;
using Application = System.Windows.Forms.Application;
using UserControl = System.Windows.Controls.UserControl;

namespace FlowMatters.Source.WebServerPanel
{
    /// <summary>
    /// Interaction logic for WebServerStatusControl.xaml
    /// </summary>
    public partial class WebServerStatusControl : UserControl, IRiverSystemPlugin, IDisposable
    {
        private RiverSystemScenario _scenario;
        private SynchronizationContext _originalContext;

        public WebServerStatusControl()
        {
            Port = SourceRESTfulService.DEFAULT_PORT;
            InitializeComponent();
            _originalContext = SynchronizationContext.Current;
            this.DataContext = this;
        }

        public RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                if (_scenario != null)
                {
                    StopServer();
                    ClearMenu();
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
            Form parent = FindParent();
            ToolStripMenuItem reportMenu = FindReportMenu(parent);
        }

        private Form FindParent()
        {
            return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.MainMenuStrip != null);
        }

        private ToolStripMenuItem FindReportMenu(Form parent)
        {
            ToolStripMenuItem result =
                parent.MainMenuStrip.Items.Cast<ToolStripItem>().
                        Where(item => item.Text == "Reporting").Cast<ToolStripMenuItem>().FirstOrDefault();

            if (result == null)
            {
                result = new ToolStripMenuItem("Reporting");
                result.DropDownOpening += PopulateReportMenu;
                parent.MainMenuStrip.Items.Add(result);
            }

            return result;
        }

        private void PopulateReportMenu(object sender, EventArgs e)
        {
            Form parent = FindParent();
            ToolStripMenuItem reportMenu = FindReportMenu(parent);
            reportMenu.DropDownItems.Clear();

            if (_scenario != null)
            {
                string projectFolder = _scenario.Project.FileDirectory;
                foreach (string reportFn in Directory.EnumerateFiles(projectFolder, "*.htm*", SearchOption.TopDirectoryOnly))
                {
                    string fn = reportFn.Replace(projectFolder + "\\", "");
                    ToolStripItem item = reportMenu.DropDownItems.Add(NiceName(fn));
                    item.Click += (eventSender, eventArgs) => Launch(fn);
                }
            }
            ToolStripItem veneer = reportMenu.DropDownItems.Add("");
            veneer.BackgroundImage = Veneer.Properties.Resources.Logo_RGB;
            veneer.BackgroundImageLayout = ImageLayout.Zoom;
            veneer.Click += (eventSender, eventArgs) => Process.Start("http://www.flowmatters.com.au");
        }

        private string NiceName(string reportFn)
        {
            return reportFn.Replace('_', ' ').Replace(".html", "").Replace(".htm", "");
        }

        private void Launch(string p)
        {
            int port = SourceRESTfulService.DEFAULT_PORT;
            string url = string.Format("http://localhost:{0}/doc/{1}", port, p);
            Process.Start(url);
        }

        private void ClearMenu()
        {
            Form parent = FindParent();
            ToolStripMenuItem reportMenu =
                parent.MainMenuStrip.Items.Cast<ToolStripItem>().
                        Where(item => item.Text == "Reporting").Cast<ToolStripMenuItem>().FirstOrDefault();

            if (reportMenu != null)
                parent.MainMenuStrip.Items.Remove(reportMenu);
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
        private void RestartIfRunning()
        {
            if (_server != null)
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
        }

        public bool NotRunning { get { return !Running; } }
        public bool Running
        {
            get { return (_server != null); }
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
            _server.Stop();
        }

        public void Dispose()
        {
            StopServer();
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
    }
}
