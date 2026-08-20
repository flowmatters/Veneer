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

        /// <summary>
        /// Live instance counts, an instance field on this singleton. Deliberately NOT
        /// cleared by ClearMenu: the counts track live OS processes, not menu state. A
        /// Dash app survives a project switch, and clearing would re-enable the item
        /// while the process still holds its port.
        /// </summary>
        private readonly RunningAddons _runningAddons = new RunningAddons();

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
                            LogOnce($"Veneer addon '{addon.name}' {invalid}");

                        // Dispatch only -- the Enabled/ToolTipText assignments that used to
                        // live in the default arm have moved to AddonMenuItemState, so there
                        // is exactly one writer of the item's appearance.
                        //
                        // The arms below and AddonMenuItemState.IsKnownType are ONE LIST IN
                        // TWO PLACES. A type added here but not there renders disabled, which
                        // is loud. A type added there but not here renders ENABLED with no
                        // Click handler -- a menu item that silently does nothing, which is
                        // the exact defect the default arm was added to fix. A drift test is
                        // not cheap for a switch inside WinForms, so this comment is the guard.
                        if (invalid == null)
                        {
                            switch (addon.type)
                            {
                                case "exe":
                                case "script":
                                    item.Click += (o, args) => LaunchAddon(addon);
                                    break;

                                case "url":
                                    item.Click += (o, args) => LaunchUrlAddon(addon);
                                    break;

                                default:
                                    LogOnce($"Veneer addon '{addon.name}' has unknown type '{addon.type}'");
                                    break;
                            }
                        }

                        var applies = VeneerConfiguration.AddonAppliesTo(addon, currentScenario);
                        var filter = VeneerConfiguration.EffectiveFilter(addon);

                        if (!applies)
                            TIME.Management.Log.WriteError(
                                this,
                                $"Veneer addon '{addon.name}' disabled: requires scenario '{filter}', current is '{currentScenario?.Name ?? "none"}'");

                        // Running deliberately adds NO log line: it is not a problem, and
                        // this runs on every dropdown open.
                        var state = AddonMenuItemState.For(
                            addon, invalid, applies, filter, _runningAddons.RunningCount(addon));

                        item.Text = state.Text;
                        item.Enabled = state.Enabled;
                        item.ToolTipText = state.ToolTipText;
                    }
                }

                // Auto-discovered reports belong to Reporting only, and sit below the
                // addons that the .veneer file specified explicitly.
                if (mnu == MenuLayout.DEFAULT_MENU)
                {
                    AddHtmlReports(reportMenu);
                }

                // Per field, not per block. The old code assigned allowScripts
                // unconditionally, so any .veneer file with an options block that
                // omitted the field reset it to false -- overwriting a value set
                // from VENEER_ALLOW_SCRIPTS or the GUI.
                if (config?.options != null)
                {
                    if (config.options.allowScripts != null)
                        WebServerStatusControl.DefaultAllowScripts = config.options.allowScripts.Value;

                    if (config.options.defaultPort != null && config.options.defaultPort.Value > 0)
                        WebServerStatusControl.DefaultPort = config.options.defaultPort.Value;
                }
            }

            // Only add the Veneer logo to the last menu in the layout
            var layout = _menuLayout.Count > 0 ? _menuLayout : RequiredMenus();
            if (layout.Count > 0 && layout[layout.Count - 1] == mnu)
            {
                ToolStripItem veneer = reportMenu.DropDownItems.Add("");
                veneer.BackgroundImage = Veneer.Properties.Resources.Logo_RGB;
                veneer.BackgroundImageLayout = ImageLayout.Zoom;
                veneer.Click += (eventSender, eventArgs) =>
                    OpenLink("http://www.flowmatters.com.au", "the Veneer home page");
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
            VeneerConfiguration.ClearLoggedProblems();
        }

        private void LaunchAddon(VeneerAddon addon)
        {
            // The one-shot is constructed HERE, not inside AddonLauncher.Launch, because
            // the catch below is a second place that has to report. One object spanning
            // both layers is what makes a double decrement impossible; see
            // OneShotLifecycle.
            //
            // Outside the try, and safe there despite the constructor throwing on a null
            // callback: _runningAddons is readonly with an inline initialiser, so the
            // method group cannot be null and the ArgumentNullException is unreachable
            // from this call site. Were it reachable, it would escape a Click handler as
            // an unhandled-exception dialog.
            var lifecycle = new OneShotLifecycle(_runningAddons.Finished);

            // FIRST, and outside the try, so the increment and the catch's decrement are
            // trivially balanced. With this inside the try, a throw from TryRaisePanel,
            // AddonLog() or BuildAddonContext() would decrement a count that was never
            // incremented -- masked at zero by the floor in RunningAddons, but with a
            // second instance genuinely live the count would go 2 -> 1 and the label
            // would lie.
            _runningAddons.MarkRunning(addon);

            try
            {
                TryRaisePanel();

                var log = AddonLog();
                log.Write(string.Format("Launching '{0}'...", addon.name), AddonLogLevel.Info);

                AddonLauncher.Launch(addon, BuildAddonContext(), log, lifecycle);
            }
            catch (Exception ex)
            {
                // Launch is documented as never throwing, but that promise rests on the
                // supplied IAddonLog never throwing, which ControlAddonLog does not
                // guarantee. Without this, a throw between MarkRunning and the watcher
                // strands the count and disables the item permanently -- and this is a
                // Click handler, so it would also raise an unhandled-exception dialog.
                lifecycle.Finished(addon);
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer addon '{0}' could not be launched: {1}",
                                        addon.name, ex.Message));
            }
        }

        /// <summary>
        /// Raise the Veneer panel, unconditionally -- the old `if (Control == null)` guard
        /// meant this ran on the FIRST click of a session only. Control is assigned by
        /// WebServerStatusControl.PopulateMenu and never nulled, so once the operator
        /// closes the panel (HideOnClose merely hides it) every later click silently failed
        /// to bring it back, leaving the click with no acknowledgement at all.
        ///
        /// Guarded because Launch() ends in unguarded reflection (GetMethod can return
        /// null, Invoke can throw) inside MainForm.Instance.Invoke, which rethrows on this
        /// thread. At most once per session that was survivable; on every click, at the top
        /// of a Click handler, it is not.
        /// </summary>
        private void TryRaisePanel()
        {
            try
            {
                WebServerStatusControl.Launch();
            }
            catch (Exception ex)
            {
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer could not open the monitoring panel: {0}",
                                        ex.Message));
            }
        }

        private void LaunchUrlAddon(VeneerAddon addon)
        {
            AddonLauncher.LaunchUrl(addon, BuildAddonContext(), AddonLog());
        }

        /// <summary>
        /// The panel to route addon output and port lookups through.
        ///
        /// Control alone is not enough. It is assigned only by
        /// WebServerStatusControl.PopulateMenu, which runs on the async continuation
        /// of ChangeScenarioAsync -- after an awaited StartServer. So on the first
        /// addon launch of a session it is still null even though LaunchAddon has
        /// just opened the panel synchronously, and every Debug and Warning line
        /// would go to SourceAddonLog and be dropped, leaving the panel we just
        /// opened empty. ActiveInstance is set in the control's constructor, so it
        /// is already there by the time Launch() returns. ProjectLoadListener
        /// resolves the panel the same way.
        /// </summary>
        private WebServerStatusControl EffectiveControl
        {
            get { return Control ?? WebServerStatusControl.ActiveInstance; }
        }

        private AddonContext BuildAddonContext()
        {
            var control = EffectiveControl;
            return new AddonContext
            {
                ProjectDirectory = Scenario?.Project?.FileDirectory,
                ProjectFile = Scenario?.Project?.FullFilename,
                ConfigDirectory = VeneerConfiguration.ConfigDirectory(),
                // The configured port, not a promise the server is listening --
                // Port is set independently of Running, and addons may be launched
                // with the server stopped. Still null on the URL path when no panel
                // was ever opened, which that path deliberately does not force.
                Port = control != null ? control.Port : WebServerStatusControl.DefaultPort
            };
        }

        private IAddonLog AddonLog()
        {
            var control = EffectiveControl;
            return control != null ? (IAddonLog)new ControlAddonLog(control) : new SourceAddonLog();
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
                           : level == AddonLogLevel.Info    ? LogLevel.Info
                           : LogLevel.Debug;

                _control.LogAddonMessage(message, mapped);

                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
            }
        }

        /// <summary>
        /// Used when no Veneer panel is available: the URL path, which deliberately does
        /// not open one, and the process path when TryRaisePanel failed.
        ///
        /// Info is passed through, not dropped. That second case is exactly where feedback
        /// matters most -- if the panel could not be raised, an Error-only sink would
        /// discard 'Launching ...' too, leaving the operator with no panel, no line, and a
        /// disabled menu item.
        /// </summary>
        private sealed class SourceAddonLog : IAddonLog
        {
            public void Write(string message, AddonLogLevel level)
            {
                if (level == AddonLogLevel.Error)
                    TIME.Management.Log.WriteError(this, message);
                else if (level == AddonLogLevel.Info)
                    TIME.Management.Log.WriteInfo(this, message);
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
            // Was SourceRESTfulService.DEFAULT_PORT -- the compile-time constant
            // 9876 -- so report links pointed there no matter where the server
            // was actually listening.
            var control = EffectiveControl;
            int port = control != null ? control.Port : WebServerStatusControl.DefaultPort;
            string url = string.Format("http://localhost:{0}/doc/{1}", port, p);
            OpenLink(url, string.Format("report '{0}'", p));
        }

        /// <summary>
        /// Click handlers must not throw -- an escaping exception becomes an
        /// unhandled-exception dialog in Source. Failures go straight to Source's
        /// log rather than through LogOnce, which de-duplicates by message and is
        /// cleared only on project change: right for menu-build spam, wrong for a
        /// click, where clicking a broken link twice should report twice.
        /// </summary>
        private void OpenLink(string url, string description)
        {
            string error;
            if (!ShellLink.TryOpen(url, out error))
            {
                TIME.Management.Log.WriteError(
                    this, string.Format("Veneer could not open {0}: {1}", description, error));
            }
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
