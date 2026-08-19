using System;
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
        // at the guard in sequence rather than together, so the window between the read
        // and the write is never genuinely contended. Releasing many threads from one gate
        // makes the loss of atomicity observable, but only PROBABILISTICALLY, and the rate
        // is environment-sensitive -- fewer cores means fewer racers actually overlap.
        // Measured against the non-atomic mutation (`if (_fired == 1) return; _fired = 1;`
        // in place of the Interlocked.Exchange): 9/10 standalone runs at 300 rounds, then
        // independently re-measured at 26/30 (87%) on a 16-core machine and 62% in a
        // non-NUnit host. 300 rounds keeps the full suite comfortably fast under normal
        // load; 87% is the honest number that costs, not a rounder one that doesn't.
        //
        // A rewrite onto a System.Threading.Barrier with 8 long-lived Threads (parked at
        // the barrier and released in lock-step every round, instead of gated via
        // ManualResetEventSlim + a fresh Task.Run per racer per round) was tried, on the
        // theory that removing the thread pool's injection throttling would let far more
        // rounds run for the same wall time and raise the detection rate. An initial,
        // non-interleaved comparison looked 10-20x slower, but that measurement span
        // coincided with the host's free memory dropping under heavy background load
        // (confirmed separately), so it was comparing two different machine states, not
        // two designs. A controlled back-to-back comparison at equal round counts
        // afterwards put the two designs within noise of each other, with no consistent
        // winner. Given a real but unquantified cost (another thread-lifecycle class,
        // manual Join-on-failure handling) for a benefit that could not be confirmed under
        // available conditions, the extra complexity was not worth adopting on unproven
        // grounds -- reverted in favour of keeping this simpler, already-reviewed shape.
        // Worth re-attempting with cleaner instrumentation (e.g. Stopwatch-based per-round
        // timing written to a log rather than wall-clock `dotnet test` output) before
        // concluding either way.
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

                Assert.That(calls, Is.EqualTo(1),
                    "round " + round + " fired an unexpected number of times: expected 1, got " + calls);
            }
        }

        [Test]
        public void ANullCallbackThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new OneShotLifecycle(null));
        }
    }
}
