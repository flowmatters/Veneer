using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonUrlTests
    {
        [TestCase("http://wiki.example.org/catchment")]
        [TestCase("https://wiki.example.org/catchment")]
        [TestCase("mailto:support@example.org")]
        [TestCase("HTTPS://WIKI.EXAMPLE.ORG")]        // schemes are case-insensitive
        [TestCase("MailTo:support@example.org")]
        [TestCase("  https://wiki.example.org  ")]     // incidental JSON whitespace
        [TestCase("http://localhost:%VENEER_PORT%/doc/notes.html")]
        [TestCase("https://wiki.example.org/%VENEER_SCENARIO%")]
        public void HasAllowedScheme_Accepts(string url)
        {
            Assert.That(AddonUrl.HasAllowedScheme(url), Is.True);
        }

        // file:// is deliberately excluded: it would admit file://server/share/tool.exe,
        // and without it type "url" cannot launch a local program by any spelling.
        [TestCase("file://server/share/manual.pdf")]
        // Uri.TryCreate calls this scheme "file", which is why validation is a
        // literal prefix test rather than Uri parsing.
        [TestCase(@"C:\Windows\System32\cmd.exe")]
        [TestCase(@"\\server\share\tool.exe")]
        [TestCase("ms-msdt:/id")]
        [TestCase("javascript:alert(1)")]
        [TestCase("wiki.example.org")]                 // no scheme at all
        [TestCase("https:/wiki.example.org")]          // one slash
        [TestCase("httpsx://wiki.example.org")]
        [TestCase("%HELP_URL%")]                       // scheme must be literal
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void HasAllowedScheme_Rejects(string url)
        {
            Assert.That(AddonUrl.HasAllowedScheme(url), Is.False);
        }

        [Test]
        public void Normalise_TrimsSurroundingWhitespace()
        {
            Assert.That(AddonUrl.Normalise("  https://x  "), Is.EqualTo("https://x"));
        }

        [Test]
        public void Normalise_ReturnsNullForNull()
        {
            Assert.That(AddonUrl.Normalise(null), Is.Null);
        }

        [Test]
        public void Normalise_LeavesInteriorTextAlone()
        {
            Assert.That(AddonUrl.Normalise("https://x/a b"), Is.EqualTo("https://x/a b"));
        }
    }
}
