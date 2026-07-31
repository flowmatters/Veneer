using System.Linq;
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class MenuLayoutTests
    {
        // Expected values are comma-joined rather than string[] literals: an array in a
        // [TestCase] attribute is easy to get wrong, and joining sidesteps it entirely.
        [TestCase(null,                       "Reporting")]
        [TestCase("",                         "Reporting")]
        [TestCase("   ",                      "Reporting")]
        [TestCase("|",                        "Reporting")]
        [TestCase(" | ",                      "Reporting")]
        [TestCase("Reporting",                "Reporting")]
        [TestCase("Models",                   "Models")]
        [TestCase("  Models  ",               "Models")]
        [TestCase("Models|",                  "Models")]
        [TestCase("Models|Calibration",       "Models,Calibration")]
        [TestCase("Models| |Calibration",     "Models,Calibration")]
        [TestCase("Models|Calibration|Daily", "Models,Calibration,Daily")]
        [TestCase("| |Calibration",           "Calibration")]
        public void SplitMenuPath_ProducesExpectedSegments(string input, string expected)
        {
            Assert.That(string.Join(",", MenuLayout.SplitMenuPath(input)), Is.EqualTo(expected));
        }

        [TestCase(null,                 "Reporting")]
        [TestCase("",                   "Reporting")]
        [TestCase("   ",                "Reporting")]
        [TestCase("|",                  "Reporting")]
        [TestCase("Models",             "Models")]
        [TestCase("Models|Calibration", "Models")]
        [TestCase("| |Calibration",     "Calibration")]
        public void TopLevelMenu_ReturnsFirstSegment(string input, string expected)
        {
            Assert.That(MenuLayout.TopLevelMenu(input), Is.EqualTo(expected));
        }

        private static VeneerAddon[] Addons(params string[] menus)
        {
            return menus.Select(m => new VeneerAddon { name = "addon", menu = m }).ToArray();
        }

        private static string Bar(VeneerAddon[] addons, bool hasHtmlReports)
        {
            return string.Join(",", MenuLayout.TopLevelMenus(addons, hasHtmlReports));
        }

        // --- Rule table, row by row ---

        [Test] // row 1
        public void TopLevelMenus_PreservesFileOrder()
        {
            Assert.That(Bar(Addons("Modelling", "Analysis"), false),
                Is.EqualTo("Modelling,Analysis"));
        }

        [Test] // row 1, the reported bug: alphabetical order would put Alpha first
        public void TopLevelMenus_DoesNotSortAlphabetically()
        {
            Assert.That(Bar(Addons("Zebra", "Alpha"), false), Is.EqualTo("Zebra,Alpha"));
        }

        [Test] // row 2
        public void TopLevelMenus_AppendsReportingLastWhenOnlyHtmlReportsNeedIt()
        {
            Assert.That(Bar(Addons("Modelling", "Analysis"), true),
                Is.EqualTo("Modelling,Analysis,Reporting"));
        }

        [Test] // row 3
        public void TopLevelMenus_ReportingKeepsFilePositionWhenNamed()
        {
            Assert.That(Bar(Addons("Modelling", "Reporting", "Analysis"), false),
                Is.EqualTo("Modelling,Reporting,Analysis"));
            Assert.That(Bar(Addons("Modelling", "Reporting", "Analysis"), true),
                Is.EqualTo("Modelling,Reporting,Analysis"));
        }

        [Test] // row 4
        public void TopLevelMenus_CollapsesDuplicatesToFirstAppearance()
        {
            Assert.That(Bar(Addons("Analysis", "Modelling", "Analysis"), false),
                Is.EqualTo("Analysis,Modelling"));
        }

        [Test] // row 5 — the commonest .veneer shape
        public void TopLevelMenus_MenulessAddonClaimsReportingPosition()
        {
            Assert.That(Bar(Addons(null, "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons(null, "Modelling"), true), Is.EqualTo("Reporting,Modelling"));
        }

        [Test] // row 6
        public void TopLevelMenus_MenulessAddonLaterInFileClaimsLaterPosition()
        {
            Assert.That(Bar(Addons("Modelling", null), false), Is.EqualTo("Modelling,Reporting"));
        }

        [Test] // row 7
        public void TopLevelMenus_ReportingIsTheSoleFallback()
        {
            Assert.That(Bar(null, false), Is.EqualTo("Reporting"));
            Assert.That(Bar(null, true), Is.EqualTo("Reporting"));
            Assert.That(Bar(new VeneerAddon[0], false), Is.EqualTo("Reporting"));
            Assert.That(Bar(new VeneerAddon[0], true), Is.EqualTo("Reporting"));
        }

        // --- Beyond the table ---

        [Test]
        public void TopLevelMenus_OmitsReportingWhenNothingTargetsItAndThereAreNoReports()
        {
            Assert.That(Bar(Addons("Modelling"), false), Is.EqualTo("Modelling"));
        }

        [Test]
        public void TopLevelMenus_TreatsEmptyAndWhitespaceMenusAsReporting()
        {
            Assert.That(Bar(Addons("", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("   ", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("|", "Modelling"), false), Is.EqualTo("Reporting,Modelling"));
        }

        [Test]
        public void TopLevelMenus_MenulessAndExplicitReportingCollapseToEarlierPosition()
        {
            Assert.That(Bar(Addons(null, "Modelling", "Reporting"), false),
                Is.EqualTo("Reporting,Modelling"));
            Assert.That(Bar(Addons("Modelling", "Reporting", null), false),
                Is.EqualTo("Modelling,Reporting"));
        }

        [Test]
        public void TopLevelMenus_CasingIsDistinct()
        {
            // Matches FindOrCreateReportMenu's ordinal `item.Text == mnu` comparison,
            // which already creates two separate menus for these.
            Assert.That(Bar(Addons("Analysis", "analysis"), false),
                Is.EqualTo("Analysis,analysis"));
        }

        [Test]
        public void TopLevelMenus_UsesOnlyTheFirstSegmentOfANestedPath()
        {
            Assert.That(Bar(Addons("Models|Calibration", "Models|Validation"), false),
                Is.EqualTo("Models"));
        }

        [Test]
        public void TopLevelMenus_IgnoresScenarioFilters()
        {
            // A filtered-out addon is greyed out in the menu but still claims its
            // menu's position, so the bar does not shift when the scenario changes.
            var filtered = new[]
            {
                new VeneerAddon { name = "a", menu = "Modelling", scenario = "SomeOtherScenario" },
                new VeneerAddon { name = "b", menu = "Analysis" }
            };
            Assert.That(Bar(filtered, false), Is.EqualTo("Modelling,Analysis"));
        }

        [Test]
        public void TopLevelMenus_ToleratesANullAddonEntry()
        {
            // Newtonsoft will happily deserialise `"addons": [null]`.
            var withNull = new VeneerAddon[] { null, new VeneerAddon { name = "b", menu = "Analysis" } };
            Assert.That(Bar(withNull, false), Is.EqualTo("Reporting,Analysis"));
        }
    }
}
