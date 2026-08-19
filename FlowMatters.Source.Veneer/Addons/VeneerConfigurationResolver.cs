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
    }
}
