using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Netron.GraphLib;
using RiverSystem;
using RiverSystem.Api;

namespace FlowMatters.Source.Veneer.Addons
{
    public class VeneerConfiguration
    {
        public VeneerAddon[] addons;
        public VeneerOptions options;
        public string targetScenario;

        /// <summary>
        /// The configuration directory in effect, for %VENEER_CONFIG_DIR% and for
        /// discovery. Null when there is neither VENEER_CONFIG_DIR nor a profile.
        /// </summary>
        public static string ConfigDirectory()
        {
            return VeneerConfigurationResolver.ConfigDirectory(
                Environment.GetEnvironmentVariable,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private static ConfigCandidates Candidates(RiverSystemProject project)
        {
            return VeneerConfigurationResolver.Resolve(
                ConfigDirectory(),
                project == null ? null : project.FullFilename,
                File.Exists);
        }

        public static string ConfigurationFilename(RiverSystemScenario scenario)
        {
            return ConfigurationFilename(scenario?.RiverSystemProject);
        }

        /// <summary>
        /// The sidecar beside the .rsproj, if it exists. Not "the effective
        /// configuration file": with three additive layers there is no single
        /// such file. Public API with no in-tree caller, kept because the
        /// question it answers is still a real one.
        /// </summary>
        public static string ConfigurationFilename(RiverSystemProject project)
        {
            return Candidates(project).SidecarLayer;
        }

        public static ResolvedVeneerConfiguration Load(RiverSystemScenario scenario)
        {
            return Load(scenario?.RiverSystemProject);
        }

        /// <summary>
        /// Never returns null. With no files it returns an empty resolved
        /// configuration, so consumers can dereference addons and options without
        /// a null check.
        /// </summary>
        public static ResolvedVeneerConfiguration Load(RiverSystemProject project)
        {
            var candidates = Candidates(project);
            var layers = new List<VeneerConfigurationLayer>();

            foreach (var path in candidates.Paths)
            {
                string json;
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    LogOnce("Veneer could not read '" + path + "': " + ex.Message);
                    continue;
                }

                VeneerConfiguration parsed;
                string error;
                if (!VeneerConfigurationResolver.TryParse(json, out parsed, out error))
                {
                    LogOnce("Veneer could not parse '" + path + "': " + error);
                    continue;
                }

                layers.Add(new VeneerConfigurationLayer { Path = path, Configuration = parsed });
            }

            var resolved = VeneerConfigurationResolver.Merge(layers);
            return resolved;
        }

        private static readonly HashSet<string> _loggedProblems = new HashSet<string>();

        /// <summary>
        /// Load runs on every menu open, so an unreadable file would otherwise log
        /// on every drop-down. Mirrors VeneerMenu.LogOnce, and is cleared from the
        /// same place, so a project change re-reports.
        /// </summary>
        private static void LogOnce(string message)
        {
            lock (_loggedProblems)
            {
                if (!_loggedProblems.Add(message)) return;
            }

            TIME.Management.Log.WriteError(typeof(VeneerConfiguration), message);
        }

        public static void ClearLoggedProblems()
        {
            lock (_loggedProblems)
            {
                _loggedProblems.Clear();
            }
        }

        /// <summary>
        /// After VeneerConfigurationResolver.Merge, every addon carries its own
        /// effective filter -- its layer's targetScenario was pushed into it. So
        /// there is nothing left for a config argument to contribute.
        /// </summary>
        public static string EffectiveFilter(VeneerAddon addon)
        {
            return addon == null ? null : addon.scenario;
        }

        /// <summary>
        /// Pure counterpart of AddonAppliesTo, so the matching rule is testable
        /// without a loaded RiverSystemScenario.
        /// </summary>
        public static bool AppliesTo(VeneerAddon addon, string activeScenarioName)
        {
            var filter = EffectiveFilter(addon);

            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(activeScenarioName)) return false;

            return string.Equals(activeScenarioName, filter, StringComparison.OrdinalIgnoreCase);
        }

        public static bool AddonAppliesTo(VeneerAddon addon, RiverSystemScenario currentScenario)
        {
            return AppliesTo(addon, currentScenario?.Name);
        }
    }

    public class VeneerAddon
    {
        public string name { get; set; }

        public string type { get; set; }

        public string path { get; set; }

        public string menu { get; set; }

        public string scenario { get; set; }

        public string[] args { get; set; }

        public Dictionary<string, string> env { get; set; }

        public string workingDirectory { get; set; }

        public string[] script { get; set; }

        public string url { get; set; }

        /// <summary>
        /// Permit more than one instance of this addon to run at once. Default false:
        /// the restrictive case is the default, so an addon that genuinely supports
        /// concurrency declares it rather than inheriting it by accident, and every
        /// .veneer file already deployed keeps single-instance behaviour untouched.
        /// Meaningless for type "url", which launches no process; harmless there.
        /// </summary>
        public bool allowMultiple { get; set; }

        /// <summary>
        /// Returns null when valid, otherwise a human-readable reason. Used to
        /// render a disabled menu item with a tooltip rather than silently
        /// omitting the entry.
        /// </summary>
        public static string Validate(VeneerAddon addon)
        {
            bool hasScript = addon.script != null && addon.script.Length > 0;
            bool hasUrl = !string.IsNullOrEmpty(addon.url);
            bool isUrlType = string.Equals(addon.type, "url", StringComparison.OrdinalIgnoreCase);

            // Mutual exclusion first, so an entry wrong in two ways reports the
            // structural problem rather than something downstream of it. Tests
            // script != null rather than hasScript, matching the path/script rule
            // below: an empty array must not mean "absent" for one rule and
            // "present" for another in the same method.
            if (hasUrl && (!string.IsNullOrEmpty(addon.path) || addon.script != null))
                return "specifies 'url' together with 'path' or 'script'; they are mutually exclusive";

            if (!string.IsNullOrEmpty(addon.path) && addon.script != null)
                return "specifies both 'path' and 'script'; they are mutually exclusive";

            // Without this, {"type":"exe","url":"..."} passes validation,
            // dispatches to LaunchExe, and fails inside Path.Combine(dir, null)
            // as an opaque ArgumentNullException rather than a schema error.
            if (hasUrl && !isUrlType)
                return "specifies 'url' but type is not 'url'";

            if (isUrlType && !hasUrl)
                return "is type 'url' but has no 'url'";

            if (hasUrl && !AddonUrl.HasAllowedScheme(addon.url))
                return "has a 'url' that is not http://, https:// or mailto:";

            if (string.Equals(addon.type, "script", StringComparison.OrdinalIgnoreCase) && !hasScript)
                return "is type 'script' but has no 'script' lines";

            // Without this, an 'exe' addon with no path passes validation and only
            // fails when the user clicks it. Catching it at menu-build time is the
            // whole point of rendering a disabled item with an explanatory tooltip.
            // Last, so the specific type reasons above win over this catch-all.
            if (!hasScript && !hasUrl && string.IsNullOrEmpty(addon.path))
                return "has neither 'path', 'script' nor 'url'; there is nothing to launch";

            return null;
        }
    }

    /// <summary>
    /// Nullable so that "absent" and "false" are distinguishable. Merging layers
    /// needs that distinction, and so does leaving a value alone that the GUI or
    /// an environment variable already set.
    /// </summary>
    public class VeneerOptions
    {
        public bool? autoStart;
        public bool? allowScripts;
        public int? defaultPort;
    }
}
