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

        // Parallel.For alone does not catch a non-atomic check-then-set: its workers arrive
        // at the guard in sequence, so the window between the read and the write is never
        // contended. Releasing every thread from one gate, over many rounds, is what makes
        // the loss of atomicity observable -- and this class's whole contract is that the
        // callback fires once no matter how many threads race it.
        [Test]
        public void FinishedIsAtomicUnderContention()
        {
            const int rounds = 300;
            const int threads = 8;

            for (var round = 0; round < rounds; round++)
            {
                var calls = 0;
                var once = new OneShotLifecycle(a => Interlocked.Increment(ref calls));
                var addon = Addon();

                using (var gate = new ManualResetEventSlim(false))
                {
                    var racers = new Task[threads];
                    for (var i = 0; i < threads; i++)
                        racers[i] = Task.Run(() => { gate.Wait(); once.Finished(addon); });

                    gate.Set();
                    Task.WaitAll(racers);
                }

                Assert.That(calls, Is.EqualTo(1), "round " + round + " fired more than once");
            }
        }

        [Test]
        public void ANullCallbackDoesNotThrow()
        {
            Assert.DoesNotThrow(() => new OneShotLifecycle(null).Finished(Addon()));
        }
    }
}
