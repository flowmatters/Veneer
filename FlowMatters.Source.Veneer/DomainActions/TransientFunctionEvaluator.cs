using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The parse / evaluate / release steps were verified live on a scenario that had run (SIFT
    /// spec 2026-10-17 §4.2); the built-in substitution, the unbound-reference guard and the
    /// lookup-table fill below were added after a scenario loaded but never run was found to
    /// evaluate silently to 0, and are verified live by their own (cold-scenario) tests. The
    /// release step is the one that matters for leaks: Parse() registers the transient in every
    /// referenced entity's ReferencingFunctions, and nothing else undoes it.
    ///
    /// Honest on any scenario, run or not, WITHOUT ever calling FunctionManager.Reset (it writes
    /// defaults into model parameters through every function usage and is not idempotent):
    /// - Built-ins ($Now.*, $Start.*, $End.*, $ActiveInputSet, pattern variables) are substituted
    ///   as literals BEFORE Parse, computed by Veneer the way Source computes them during a run,
    ///   for every token the caller did not bind. Source's own values for them are per-run state
    ///   that a scenario loaded but never run does not have.
    /// - Every other reference with no honest value at the item's date — a function reference, a
    ///   modelled, context or time series variable, a built-in Veneer could not compute — is an
    ///   item error naming it (UnboundMessage), never a silently wrong value.
    /// - Lookup tables whose in-memory interpolator has not been built yet are built from their
    ///   persisted entries (FillLookupTables).
    /// - Names resolve as Source resolves them — functions before variables, each by full name
    ///   before leaf name in the context folder — so a built-in's leaf name is never substituted
    ///   where Source would have meant another function or variable (ResolveBuiltInTokens).
    /// Known limit: a plugin custom function that reads run state itself (e.g. through
    /// ICustomFunctionContext.CurrentTimeStep) sees whatever the last run left, or nothing on a
    /// scenario that has not run; Veneer cannot supply or guard what it does internally.
    ///
    /// One instance per batch, constructed under the run lock: Evaluate each item, then — in a
    /// finally — SettlePending, then SweepStrays.
    /// Release is best-effort against a race it cannot see the end of: a re-add to a shared set
    /// (clones of one ModelledVariable) that lands after the 50 ms settle window AND after this
    /// batch's SweepStrays stays registered on the real entity until the NEXT batch's
    /// SweepStrays removes it; this batch does not clean it up.
    /// </summary>
    public class TransientFunctionEvaluator
    {
        public const string TRANSIENT_NAME = "$f_veneer_evaluate";

        /// <summary>How long Release keeps retrying for Parse's background registrations to land:
        /// they normally land within a thread-pool hop. Not a firm cap: the deadline is checked
        /// only between passes, and each TIME AsyncObservableSet.Remove is itself a Task.Run plus
        /// a blocking Wait(100) loop, so it needs a pool thread too — a starved pool can overrun
        /// this by one pass.</summary>
        private static readonly TimeSpan RegistrationWait = TimeSpan.FromSeconds(2);

        /// <summary>How long SettlePending keeps removing from the sets reached by more than one
        /// entity, after Release's first successful Remove, to catch the later entities' re-adds.
        /// One window per batch, shared by every such set.</summary>
        private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(50);

        /// <summary>Wait rounds that Thread.Yield() before falling back to Thread.Sleep(1).</summary>
        private const int SpinRounds = 50;

        private static MethodInfo _setFunctionManager;

        private readonly RiverSystemScenario _scenario;
        private readonly Action<string> _log;
        private bool _loggedTimeStepFallback;
        private readonly List<SettleEntry> _pendingSettle = new List<SettleEntry>();
        private List<Function> _functionsSnapshot;
        private readonly List<BuiltIn> _builtIns = new List<BuiltIn>();
        private readonly HashSet<string> _loggedBuiltInFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Construct once per batch, under the run lock: snapshots the scenario's
        /// built-in variables and computes the batch-wide values ($Start.*, $End.*,
        /// $ActiveInputSet). Never throws for a scenario problem: a built-in it cannot compute is
        /// left out (and logged), so an item that uses it gets "could not bind built-in".</summary>
        public TransientFunctionEvaluator(RiverSystemScenario scenario, Action<string> log)
        {
            _scenario = scenario;
            _log = log ?? (_ => { });
            List<AbstractFunctionVariable> variables = null;
            try
            {
                // Snapshot: an ObservableCollection the GUI can mutate.
                variables = _scenario.Network.FunctionManager.Variables.ToList();
                PrepareBuiltIns(variables);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                _builtIns.Clear();
                _log("WARNING: evaluate could not read the scenario's built-in variables: " + e.GetBaseException().Message
                     + " — items that use them will be errors");
            }
            // Separate from the above: a failure to tell whether the scenario has run is only a
            // missing log line, and must not cost the batch its built-ins.
            try
            {
                if (variables != null && IsCold(_scenario.Network.FunctionManager, variables))
                    _log("evaluating on a scenario that has not run in this process");
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { }
        }

        /// <summary>One of Source's built-in variables and how Veneer computes its value at an
        /// item's date. <see cref="Value"/> is null when it could not be computed for this
        /// batch.</summary>
        private sealed class BuiltIn
        {
            public string FullName;
            public string Name;
            public FolderItem Parent;
            public Func<DateTime, double> Value;
        }

        /// <summary>
        /// The per-batch half of the built-in substitution. Each value is computed the way Source
        /// computes it during a run, from state a loaded scenario already has, and nothing here
        /// writes to the model:
        /// - $Now.X at date d: NowVariableLookup.Instance.GetFunc(X)(d), what NowVariable.GetValue
        ///   returns once FunctionManager.Reset has set its NowFunc;
        /// - $Start.X / $End.X: the same function of the run configuration's StartDate / EndDate,
        ///   what FixedDateVariable.Reset(start / end) records at the start of a run;
        /// - $ActiveInputSet: Network.InputSets.IndexOf(the configuration's SelectedInputSet),
        ///   ActiveInputSetVariable.Reset's own formula;
        /// - a pattern variable at date d: its pattern time series at d, what PatternVariable.Set
        ///   records during a run.
        /// </summary>
        private void PrepareBuiltIns(List<AbstractFunctionVariable> variables)
        {
            foreach (var v in variables)
                _variableFullNames.Add(SafeName(() => v.FullName));

            DateTime? start = null, end = null;
            try
            {
                var cfg = _scenario.CurrentConfiguration;
                if (cfg != null) { start = cfg.StartDate; end = cfg.EndDate; }
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                _log("WARNING: evaluate could not read the run configuration's start and end dates: " + e.GetBaseException().Message);
            }

            double? inputSetIndex = null;
            try
            {
                // ActiveInputSetVariable.Reset(Network), without its write.
                var cfg = _scenario.CurrentRiverSystemConfiguration;
                if (cfg != null) inputSetIndex = _scenario.Network.InputSets.IndexOf(cfg.SelectedInputSet);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                _log("WARNING: evaluate could not read the selected input set: " + e.GetBaseException().Message);
            }

            foreach (var v in variables)
            {
                Func<DateTime, double> value = null;
                var fixedDate = v as FixedDateVariable;     // tested before NowVariable: it is one
                if (fixedDate != null)
                {
                    var when = fixedDate.FixedDateType == FixedDateVariableEnum.Start ? start : end;
                    if (when.HasValue)
                    {
                        var constant = NowValue(fixedDate.NowVariableEnum, when.Value);
                        value = _ => constant;
                    }
                }
                else if (v is NowVariable)
                {
                    var which = ((NowVariable)v).NowVariableEnum;
                    value = date => NowValue(which, date);
                }
                else if (v is PatternVariable)
                {
                    var pattern = (PatternVariable)v;
                    value = date => pattern.TimeSeries.TimeSeries[date];
                }
                else if (v is ActiveInputSetVariable)
                {
                    if (inputSetIndex.HasValue)
                    {
                        var constant = inputSetIndex.Value;
                        value = _ => constant;
                    }
                }
                else continue;

                var variable = v;
                _builtIns.Add(new BuiltIn
                {
                    FullName = SafeName(() => variable.FullName),
                    Name = SafeName(() => variable.Name),
                    Parent = variable.Parent,
                    Value = value,
                });
            }
        }

        /// <summary>$Now.X (and $Start.X, $End.X) at <paramref name="date"/>, by Source's own
        /// (public) mapping, NowVariableLookup.</summary>
        internal static double NowValue(NowVariableEnum which, DateTime date) =>
            NowVariableLookup.Instance.GetFunc(which)(date);

        private static PropertyInfo _runTimeStep;
        private static bool _runTimeStepResolved;

        /// <summary>For the log only. FunctionManager.RunTimeStep is internal on Source 6.10 and set
        /// only by FunctionManager.Reset (at the start of a run), so null means no run in this
        /// process; read by reflection, as the FunctionManager setter is. Without it: the
        /// $Start.Year variable has never recorded a year.</summary>
        private static bool IsCold(FunctionManager fm, List<AbstractFunctionVariable> variables)
        {
            try
            {
                if (!_runTimeStepResolved)
                {
                    _runTimeStep = typeof(FunctionManager).GetProperty("RunTimeStep",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _runTimeStepResolved = true;
                }
                if (_runTimeStep != null) return _runTimeStep.GetValue(fm) == null;
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { }
            var startYear = variables.OfType<FixedDateVariable>().FirstOrDefault(v =>
                v.FixedDateType == FixedDateVariableEnum.Start && v.NowVariableEnum == NowVariableEnum.Year);
            return startYear != null && startYear.Value <= 1;
        }

        /// <summary>
        /// The per-item half: token -> value for every built-in Veneer could compute at
        /// <paramref name="date"/>, resolved as Source resolves names (see ResolveBuiltInTokens).
        /// A value that throws or is not finite is left out, so the guard reports the built-in; a
        /// throw is logged once per batch.
        /// </summary>
        private Dictionary<string, double> BuiltInValues(FunctionManager fm, DateTime date, FolderItem context)
        {
            var computed = new List<BuiltInToken>();
            foreach (var b in _builtIns)
            {
                if (b.Value == null) continue;
                double value;
                try { value = b.Value(date); }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    if (_loggedBuiltInFailures.Add(b.FullName))
                        _log("WARNING: evaluate could not compute built-in " + b.FullName + ": " + e.GetBaseException().Message);
                    continue;
                }
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                computed.Add(new BuiltInToken(b.FullName, b.Name, context != null && b.Parent == context, value));
            }
            return ResolveBuiltInTokens(computed, FunctionTokens(fm, context), _variableFullNames);
        }

        /// <summary>A built-in's names and its value at an item's date; InContext is true when it
        /// lives in the item's context folder (so its leaf name can resolve to it).</summary>
        internal struct BuiltInToken
        {
            public readonly string FullName;
            public readonly string Name;
            public readonly bool InContext;
            public readonly double Value;

            public BuiltInToken(string fullName, string name, bool inContext, double value)
            {
                FullName = fullName;
                Name = name;
                InContext = inContext;
                Value = value;
            }
        }

        /// <summary>
        /// Which tokens to substitute with which built-in's value, deciding a name the way Source
        /// does (FunctionCaches): functions before variables, and within each, the FULL name before
        /// the leaf name within the context folder. So:
        /// - a built-in's full name is substituted unless a function answers to that token;
        /// - a built-in's leaf name is substituted only when the built-in is in the context folder,
        ///   no function answers to it, and no variable at all has that FULL name — otherwise
        ///   Source would resolve the leaf token to that function or variable, and substituting
        ///   would silently replace a reference the guard must see.
        /// Every full name is decided before any leaf name, so a leaf name never pre-empts another
        /// built-in's full name. Names that do not start with '$' (unreadable names) are skipped.
        /// <paramref name="functionTokens"/>: every function's full name, and the leaf name of each
        /// function in the context folder. <paramref name="variableFullNames"/>: every variable's
        /// full name, built-ins included. Source compares names case-insensitively, so both
        /// collections must too (e.g. HashSets with StringComparer.OrdinalIgnoreCase).
        /// </summary>
        internal static Dictionary<string, double> ResolveBuiltInTokens(
            IEnumerable<BuiltInToken> builtIns, ICollection<string> functionTokens, ICollection<string> variableFullNames)
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var list = builtIns.ToList();
            foreach (var b in list)
            {
                if (!IsToken(b.FullName) || values.ContainsKey(b.FullName) || Contains(functionTokens, b.FullName)) continue;
                values[b.FullName] = b.Value;
            }
            foreach (var b in list)
            {
                if (!b.InContext || !IsToken(b.Name) || values.ContainsKey(b.Name)) continue;
                if (Contains(functionTokens, b.Name) || Contains(variableFullNames, b.Name)) continue;
                values[b.Name] = b.Value;
            }
            return values;
        }

        private static bool IsToken(string name) => name != null && name.Length > 1 && name[0] == '$';

        private static bool Contains(ICollection<string> names, string name) => names != null && names.Contains(name);

        /// <summary>Every variable's full name (built-ins included), per batch.</summary>
        private readonly HashSet<string> _variableFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _functionFullNames;
        private readonly Dictionary<FolderItem, HashSet<string>> _functionTokensByContext = new Dictionary<FolderItem, HashSet<string>>();

        /// <summary>The tokens a function answers to from <paramref name="context"/>: every
        /// function's full name, plus the leaf names of the functions in that folder. Per batch,
        /// per context.</summary>
        private HashSet<string> FunctionTokens(FunctionManager fm, FolderItem context)
        {
            if (_functionFullNames == null)
                _functionFullNames = new HashSet<string>(FunctionsSnapshot(fm).Select(f => SafeName(() => f.FullName)), StringComparer.OrdinalIgnoreCase);
            if (context == null) return _functionFullNames;
            HashSet<string> tokens;
            if (_functionTokensByContext.TryGetValue(context, out tokens)) return tokens;
            tokens = new HashSet<string>(_functionFullNames, StringComparer.OrdinalIgnoreCase);
            foreach (var f in FunctionsSnapshot(fm))
                if (f.Parent == context) tokens.Add(SafeName(() => f.Name));
            _functionTokensByContext[context] = tokens;
            return tokens;
        }

        private List<Function> FunctionsSnapshot(FunctionManager fm) =>
            _functionsSnapshot ?? (_functionsSnapshot = fm.Functions.ToList());

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

                var context = ResolveContextFolder(fm, item.Context);
                // Built-ins become literals BEFORE Parse, so none depends on per-run state or on
                // Parse's background registration; the caller's bindings take precedence.
                var substituted = ExpressionSubstitution.Apply(item.Expression, bindings, BuiltInValues(fm, date, context));
                fn = new Function { Name = TRANSIENT_NAME, Expression = substituted };
                fn.Parent = context;
                FunctionManagerSetter().Invoke(fn, new object[] { fm });
                string error;
                var parsed = fn.Parse(out error);
                // Parse launches the registration tasks whenever it RETURNS (even false: a
                // ModelledVariable.Clone failure appends to the error and still registers) unless
                // the manager has disabled referencing functions. Read that flag now, as Parse saw
                // it, not later in finally. When TryParse itself fails the lists are empty, so the
                // wait in Release costs nothing; ValidateUsages cannot fail for this transient
                // (default EvaluationTimes = StartOfTimeStep, no Usages).
                registrationLaunched = !ReferenceRegistrationDisabled(fm);
                if (!parsed)
                {
                    // A reference the guard refuses can itself fail Parse — on a never-run
                    // scenario, cloning a modelled variable throws inside Parse, which appends the
                    // exception's message — so the guard's message, which says what to do, wins.
                    return Error(item, UnboundReferencesOrNull(fn)
                        ?? (string.IsNullOrEmpty(error) ? "parse failed" : StripNamePrefix(error, fn.FullName)));
                }
                // Mandatory, and independent of the substitution above: nothing without an honest
                // value at the item's date is evaluated. The finally still releases.
                var unbound = UnboundReferences(fn.Variables.Cast<object>().Concat(fn.Functions), EntityName);
                if (unbound != null) return Error(item, unbound);
                FillLookupTables(fn.Variables);
                // Source's own runner passes TimeStep.GetTimeSpanToNextTick(now) to Evaluate
                // (NetworkRunner); modelled variables use it to convert rates to per-step amounts.
                // No fallback: a 0-second step would give plausible WRONG amounts. Computed outside
                // the ArithmeticException catch below, so an OverflowException from it is reported
                // as itself (by the outer catch), not as a non-finite result.
                var stepLength = timeStep.GetTimeSpanToNextTick(date);
                try
                {
                    fn.Evaluate(date, timeStep, stepLength, PointOfUpdate.AfterTimeSeriesPlayed);
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
                // Function.Parse can also THROW ("Error in <FullName>: …"); never show the
                // transient's internal name to the client.
                var message = e.GetBaseException().Message;
                if (fn == null) return Error(item, message);
                // As for a false Parse: when Parse threw because of a reference the guard refuses,
                // say so. (After a successful Parse the guard has already passed, so this is null.)
                return Error(item, UnboundReferencesOrNull(fn) ?? StripNamePrefix(message, SafeName(() => fn.FullName)));
            }
            finally
            {
                // A throw out of Release (e.g. from its grouping) must not replace the item's result.
                if (fn != null)
                {
                    try { Release(item, fn, registrationLaunched); }
                    catch (Exception e) when (!(e is OutOfMemoryException))
                    {
                        _log("WARNING: evaluate item " + item.Id + ": release failed: " + e.GetBaseException().Message
                             + " — SweepStrays should catch any stray");
                    }
                }
            }
        }

        private static EvaluateResultItem Error(EvaluateItem item, string message) =>
            new EvaluateResultItem { Id = item.Id, Error = message };

        /// <summary>How the guard treats an entity the parsed transient references.</summary>
        internal enum ReferenceKind
        {
            /// <summary>Lookup tables: their values are model data, not run state.</summary>
            Allowed,
            FunctionReference,
            Modelled,
            Context,
            TimeSeries,
            /// <summary>A built-in that Veneer should have substituted but could not.</summary>
            BuiltIn,
            /// <summary>A variable type this code does not know: refused rather than guessed.</summary>
            Unsupported,
        }

        internal static ReferenceKind Classify(object entity)
        {
            if (entity is Function) return ReferenceKind.FunctionReference;
            if (entity is ModelledVariable) return ReferenceKind.Modelled;
            if (entity is ContextVariable) return ReferenceKind.Context;
            if (entity is TimeSeriesVariable) return ReferenceKind.TimeSeries;
            if (entity is NowVariable || entity is PatternVariable || entity is ActiveInputSetVariable)
                return ReferenceKind.BuiltIn;
            if (entity is LinearVariable || entity is BilinearVariable) return ReferenceKind.Allowed;
            return ReferenceKind.Unsupported;
        }

        /// <summary>The item error for an unbound reference of <paramref name="kind"/>, or null
        /// for an allowed one. <paramref name="typeName"/> is used only for an unsupported
        /// type.</summary>
        internal static string UnboundMessage(ReferenceKind kind, string fullName, string typeName)
        {
            switch (kind)
            {
                case ReferenceKind.Allowed:
                    return null;
                case ReferenceKind.FunctionReference:
                    return fullName + " is a function reference: Source holds only its last evaluated value, not its value at the item's date; bind it";
                case ReferenceKind.Modelled:
                    return fullName + " is a modelled variable: it has no value outside a run; bind it";
                case ReferenceKind.Context:
                    return fullName + " is a context variable: it only has a value at a function usage during a run; bind it";
                case ReferenceKind.TimeSeries:
                    return fullName + " is a time series variable: Veneer cannot read it at the item's date; bind it";
                case ReferenceKind.BuiltIn:
                    return "could not bind built-in " + fullName;
                default:
                    return fullName + " is a " + typeName + ", which Veneer cannot evaluate outside a run; bind it";
            }
        }

        /// <summary>
        /// The guard: the messages for every distinct entity among <paramref name="entities"/>
        /// (the parsed transient's Variables and Functions) that has no honest value at the
        /// item's date, distinct and joined with "; ", or null when there are none. Source's
        /// in-memory state is not an honest value: a function's Value is its LAST evaluated
        /// value, a time series variable's Value is whatever was last played, a modelled variable
        /// reads current model state (and throws on a scenario that has never run) — none is the
        /// value at the item's date.
        /// </summary>
        internal static string UnboundReferences(IEnumerable<object> entities, Func<object, string> fullName)
        {
            var messages = entities
                .Where(e => e != null)
                .Distinct()
                .Select(e => UnboundMessage(Classify(e), fullName(e), e.GetType().Name))
                .Where(m => m != null)
                .Distinct()
                .ToList();
            return messages.Count == 0 ? null : string.Join("; ", messages);
        }

        /// <summary>The guard over whatever <paramref name="fn"/> holds after a failed Parse; null
        /// when it refuses nothing or cannot be read.</summary>
        private static string UnboundReferencesOrNull(Function fn)
        {
            try
            {
                if (fn.Variables == null || fn.Functions == null) return null;
                return UnboundReferences(fn.Variables.Cast<object>().Concat(fn.Functions), EntityName);
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { return null; }
        }

        private static string EntityName(object entity)
        {
            var f = entity as Function;
            if (f != null) return SafeName(() => f.FullName);
            var v = entity as AbstractFunctionVariable;
            if (v != null) return SafeName(() => v.FullName);
            return SafeName(entity.ToString);
        }

        /// <summary>
        /// Build the in-memory interpolator of each lookup table the transient references that
        /// has persisted entries but no interpolator yet: the state of every lookup table on a
        /// scenario loaded but not run in this process, where lookup() would otherwise answer 0.
        /// This is the ONE write evaluate makes to a model object, and it is acceptable because
        /// LinearVariable.Reset() (public) only rebuilds LinearFunction — a derived cache, not
        /// persisted — from Entries, the persisted table, which it does not touch; the next run
        /// does exactly the same to every lookup table; and the test (no points, some entries)
        /// makes it idempotent and never replaces a table that is already built.
        /// (FunctionManager.Reset, which would also do it, must never be called: it writes
        /// defaults into model parameters through every function usage.) Bilinear tables need
        /// nothing: BilinearInterpolator builds its cache on first lookup.
        /// </summary>
        internal static void FillLookupTables(IEnumerable<AbstractFunctionVariable> variables)
        {
            foreach (var table in variables.OfType<LinearVariable>().Distinct())
                if (table.LinearFunction.Count == 0 && table.Entries.Count > 0)
                    table.Reset();
        }

        /// <summary>Source prefixes parse errors with the function's FullName and ": " (returned
        /// errors) or "Error in " + FullName + ": " (thrown ones), which here would be the
        /// transient's internal name. Strips either prefix; anything else is returned
        /// unchanged.</summary>
        internal static string StripNamePrefix(string error, string fullName)
        {
            if (error == null || string.IsNullOrEmpty(fullName)) return error;
            foreach (var prefix in new[] { "Error in " + fullName + ": ", fullName + ": " })
                if (error.StartsWith(prefix, StringComparison.Ordinal)) return error.Substring(prefix.Length);
            return error;
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

        private static PropertyInfo _cachedDisableReferenceFunctions;
        private static FieldInfo _disableFunctionReferencesOption;
        private static bool _disableReferenceFunctionsResolved;

        /// <summary>
        /// Whether Parse skips launching its ReferencingFunctions registrations, read as Parse
        /// itself reads it. Found by reflection because master must compile against EVERY CoreWCF
        /// Source version, and the flag is not in all of them: the instance property
        /// FunctionManager.CachedDisableReferenceFunctions first appears in Source 6.2 (6.0 and
        /// 6.1 lack it), and older versions read the static user option
        /// RiverSystem.Options.RiverSystemOptions.DISABLE_FUNCTION_REFERENCES instead. legacy_ci
        /// reads it the same way. When neither member is found, or reading it throws, the answer
        /// is false (registration launched): the safe side, since Release then waits for the adds
        /// before giving up, and SweepStrays backs it up.
        /// </summary>
        private static bool ReferenceRegistrationDisabled(FunctionManager fm)
        {
            try
            {
                if (!_disableReferenceFunctionsResolved)
                {
                    _cachedDisableReferenceFunctions = typeof(FunctionManager).GetProperty("CachedDisableReferenceFunctions",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_cachedDisableReferenceFunctions == null)
                        _disableFunctionReferencesOption = typeof(Function).Assembly
                            .GetType("RiverSystem.Options.RiverSystemOptions", false)
                            ?.GetField("DISABLE_FUNCTION_REFERENCES", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    _disableReferenceFunctionsResolved = true;
                }
                if (_cachedDisableReferenceFunctions != null) return (bool)_cachedDisableReferenceFunctions.GetValue(fm);
                if (_disableFunctionReferencesOption != null) return (bool)_disableFunctionReferencesOption.GetValue(null);
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { }
            return false;
        }

        /// <summary>
        /// Remove the transient from EVERY entity Parse bound it to — functions, variables, date
        /// builtins, lookup tables alike. Never a counted subset. See RemoveFromSets for why this
        /// waits; sets that still need settling are queued for the batch's SettlePending.
        /// </summary>
        private void Release(EvaluateItem item, Function fn, bool registrationLaunched)
        {
            // Group the DISTINCT entities (the same reference equality Parse's Except uses, so one
            // entity = one TryAddAsync) by the set they reach: a ModelledVariable clone's
            // ReferencingFunctions is its original's, so clones of one variable share a set.
            var groups = GroupBySet(
                fn.Functions.Distinct().Select(f => (f.ReferencingFunctions, SafeName(() => f.FullName)))
                    .Concat(fn.Variables.Distinct().Select(v => (v.ReferencingFunctions, SafeName(() => v.FullName)))));
            var names = groups.ToDictionary(g => g.set, g => g.names);
            var sets = groups.Select(g => (g.set, g.entityCount)).ToList();

            List<(AsyncObservableSet<Function> set, int entityCount, int removals)> unsettled;
            var outstanding = RemoveFromSets(sets, s => s.Remove(fn), registrationLaunched, RegistrationWait,
                (set, e) => _log("WARNING: evaluate item " + item.Id + ": could not release from " + names[set] + ": " + e.GetBaseException().Message),
                out unsettled);
            foreach (var u in unsettled)
            {
                var set = u.set;
                _pendingSettle.Add(new SettleEntry
                {
                    Remove = () => set.Remove(fn),
                    Entities = u.entityCount,
                    Removals = u.removals,
                    Label = "item " + item.Id + " on " + names[set],
                });
            }
            if (registrationLaunched && outstanding.Count > 0)
                _log("WARNING: evaluate item " + item.Id + ": release timed out with registration(s) outstanding on "
                     + string.Join("; ", outstanding.Select(s => names[s])) + " — SweepStrays should catch them");
        }

        /// <summary>
        /// Release's grouping: one row per distinct non-null set, with how many of the given
        /// entities reach it and their distinct names joined with ", ", in first-seen order. The
        /// caller passes each entity once (already Distinct), so the count is the number of
        /// TryAddAsync calls that target the set. Generic over the set type so it is testable
        /// without Source.
        /// </summary>
        internal static List<(TSet set, int entityCount, string names)> GroupBySet<TSet>(IEnumerable<(TSet set, string name)> entities)
            where TSet : class
        {
            return entities
                .Where(x => x.set != null)
                .GroupBy(x => x.set)
                .Select(g => (g.Key, g.Count(), string.Join(", ", g.Select(x => x.name).Distinct())))
                .ToList();
        }

        /// <summary>
        /// Once per batch, after the last item and before SweepStrays: run ONE bounded settle
        /// window across every set that Release left needing one (see Settle), then clear the
        /// list. Returns how many of them still held the transient once the window had closed
        /// (those are removed, and named in this method's OWN warning — callers must not log a
        /// second one).
        /// </summary>
        public int SettlePending()
        {
            if (_pendingSettle.Count == 0) return 0;
            var entries = _pendingSettle.ToList();
            _pendingSettle.Clear();
            var strays = Settle(entries, SettleWindow,
                (entry, e) => _log("WARNING: evaluate " + entry.Label + ": could not settle release: " + e.GetBaseException().Message));
            if (strays.Count > 0)
                _log("WARNING: evaluate found " + strays.Count + " late re-registration(s) after the settle window, now removed: "
                     + string.Join("; ", strays.Select(s => s.Label)));
            return strays.Count;
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
        /// removing until every set has had a successful Remove, or until <paramref name="wait"/>
        /// has elapsed. That bound is checked only between passes, and the real TIME
        /// AsyncObservableSet.Remove is a Task.Run plus a blocking Wait(100) loop — each Remove
        /// needs a pool thread and blocks this caller — so a starved pool can overrun
        /// <paramref name="wait"/> by one pass. A set reached by more than one entity (clones of one
        /// ModelledVariable) gets more than one add, and a later add can re-insert after the
        /// first Remove; such sets, once removed from, are returned in
        /// <paramref name="unsettled"/> (with their removal count so far) for Settle to finish —
        /// once per batch, not per item. A Remove that throws is reported via
        /// <paramref name="onError"/> and that set is skipped.
        /// </summary>
        internal static List<TSet> RemoveFromSets<TSet>(
            IReadOnlyList<(TSet set, int entityCount)> sets,
            Func<TSet, bool> remove,
            bool waitForAdds,
            TimeSpan wait,
            Action<TSet, Exception> onError,
            out List<(TSet set, int entityCount, int removals)> unsettled)
        {
            var pending = sets.ToList();
            var settle = new List<(TSet set, int entityCount, int removals)>();

            var clock = Stopwatch.StartNew();
            var round = 0;
            while (true)
            {
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    var entry = pending[i];
                    bool removed;
                    if (!TryRemove(() => remove(entry.set), e => onError?.Invoke(entry.set, e), out removed)) { pending.RemoveAt(i); continue; }
                    if (!removed) continue;
                    // Without waitForAdds no registration was launched, so nothing can re-add.
                    if (waitForAdds && entry.entityCount > 1) settle.Add((entry.set, entry.entityCount, 1));
                    pending.RemoveAt(i);
                }
                if (pending.Count == 0 || !waitForAdds || clock.Elapsed >= wait) break;
                Pause(round++);
            }

            unsettled = settle;
            return pending.Select(p => p.set).ToList();
        }

        /// <summary>A set Release removed the transient from once, but which more than one entity's
        /// registration reaches, so a later add may re-insert it.</summary>
        internal sealed class SettleEntry
        {
            /// <summary>Removes the transient from the set; true when it was present.</summary>
            public Func<bool> Remove;
            public int Entities;
            public int Removals;
            public string Label;
        }

        /// <summary>
        /// Keep removing from each entry for up to <paramref name="window"/>, stopping early for an
        /// entry once it has had as many successful Removes as entities (then every add has
        /// landed and been undone). An entry often never gets there, and that is fine: the set
        /// dedups, so adds that both landed before the first Remove leave one presence and one
        /// successful Remove. That is why falling short is not itself reported. After the window,
        /// one last Remove per remaining entry: the entries it found the transient in (an add that
        /// landed after the window's last pass) are returned. This is a heuristic — a pool
        /// starved for longer than the window can still re-add later; SweepStrays is the
        /// backstop. A Remove that throws is reported via <paramref name="onError"/> and that
        /// entry is dropped.
        /// </summary>
        internal static List<SettleEntry> Settle(IReadOnlyList<SettleEntry> entries, TimeSpan window, Action<SettleEntry, Exception> onError)
        {
            var active = entries.Where(e => e.Removals < e.Entities).ToList();
            var clock = Stopwatch.StartNew();
            var round = 0;
            while (active.Count > 0)
            {
                for (var i = active.Count - 1; i >= 0; i--)
                {
                    var entry = active[i];
                    bool removed;
                    if (!TryRemove(entry.Remove, e => onError?.Invoke(entry, e), out removed)) { active.RemoveAt(i); continue; }
                    if (removed && ++entry.Removals >= entry.Entities) active.RemoveAt(i);
                }
                if (active.Count == 0 || clock.Elapsed >= window) break;
                Pause(round++);
            }

            var strays = new List<SettleEntry>();
            foreach (var entry in active)
            {
                bool removed;
                if (TryRemove(entry.Remove, e => onError?.Invoke(entry, e), out removed) && removed)
                {
                    entry.Removals++;
                    strays.Add(entry);
                }
            }
            return strays;
        }

        /// <summary>False when remove threw (reported via onError).</summary>
        private static bool TryRemove(Func<bool> remove, Action<Exception> onError, out bool removed)
        {
            try
            {
                removed = remove();
                return true;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                removed = false;
                onError?.Invoke(e);
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
        private FolderItem ResolveContextFolder(FunctionManager fm, string context)
        {
            if (string.IsNullOrEmpty(context)) return null;
            // Snapshot once per batch (the GUI can mutate this ObservableCollection), not per item.
            var functions = FunctionsSnapshot(fm);
            var f = functions.FirstOrDefault(x => string.Equals(x.FullName, context, StringComparison.InvariantCultureIgnoreCase));
            if (f != null) return f.Parent;
            var byName = functions.Where(x => string.Equals(x.Name, context, StringComparison.InvariantCultureIgnoreCase)).Take(2).ToList();
            if (byName.Count > 1) throw new ArgumentException("ambiguous context: " + context);
            if (byName.Count == 0) throw new ArgumentException("context function not found: " + context);
            return byName[0].Parent;
        }
    }
}
