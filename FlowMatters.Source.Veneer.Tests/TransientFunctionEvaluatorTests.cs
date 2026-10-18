using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class TransientFunctionEvaluatorTests
    {
        [Test] public void ParseIsoDate_AcceptsIsoDate()
        {
            Assert.That(TransientFunctionEvaluator.ParseIsoDate("2025-11-01"), Is.EqualTo(new DateTime(2025, 11, 1)));
        }

        [Test] public void ParseIsoDate_RejectsOtherFormats()
        {
            Assert.Throws<FormatException>((Action)(() => TransientFunctionEvaluator.ParseIsoDate("01/11/2025")));
            Assert.Throws<FormatException>((Action)(() => TransientFunctionEvaluator.ParseIsoDate("2025-11-01T00:00:00")));
        }

        // The REAL TIME.Collections.AsyncObservableSet<object>, reached by reflection: the test
        // project has no compile-time TIME reference (TIME.dll is in its output only as a
        // runtime dependency of Veneer), and RemoveFromSets is generic over the set type so it
        // needs none.
        private static readonly Lazy<Type[]> SetTypes = new Lazy<Type[]>(() =>
        {
            var time = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "TIME.dll"));
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
        /// one set) and one unrelated variable — while Release runs concurrently. Without the
        /// settle step a reviewer's probe left the shared set holding the transient in over a
        /// third of trials.
        /// </summary>
        [Test] public void RemoveFromSets_LeavesNoStrayWhenTwoEntitiesShareASet()
        {
            const int trials = 200;
            var straysShared = 0;
            var straysOther = 0;
            var outstandingReported = 0;
            for (var t = 0; t < trials; t++)
            {
                var fn = new object();
                var shared = NewSet();
                var other = NewSet();
                var entitySets = new[] { shared, shared, other };   // clone, clone, unrelated
                var addTask = Task.Run(async () => { foreach (var s in entitySets) await TryAddAsync(s, fn); });

                var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                    new List<(object set, int entityCount)> { (shared, 2), (other, 1) },
                    s => Remove(s, fn),
                    waitForAdds: true,
                    wait: TimeSpan.FromSeconds(2),
                    settle: TimeSpan.FromMilliseconds(50),
                    onError: (s, e) => Assert.Fail("Remove threw: " + e));

                Assert.That(addTask.Wait(TimeSpan.FromSeconds(10)), Is.True, "add task did not finish");
                outstandingReported += outstanding.Count;
                if (Contains(shared, fn)) straysShared++;
                if (Contains(other, fn)) straysOther++;
            }
            Assert.That(straysShared, Is.EqualTo(0), "strays in the shared set");
            Assert.That(straysOther, Is.EqualTo(0), "strays in the unique set");
            Assert.That(outstandingReported, Is.EqualTo(0), "sets reported outstanding");
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
                settle: TimeSpan.FromMilliseconds(50),
                onError: null);

            Assert.That(Contains(present, fn), Is.False);
            // Is.SameAs, not Is.EqualTo: NUnit would compare the sets as (empty) collections.
            Assert.That(outstanding.Count, Is.EqualTo(1));
            Assert.That(outstanding[0], Is.SameAs(absent));
        }

        [Test] public void RemoveFromSets_GivesUpAtTheDeadlineWhenAnAddNeverLands()
        {
            var fn = new object();
            var never = NewSet();
            var started = DateTime.UtcNow;

            var outstanding = TransientFunctionEvaluator.RemoveFromSets(
                new List<(object set, int entityCount)> { (never, 1) },
                s => Remove(s, fn),
                waitForAdds: true,
                wait: TimeSpan.FromMilliseconds(100),
                settle: TimeSpan.FromMilliseconds(50),
                onError: null);

            // Is.SameAs, not Is.EqualTo: NUnit would compare the sets as (empty) collections.
            Assert.That(outstanding.Count, Is.EqualTo(1));
            Assert.That(outstanding[0], Is.SameAs(never));
            Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(2)));
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
                settle: TimeSpan.FromMilliseconds(50),
                onError: (s, e) => errors.Add(s));

            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Is.SameAs(bad));
            Assert.That(Contains(good, fn), Is.False);
            Assert.That(outstanding, Is.Empty);
        }
    }
}
