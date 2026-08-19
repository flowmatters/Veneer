using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonMenuItemStateTests
    {
        private static VeneerAddon Addon(string type = "script", bool allowMultiple = false)
        {
            return new VeneerAddon
            {
                name = "Example Tool", type = type, script = new[] { "echo hi" },
                allowMultiple = allowMultiple
            };
        }

        private static AddonMenuItemState State(VeneerAddon addon, string invalid = null,
                                                bool applies = true, string filter = null,
                                                int running = 0)
        {
            return AddonMenuItemState.For(addon, invalid, applies, filter, running);
        }

        [Test]
        public void NotRunningAndValidIsPlainAndEnabled()
        {
            var s = State(Addon());
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
            Assert.That(s.Enabled, Is.True);
            Assert.That(s.ToolTipText, Is.Null);
        }

        [Test]
        public void InvalidIsDisabledWithTheReason()
        {
            var s = State(Addon(), invalid: "has no 'script' lines");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
        }

        [Test]
        public void UnknownTypeIsDisabledAndNamesTheType()
        {
            var s = State(Addon(type: "wibble"));
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "wibble");
        }

        // The dispatch switch in VeneerMenu.PopulateReportMenu is case-SENSITIVE while
        // VeneerAddon.Validate is not. If this function disagreed, a "URL" addon could be
        // rendered enabled while the switch attached no Click handler -- a menu item that
        // silently does nothing, which is the defect that switch's default arm was added
        // to fix. Same ordinal comparison, deliberately.
        [TestCase("exe")]
        [TestCase("script")]
        [TestCase("url")]
        public void KnownTypesAreEnabled(string type)
        {
            Assert.That(State(Addon(type: type)).Enabled, Is.True);
        }

        [TestCase("Script")]
        [TestCase("EXE")]
        [TestCase("Url")]
        public void TypeMatchingIsCaseSensitiveLikeTheDispatchSwitch(string type)
        {
            Assert.That(State(Addon(type: type)).Enabled, Is.False);
        }

        [Test]
        public void ScenarioFilteredIsDisabledAndNamesTheFilter()
        {
            var s = State(Addon(), applies: false, filter: "Ops");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "Ops");
        }

        // The filter comes from VeneerConfiguration.EffectiveFilter, which falls back to
        // config.targetScenario when the addon carries no `scenario` -- so it must be
        // passed in. This test fails if someone "simplifies" the signature to read
        // addon.scenario directly.
        [Test]
        public void TheFilterComesFromTheParameterNotTheAddon()
        {
            var addon = Addon();
            addon.scenario = null;
            var s = State(addon, applies: false, filter: "FromTargetScenario");
            AddonAssert.Contains(s.ToolTipText, "FromTargetScenario");
        }

        // Regression for the defect documented at VeneerMenu.cs:126-128, where the
        // scenario-filter block overwrote an invalid addon's tooltip. Precedence is
        // top to bottom and invalid wins.
        [Test]
        public void InvalidBeatsScenarioFilterForTheTooltip()
        {
            var s = State(Addon(), invalid: "has no 'script' lines", applies: false, filter: "Ops");
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
            Assert.That(s.ToolTipText.Contains("Ops"), Is.False,
                        "the scenario filter overwrote the invalid reason");
        }

        [Test]
        public void InvalidBeatsRunning()
        {
            var s = State(Addon(), invalid: "has no 'script' lines", running: 1);
            AddonAssert.Contains(s.ToolTipText, "has no 'script' lines");
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
        }

        [Test]
        public void ScenarioFilterBeatsRunning()
        {
            var s = State(Addon(), applies: false, filter: "Ops", running: 1);
            AddonAssert.Contains(s.ToolTipText, "Ops");
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
        }

        [Test]
        public void RunningSingleInstanceIsLabelledAndDisabled()
        {
            var s = State(Addon(), running: 1);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.False);
            AddonAssert.Contains(s.ToolTipText, "Already running");
        }

        // Reachable by flipping allowMultiple true->false in the .veneer between
        // launches: VeneerConfiguration.Load re-reads on every dropdown open. The label
        // stays "(running)" rather than exposing a count the addon has declared it does
        // not support.
        [Test]
        public void RunningManyWithoutAllowMultipleStaysDisabledAndUncounted()
        {
            var s = State(Addon(), running: 3);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.False);
        }

        [Test]
        public void AllowMultipleStaysEnabledWhileRunning()
        {
            var s = State(Addon(allowMultiple: true), running: 1);
            Assert.That(s.Text, Is.EqualTo("Example Tool (running)"));
            Assert.That(s.Enabled, Is.True);
            AddonAssert.Contains(s.ToolTipText, "1 instance already running");
        }

        [Test]
        public void AllowMultipleShowsTheCountAboveOne()
        {
            var s = State(Addon(allowMultiple: true), running: 3);
            Assert.That(s.Text, Is.EqualTo("Example Tool (3 running)"));
            Assert.That(s.Enabled, Is.True);
            AddonAssert.Contains(s.ToolTipText, "3 instances already running");
        }

        [Test]
        public void AllowMultipleNotRunningIsPlain()
        {
            var s = State(Addon(allowMultiple: true));
            Assert.That(s.Text, Is.EqualTo("Example Tool"));
            Assert.That(s.Enabled, Is.True);
        }
    }
}
