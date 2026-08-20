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
        // and the write is never genuinely contended. This test instead parks several
        // threads and releases them together, over many rounds.
        //
        // The gate is TWO events on purpose. ManualResetEventSlim by itself gates nothing:
        // Set() runs a few microseconds after the Task.Run loop, which is not long enough
        // for the pool to inject and park 8 threads, so several racers reach Wait() after
        // the event is already set and never race at all. That, not the guard, is what
        // made the old detection rate so core-count-sensitive. CountdownEvent fixes the
        // ordering: ready.Wait() returns only once every racer has started and run as far
        // as its own Signal(). Signal() returns BEFORE gate.Wait() parks, so what this
        // buys is "every racer exists and is about to block", not a hard barrier -- much
        // tighter than gate-only, at the cost of one extra event, where a true barrier
        // would cost a thread-lifecycle class.
        //
        // Detection is therefore still PROBABILISTIC. Measured against the non-atomic
        // mutation (`if (_fired == 1) return; _fired = 1;` in place of the
        // Interlocked.Exchange), applied to a copy of the tree built and run outside the
        // repository: 19 of 20 standalone runs of this test detected it, at 300 rounds,
        // on a machine with 16 logical processors (12 cores). The same measurement against
        // the gate-only shape this replaced was 26 of 30 (87%).
        //
        // Rounds stay at 300, and that is now a measured choice rather than an inherited
        // one: 19/20 is what 300 rounds delivers WITH this gate, so the earlier proposal
        // to raise it to 3000 is buying a rate the gate already provides, at 10x the
        // suite time on every run for everyone.
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

                using (var ready = new CountdownEvent(threads))
                using (var gate = new ManualResetEventSlim(false))
                {
                    var racers = new Task[threads];
                    for (var i = 0; i < threads; i++)
                        racers[i] = Task.Run(() => { ready.Signal(); gate.Wait(); once.Finished(addon); });

                    ready.Wait();
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
