using System;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// The scheme allowlist for <c>type: "url"</c> addons. Pure: no RiverSystem,
    /// TIME or WinForms dependency, so it is testable without a loaded scenario.
    /// </summary>
    public static class AddonUrl
    {
        private static readonly string[] AllowedPrefixes = { "http://", "https://", "mailto:" };

        /// <summary>
        /// The canonical form of a url value. Trimmed, because an incidental
        /// leading space in JSON would otherwise fail the prefix test for no
        /// reason the author could see. Null in, null out.
        /// </summary>
        public static string Normalise(string url)
        {
            return url == null ? null : url.Trim();
        }

        /// <summary>
        /// A literal prefix test, not Uri parsing, and deliberately so.
        /// Uri.TryCreate rejects "http://localhost:%VENEER_PORT%/x" -- the
        /// variable sits in the port position -- and reports scheme "file" for
        /// "C:\Windows\System32\cmd.exe". Parsing would therefore reject the most
        /// useful form and accept a bare executable path.
        /// </summary>
        public static bool HasAllowedScheme(string url)
        {
            var normalised = Normalise(url);
            if (string.IsNullOrEmpty(normalised)) return false;

            foreach (var prefix in AllowedPrefixes)
            {
                if (normalised.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
