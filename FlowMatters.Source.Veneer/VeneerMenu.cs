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
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.WebServer;
using FlowMatters.Source.WebServerPanel;
using RiverSystem;
using RiverSystem.Api;
using RiverSystem.Forms;

namespace FlowMatters.Source.Veneer
{
    internal class VeneerMenu
    {
        private VeneerMenu()
        {
        }

        private static VeneerMenu _instance;
        public static VeneerMenu Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VeneerMenu();
                }
                return _instance;
            }
        }

        private RiverSystemScenario Scenario { get; set; }

        public WebServerStatusControl Control { get; set; }

        /// <summary>Top-level menus this project requires, in bar order.</summary>
        private List<string> _menuLayout = new List<string>();

        /// <summary>Menu items Veneer itself added to the main menu strip.</summary>
        private readonly List<ToolStripMenuItem> _createdMenus = new List<ToolStripMenuItem>();

        public static Form FindMainForm()
        {
            return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.MainMenuStrip != null);
        }

        public ToolStripMenuItem FindOrCreateReportMenu(Form parent, string mnu = MenuLayout.DEFAULT_MENU)
        {
            ToolStripMenuItem result =
                parent.MainMenuStrip.Items.Cast<ToolStripItem>().Where(item => item.Text == mnu)
                    .Cast<ToolStripMenuItem>().FirstOrDefault();

            if (result == null)
            {
                result = new ToolStripMenuItem(mnu);
                result.DropDownOpening += (sender, args) => PopulateReportMenu(mnu);
                parent.MainMenuStrip.Items.Add(result);
                _createdMenus.Add(result);
            }

            return result;
        }

        private void PopulateReportMenu(string mnu)
        {
            Form parent = VeneerMenu.FindMainForm();
            ToolStripMenuItem reportMenu = FindOrCreateReportMenu(parent, mnu);
            reportMenu.DropDownItems.Clear();

            if (Scenario != null)
            {
                var config = VeneerConfiguration.Load(Scenario);
                var currentScenario = MainForm.Instance.CurrentScenario;
                if (config?.addons != null)
                {
                    var addonsForMenu = config.addons.Where(a => MenuLayout.TopLevelMenu(a.menu) == mnu);
                    foreach (var addon in addonsForMenu)
                    {
                        var menuPath = MenuLayout.SplitMenuPath(addon.menu);
                        ToolStripMenuItem targetMenu = reportMenu;

                        if (menuPath.Length > 1)
                        {
                            targetMenu = FindOrCreateNestedMenu(reportMenu, menuPath);
                        }

                        ToolStripItem item = targetMenu.DropDownItems.Add(addon.name);

                        string invalid = VeneerAddon.Validate(addon);
                        if (invalid != null)
                        {
                            item.Enabled = false;
                            item.ToolTipText = $"Invalid addon: {invalid}";
                            LogOnce($"Veneer addon '{addon.name}' {invalid}");
                        }
                        else
                        {
                            switch (addon.type)
                            {
                                case "exe":
                                case "script":
                                    item.Click += (o, args) => LaunchAddon(addon);
                                    break;

                                // Previously absent, so an unrecognised type silently
                                // produced a menu item that did nothing when clicked.
                                default:
                                    item.Enabled = false;
                                    item.ToolTipText = $"Unknown addon type '{addon.type}'";
                                    LogOnce($"Veneer addon '{addon.name}' has unknown type '{addon.type}'");
                                    break;
                            }
                        }

                        // Runs after the above and may overwrite ToolTipText for an
                        // addon that is both invalid and scenario-filtered. Harmless:
                        // this block only ever disables.
                        if (!VeneerConfiguration.AddonAppliesTo(addon, config, currentScenario))
                        {
                            var filter = VeneerConfiguration.EffectiveFilter(addon, config);
                            item.Enabled = false;
                            item.ToolTipText = $"Requires scenario '{filter}' to be active";
                            TIME.Management.Log.WriteError(
                                this,
                                $"Veneer addon '{addon.name}' disabled: requires scenario '{filter}', current is '{currentScenario?.Name ?? "none"}'");
                        }
                    }
                }

                // Auto-discovered reports belong to Reporting only, and sit below the
                // addons that the .veneer file specified explicitly.
                if (mnu == MenuLayout.DEFAULT_MENU)
                {
                    AddHtmlReports(reportMenu);
                }

                if (config?.options!= null)
                {
                    WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts;
                    WebServerStatusControl.DefaultPort = config.options.defaultPort > 0
                        ? config.options.defaultPort
                        : WebServerStatusControl.DefaultPort;
                }
            }

            // Only add the Veneer logo to the last menu in the layout
            var layout = _menuLayout.Count > 0 ? _menuLayout : RequiredMenus();
            if (layout.Count > 0 && layout[layout.Count - 1] == mnu)
            {
                ToolStripItem veneer = reportMenu.DropDownItems.Add("");
                veneer.BackgroundImage = Veneer.Properties.Resources.Logo_RGB;
                veneer.BackgroundImageLayout = ImageLayout.Zoom;
                veneer.Click += (eventSender, eventArgs) => Process.Start("http://www.flowmatters.com.au");
            }
        }

        private ToolStripMenuItem FindOrCreateNestedMenu(ToolStripMenuItem parentMenu, string[] menuPath, int startIndex = 1)
        {
            if (startIndex >= menuPath.Length)
                return parentMenu;

            string menuName = menuPath[startIndex];
            ToolStripMenuItem subMenu = parentMenu.DropDownItems.Cast<ToolStripItem>()
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == menuName);

            if (subMenu == null)
            {
                subMenu = new ToolStripMenuItem(menuName);
                parentMenu.DropDownItems.Add(subMenu);
            }

            return FindOrCreateNestedMenu(subMenu, menuPath, startIndex + 1);
        }

        private static readonly HashSet<string> _loggedProblems = new HashSet<string>();

        /// <summary>
        /// VeneerConfiguration.Load runs on every menu open (four call sites), so a
        /// malformed entry would otherwise log on every drop-down. Cleared by
        /// ClearMenu so a project change re-reports.
        /// </summary>
        private void LogOnce(string message)
        {
            if (_loggedProblems.Add(message))
                TIME.Management.Log.WriteError(this, message);
        }

        internal void ClearLoggedProblems()
        {
            _loggedProblems.Clear();
        }

        private void LaunchAddon(VeneerAddon addon)
        {
            if (Control == null)
            {
                WebServerStatusControl.Launch();
            }

            var context = new AddonContext
            {
                ProjectDirectory = Scenario.Project.FileDirectory,
                ProjectFile = Scenario.Project.FullFilename,
                // The configured port, not a promise the server is listening --
                // Port is set independently of Running, and addons may be launched
                // with the server stopped.
                Port = Control.Port
            };

            AddonLauncher.Launch(addon, context, new ControlAddonLog(Control));
        }

        /// <summary>
        /// Bridges IAddonLog to the Veneer log panel, and sends errors to Source's
        /// log as well so they survive the panel being closed or cleared.
        /// </summary>
        private sealed class ControlAddonLog : IAddonLog
        {
            private readonly WebServerStatusControl _control;

            public ControlAddonLog(WebServerStatusControl control)
            {
                _control = control;
            }

            public void Write(string message, AddonLogLevel level)
            {
                var mapped = level == AddonLogLevel.Error   ? LogLevel.Error
                           : level == AddonLogLevel.Warning ? LogLevel.Warning
                           : LogLevel.Debug;

                _control.LogAddonMessage(message, mapped);

                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
            }
        }

        private void AddHtmlReports(ToolStripMenuItem reportMenu)
        {
            foreach (string reportFn in HtmlReportFiles())
            {
                string fn = Path.GetFileName(reportFn);
                ToolStripItem item = reportMenu.DropDownItems.Add(NiceName(fn));
                item.Click += (eventSender, eventArgs) => Launch(fn);
            }
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

        public void ClearMenu()
        {
            // A project change should re-report addon config problems.
            ClearLoggedProblems();

            foreach (var menu in _createdMenus)
            {
                // Owner is the ToolStrip the item currently lives on, so this removes
                // the item from wherever it actually is rather than from a menu strip
                // we look up by hand. Only menus Veneer created are ever in this list,
                // so a .veneer file naming an existing Source menu can no longer make
                // us delete Source's own menu.
                if (menu.Owner != null)
                    menu.Owner.Items.Remove(menu);
            }

            _createdMenus.Clear();
            _menuLayout.Clear();
        }

        public void InitialiseRequiredMenus(Form parent, RiverSystemScenario scenario)
        {
            Scenario = scenario;
            _menuLayout = RequiredMenus();
            foreach (var mnu in _menuLayout)
            {
                FindOrCreateReportMenu(parent, mnu);
            }
        }

        private List<string> RequiredMenus()
        {
            var config = VeneerConfiguration.Load(Scenario);
            return MenuLayout.TopLevelMenus(config?.addons, HtmlReportFiles().Any());
        }

        private IEnumerable<string> HtmlReportFiles()
        {
            var projectFolder = Scenario?.Project?.FileDirectory;
            if (projectFolder == null)
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(projectFolder, "*.htm*", SearchOption.TopDirectoryOnly);
        }

    }
}
