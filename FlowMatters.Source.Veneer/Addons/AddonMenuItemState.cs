namespace FlowMatters.Source.Veneer.Addons
{
    /// <summary>
    /// How one addon's menu item should look: its text, whether it is clickable, and
    /// the tooltip explaining why not.
    ///
    /// Pure, and will become the SINGLE writer of those three properties once Task 6
    /// wires it into VeneerMenu.PopulateReportMenu, which today assigns them three
    /// times in a row -- where the scenario-filter block overwrites an invalid addon's
    /// tooltip, a defect the code currently carries as a comment rather than a fix.
    /// At this commit it has no callers.
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
        ///
        /// Precedence is top to bottom:
        ///   1. invalid (structurally broken -- Validate already rejected it)
        ///   2. unknown type
        ///   3. scenario filter
        ///   4. running
        /// Unknown-type sits second, ahead of the scenario filter and running checks,
        /// because a type with no arm in the dispatch switch can never be launched no
        /// matter what else is true of it -- an addon can be both scenario-filtered (or
        /// running) and unknown-typed at once, and there is nothing the other checks
        /// could say that would make it launchable.
        ///
        /// Callers hold an invariant this function relies on but does not itself check:
        /// appliesToScenario is only ever false alongside a non-empty effectiveFilter,
        /// because VeneerConfiguration.AddonAppliesTo returns true whenever the filter
        /// is empty. Calling For directly with (applies: false, filter: null) -- as no
        /// real call site does -- renders "Requires scenario '' to be active".
        /// </summary>
        public static AddonMenuItemState For(VeneerAddon addon, string invalid,
                                             bool appliesToScenario, string effectiveFilter,
                                             int runningCount)
        {
            var name = addon.name;

            if (invalid != null)
                return new AddonMenuItemState(name, false, "Invalid addon: " + invalid + ".");

            if (!IsKnownType(addon.type))
                return new AddonMenuItemState(name, false,
                    "Unknown addon type '" + addon.type + "'.");

            if (!appliesToScenario)
                return new AddonMenuItemState(name, false,
                    "Requires scenario '" + effectiveFilter + "' to be active.");

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
