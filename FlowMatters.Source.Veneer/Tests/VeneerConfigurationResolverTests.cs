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
        private const string HOME_PROJECT = @"C:\Users\joel\.veneer\ExampleProject.rsproj.veneer";
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
        public void Resolve_FindsAllThreeLayersMostSpecificFirst()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, HOME_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT + " | " + SIDECAR + " | " + GLOBAL));
        }

        // The whole point of the change: a home file that supplies only env must
        // leave the sidecar's addons in place rather than replacing them.
        [Test]
        public void Resolve_HomeProjectFileAndSidecarBothLoad()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT + " | " + SIDECAR));
        }

        [Test]
        public void Resolve_SidecarOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(SIDECAR));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR));
            Assert.That(c.HomeProjectLayer, Is.Null);
        }

        [Test]
        public void Resolve_HomeProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT));
            Assert.That(c.SidecarLayer, Is.Null);
        }

        [Test]
        public void Resolve_GlobalOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing(GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
            Assert.That(c.HomeProjectLayer, Is.Null);
            Assert.That(c.SidecarLayer, Is.Null);
        }

        [Test]
        public void Resolve_SidecarAndGlobalWithoutHomeProject()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, PROJECT, Existing(SIDECAR, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Resolve_NoFilesAtAll()
        {
            var c = VeneerConfigurationResolver.Resolve(CONFIG, PROJECT, Existing());
            Assert.That(Chain(c), Is.EqualTo(""));
            Assert.That(c.HomeProjectLayer, Is.Null);
            Assert.That(c.SidecarLayer, Is.Null);
            Assert.That(c.GlobalLayer, Is.Null);
        }

        // An unsaved project has no project-specific layer but must still receive
        // the wildcard file -- that is the point of a wildcard file.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_WithoutAProjectFileStillFindsGlobal(string projectFile)
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, projectFile, Existing(SIDECAR, HOME_PROJECT, GLOBAL));
            Assert.That(Chain(c), Is.EqualTo(GLOBAL));
        }

        [Test]
        public void Resolve_WithoutAConfigDirectoryFindsOnlyTheSidecar()
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
        public void Resolve_HomeProjectFileIsNamedForTheProjectFileOnly()
        {
            var c = VeneerConfigurationResolver.Resolve(
                CONFIG, @"D:\elsewhere\ExampleProject.rsproj", Existing(HOME_PROJECT));
            Assert.That(Chain(c), Is.EqualTo(HOME_PROJECT),
                        "matching is by file name, so a project of the same name anywhere matches");
        }

        private static VeneerConfigurationLayer Layer(
            string path, string targetScenario, params VeneerAddon[] addons)
        {
            return new VeneerConfigurationLayer
            {
                Path = path,
                Configuration = new VeneerConfiguration
                {
                    targetScenario = targetScenario,
                    addons = addons
                }
            };
        }

        private static VeneerAddon Addon(string name, string scenario)
        {
            return new VeneerAddon { name = name, type = "exe", path = "x.bat", scenario = scenario };
        }

        private static VeneerConfigurationLayer OptionsLayer(
            string path, bool? allowScripts, int? defaultPort)
        {
            return new VeneerConfigurationLayer
            {
                Path = path,
                Configuration = new VeneerConfiguration
                {
                    options = new VeneerOptions { allowScripts = allowScripts, defaultPort = defaultPort }
                }
            };
        }

        private static string Names(ResolvedVeneerConfiguration resolved)
        {
            var names = new List<string>();
            foreach (var addon in resolved.addons) names.Add(addon.name);
            return string.Join(",", names);
        }

        [Test]
        public void Merge_ConcatenatesProjectAddonsBeforeGlobal()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("proj1", null), Addon("proj2", null)),
                Layer(GLOBAL, null, Addon("mine", null))
            });
            Assert.That(Names(resolved), Is.EqualTo("proj1,proj2,mine"));
        }

        [Test]
        public void Merge_RecordsSourceFilesInOrder()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null), Layer(GLOBAL, null)
            });
            Assert.That(string.Join(" | ", resolved.SourceFiles),
                        Is.EqualTo(SIDECAR + " | " + GLOBAL));
        }

        [Test]
        public void Merge_KeepsDuplicateAddonNames()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("Reports", null)),
                Layer(GLOBAL, null, Addon("Reports", null))
            });
            Assert.That(Names(resolved), Is.EqualTo("Reports,Reports"),
                        "two menu items is visible and diagnosable; silently dropping one is not");
        }

        // The whole reason targetScenario is pushed down instead of merged: a
        // wildcard file's default must not gate the project's addons.
        [Test]
        public void Merge_TargetScenarioStaysInsideItsOwnLayer()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("project", null)),
                Layer(GLOBAL, "Operations", Addon("global", null))
            });
            Assert.That(resolved.addons[0].scenario, Is.Null);
            Assert.That(resolved.addons[1].scenario, Is.EqualTo("Operations"));
        }

        [Test]
        public void Merge_PerAddonScenarioBeatsItsLayerTargetScenario()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, "Operations", Addon("calib", "Calibration"))
            });
            Assert.That(resolved.addons[0].scenario, Is.EqualTo("Calibration"));
        }

        [Test]
        public void Merge_ProjectOptionsBeatGlobalOptions()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, false, 9000),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(false),
                        "an explicit project false must beat a global true");
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9000));
        }

        [Test]
        public void Merge_GlobalOptionsFillFieldsTheProjectOmits()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, null, 9000),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(true));
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9000));
        }

        [Test]
        public void Merge_FieldSetByNeitherLayerStaysNull()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                OptionsLayer(SIDECAR, null, null),
                OptionsLayer(GLOBAL, null, null)
            });
            Assert.That(resolved.options.allowScripts, Is.Null);
            Assert.That(resolved.options.defaultPort, Is.Null);
            Assert.That(resolved.options.autoStart, Is.Null);
        }

        [Test]
        public void Merge_LayerWithNoOptionsBlockDoesNotBlockFallback()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null),
                OptionsLayer(GLOBAL, true, 9877)
            });
            Assert.That(resolved.options.allowScripts, Is.EqualTo(true));
            Assert.That(resolved.options.defaultPort, Is.EqualTo(9877));
        }

        // Load must be able to hand Merge whatever survived parsing, including
        // nothing, and get back something the consumers can dereference.
        [Test]
        public void Merge_OfNoLayersReturnsAnEmptyConfiguration()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>());
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.addons.Length, Is.EqualTo(0));
            Assert.That(resolved.options, Is.Not.Null);
            Assert.That(resolved.SourceFiles.Length, Is.EqualTo(0));
        }

        [Test]
        public void Merge_OfNullIsEmptyRatherThanAThrow()
        {
            var resolved = VeneerConfigurationResolver.Merge(null);
            Assert.That(resolved.addons.Length, Is.EqualTo(0));
            Assert.That(resolved.options, Is.Not.Null);
        }

        [Test]
        public void Merge_SkipsNullAddonEntries()
        {
            // A trailing comma in the JSON array produces a null element.
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("real", null), null)
            });
            Assert.That(Names(resolved), Is.EqualTo("real"));
        }

        [Test]
        public void Merge_LayerWithNoAddonsContributesOnlyItsSourceFile()
        {
            var resolved = VeneerConfigurationResolver.Merge(new List<VeneerConfigurationLayer>
            {
                Layer(SIDECAR, null, Addon("only", null)),
                new VeneerConfigurationLayer { Path = GLOBAL, Configuration = new VeneerConfiguration() }
            });
            Assert.That(Names(resolved), Is.EqualTo("only"));
            Assert.That(resolved.SourceFiles.Length, Is.EqualTo(2));
        }

        [Test]
        public void EffectiveFilter_IsTheAddonScenarioAfterPushDown()
        {
            Assert.That(VeneerConfiguration.EffectiveFilter(Addon("a", "Operations")),
                        Is.EqualTo("Operations"));
            Assert.That(VeneerConfiguration.EffectiveFilter(Addon("a", null)), Is.Null);
        }

        [Test]
        public void EffectiveFilter_OfNullAddonIsNull()
        {
            Assert.That(VeneerConfiguration.EffectiveFilter(null), Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        public void AppliesTo_UnfilteredAddonIsAlwaysEnabled(string filter)
        {
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), "Anything"), Is.True);
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), null), Is.True);
        }

        [TestCase("Operations", "Operations", true)]
        [TestCase("Operations", "operations", true)]
        [TestCase("OPERATIONS", "Operations", true)]
        [TestCase("Operations", "Calibration", false)]
        [TestCase("Operations", null, false)]
        // The refactor swapped a `currentScenario == null` guard for an
        // IsNullOrEmpty one, so pin the empty case as well as the null case.
        [TestCase("Operations", "", false)]
        public void AppliesTo_MatchesCaseInsensitively(string filter, string active, bool expected)
        {
            Assert.That(VeneerConfiguration.AppliesTo(Addon("a", filter), active),
                        Is.EqualTo(expected));
        }

        [Test]
        public void TryParse_ReadsAValidConfiguration()
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse(
                "{\"targetScenario\":\"Operations\"}", out config, out error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(config.targetScenario, Is.EqualTo("Operations"));
        }

        // One bad file must not take out the others -- and Load runs on every
        // dropdown open, so a throw here is a dialog every time the menu opens.
        [Test]
        public void TryParse_ReportsMalformedJsonRatherThanThrowing()
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse("{ not json", out config, out error);

            Assert.That(ok, Is.False);
            Assert.That(config, Is.Null);
            Assert.That(string.IsNullOrEmpty(error), Is.False);
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("")]
        public void TryParse_TreatsAnEmptyDocumentAsAnEmptyConfiguration(string json)
        {
            VeneerConfiguration config;
            string error;
            var ok = VeneerConfigurationResolver.TryParse(json, out config, out error);

            Assert.That(ok, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.addons, Is.Null);
        }
    }
}
