using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.Functions;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.Functions;
using RiverSystem.Functions.Variables;
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

        /// <summary>How long Release waits for Parse's background registrations to land.</summary>
        private static readonly TimeSpan RegistrationWait = TimeSpan.FromSeconds(2);

        /// <summary>Release rounds that Thread.Yield() before falling back to Thread.Sleep(1).</summary>
        private const int SpinRounds = 50;

        // Function.FunctionManager has an internal setter; NOT derived from Parent. Cached once.
        private static readonly MethodInfo SetFunctionManager =
            typeof(Function).GetProperty("FunctionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .GetSetMethod(nonPublic: true);

        private readonly RiverSystemScenario _scenario;
        private readonly Action<string> _log;

        public TransientFunctionEvaluator(RiverSystemScenario scenario, Action<string> log)
        {
            _scenario = scenario;
            _log = log ?? (_ => { });
        }

        /// <summary>The scenario's configured time step (RunningConfiguration.TimeStep), or Daily
        /// when it cannot be read.</summary>
        public TimeStep EvaluationTimeStep()
        {
            try { return _scenario.CurrentConfiguration?.TimeStep ?? TimeStep.Daily; }
            catch { return TimeStep.Daily; }
        }

        public EvaluateResultItem Evaluate(EvaluateItem item, DateTime date, TimeStep timeStep)
        {
            var fm = _scenario.Network.FunctionManager;
            Function fn = null;
            var registrationLaunched = false;
            try
            {
                // Validate bindings BEFORE touching Source: ExpressionSubstitution documents these as
                // preconditions and checks neither (see BindingsByToken's doc).
                var bindings = item.BindingsByToken();
                foreach (var kv in bindings)
                {
                    if (!kv.Key.StartsWith("$", StringComparison.Ordinal))
                        return Error(item, "binding token must start with '$': " + kv.Key);
                    if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value))
                        return Error(item, "binding " + kv.Key + ": non-finite value");
                }
                var substituted = ExpressionSubstitution.Apply(item.Expression, bindings);
                fn = new Function { Name = TRANSIENT_NAME, Expression = substituted };
                fn.Parent = ResolveContextFolder(fm, item.Context);
                SetFunctionManager.Invoke(fn, new object[] { fm });
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
                    return Error(item, error ?? "parse failed");
                // Source's own runner passes TimeStep.GetTimeSpanToNextTick(now) here
                // (NetworkRunner); modelled variables use it to convert rates to per-step amounts.
                fn.Evaluate(date, timeStep, SecondsInStep(timeStep, date), PointOfUpdate.AfterTimeSeriesPlayed);
                // NaN / ±Infinity would serialise as bare `NaN` / `INF` tokens — not JSON (Python
                // rejects INF, browsers reject both) — and one 0/0 would lose the whole batch.
                // (Source 6.10's Function.Evaluate already throws ArithmeticException for these;
                // this check is for versions that don't.)
                if (double.IsNaN(fn.Value) || double.IsInfinity(fn.Value))
                    return Error(item, "non-finite result");
                return new EvaluateResultItem { Id = item.Id, Value = fn.Value };
            }
            catch (Exception e)
            {
                return Error(item, e.Message);
            }
            finally
            {
                if (fn != null) Release(fn, registrationLaunched);
            }
        }

        private static EvaluateResultItem Error(EvaluateItem item, string message) =>
            new EvaluateResultItem { Id = item.Id, Error = message };

        private static double SecondsInStep(TimeStep timeStep, DateTime date)
        {
            try { return timeStep.GetTimeSpanToNextTick(date); }
            catch { return 0.0; }
        }

        /// <summary>
        /// Remove the transient from EVERY entity Parse bound it to — functions, variables, date
        /// builtins, lookup tables alike. Never a counted subset.
        ///
        /// Parse does not register synchronously: it starts two fire-and-forget Task.Run loops
        /// that TryAddAsync the transient into each entity's ReferencingFunctions, so a single
        /// immediate Remove pass can run before the add and leave a stray behind. When
        /// <paramref name="registrationLaunched"/>, every distinct SET receives exactly one add,
        /// so we keep removing until each set has had one successful Remove, bounded by
        /// RegistrationWait. Otherwise one pass is made and SweepStrays is the backstop.
        ///
        /// Dedupe by set, not by entity: Parse replaces each ModelledVariable with a Clone(), and
        /// a clone's ReferencingFunctions is its original's set, so two clones of one variable
        /// (e.g. referenced by full name and by leaf name) share one set, which can only yield
        /// one successful Remove. AsyncObservableSet has reference equality.
        /// </summary>
        private void Release(Function fn, bool registrationLaunched)
        {
            var pending = (fn.Functions ?? Enumerable.Empty<Function>()).Select(f => f.ReferencingFunctions)
                .Concat((fn.Variables ?? Enumerable.Empty<AbstractFunctionVariable>()).Select(v => v.ReferencingFunctions))
                .Distinct()
                .ToList();

            var deadline = DateTime.UtcNow + RegistrationWait;
            var rounds = 0;
            while (true)
            {
                for (var i = pending.Count - 1; i >= 0; i--)
                    if (pending[i].Remove(fn)) pending.RemoveAt(i);
                if (pending.Count == 0 || !registrationLaunched || DateTime.UtcNow >= deadline) break;
                // Yield briefly first: the adds are usually only a thread-pool hop behind, and
                // Thread.Sleep(1) costs ~15.6 ms at Windows' default timer resolution. (Not
                // SpinWait.SpinOnce: it sleeps 1 ms itself after ~20 calls, and the overload that
                // disables that doesn't exist on .NET Framework 4.8.)
                if (rounds++ < SpinRounds) Thread.Yield();
                else Thread.Sleep(1);
            }
            if (registrationLaunched && pending.Count > 0)
                _log("WARNING: evaluate release timed out with " + pending.Count + " registration(s) outstanding — SweepStrays should catch them");
        }

        /// <summary>Belt and braces, once per batch: any transient still registered anywhere
        /// is a release bug. Returns how many were removed, and logs its OWN warning if > 0 —
        /// callers must not log a second one.</summary>
        public int SweepStrays()
        {
            var fm = _scenario.Network.FunctionManager;
            var removed = 0;
            foreach (var f in fm.Functions)
                foreach (var s in f.ReferencingFunctions.Where(x => x.Name == TRANSIENT_NAME).ToList())
                    if (f.ReferencingFunctions.Remove(s)) removed++;
            foreach (var v in fm.Variables)
                foreach (var s in v.ReferencingFunctions.Where(x => x.Name == TRANSIENT_NAME).ToList())
                    if (v.ReferencingFunctions.Remove(s)) removed++;
            if (removed > 0) _log("WARNING: evaluate swept " + removed + " stray transient reference(s) — release missed an entity");
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
        /// </summary>
        private static FolderItem ResolveContextFolder(FunctionManager fm, string context)
        {
            if (string.IsNullOrEmpty(context)) return null;
            var f = fm.Functions.FirstOrDefault(x => x.FullName == context) ?? fm.Functions.FirstOrDefault(x => x.Name == context);
            if (f == null) throw new ArgumentException("context function not found: " + context);
            return f.Parent;
        }

        public static DateTime ParseIsoDate(string text) =>
            DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }
}
