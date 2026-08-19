using System;
using System.Collections.Generic;
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class VeneerConfigurationResolverTests
    {
        private static Func<string, string> Env(string configDir)
        {
            return name => name == "VENEER_CONFIG_DIR" ? configDir : null;
        }

        [Test]
        public void ConfigDirectory_PrefersTheEnvironmentVariable()
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(@"D:\shared\veneer"), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"D:\shared\veneer"));
        }

        [Test]
        public void ConfigDirectory_FallsBackToDotVeneerUnderTheProfile()
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(null), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"C:\Users\joel\.veneer"));
        }

        // Whitespace, not just empty: an unset variable read through a batch file
        // frequently arrives as " ", and treating that as a directory would send
        // every lookup to the process working directory.
        [TestCase("")]
        [TestCase("   ")]
        public void ConfigDirectory_TreatsBlankEnvironmentValueAsUnset(string configured)
        {
            var dir = VeneerConfigurationResolver.ConfigDirectory(
                Env(configured), () => @"C:\Users\joel");
            Assert.That(dir, Is.EqualTo(@"C:\Users\joel\.veneer"));
        }
    }
}
