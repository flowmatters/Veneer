using FlowMatters.Source.Veneer.DomainActions;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonCommandLineTests
    {
        [TestCase("plain", "plain")]
        [TestCase("has space", "\"has space\"")]
        [TestCase("", "\"\"")]
        [TestCase("a&b", "a&b")]
        [TestCase("a|b", "a|b")]
        public void QuoteArgument_QuotesOnWhitespaceOnly(string input, string expected)
        {
            Assert.That(AddonCommandLine.QuoteArgument(input), Is.EqualTo(expected));
        }

        [TestCase("plain", "plain")]
        [TestCase("has space", "\"has space\"")]
        [TestCase("", "\"\"")]
        [TestCase("a&b", "\"a&b\"")]
        [TestCase("a|b", "\"a|b\"")]
        [TestCase("a<b", "\"a<b\"")]
        [TestCase("a>b", "\"a>b\"")]
        [TestCase("a^b", "\"a^b\"")]
        [TestCase("a(b)c", "\"a(b)c\"")]
        public void QuoteArgumentForCmd_AlsoQuotesOnMetacharacters(string input, string expected)
        {
            Assert.That(AddonCommandLine.QuoteArgumentForCmd(input), Is.EqualTo(expected));
        }

        [Test]
        public void QuoteArgument_EscapesEmbeddedQuote()
        {
            Assert.That(AddonCommandLine.QuoteArgument("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
        }

        [Test]
        public void QuoteArgument_DoublesTrailingBackslashesBeforeClosingQuote()
        {
            Assert.That(AddonCommandLine.QuoteArgument("C:\\dir with space\\"), Is.EqualTo("\"C:\\dir with space\\\\\""));
        }

        [Test]
        public void Compose_Exe_UsesPathAsFileName()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\tools\calib.exe", new[] { "--out", "results 2026" },
                                     out fileName, out arguments);
            Assert.That(fileName, Is.EqualTo(@"C:\tools\calib.exe"));
            Assert.That(arguments, Is.EqualTo("--out \"results 2026\""));
        }

        [Test]
        public void Compose_Bat_WrapsInCmdWithDoubledOuterQuotes()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\dir with space\x.bat", new[] { "arg with space" },
                                     out fileName, out arguments);
            Assert.That(fileName, Is.EqualTo("cmd.exe"));
            Assert.That(arguments, Is.EqualTo(
                "/D /V:OFF /C \"\"C:\\dir with space\\x.bat\" \"arg with space\"\""));
        }

        [Test]
        public void Compose_Bat_QuotesMetacharacterArgument()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\t\x.bat", new[] { "a&b" }, out fileName, out arguments);
            Assert.That(arguments, Is.EqualTo("/D /V:OFF /C \"\"C:\\t\\x.bat\" \"a&b\"\""));
        }

        [Test]
        public void Compose_Bat_NoArgs_StillDoublesQuotes()
        {
            string fileName, arguments;
            AddonCommandLine.Compose(@"C:\dir with space\x.bat", null, out fileName, out arguments);
            Assert.That(arguments, Is.EqualTo("/D /V:OFF /C \"\"C:\\dir with space\\x.bat\"\""));
        }
    }
}
