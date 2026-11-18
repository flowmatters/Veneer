using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class TransientFunctionEvaluatorTests
    {
        // The REAL TIME.Collections.AsyncObservableSet<object>, reached by reflection (written
        // before the test project had a compile-time TIME reference; RemoveFromSets is generic
        // over the set type so it needs none).
        private static readonly Lazy<Type[]> SetTypes = new Lazy<Type[]>(() =>
        {
            var time = Assembly.Load("TIME");
            return new[]
            {
                time.GetType("TIME.Collections.AsyncObservableSet`1", true).MakeGenericType(typeof(object)),
                time.GetType("TIME.Collections.EmptyCollectionChangedHandler`1", true).MakeGenericType(typeof(object)),
            };
        });

        private static object NewSet() =>
            Activator.CreateInstance(SetTypes.Value[0], Activator.CreateInstance(SetTypes.Value[1]));

        private static bool Remove(object set, object item) => (bool)((dynamic)set).Remove(item);
        private static bool Contains(object set, object item) => (bool)((dynamic)set).Contains(item);
        private static Task<bool> TryAddAsync(object set, object item) => (Task<bool>)((dynamic)set).TryAddAsync(item);

        [Test] public void NewSet_IsTheRealAsyncObservableSet()
        {
            Assert.That(NewSet().GetType().FullName, Does.StartWith("TIME.Collections.AsyncObservableSet`1"));
        }

        /// <summary>
        /// The real race against the real TIME AsyncObservableSet: Parse's fire-and-forget loop
        /// awaits TryAddAsync once per entity — here two clones of one ModelledVariable (sharing
        /// one set) and one unrelated variable — while Release runs concurrently, followed by the
        /// batch's settle window (here immediately, the tightest case: in the endpoint other items
        /// run in between). Without the settle step a reviewer's probe left the shared set holding
        /// the transient in over a third of trials; the companion test below disables the
        /// hand-off and shows strays do appear then.
        /// </summary>
        [Test, Category("Slow")] public void RemoveFromSets_ThenSettle_LeavesNoStrayWhenTwoEntitiesShareASet()
        {
            var result = RaceReleaseAgainstAdds(trials: 100, sharedEntityCount: 2);
            Assert.That(result.StraysShared, Is.EqualTo(0), "strays in the shared set");
            Assert.That(result.StraysOther, Is.EqualTo(0), "strays in the unique set");
            Assert.That(result.Outstanding, Is.EqualTo(0), "sets reported outstanding");
        }

        /// <summary>
        /// The control for the test above: the same race, but the shared set is declared as
        /// reached by ONE entity, so RemoveFromSets never hands it on and Settle never sees it.
        /// The second clone's add then sometimes lands after the only Remove and is left behind
        /// (measured 7–19 strays per 100 trials on a dev machine) — proof that the race is real
        /// and that the settle step is what removes it. Stops at the first stray; if none
        /// appears in 2000 trials the race did not occur on this machine, which is inconclusive
        /// rather than a failure.
        /// </summary>
        [Test, Category("Slow")] public void RemoveFromSets_WithoutTheHandOff_LeavesStraysInASharedSet()
        {
            const int maxTrials = 2000;
            var result = RaceReleaseAgainstAdds(maxTrials, sharedEntityCount: 1, stopAtFirstStray: true);
            TestContext.Out.WriteLine("strays in the shared set: " + result.StraysShared + " of " + result.Trials + " trials");
            Assert.That(result.StraysOther, Is.EqualTo(0), "strays in the unique set");
            if (result.StraysShared == 0)
                Assert.Inconclusive("the race did not occur in 2000 trials on this machine");
        }

        private sealed class RaceResult
        {
            public int StraysShared;
            public int StraysOther;
            public int Outstanding;
            public int Trials;
        }

        private static RaceResult RaceReleaseAgainstAdds(int trials, int sharedEntityCount, bool stopAtFirstStray = false)
        {
            var result = new RaceResult();
            for (var t = 0; t < trials; t++)
            {
                if (stopAtFirstStray && result.StraysShared > 0) break;
                result.Trials++;
                var fn = new object();
                var shared = NewSet();
                var other = NewSet();
                var entitySets = new[] { shared, shared, other };   // clone, clone, unrelated
                var addTask = Task.Run(async () => { foreach (var s in entitySets) await TryAddAsync(s, fn); });

                List<(object set, int entityCount, int removals)> unsettled;
                var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                    new List<(object set, int entityCount)> { (shared, sharedEntityCount), (other, 1) },
                    s => Remove(s, fn),
                    waitForAdds: true,
                    wait: TimeSpan.FromSeconds(2),
                    onError: (s, e) => Assert.Fail("Remove threw: " + e),
                    unsettled: out unsettled);
                TransientFunctionEvaluator.Settle(
                    unsettled.Select(u => Entry(() => Remove(u.set, fn), u.entityCount, u.removals)).ToList(),
                    TimeSpan.FromMilliseconds(50),
                    (s, e) => Assert.Fail("Remove threw: " + e));

                Assert.That(addTask.Wait(TimeSpan.FromSeconds(10)), Is.True, "add task did not finish");
                result.Outstanding += outstanding.Count;
                if (Contains(shared, fn)) result.StraysShared++;
                if (Contains(other, fn)) result.StraysOther++;
            }
            return result;
        }

        private static TransientFunctionEvaluator.SettleEntry Entry(Func<bool> remove, int entities, int removals) =>
            new TransientFunctionEvaluator.SettleEntry { Remove = remove, Entities = entities, Removals = removals, Label = "test" };

        /// <summary>The probe behind Settle's reporting rule: the real set dedups, so two adds
        /// that both land before the first Remove leave ONE presence and allow ONE successful
        /// Remove — an entry can fall short of its entity count with no stray.</summary>
        [Test] public void AsyncObservableSet_DedupsASecondAdd()
        {
            var fn = new object();
            var set = NewSet();
            Assert.That(TryAddAsync(set, fn).Result, Is.True);
            Assert.That(TryAddAsync(set, fn).Result, Is.False);
            Assert.That(Remove(set, fn), Is.True);
            Assert.That(Remove(set, fn), Is.False);
        }

        [Test] public void RemoveFromSets_HandsMultiEntitySetsOnForSettling()
        {
            var fn = new object();
            var shared = NewSet();
            var single = NewSet();
            TryAddAsync(shared, fn).Wait();
            TryAddAsync(single, fn).Wait();

            List<(object set, int entityCount, int removals)> unsettled;
            var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (shared, 2), (single, 1) },
                s => Remove(s, fn),
                waitForAdds: true,
                wait: TimeSpan.FromSeconds(2),
                onError: null,
                unsettled: out unsettled);

            Assert.That(outstanding, Is.Empty);
            Assert.That(unsettled.Count, Is.EqualTo(1));
            Assert.That(unsettled[0].set, Is.SameAs(shared));
            Assert.That(unsettled[0].entityCount, Is.EqualTo(2));
            Assert.That(unsettled[0].removals, Is.EqualTo(1));
        }

        [Test] public void RemoveFromSets_WithoutWaitHandsNothingOnForSettling()
        {
            var fn = new object();
            var shared = NewSet();
            TryAddAsync(shared, fn).Wait();

            List<(object set, int entityCount, int removals)> unsettled;
            TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (shared, 2) },
                s => Remove(s, fn),
                waitForAdds: false,
                wait: TimeSpan.FromSeconds(2),
                onError: null,
                unsettled: out unsettled);

            Assert.That(Contains(shared, fn), Is.False);
            Assert.That(unsettled, Is.Empty);
        }

        [Test] public void Settle_StopsEarlyOnceEveryAddIsUndone()
        {
            var calls = 0;
            var entry = Entry(() => ++calls == 3, entities: 2, removals: 1);   // the re-add is found on the 3rd look
            var clock = Stopwatch.StartNew();

            var strays = TransientFunctionEvaluator.Settle(new[] { entry }, TimeSpan.FromSeconds(5), null);

            Assert.That(strays, Is.Empty);
            Assert.That(entry.Removals, Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test] public void Settle_SkipsEntriesAlreadyConfirmed()
        {
            var calls = 0;
            var entry = Entry(() => { calls++; return false; }, entities: 2, removals: 2);
            Assert.That(TransientFunctionEvaluator.Settle(new[] { entry }, TimeSpan.FromMilliseconds(50), null), Is.Empty);
            Assert.That(calls, Is.EqualTo(0));
        }

        [Test] public void Settle_DoesNotReportAnEntryThatFellShortButHoldsNoStray()
        {
            // The common dedup case: both adds landed before Release — one presence, one Remove.
            var entry = Entry(() => false, entities: 2, removals: 1);
            Assert.That(TransientFunctionEvaluator.Settle(new[] { entry }, TimeSpan.FromMilliseconds(20), null), Is.Empty);
        }

        [Test] public void Settle_ReportsAnEntryStillHoldingTheTransientAfterTheWindow()
        {
            // A set that keeps being re-added to: more entities than the window can drain.
            var entry = Entry(() => true, entities: int.MaxValue, removals: 1);
            var strays = TransientFunctionEvaluator.Settle(new[] { entry }, TimeSpan.FromMilliseconds(20), null);
            Assert.That(strays.Count, Is.EqualTo(1));
            Assert.That(strays[0], Is.SameAs(entry));
        }

        [Test] public void Settle_ReportsAThrowingRemoveAndDropsIt()
        {
            var errors = new List<TransientFunctionEvaluator.SettleEntry>();
            var bad = Entry(() => throw new InvalidOperationException("boom"), entities: 2, removals: 1);
            var strays = TransientFunctionEvaluator.Settle(new[] { bad }, TimeSpan.FromMilliseconds(20), (e, ex) => errors.Add(e));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Is.SameAs(bad));
            Assert.That(strays, Is.Empty);
        }

        [Test] public void RemoveFromSets_WithoutWaitMakesOnePassAndReportsTheRest()
        {
            var fn = new object();
            var present = NewSet();
            TryAddAsync(present, fn).Wait();
            var absent = NewSet();

            var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (present, 1), (absent, 1) },
                s => Remove(s, fn),
                waitForAdds: false,
                wait: TimeSpan.FromSeconds(2),
                onError: null,
                unsettled: out _);

            Assert.That(Contains(present, fn), Is.False);
            // Is.SameAs, not Is.EqualTo: NUnit would compare the sets as (empty) collections.
            Assert.That(outstanding.Count, Is.EqualTo(1));
            Assert.That(outstanding[0], Is.SameAs(absent));
        }

        [Test] public void RemoveFromSets_GivesUpAtTheDeadlineWhenAnAddNeverLands()
        {
            var fn = new object();
            var never = NewSet();
            var clock = Stopwatch.StartNew();

            var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (never, 1) },
                s => Remove(s, fn),
                waitForAdds: true,
                wait: TimeSpan.FromMilliseconds(100),
                onError: null,
                unsettled: out _);

            // Is.SameAs, not Is.EqualTo: NUnit would compare the sets as (empty) collections.
            Assert.That(outstanding.Count, Is.EqualTo(1));
            Assert.That(outstanding[0], Is.SameAs(never));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test] public void RemoveFromSets_ReportsAThrowingRemoveAndCarriesOn()
        {
            var fn = new object();
            var bad = NewSet();
            var good = NewSet();
            TryAddAsync(good, fn).Wait();
            var errors = new List<object>();

            var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (bad, 1), (good, 1) },
                s => ReferenceEquals(s, bad) ? throw new InvalidOperationException("boom") : Remove(s, fn),
                waitForAdds: true,
                wait: TimeSpan.FromSeconds(2),
                onError: (s, e) => errors.Add(s),
                unsettled: out _);

            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Is.SameAs(bad));
            Assert.That(Contains(good, fn), Is.False);
            Assert.That(outstanding, Is.Empty);
        }

        [TestCase("$f_veneer_evaluate: Unknown variable $x", "Unknown variable $x")]
        [TestCase("Error in $f_veneer_evaluate: Unknown variable $x", "Unknown variable $x")]
        [TestCase("Unknown variable $x", "Unknown variable $x")]
        [TestCase("Error in $other: oops", "Error in $other: oops")]
        public void StripNamePrefix_RemovesEitherFormOfTheTransientsName(string error, string expected)
        {
            Assert.That(TransientFunctionEvaluator.StripNamePrefix(error, TransientFunctionEvaluator.TRANSIENT_NAME), Is.EqualTo(expected));
        }

        [Test] public void GroupBySet_CountsEntitiesPerSetAndSkipsNullSets()
        {
            var shared = new object();
            var single = new object();
            var groups = TransientFunctionEvaluator.GroupBySet(new List<(object set, string name)>
            {
                (shared, "$v"), (single, "$f"), (shared, "$v"), (null, "$none"),
            });

            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].set, Is.SameAs(shared));
            Assert.That(groups[0].entityCount, Is.EqualTo(2));
            Assert.That(groups[0].names, Is.EqualTo("$v"));
            Assert.That(groups[1].set, Is.SameAs(single));
            Assert.That(groups[1].entityCount, Is.EqualTo(1));
            Assert.That(groups[1].names, Is.EqualTo("$f"));
        }
    }
}
