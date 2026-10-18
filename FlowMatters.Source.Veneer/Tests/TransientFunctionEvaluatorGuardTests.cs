using System;
using System.Collections.Generic;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;
using RiverSystem.Functions;
using RiverSystem.Functions.Variables;
using Kind = FlowMatters.Source.Veneer.DomainActions.TransientFunctionEvaluator.ReferenceKind;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// The scenario-free halves of evaluating honestly on a scenario that has never run: Veneer's
    /// values for the date built-ins, and the guard that turns every reference without an honest
    /// value at the item's date into an item error. Uses the real Source variable types (their
    /// parameterless constructors need no scenario).
    /// </summary>
    [TestFixture]
    public class TransientFunctionEvaluatorGuardTests
    {
        [TestCase(NowVariableEnum.Day, "2001-07-01", 1)]
        [TestCase(NowVariableEnum.Month, "2001-07-01", 7)]
        [TestCase(NowVariableEnum.Year, "2001-07-01", 2001)]
        [TestCase(NowVariableEnum.Hour, "2001-07-01", 0)]
        [TestCase(NowVariableEnum.DayOfYear, "2001-07-01", 182)]
        [TestCase(NowVariableEnum.DaysInMonth, "2001-07-01", 31)]
        [TestCase(NowVariableEnum.DayOfYear, "2000-02-15", 46)]
        [TestCase(NowVariableEnum.DaysInMonth, "2000-02-15", 29)]
        [TestCase(NowVariableEnum.DaysInMonth, "1900-02-15", 28)]
        public void NowValue_IsSourcesOwnMappingOfTheDate(NowVariableEnum which, string date, double expected)
        {
            Assert.That(TransientFunctionEvaluator.NowValue(which, DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(expected));
        }

        private static IEnumerable<TestCaseData> Entities()
        {
            yield return new TestCaseData(new Function()).Returns("FunctionReference").SetName("Classify_Function");
            yield return new TestCaseData(new ModelledVariable()).Returns("Modelled").SetName("Classify_ModelledVariable");
            yield return new TestCaseData(new ContextVariable()).Returns("Context").SetName("Classify_ContextVariable");
            yield return new TestCaseData(new TimeSeriesVariable()).Returns("TimeSeries").SetName("Classify_TimeSeriesVariable");
            yield return new TestCaseData(new NowVariable()).Returns("BuiltIn").SetName("Classify_NowVariable");
            yield return new TestCaseData(new FixedDateVariable()).Returns("BuiltIn").SetName("Classify_FixedDateVariable");
            yield return new TestCaseData(new PatternVariable()).Returns("BuiltIn").SetName("Classify_PatternVariable");
            yield return new TestCaseData(new ActiveInputSetVariable()).Returns("BuiltIn").SetName("Classify_ActiveInputSetVariable");
            yield return new TestCaseData(new LinearVariable()).Returns("Allowed").SetName("Classify_LinearVariable");
            yield return new TestCaseData(new BilinearVariable()).Returns("Allowed").SetName("Classify_BilinearVariable");
            yield return new TestCaseData(new object()).Returns("Unsupported").SetName("Classify_UnknownType");
        }

        [TestCaseSource(nameof(Entities))]
        public string Classify_SortsEachSourceTypeIntoItsKind(object entity) => TransientFunctionEvaluator.Classify(entity).ToString();

        [TestCase("Allowed", null)]
        [TestCase("FunctionReference", "$A.f is a function reference: Source holds only its last evaluated value, not its value at the item's date; bind it")]
        [TestCase("Modelled", "$A.f is a modelled variable: it has no value outside a run; bind it")]
        [TestCase("Context", "$A.f is a context variable: it only has a value at a function usage during a run; bind it")]
        [TestCase("TimeSeries", "$A.f is a time series variable: Veneer cannot read it at the item's date; bind it")]
        [TestCase("BuiltIn", "could not bind built-in $A.f")]
        [TestCase("Unsupported", "$A.f is a SomeVariable, which Veneer cannot evaluate outside a run; bind it")]
        public void UnboundMessage_NamesTheReferenceAndWhatToDo(string kind, string expected)
        {
            Assert.That(TransientFunctionEvaluator.UnboundMessage((Kind)Enum.Parse(typeof(Kind), kind), "$A.f", "SomeVariable"), Is.EqualTo(expected));
        }

        private static string Name(object entity) =>
            entity is Function f ? f.Name : entity is AbstractFunctionVariable v ? v.Name : entity.ToString();

        [Test] public void UnboundReferences_IsNullWhenOnlyLookupTablesAreReferenced()
        {
            Assert.That(TransientFunctionEvaluator.UnboundReferences(new object[] { new LinearVariable(), new BilinearVariable() }, Name), Is.Null);
            Assert.That(TransientFunctionEvaluator.UnboundReferences(new object[0], Name), Is.Null);
        }

        [Test] public void UnboundReferences_ReportsEveryOffenderOnceAndIgnoresLookupTables()
        {
            var modelled = new ModelledVariable { Name = "$v_Store" };
            var clone = new ModelledVariable { Name = "$v_Store" };   // clones share a name: one message
            var series = new TimeSeriesVariable { Name = "$v_Series" };
            var fn = new Function { Name = "$f_Other" };
            var message = TransientFunctionEvaluator.UnboundReferences(
                new object[] { new LinearVariable(), modelled, clone, modelled, series, fn, null }, Name);
            Assert.That(message, Is.EqualTo(
                "$v_Store is a modelled variable: it has no value outside a run; bind it; "
                + "$v_Series is a time series variable: Veneer cannot read it at the item's date; bind it; "
                + "$f_Other is a function reference: Source holds only its last evaluated value, not its value at the item's date; bind it"));
        }

        // ---- ResolveBuiltInTokens: names resolve as Source resolves them

        private static TransientFunctionEvaluator.BuiltInToken Token(string fullName, string name, bool inContext, double value) =>
            new TransientFunctionEvaluator.BuiltInToken(fullName, name, inContext, value);

        private static HashSet<string> Names(params string[] names) =>
            new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, double> Resolve(TransientFunctionEvaluator.BuiltInToken[] builtIns,
            HashSet<string> functionTokens = null, HashSet<string> variableFullNames = null) =>
            TransientFunctionEvaluator.ResolveBuiltInTokens(builtIns, functionTokens ?? Names(), variableFullNames ?? Names());

        [Test] public void Resolve_FullNamesAlwaysAndLeafNamesOnlyInTheContextFolder()
        {
            var values = Resolve(new[]
            {
                Token("$Now.Day", "$Day", false, 5),
                Token("$WhatIf.pat_Season", "$pat_Season", true, 7),
            });
            Assert.That(values, Is.EquivalentTo(new Dictionary<string, double>
            {
                { "$Now.Day", 5 }, { "$WhatIf.pat_Season", 7 }, { "$pat_Season", 7 },
            }));
        }

        [Test] public void Resolve_ALeafNameIsNeverSubstitutedForAVariableWithThatFullName()
        {
            // A top-level time series variable $pat_Season: Source resolves "$pat_Season" to it
            // (full name before leaf name), so it must reach Parse and the guard untouched.
            var values = Resolve(new[] { Token("$WhatIf.pat_Season", "$pat_Season", true, 7) },
                variableFullNames: Names("$WhatIf.pat_Season", "$PAT_SEASON"));
            Assert.That(values.Keys, Is.EquivalentTo(new[] { "$WhatIf.pat_Season" }));
        }

        [Test] public void Resolve_ALeafNameIsNeverSubstitutedForAFunctionThatAnswersToIt()
        {
            var values = Resolve(new[] { Token("$WhatIf.pat_Season", "$pat_Season", true, 7) },
                functionTokens: Names("$pat_season"));
            Assert.That(values.Keys, Is.EquivalentTo(new[] { "$WhatIf.pat_Season" }));
        }

        [Test] public void Resolve_AFullNameIsNotSubstitutedWhenAFunctionAnswersToIt()
        {
            var values = Resolve(new[] { Token("$WhatIf.pat_Season", "$pat_Season", false, 7) },
                functionTokens: Names("$WhatIf.pat_Season"));
            Assert.That(values, Is.Empty);
        }

        [Test] public void Resolve_ALeafNameNeverPreEmptsAnotherBuiltInsFullName()
        {
            // The first built-in's leaf name is the second's full name: the full name wins,
            // whatever the order.
            var values = Resolve(new[]
            {
                Token("$Ctx.x", "$Top", true, 1),
                Token("$Top", "$Top", false, 2),
            });
            Assert.That(values, Is.EquivalentTo(new Dictionary<string, double> { { "$Ctx.x", 1 }, { "$Top", 2 } }));
        }

        [Test] public void Resolve_SkipsUnreadableNames()
        {
            var values = Resolve(new[] { Token("?", "?", true, 1), Token("$", "$", true, 2), Token(null, null, true, 3) });
            Assert.That(values, Is.Empty);
        }

        // ---- FillLookupTables: builds only an unbuilt table that has entries

        private static LinearVariable Table(params double[] xy)
        {
            var table = new LinearVariable();
            for (var i = 0; i < xy.Length; i += 2)
                table.Entries.Add(new LinearFunctionVariableEntry { X = xy[i], Y = xy[i + 1] });
            return table;
        }

        [Test] public void FillLookupTables_BuildsAnUnbuiltTableFromItsEntries()
        {
            var table = Table(0, 0, 10, 5, 20, 20);
            Assert.That(table.LinearFunction.Count, Is.EqualTo(0));
            TransientFunctionEvaluator.FillLookupTables(new AbstractFunctionVariable[] { table, table });
            Assert.That(table.LinearFunction.Count, Is.EqualTo(3));
            Assert.That(table.LinearFunction.f(5), Is.EqualTo(2.5));
            Assert.That(table.Entries.Count, Is.EqualTo(3), "entries untouched");
        }

        [Test] public void FillLookupTables_NeverReplacesABuiltTable()
        {
            var table = Table(0, 0, 10, 5);
            table.Reset();
            var built = table.LinearFunction;
            TransientFunctionEvaluator.FillLookupTables(new AbstractFunctionVariable[] { table });
            Assert.That(table.LinearFunction, Is.SameAs(built));
        }

        [Test] public void FillLookupTables_LeavesATableWithNoEntriesAlone()
        {
            var table = Table();
            var original = table.LinearFunction;
            TransientFunctionEvaluator.FillLookupTables(new AbstractFunctionVariable[] { table, new BilinearVariable() });
            Assert.That(table.LinearFunction, Is.SameAs(original));
        }

        [Test] public void UnboundReferences_ReportsABuiltInLeftUnsubstituted()
        {
            var now = new NowVariable { NowVariableEnum = NowVariableEnum.Day };
            Assert.That(TransientFunctionEvaluator.UnboundReferences(new object[] { now }, Name),
                Is.EqualTo("could not bind built-in $Day"));
        }
    }
}
