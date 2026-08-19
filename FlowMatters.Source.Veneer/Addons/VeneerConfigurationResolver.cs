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
}
