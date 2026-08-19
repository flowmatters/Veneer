namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How one addon's menu item should look: its text, whether it is clickable, and
    /// the tooltip explaining why not.
    ///
    /// Pure, and the SINGLE writer of those three properties. It replaces a sequence in
    /// PopulateReportMenu that assigned them three times in a row, where the
    /// scenario-filter block overwrote an invalid addon's tooltip -- a defect the code
    /// carried as a comment rather than a fix.
    /// </summary>
    public class AddonMenuItemState
    {
        public string Text { get; private set; }
        public bool Enabled { get; private set; }
        public string ToolTipText { get; private set; }

        private AddonMenuItemState(string text, bool enabled, string toolTipText)
        {
            Text = text;
            Enabled = enabled;
            ToolTipText = toolTipText;
        }

        /// <summary>
        /// effectiveFilter is a separate parameter and NOT derivable from the addon:
        /// VeneerConfiguration.EffectiveFilter falls back to config.targetScenario when
        /// the addon carries no `scenario`, and this function has no config.
        /// Precedence is top to bottom.
        /// </summary>
        public static AddonMenuItemState For(VeneerAddon addon, string invalid,
                                             bool appliesToScenario, string effectiveFilter,
                                             int runningCount)
        {
            var name = addon.name;

            if (invalid != null)
                return new AddonMenuItemState(name, false, "Invalid addon: " + invalid);

            if (!IsKnownType(addon.type))
                return new AddonMenuItemState(name, false,
                    "Unknown addon type '" + addon.type + "'");

            if (!appliesToScenario)
                return new AddonMenuItemState(name, false,
                    "Requires scenario '" + effectiveFilter + "' to be active");

            if (runningCount > 0)
            {
                if (!addon.allowMultiple)
                    return new AddonMenuItemState(name + " (running)", false,
                        "Already running - close the app to launch it again.");

                if (runningCount == 1)
                    return new AddonMenuItemState(name + " (running)", true,
                        "1 instance already running - launching again will start another.");

                return new AddonMenuItemState(
                    name + " (" + runningCount + " running)", true,
                    runningCount + " instances already running - launching again will start another.");
            }

            // null rather than "": both render no tooltip, and null is what an item that
            // was never assigned one carries.
            return new AddonMenuItemState(name, true, null);
        }

        /// <summary>
        /// Ordinal, matching the case-SENSITIVE dispatch switch in
        /// VeneerMenu.PopulateReportMenu. VeneerAddon.Validate is case-insensitive, so
        /// the two can disagree about e.g. "URL" -- if this function were the lenient
        /// one, such an addon would render enabled with no Click handler attached.
        /// </summary>
        private static bool IsKnownType(string type)
        {
            return type == "exe" || type == "script" || type == "url";
        }
    }
}
