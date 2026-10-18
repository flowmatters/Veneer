using System.Collections.Generic;
using FlowMatters.Source.Veneer.Functions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class ExpressionSubstitutionTests
    {
        private static string Apply(string expr, params (string, double)[] b)
        {
            var d = new Dictionary<string, double>();
            foreach (var (k, v) in b) d[k] = v;
            return ExpressionSubstitution.Apply(expr, d);
        }

        [Test] public void ReplacesAWholeToken() =>
            Assert.That(Apply("$v_X * 2", ("$v_X", 12.5)), Is.EqualTo("12.5 * 2"));

        [Test] public void DoesNotTouchLongerTokensWithTheSamePrefix() =>
            Assert.That(Apply("$v_X + $v_X2", ("$v_X", 1)), Is.EqualTo("1 + $v_X2"));

        [Test] public void DoesNotTouchADottedTokenEndingInTheName() =>
            Assert.That(Apply("$A.v_X + $v_X", ("$v_X", 1)), Is.EqualTo("$A.v_X + 1"));

        [Test] public void MatchesCaseInsensitively() =>
            Assert.That(Apply("$V_X + $v_x", ("$v_X", 3)), Is.EqualTo("3 + 3"));

        [Test] public void StripsBlockAndLineCommentsFirst() =>
            Assert.That(Apply("/* a $v_X */ $v_X // $v_X\n+ 1", ("$v_X", 2)), Does.Not.Contain("/*").And.Contains("2"));

        [Test] public void IgnoresUnknownKeys() =>
            Assert.That(Apply("1 + 2", ("$nope", 9)), Is.EqualTo("1 + 2"));

        [Test] public void FormatsInvariantRoundTrip() =>
            Assert.That(Apply("$a", ("$a", 0.1 + 0.2)), Is.EqualTo((0.1 + 0.2).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        [Test] public void NegativeValuesAreParenthesised() =>
            Assert.That(Apply("2 - $a", ("$a", -3)), Is.EqualTo("2 - (-3)"));
    }
}
