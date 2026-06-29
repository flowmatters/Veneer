using System.Collections.Generic;
using FlowMatters.Source.Veneer.Formatting;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class SchematicNameSanitiserTests
    {
        [TestCase("Storage @ Site #3", "storage_site_3")]
        [TestCase("Link  -- 5",        "link_5")]
        [TestCase("Burrendong Dam",    "burrendong_dam")]
        [TestCase("  ALL CAPS  ",      "all_caps")]
        [TestCase("__leading-trailing__", "leading_trailing")]
        [TestCase("Already_snake_case", "already_snake_case")]
        [TestCase("123Start",          "123start")]
        [TestCase("",                  "")]
        [TestCase("---",               "")]
        [TestCase("café",              "caf")]
        public void Sanitise_ProducesExpectedOutput(string input, string expected)
        {
            Assert.That(SchematicNameSanitiser.Sanitise(input), Is.EqualTo(expected));
        }

        [Test]
        public void DeCollide_AppendsNumericSuffixInOrder()
        {
            var input = new List<string> { "Storage 1", "Storage 1", "Storage 1", "Other" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "elem");
            Assert.That(result, Is.EqualTo(new[] { "storage_1", "storage_1_2", "storage_1_3", "other" }));
        }

        [Test]
        public void DeCollide_FallbackUsedForEmptySanitisation()
        {
            var input = new List<string> { "---", "Real Name", "***" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "link");
            Assert.That(result, Is.EqualTo(new[] { "link_0", "real_name", "link_2" }));
        }

        [Test]
        public void DeCollide_FallbacksCanThemselvesCollide()
        {
            var input = new List<string> { "Link 0", "***" };
            var result = SchematicNameSanitiser.SaniseAndDeCollide(input, "link");
            Assert.That(result, Is.EqualTo(new[] { "link_0", "link_1" }));
        }
    }
}
