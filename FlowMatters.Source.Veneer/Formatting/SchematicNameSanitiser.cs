using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.Formatting
{
    internal static class SchematicNameSanitiser
    {
        private static readonly Regex NonAlphaNum = new Regex("[^A-Za-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Sanitise a single Source element name to the tag-name character class.
        /// Lowercase; runs of non-alphanumerics collapse to a single '_'; trim leading/trailing '_'.
        /// Returns an empty string if the input contains no alphanumerics.
        /// </summary>
        public static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var collapsed = NonAlphaNum.Replace(name, "_").ToLowerInvariant();
            return collapsed.Trim('_');
        }

        /// <summary>
        /// Sanitise a list of names, falling back to "&lt;fallbackPrefix&gt;_&lt;index&gt;" when sanitisation yields
        /// an empty string, and de-colliding duplicates by appending "_2", "_3", ... in order.
        /// Returns one tag-name per input, same order.
        /// </summary>
        public static List<string> SaniseAndDeCollide(IList<string> names, string fallbackPrefix)
        {
            var taken = new HashSet<string>();
            var result = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                var raw = Sanitise(names[i]);
                if (raw.Length == 0) raw = fallbackPrefix + "_" + i;

                var candidate = raw;
                int suffix = 2;
                while (taken.Contains(candidate))
                {
                    candidate = raw + "_" + suffix;
                    suffix++;
                }
                taken.Add(candidate);
                result.Add(candidate);
            }
            return result;
        }
    }
}
