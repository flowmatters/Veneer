using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.Functions;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions;
using RiverSystem.Management.ExpressionBuilder;
using TIME.Collections;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Evaluates expressions through a transient Function that is never added to the manager.
    /// Every step here was verified live (SIFT spec 2026-10-17 §4.2). The release step is the
    /// one that matters: Parse() registers the transient in every referenced entity's
    /// ReferencingFunctions, and nothing else undoes it.
    /// </summary>
    public class TransientFunctionEvaluator
    {
        public const string TRANSIENT_NAME = "$f_veneer_evaluate";

        /// <summary>How long Release waits for Parse's background registrations to land: they
        /// normally land within a thread-pool hop; 2 s caps a starved pool.</summary>
        private static readonly TimeSpan RegistrationWait = TimeSpan.FromSeconds(2);

        /// <summary>How long Release keeps removing from a set reached by more than one entity,
        /// after its first successful Remove, to catch the later entities' re-adds.</summary>
        private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(50);

        /// <summary>Wait rounds that Thread.Yield() before falling back to Thread.Sleep(1).</summary>
        private const int SpinRounds = 50;

        private static MethodInfo _setFunctionManager;

        private readonly RiverSystemScenario _scenario;
        private readonly Action<string> _log;
        private bool _loggedTimeStepFallback;

        public TransientFunctionEvaluator(RiverSystemScenario scenario, Action<string> log)
        {
            _scenario = scenario;
            _log = log ?? (_ => { });
        }

        /// <summary>The scenario's configured time step (RunningConfiguration.TimeStep), or Daily
        /// when it cannot be read (logged once per evaluator).</summary>
        public TimeStep EvaluationTimeStep()
        {
            TimeStep configured = null;
            string why = "no run configuration time step";
            try { configured = _scenario.CurrentConfiguration?.TimeStep; }
            catch (Exception e) { why = e.GetBaseException().Message; }
            if (configured != null) return configured;
            if (!_loggedTimeStepFallback)
            {
                _loggedTimeStepFallback = true;
                _log("WARNING: evaluate using a Daily time step (" + why + ")");
            }
            return TimeStep.Daily;
        }

        public EvaluateResultItem Evaluate(EvaluateItem item, DateTime date, TimeStep timeStep)
        {
            var fm = _scenario.Network.FunctionManager;
            Function fn = null;
            var registrationLaunched = false;
            try
            {
                // Validate bindings BEFORE touching Source: ExpressionSubstitution documents these as
                // preconditions and checks neither.
                var bindings = item.BindingsByToken();
                var bindingError = EvaluateItem.BindingError(bindings);
                if (bindingError != null) return Error(item, bindingError);

                var substituted = ExpressionSubstitution.Apply(item.Expression, bindings);
                fn = new Function { Name = TRANSIENT_NAME, Expression = substituted };
                fn.Parent = ResolveContextFolder(fm, item.Context);
                FunctionManagerSetter().Invoke(fn, new object[] { fm });
                string error;
                var parsed = fn.Parse(out error);
                // Parse launches the registration tasks whenever it RETURNS (even false: a
                // ModelledVariable.Clone failure appends to the error and still registers) unless
                // the manager has disabled referencing functions. Read that flag now, as Parse saw
                // it, not later in finally. When TryParse itself fails the lists are empty, so the
                // wait in Release costs nothing; ValidateUsages cannot fail for this transient
                // (default EvaluationTimes = StartOfTimeStep, no Usages).
                registrationLaunched = !fm.CachedDisableReferenceFunctions;
                if (!parsed)
                    return Error(item, string.IsNullOrEmpty(error) ? "parse failed" : StripNamePrefix(error, fn.FullName));
                try
                {
                    // Source's own runner passes TimeStep.GetTimeSpanToNextTick(now) here
                    // (NetworkRunner); modelled variables use it to convert rates to per-step
                    // amounts. No fallback: a 0-second step would give plausible WRONG amounts.
                    fn.Evaluate(date, timeStep, timeStep.GetTimeSpanToNextTick(date), PointOfUpdate.AfterTimeSeriesPlayed);
                }
                catch (ArithmeticException)
                {
                    // Source 6.10 throws "The Function … has evaluated to NaN." for NaN and ±Inf.
                    return Error(item, "non-finite result");
                }
                // NaN / ±Infinity would serialise as bare `NaN` / `INF` tokens — not JSON (Python
                // rejects INF, browsers reject both) — and one 0/0 would lose the whole batch.
                // (For Source versions whose Evaluate does not throw on these.)
                if (double.IsNaN(fn.Value) || double.IsInfinity(fn.Value))
                    return Error(item, "non-finite result");
                return new EvaluateResultItem { Id = item.Id, Value = fn.Value };
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return Error(item, e.GetBaseException().Message);
            }
            finally
            {
                if (fn != null) Release(item, fn, registrationLaunched);
            }
        }

        private static EvaluateResultItem Error(EvaluateItem item, string message) =>
            new EvaluateResultItem { Id = item.Id, Error = message };

        /// <summary>Source prefixes parse errors with the function's FullName and ": ", which
        /// here would be the transient's internal name.</summary>
        private static string StripNamePrefix(string error, string fullName)
        {
            var prefix = fullName + ": ";
            return error.StartsWith(prefix, StringComparison.Ordinal) ? error.Substring(prefix.Length) : error;
        }

        /// <summary>
        /// Function.FunctionManager's setter is internal on Source 6.10. Resolved lazily so a
        /// Source version without it fails each item with a clear message instead of a
        /// TypeInitializationException. (Function.Reset(FunctionManager, bool parse) sets
        /// FunctionManager publicly on 6.10 and would avoid reflection if the legacy target has
        /// it — Task 6 decides.)
        /// </summary>
        private static MethodInfo FunctionManagerSetter()
        {
            var setter = _setFunctionManager;
            if (setter != null) return setter;
            setter = typeof(Function)
                .GetProperty("FunctionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(nonPublic: true);
            if (setter == null)
                throw new NotSupportedException("this Source version exposes no Function.FunctionManager setter");
            _setFunctionManager = setter;
            return setter;
        }

        /// <summary>
        /// Remove the transient from EVERY entity Parse bound it to — functions, variables, date
        /// builtins, lookup tables alike. Never a counted subset. See RemoveFromSets for why this
        /// waits and settles.
        /// </summary>
        private void Release(EvaluateItem item, Function fn, bool registrationLaunched)
        {
            // Group the DISTINCT entities (the same reference equality Parse's Except uses, so one
            // entity = one TryAddAsync) by the set they reach: a ModelledVariable clone's
            // ReferencingFunctions is its original's, so clones of one variable share a set.
            var groups = fn.Functions.Distinct().Select(f => new { Set = f.ReferencingFunctions, Name = SafeName(() => f.FullName) })
                .Concat(fn.Variables.Distinct().Select(v => new { Set = v.ReferencingFunctions, Name = SafeName(() => v.FullName) }))
                .Where(x => x.Set != null)
                .GroupBy(x => x.Set)
                .ToList();
            var names = groups.ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));
            var sets = groups.Select(g => (g.Key, g.Count())).ToList();

            var outstanding = RemoveFromSets(sets, s => s.Remove(fn), registrationLaunched, RegistrationWait, SettleWindow,
                (set, e) => _log("WARNING: evaluate item " + item.Id + ": could not release from " + names[set] + ": " + e.GetBaseException().Message));
            if (registrationLaunched && outstanding.Count > 0)
                _log("WARNING: evaluate item " + item.Id + ": release timed out with registration(s) outstanding on "
                     + string.Join("; ", outstanding.Select(s => names[s])) + " — SweepStrays should catch them");
        }

        /// <summary>
        /// Remove the transient (via <paramref name="remove"/>, which reports whether it was
        /// present) from each set; returns the sets it was never removed from. Each entry is a
        /// distinct set and the number of distinct entities whose registration reaches it.
        /// Generic over the set type only so the tests can drive the real TIME
        /// AsyncObservableSet without a compile-time TIME reference.
        ///
        /// Parse does not register synchronously: it starts fire-and-forget Task.Run loops that
        /// await TryAddAsync once per entity, with a thread-pool hop between, so a single
        /// immediate Remove pass can run before an add. When <paramref name="waitForAdds"/>, keep
        /// removing until every set has had a successful Remove, bounded by
        /// <paramref name="wait"/>. A set reached by more than one entity (clones of one
        /// ModelledVariable) gets more than one add, and a later add can re-insert after the
        /// first Remove; so such sets keep being removed from for up to
        /// <paramref name="settle"/>, stopping early once they have had as many successful
        /// Removes as entities (then every add has landed and been undone). This settle is a
        /// heuristic — a pool starved for longer than the window can still re-add; SweepStrays
        /// is the backstop. A Remove that throws is reported via <paramref name="onError"/> and
        /// that set is skipped.
        /// </summary>
        internal static List<TSet> RemoveFromSets<TSet>(
            IReadOnlyList<(TSet set, int entityCount)> sets,
            Func<TSet, bool> remove,
            bool waitForAdds,
            TimeSpan wait,
            TimeSpan settle,
            Action<TSet, Exception> onError)
        {
            var pending = sets.Select(x => new SetRelease<TSet> { Set = x.set, Entities = x.entityCount }).ToList();
            var settling = new List<SetRelease<TSet>>();

            var clock = Stopwatch.StartNew();
            var round = 0;
            while (true)
            {
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    var entry = pending[i];
                    bool removed;
                    if (!TryRemove(entry.Set, remove, onError, out removed)) { pending.RemoveAt(i); continue; }
                    if (!removed) continue;
                    entry.Removals = 1;
                    if (entry.Entities > 1) settling.Add(entry);
                    pending.RemoveAt(i);
                }
                if (pending.Count == 0 || !waitForAdds || clock.Elapsed >= wait) break;
                Pause(round++);
            }

            if (waitForAdds && settling.Count > 0)
            {
                var settleClock = Stopwatch.StartNew();
                round = 0;
                while (true)
                {
                    for (var i = settling.Count - 1; i >= 0; i--)
                    {
                        var entry = settling[i];
                        bool removed;
                        if (!TryRemove(entry.Set, remove, onError, out removed)) { settling.RemoveAt(i); continue; }
                        if (removed && ++entry.Removals >= entry.Entities) settling.RemoveAt(i);
                    }
                    if (settling.Count == 0 || settleClock.Elapsed >= settle) break;
                    Pause(round++);
                }
            }

            return pending.Select(p => p.Set).ToList();
        }

        private sealed class SetRelease<TSet>
        {
            public TSet Set;
            public int Entities;
            public int Removals;
        }

        /// <summary>False when remove threw (reported via onError).</summary>
        private static bool TryRemove<TSet>(TSet set, Func<TSet, bool> remove, Action<TSet, Exception> onError, out bool removed)
        {
            try
            {
                removed = remove(set);
                return true;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                removed = false;
                onError?.Invoke(set, e);
                return false;
            }
        }

        /// <summary>Yield briefly first: the adds are usually only a thread-pool hop behind, and
        /// Thread.Sleep(1) costs ~15.6 ms at Windows' default timer resolution. (Not
        /// SpinWait.SpinOnce: it sleeps 1 ms itself after ~20 calls, and the overload that
        /// disables that doesn't exist on .NET Framework 4.8.)</summary>
        private static void Pause(int round)
        {
            if (round < SpinRounds) Thread.Yield();
            else Thread.Sleep(1);
        }

        private static string SafeName(Func<string> name)
        {
            try { return name() ?? "?"; }
            catch { return "?"; }
        }

        /// <summary>Belt and braces, once per batch: any transient still registered anywhere
        /// is a release bug. Returns how many were removed, and logs its OWN warning if > 0 —
        /// callers must not log a second one. A real model function that happens to share the
        /// transient's name is never swept.</summary>
        public int SweepStrays()
        {
            var fm = _scenario.Network.FunctionManager;
            // Snapshots: these are ObservableCollections the GUI can mutate.
            var functions = fm.Functions.ToList();
            var variables = fm.Variables.ToList();
            var real = new HashSet<Function>(functions);
            var removed = 0;
            var swept = new List<string>();

            Action<string, AsyncObservableSet<Function>> sweep = (name, set) =>
            {
                try
                {
                    var hit = false;
                    foreach (var s in set.Where(x => x.Name == TRANSIENT_NAME && !real.Contains(x)).ToList())
                        if (set.Remove(s)) { removed++; hit = true; }
                    if (hit) swept.Add(name);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    _log("WARNING: evaluate sweep could not check " + name + ": " + e.GetBaseException().Message);
                }
            };
            foreach (var f in functions) sweep(SafeName(() => f.FullName), f.ReferencingFunctions);
            foreach (var v in variables) sweep(SafeName(() => v.FullName), v.ReferencingFunctions);

            if (removed > 0)
                _log("WARNING: evaluate swept " + removed + " stray transient reference(s) from "
                     + string.Join(", ", swept) + " — release missed an entity");
            return removed;
        }

        /// <summary>
        /// The folder the transient lives in, which decides how leaf names resolve: Source
        /// resolves a reference by full name first, then (only when the function has a Parent)
        /// by leaf name within that folder. With no context the transient has no Parent — the
        /// same as a top-level function — so only full names (and top-level functions, whose
        /// full name is their leaf name) resolve.
        /// There is no root FolderItem to use instead: FunctionManager.Folders is a flat
        /// ObservableCollection&lt;FolderItem&gt; with several parentless folders (including the
        /// built-in Now/Start/End), and top-level functions have Parent == null.
        /// Names match case-insensitively, as Source's own Function.NameMatches does.
        /// </summary>
        private static FolderItem ResolveContextFolder(FunctionManager fm, string context)
        {
            if (string.IsNullOrEmpty(context)) return null;
            var functions = fm.Functions.ToList();
            var f = functions.FirstOrDefault(x => string.Equals(x.FullName, context, StringComparison.InvariantCultureIgnoreCase));
            if (f != null) return f.Parent;
            var byName = functions.Where(x => string.Equals(x.Name, context, StringComparison.InvariantCultureIgnoreCase)).Take(2).ToList();
            if (byName.Count > 1) throw new ArgumentException("ambiguous context: " + context);
            if (byName.Count == 0) throw new ArgumentException("context function not found: " + context);
            return byName[0].Parent;
        }

        public static DateTime ParseIsoDate(string text) =>
            DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}
