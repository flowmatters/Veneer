using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.WebServer;
using FlowMatters.Source.WebServerPanel;
using RiverSystem;
using RiverSystem.Api;

namespace FlowMatters.Source.Veneer
{
    internal class ReportingMenu
    {
        private ReportingMenu()
        {
        }

        private static ReportingMenu _instance;
        public static ReportingMenu Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ReportingMenu();
                }
                return _instance;
            }
        }

        private RiverSystemScenario Scenario { get; set; }

        public WebServerStatusControl Control { get; set; }

        public static Form FindMainForm()
        {
            return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.MainMenuStrip != null);
        }

        public ToolStripMenuItem FindOrCreateReportMenu(Form parent,RiverSystemScenario scenario)
        {
            Scenario = scenario;
            ToolStripMenuItem result =
                parent.MainMenuStrip.Items.Cast<ToolStripItem>().Where(item => item.Text == "Reporting")
                    .Cast<ToolStripMenuItem>().FirstOrDefault();

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
            Form parent = ReportingMenu.FindMainForm();
            ToolStripMenuItem reportMenu = FindOrCreateReportMenu(parent, Scenario);
            reportMenu.DropDownItems.Clear();

            if (Scenario != null)
            {
                string projectFolder = Scenario.Project.FileDirectory;
                if (projectFolder != null)
                {
                    foreach (string reportFn in Directory.EnumerateFiles(projectFolder, "*.htm*",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string fn = reportFn.Replace(projectFolder + "\\", "");
                        ToolStripItem item = reportMenu.DropDownItems.Add(NiceName(fn));
                        item.Click += (eventSender, eventArgs) => Launch(fn);
                    }
                }

                var config = VeneerConfiguration.Load(Scenario);
                if (config?.addons != null)
                {
                    foreach (var addon in config.addons)
                    {
                        ToolStripItem item = reportMenu.DropDownItems.Add(addon.name);
                        switch (addon.type)
                        {
                            case "exe":
                                item.Click += (o, args) => LaunchExeAddon(addon.path);
                                break;

                        }
                    }
                }

                if (config?.options!= null)
                {
                    WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts;
                    WebServerStatusControl.DefaultPort = config.options.defaultPort > 0
                        ? config.options.defaultPort
                        : WebServerStatusControl.DefaultPort;
                }
            }

            ToolStripItem veneer = reportMenu.DropDownItems.Add("");
            veneer.BackgroundImage = Veneer.Properties.Resources.Logo_RGB;
            veneer.BackgroundImageLayout = ImageLayout.Zoom;
            veneer.Click += (eventSender, eventArgs) => Process.Start("http://www.flowmatters.com.au");
        }


        private void LaunchExeAddon(string addonPath)
        {
            if (Control == null)
            {
                WebServerStatusControl.Launch();
            }

            var fullPath = Path.Combine(Scenario.Project.FileDirectory, addonPath);
            var startInfo = new ProcessStartInfo();
            if (fullPath.EndsWith(".bat"))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/C " + fullPath;
            }
            else
            {
                startInfo.FileName = fullPath;
            }

            startInfo.Environment["VENEER_PORT"] = this.Control.Port.ToString();
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
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

        public static void ClearMenu()
        {
            Form parent = ReportingMenu.FindMainForm();
            ToolStripMenuItem reportMenu =
                parent.MainMenuStrip.Items.Cast<ToolStripItem>().
                    Where(item => item.Text == "Reporting").Cast<ToolStripMenuItem>().FirstOrDefault();

            if (reportMenu != null)
                parent.MainMenuStrip.Items.Remove(reportMenu);
        }

    }
}
