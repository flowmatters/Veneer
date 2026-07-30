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
        /// process environment + Veneer's injected variables + the addon's own env
        /// (which wins). Values in the addon's env are expanded against everything
        /// above them, but NOT against each other -- that would make the result
        /// depend on JSON key order.
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

            if (addonEnv != null)
            {
                // Snapshot first so addon entries cannot resolve against each other.
                var baseline = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in addonEnv)
                    env[kv.Key] = Expand(kv.Value, baseline);
            }

            return env;
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
