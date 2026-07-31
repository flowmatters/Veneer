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

        public static string ConfigurationFilename(RiverSystemScenario scenario)
        {
            return ConfigurationFilename(scenario?.RiverSystemProject);
        }

        public static string ConfigurationFilename(RiverSystemProject project){
            if (project?.FullFilename == null)
            {
                return null;
            }

            var result = project.FullFilename.Replace(".rsproj", ".rsproj.veneer");
            if (File.Exists(result))
            {
                return result;
            }

            return null;
        }

        public static VeneerConfiguration Load(RiverSystemScenario scenario)
        {
            return Load(scenario?.RiverSystemProject);
        }

        public static VeneerConfiguration Load(RiverSystemProject project)
        {
            var filename = ConfigurationFilename(project);
            if (filename == null)
            {
                return null;
            }
            var json = File.ReadAllText(filename);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<VeneerConfiguration>(json);
        }

        public static bool AddonAppliesTo(
            VeneerAddon addon,
            VeneerConfiguration config,
            RiverSystemScenario currentScenario)
        {
            var filter = EffectiveFilter(addon, config);

            if (string.IsNullOrEmpty(filter)) return true;
            if (currentScenario == null) return false;

            return string.Equals(
                currentScenario.Name,
                filter,
                StringComparison.OrdinalIgnoreCase);
        }

        public static string EffectiveFilter(VeneerAddon addon, VeneerConfiguration config)
        {
            return !string.IsNullOrEmpty(addon?.scenario)
                ? addon.scenario
                : config?.targetScenario;
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

    public class VeneerOptions
    {
        public bool autoStart;
        public bool allowScripts;
        public int defaultPort;
    }
}
