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
    }
}
