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

            running.Finished(addon);
            Assert.That(running.RunningCount(addon), Is.EqualTo(0));
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
    }
}
