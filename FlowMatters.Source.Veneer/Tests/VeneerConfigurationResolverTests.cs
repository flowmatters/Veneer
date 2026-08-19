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

        private const string CONFIG = @"C:\Users\joel\.veneer";
        private const string PROJECT = @"C:\models\ExampleProject.rsproj";
        private const string SIDECAR = @"C:\models\ExampleProject.rsproj.veneer";
        private const string GLOBAL_PROJECT = @"C:\Users\joel\.veneer\ExampleProject.rsproj.veneer";
        private const string GLOBAL = @"C:\Users\joel\.veneer\global.veneer";

        private static Func<string, bool> Existing(params string[] paths)
        {
            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        private static string Chain(ConfigCandidates candidates)
        {
            return string.Join(" | ", candidates.Paths);
        }

        [Test]
        public void Resolve_SidecarOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(SIDECAR));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_GlobalProjectFileReplacesTheSidecar()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT),
                        "the sidecar must not also appear -- the project layer is one slot");
            Assert.That(c.SupersededSidecar, Is.EqualTo(SIDECAR));
        }

        // SupersededSidecar drives a log line that says "superseding". With no
        // sidecar on disk nothing was displaced and the line would be a lie.
        [Test]
        public void Resolve_GlobalProjectFileAloneSupersedesNothing()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_NoFilesAtAll()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing());
            Assert.That(Chain(c), Is.EqualTo(""));
            Assert.That(c.ProjectLayer, Is.Null);
            Assert.That(c.GlobalLayer, Is.Null);
        }

        [Test]
        public void Resolve_GlobalIsAppendedAfterTheProjectLayer()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Resolve_GlobalIsAppendedAfterAGlobalProjectFileToo()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT + " | " + GLOBAL));
            Assert.That(c.SupersededSidecar, Is.EqualTo(SIDECAR));
        }

        [Test]
        public void Resolve_GlobalAlone()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.ProjectLayer, Is.Null);
        }

        // An unsaved project has no project layer but must still receive the
        // wildcard file -- that is the point of a wildcard file.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_WithoutAProjectFileStillFindsGlobal(string projectFile)
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, projectFile, Existing(SIDECAR, GLOBAL_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.SupersededSidecar, Is.Null);
        }

        [Test]
        public void Resolve_WithoutAConfigDirectoryFallsBackToTheSidecar()
        {
            var c = VeneerConfigurationResolver.Resolve(null, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
        }

        // The old ConfigurationFilename used FullFilename.Replace(".rsproj",
        // ".rsproj.veneer"), which substitutes EVERY occurrence -- so this project
        // looked for C:\models.rsproj.veneer\a.rsproj.veneer, in a directory that
        // does not exist.
        [Test]
        public void Resolve_DoesNotRewriteADirectoryNamedLikeAProject()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"C:\models.rsproj\a.rsproj",
                Existing(@"C:\models.rsproj\a.rsproj.veneer"));
            Assert.That(Chain(c), Is.EqualTo(@"C:\models.rsproj\a.rsproj.veneer"));
        }

        [Test]
        public void Resolve_GlobalProjectFileIsNamedForTheProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"D:\elsewhere\ExampleProject.rsproj", Existing(GLOBAL_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL_PROJECT),
                        "matching is by file name, so a project of the same name anywhere matches");
        }
    }
}
