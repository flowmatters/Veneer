using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        private static string CollapseWhitespace(string s) =>
            Regex.Replace(s, @"\s+", " ").Trim();

        [Test] public void ReplacesAWholeToken() =>
            Assert.That(Apply("$v_X * 2", ("$v_X", 12.5)), Is.EqualTo("12.5 * 2"));

        [Test] public void DoesNotTouchLongerTokensWithTheSamePrefix() =>
            Assert.That(Apply("$v_X + $v_X2", ("$v_X", 1)), Is.EqualTo("1 + $v_X2"));

        [Test] public void DoesNotTouchADottedTokenEndingInTheName() =>
            Assert.That(Apply("$A.v_X + $v_X", ("$v_X", 1)), Is.EqualTo("$A.v_X + 1"));

        [Test] public void MatchesCaseInsensitively() =>
            Assert.That(Apply("$V_X + $v_x", ("$v_X", 3)), Is.EqualTo("3 + 3"));

        [Test] public void StripsBlockAndLineCommentsFirst() =>
            Assert.That(Apply("/* a $v_X */ $v_X // $v_X\n+ 1", ("$v_X", 2)), Is.EqualTo("  2  \n+ 1"));

        [Test] public void LineCommentContainingBlockCommentStartDoesNotConsumeFollowingCode()
        {
            // A "//" line comment that happens to contain a "/*" must not let a later, unrelated
            // "*/" close a "block comment" that was never opened — the two comment forms are
            // scanned together, left to right, so whichever one starts first wins.
            var result = Apply("1 // see /* note\n+ $v_X /* y */ + 2", ("$v_X", 2));
            Assert.That(CollapseWhitespace(result), Is.EqualTo("1 + 2 + 2"));
        }

        [Test] public void IgnoresUnknownKeys() =>
            Assert.That(Apply("1 + 2", ("$nope", 9)), Is.EqualTo("1 + 2"));

        [Test] public void FormatsInvariantRoundTrip() =>
            Assert.That(Apply("$a", ("$a", 0.1 + 0.2)), Is.EqualTo((0.1 + 0.2).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        [Test] public void NegativeValuesAreParenthesised() =>
            Assert.That(Apply("2 - $a", ("$a", -3)), Is.EqualTo("2 - (-3)"));

        [Test] public void NegativeZeroIsParenthesisedFromItsFormattedText() =>
            Assert.That(Apply("2 - $a", ("$a", -0.0)), Is.EqualTo("2 - (-0)"));

        [Test] public void BothPrefixTokensBoundInInsertionOrder() =>
            Assert.That(Apply("$v_X + $v_X2", ("$v_X", 1), ("$v_X2", 2)), Is.EqualTo("1 + 2"));

        [Test] public void BothPrefixTokensBoundInReverseInsertionOrder() =>
            Assert.That(Apply("$v_X + $v_X2", ("$v_X2", 2), ("$v_X", 1)), Is.EqualTo("1 + 2"));

        [Test] public void TokenNextToCommaInsideFunctionCall() =>
            Assert.That(Apply("max($v_X,1)", ("$v_X", 5)), Is.EqualTo("max(5,1)"));

        [Test] public void TokenNextToBareParentheses() =>
            Assert.That(Apply("($v_X)", ("$v_X", 5)), Is.EqualTo("(5)"));

        [Test] public void BlockCommentSpanningSeveralLinesIsStrippedAsOne() =>
            Assert.That(Apply("/*\nmulti\nline\n*/ $v_X", ("$v_X", 9)), Is.EqualTo("  9"));

        [Test] public void NullExpressionStripsToEmptyString() =>
            Assert.That(ExpressionSubstitution.StripComments(null), Is.EqualTo(""));

        [Test] public void NullBindingsStillStripComments() =>
            Assert.That(ExpressionSubstitution.Apply("/* c */ 1", null), Is.EqualTo("  1"));

        [Test] public void EmptyBindingsStillStripComments() =>
            Assert.That(ExpressionSubstitution.Apply("/* c */ 1", new Dictionary<string, double>()), Is.EqualTo("  1"));

        [Test] public void EmptyKeyIsIgnored() =>
            Assert.That(Apply("1 + 2", ("", 9)), Is.EqualTo("1 + 2"));
    }
}
