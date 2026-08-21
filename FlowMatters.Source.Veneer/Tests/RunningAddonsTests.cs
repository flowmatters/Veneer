using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class RunningAddonsTests
    {
        private static VeneerAddon Addon(string menu, string name)
        {
            return new VeneerAddon { name = name, type = "script", menu = menu };
        }

        // The six spellings below all render in the SAME menu (MenuLayout.SplitMenuPath
        // maps null/whitespace to "Reporting", trims segments and drops empties). Keying
        // on the raw string would give six independent counts for one menu item, and the
        // double launch this class exists to prevent would come straight back.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Reporting")]
        [TestCase(" Reporting ")]
        [TestCase("Reporting|")]
        public void MenuSpellingsThatRenderTogetherShareOneCount(string menu)
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Reporting", "Tool"));

            Assert.That(running.RunningCount(Addon(menu, "Tool")), Is.EqualTo(1));
        }

        [Test]
        public void DifferentMenusDoNotCollide()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Models", "Tool"));

            Assert.That(running.RunningCount(Addon("Reporting", "Tool")), Is.EqualTo(0));
        }

        [Test]
        public void NestedMenuPathsAreDistinguished()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Models|Calibration", "Tool"));

            Assert.That(running.RunningCount(Addon("Models", "Tool")), Is.EqualTo(0));
            Assert.That(running.RunningCount(Addon("Models|Calibration", "Tool")), Is.EqualTo(1));
        }

        [Test]
        public void DifferentNamesDoNotCollide()
        {
            var running = new RunningAddons();
            running.MarkRunning(Addon("Reporting", "Tool"));

            Assert.That(running.RunningCount(Addon("Reporting", "Other")), Is.EqualTo(0));
        }

        [Test]
        public void UnknownAddonReportsZero()
        {
            Assert.That(new RunningAddons().RunningCount(Addon("Reporting", "Tool")), Is.EqualTo(0));
        }

        [Test]
        public void CountsAccumulateAndDrainToZero()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            running.MarkRunning(addon);
            running.MarkRunning(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(2));

            running.Finished(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(1));

            // VeneerConfiguration.Load re-deserialises the .veneer file on every
            // dropdown open, so production always calls Finished against a
            // freshly-constructed VeneerAddon, never the instance MarkRunning saw.
            // Exercise that here instead of reusing one instance throughout.
            running.Finished(Addon("Reporting", "Tool"));
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        [Test]
        public void SeparatorCannotBeConfusedWithMenuPipes()
        {
            // "|" already delimits menu segments (see NestedMenuPathsAreDistinguished),
            // so it cannot double as the menu/name separator without risking two
            // different (menu, name) pairs producing the same key. Key uses "\0"
            // instead, which cannot appear in either side.
            var running = new RunningAddons();
            running.MarkRunning(Addon("Models", "Calibration"));

            Assert.That(running.RunningCount(Addon("Models|Calibration", "")), Is.EqualTo(0));
        }

        [Test]
        public void KeyToleratesNullAddon()
        {
            Assert.That(RunningAddons.Key(null), Is.EqualTo("Reporting\0"));
        }

        [Test]
        public void NullAddonSharesBucketWithAddonMissingMenuAndName()
        {
            // The one case where "same key" does NOT mean "same rendered menu
            // item": a null VeneerAddon and an addon with both menu and name
            // unset collapse to the same key. VeneerAddon.Validate never checks
            // `name`, so a .veneer entry missing "name" is reachable and would
            // share this bucket with a genuinely absent addon.
            Assert.That(RunningAddons.Key(null), Is.EqualTo(RunningAddons.Key(new VeneerAddon())));
        }

        [Test]
        public void MarkRunningToleratesNullAddon()
        {
            var running = new RunningAddons();

            Assert.DoesNotThrow(() => running.MarkRunning(null));
            Assert.That(running.RunningCount(null), Is.EqualTo(1));
        }

        [Test]
        public void FinishedToleratesNullAddon()
        {
            var running = new RunningAddons();
            running.MarkRunning(null);

            Assert.DoesNotThrow(() => running.Finished(null));
            Assert.That(running.RunningCount(null), Is.EqualTo(0));
        }

        [Test]
        public void RunningCountToleratesNullAddon()
        {
            Assert.That(new RunningAddons().RunningCount(null), Is.EqualTo(0));
        }

        // Finished() is called from a threadpool watcher and must never throw there --
        // an escaping exception becomes an unobserved task exception, silently swallowed.
        [Test]
        public void FinishedBelowZeroIsANoOpNotAnException()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Assert.DoesNotThrow(() => running.Finished(addon));
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));

            running.MarkRunning(addon);
            running.Finished(addon);
            Assert.DoesNotThrow(() => running.Finished(addon));
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        [Test]
        public void ConcurrentMarkAndFinishBalanceOut()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Parallel.For(0, 500, i =>
            {
                running.MarkRunning(addon);
                running.Finished(addon);
            });

            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
        }

        [Test]
        public void ConcurrentMarksAreAllCounted()
        {
            var running = new RunningAddons();
            var addon = Addon("Reporting", "Tool");

            Parallel.For(0, 500, i => running.MarkRunning(addon));

            Assert.That(running.RunningCount(addon), Is.EqualTo(500));
        }

        // A lock scoped incorrectly (e.g. one lock object allocated per key rather
        // than one shared lock guarding the dictionary) can still pass every test
        // above, since those only ever touch one key at a time. Concurrent writers
        // to DIFFERENT keys on a plain Dictionary<,> can corrupt its internal
        // structure, not just the specific entries -- this interleaves marks across
        // ten distinct addons to catch that.
        [Test]
        public void ConcurrentMarksAcrossDistinctKeysAreIndependentlyCounted()
        {
            var running = new RunningAddons();
            const int addonCount = 10;
            const int marksPerAddon = 50;

            Parallel.For(0, addonCount * marksPerAddon, i =>
            {
                var addonIndex = i % addonCount;
                running.MarkRunning(Addon("Reporting", "Tool" + addonIndex));
            });

            for (var i = 0; i < addonCount; i++)
            {
                Assert.That(running.RunningCount(Addon("Reporting", "Tool" + i)), Is.EqualTo(marksPerAddon));
            }
        }
    }
}
