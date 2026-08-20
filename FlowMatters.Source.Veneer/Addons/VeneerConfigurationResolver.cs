using System;
using System.Collections.Generic;
using System.IO;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// Discovery and merge logic for .veneer configuration. Deliberately free of
    /// RiverSystem, TIME, WinForms and System.IO.File dependencies -- the file
    /// system enters only through injected delegates -- so that the whole
    /// precedence table is testable without a loaded Source scenario. Path.Combine
    /// is string manipulation and touches no disk.
    /// </summary>
    public static class VeneerConfigurationResolver
    {
        public const string GLOBAL_FILENAME = "global.veneer";
        public const string CONFIG_DIR_VARIABLE = "VENEER_CONFIG_DIR";
        public const string DEFAULT_CONFIG_DIR_NAME = ".veneer";
        public const string CONFIG_EXTENSION = ".veneer";

        /// <summary>
        /// Never creates the directory and never checks that it exists -- a
        /// missing directory simply contributes no layers. Returns null when
        /// there is no profile to fall back on, which callers treat as
        /// "no global configuration".
        /// </summary>
        public static string ConfigDirectory(Func<string, string> getEnv, Func<string> userProfile)
        {
            var configured = getEnv(CONFIG_DIR_VARIABLE);
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var profile = userProfile();
            if (string.IsNullOrWhiteSpace(profile))
                return null;

            return Path.Combine(profile, DEFAULT_CONFIG_DIR_NAME);
        }

        /// <summary>
        /// The project layer is one slot with two candidates: a file named for the
        /// project in the configuration directory REPLACES the sidecar, which is
        /// the escape hatch for a repository whose committed sidecar you do not
        /// want. The global layer is additive and never replaces anything.
        /// </summary>
        public static ConfigCandidates Resolve(
            string configDir, string projectFullFilename, Func<string, bool> exists)
        {
            var result = new ConfigCandidates();

            if (!string.IsNullOrWhiteSpace(projectFullFilename))
            {
                // Path.GetFileName, not FullFilename.Replace(".rsproj", ...):
                // Replace substitutes every occurrence, so a project under a
                // directory named "models.rsproj" had its directory rewritten too.
                var projectFileName = Path.GetFileName(projectFullFilename);
                var directory = Path.GetDirectoryName(projectFullFilename) ?? string.Empty;
                var sidecar = Path.Combine(directory, projectFileName + CONFIG_EXTENSION);

                var globalProject = configDir == null
                    ? null
                    : Path.Combine(configDir, projectFileName + CONFIG_EXTENSION);

                if (globalProject != null && exists(globalProject))
                {
                    result.ProjectLayer = globalProject;
                    // Only when a sidecar is actually on disk: this drives a log
                    // line saying "superseding", which is a lie if nothing was.
                    if (exists(sidecar))
                        result.SupersededSidecar = sidecar;
                }
                else if (exists(sidecar))
                {
                    result.ProjectLayer = sidecar;
                }
            }

            if (configDir != null)
            {
                var global = Path.Combine(configDir, GLOBAL_FILENAME);
                if (exists(global))
                    result.GlobalLayer = global;
            }

            return result;
        }

        /// <summary>
        /// Layers arrive project-first, so "the first layer to specify a field
        /// wins" is exactly "the project layer beats the global one".
        ///
        /// Mutates each addon's `scenario` to push its layer's targetScenario
        /// down. Safe because Load deserializes a fresh object graph on every
        /// call; nothing else holds a reference to these addons.
        ///
        /// That safety is the reason Load does not cache. Caching a layer so it
        /// outlives one Load call would let this run twice over the same addon,
        /// or let two callers see a half-mutated list. Anything that adds caching
        /// has to copy the addons here first.
        /// </summary>
        public static ResolvedVeneerConfiguration Merge(IList<VeneerConfigurationLayer> layers)
        {
            var result = new ResolvedVeneerConfiguration();
            if (layers == null) return result;

            var addons = new List<VeneerAddon>();
            var sources = new List<string>();
            var options = new VeneerOptions();

            foreach (var layer in layers)
            {
                if (layer == null || layer.Configuration == null) continue;
                sources.Add(layer.Path);

                if (layer.Configuration.addons != null)
                {
                    foreach (var addon in layer.Configuration.addons)
                    {
                        if (addon == null) continue;

                        // Push targetScenario down now, while we still know which
                        // file this addon came from. Concatenated into one list
                        // there is no longer any correct single value for it.
                        if (string.IsNullOrEmpty(addon.scenario))
                            addon.scenario = layer.Configuration.targetScenario;

                        addons.Add(addon);
                    }
                }

                var layerOptions = layer.Configuration.options;
                if (layerOptions != null)
                {
                    if (options.allowScripts == null) options.allowScripts = layerOptions.allowScripts;
                    if (options.defaultPort == null) options.defaultPort = layerOptions.defaultPort;
                    if (options.autoStart == null) options.autoStart = layerOptions.autoStart;
                }
            }

            result.addons = addons.ToArray();
            result.options = options;
            result.SourceFiles = sources.ToArray();
            return result;
        }

        /// <summary>
        /// Never throws. Each layer is parsed in isolation so that one malformed
        /// file leaves the others working -- and because Load runs on every
        /// dropdown open, where an exception would surface as a dialog.
        /// </summary>
        public static bool TryParse(string json, out VeneerConfiguration config, out string error)
        {
            error = null;

            // Guarded rather than left to Newtonsoft: an empty or whitespace file
            // is a user saying "nothing here", not an error, and this does not
            // depend on how the parser happens to treat an empty document.
            if (string.IsNullOrWhiteSpace(json))
            {
                config = new VeneerConfiguration();
                return true;
            }

            try
            {
                config = Newtonsoft.Json.JsonConvert.DeserializeObject<VeneerConfiguration>(json);

                // The literal "null" is valid JSON and deserializes to null.
                if (config == null)
                    config = new VeneerConfiguration();

                return true;
            }
            catch (Exception ex)
            {
                config = null;
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// The files that will be loaded, in resolution order, plus the sidecar a
    /// global project file displaced -- reported so the user can be told why the
    /// file next to their .rsproj is being ignored.
    /// </summary>
    public class ConfigCandidates
    {
        public string ProjectLayer;
        public string GlobalLayer;
        public string SupersededSidecar;

        public string[] Paths
        {
            get
            {
                var result = new List<string>();
                if (ProjectLayer != null) result.Add(ProjectLayer);
                if (GlobalLayer != null) result.Add(GlobalLayer);
                return result.ToArray();
            }
        }
    }

    public class VeneerConfigurationLayer
    {
        public string Path;
        public VeneerConfiguration Configuration;
    }

    /// <summary>
    /// The effective configuration for a project. Has no targetScenario: it was
    /// pushed into each addon during the merge, and across layers there is no
    /// correct single value for one. addons and options are never null, so
    /// consumers can test individual option fields rather than the block.
    /// </summary>
    public class ResolvedVeneerConfiguration
    {
        public VeneerAddon[] addons = new VeneerAddon[0];
        public VeneerOptions options = new VeneerOptions();
        public string[] SourceFiles = new string[0];
        public string SupersededSidecar;
    }
}
