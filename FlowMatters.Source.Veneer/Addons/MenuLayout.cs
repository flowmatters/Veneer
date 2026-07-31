using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// Pure menu-path and menu-ordering logic for <c>.veneer</c> addon configuration.
    /// Deliberately free of RiverSystem, TIME and WinForms dependencies so that it is
    /// testable without a loaded Source scenario.
    /// </summary>
    public static class MenuLayout
    {
        public const string DEFAULT_MENU = "Reporting";

        public static string[] SplitMenuPath(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return new[] { DEFAULT_MENU };

            // Trim *before* discarding empties. The original did the reverse, so a
            // whitespace-only segment survived as "" and became a blank-titled menu.
            var segments = menuPath.Split('|')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            return segments.Length > 0 ? segments : new[] { DEFAULT_MENU };
        }

        public static string TopLevelMenu(string menuPath)
        {
            return SplitMenuPath(menuPath)[0];
        }

        public static List<string> TopLevelMenus(VeneerAddon[] addons, bool hasHtmlReports)
        {
            throw new NotImplementedException();
        }
    }
}
