using System.Threading;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class OneShotLifecycleTests
    {
        private static VeneerAddon Addon()
        {
            return new VeneerAddon { name = "Tool", type = "script" };
        }

        [Test]
        public void FirstFinishedCallsThrough()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => calls++);

            once.Finished(Addon());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void LaterFinishedCallsAreNoOps()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => calls++);

            once.Finished(Addon());
            once.Finished(Addon());
            once.Finished(Addon());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ThePassedAddonReachesTheCallback()
        {
            VeneerAddon seen = null;
            var addon = Addon();

            new OneShotLifecycle(a => seen = a).Finished(addon);

            Assert.That(ReferenceEquals(seen, addon), Is.True);
        }

        // Finished can be raced by the threadpool watcher and the UI thread's catch
        // block. Exactly one must win.
        [Test]
        public void ConcurrentFinishedCallsFireExactlyOnce()
        {
            var calls = 0;
            var once = new OneShotLifecycle(a => Interlocked.Increment(ref calls));
            var addon = Addon();

            Parallel.For(0, 200, i => once.Finished(addon));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ANullCallbackDoesNotThrow()
        {
            Assert.DoesNotThrow(() => new OneShotLifecycle(null).Finished(Addon()));
        }
    }
}
