using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonEnvironment
    {
        private static readonly Regex VariablePattern =
            new Regex("%([^%]+)%", RegexOptions.Compiled);

        /// <summary>
        /// process environment + Veneer's injected variables + the .veneer files'
        /// file-level env + the addon's own env, each winning over the one before.
        /// Each layer is expanded against a snapshot of the layers above it, never
        /// against itself, so no result depends on JSON key order.
        /// </summary>
        public static Dictionary<string, string> BuildEffective(
            AddonContext context, IDictionary<string, string> addonEnv)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                env[(string)entry.Key] = entry.Value as string ?? string.Empty;

            env["VENEER_PORT"] = context.Port.ToString();
            env["VENEER_PROJECT_DIR"] = context.ProjectDirectory ?? string.Empty;
            env["VENEER_PROJECT_FILE"] = context.ProjectFile ?? string.Empty;
            env["VENEER_CONFIG_DIR"] = context.ConfigDirectory ?? string.Empty;

            // The .veneer files' own env, then the addon's, which is more specific
            // and so is applied last.
            ApplyLayer(env, context.ConfigEnv);
            ApplyLayer(env, addonEnv);

            return env;
        }

        /// <summary>
        /// Writes one layer over the accumulated environment, expanding its values
        /// against a snapshot taken before any of them are written.
        ///
        /// The snapshot is the whole point, and it lives here rather than being
        /// repeated per layer so that it cannot be preserved in one place and lost
        /// in another. Expanding against the live dictionary instead would let one
        /// entry resolve against another in the same layer, making the result
        /// depend on dictionary iteration order.
        /// </summary>
        private static void ApplyLayer(
            Dictionary<string, string> env, IDictionary<string, string> layer)
        {
            if (layer == null) return;

            var snapshot = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in layer)
                env[kv.Key] = Expand(kv.Value, snapshot);
        }

        /// <summary>
        /// Replaces %NAME% where NAME is present. Unknown variables are left
        /// intact so a typo is visible rather than silently blanking an argument.
        /// Single pass: substituted values are not themselves rescanned.
        /// </summary>
        public static string Expand(string input, IDictionary<string, string> env)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return VariablePattern.Replace(input, match =>
            {
                string value;
                return env.TryGetValue(match.Groups[1].Value, out value) ? value : match.Value;
            });
        }
    }
}
