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
            var result = new List<string>();

            if (addons != null)
            {
                foreach (var addon in addons)
                {
                    // A null or absent `menu` resolves to DEFAULT_MENU, so a menuless
                    // addon claims Reporting's file position like any other.
                    var menu = TopLevelMenu(addon?.menu);
                    if (!result.Contains(menu))
                        result.Add(menu);
                }
            }

            // Reporting is appended only if no addon already claimed a position for it:
            // either because HTML reports need somewhere to live, or because the bar
            // would otherwise be empty and the Veneer logo would have no home.
            if (!result.Contains(DEFAULT_MENU) && (hasHtmlReports || result.Count == 0))
                result.Add(DEFAULT_MENU);

            return result;
        }
    }
}
