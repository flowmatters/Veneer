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
            yield return new TestCaseData(new FixedDateVariable(FixedDateVariableEnum.Start)).Returns("BuiltIn").SetName("Classify_FixedDateVariable");
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

        [Test] public void UnboundReferences_ReportsABuiltInLeftUnsubstituted()
        {
            var now = new NowVariable { NowVariableEnum = NowVariableEnum.Day };
            Assert.That(TransientFunctionEvaluator.UnboundReferences(new object[] { now }, Name),
                Is.EqualTo("could not bind built-in $Day"));
        }
    }
}
