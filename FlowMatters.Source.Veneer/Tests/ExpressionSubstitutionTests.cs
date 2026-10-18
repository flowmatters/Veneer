using System.Collections.Generic;
using System.Linq;
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

        // master pins "2 - (-0)": on .NET Core 3.0+ "R" formats -0.0 as "-0". .NET Framework 4.8
        // formats it as "0", so here it needs (and gets) no parentheses; either way the
        // parenthesising follows the formatted text, not the sign of the double.
        [Test] public void NegativeZeroFormatsAsAPlainZeroOnNetFramework() =>
            Assert.That(Apply("2 - $a", ("$a", -0.0)), Is.EqualTo("2 - 0"));

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

        // ---- built-ins: values Veneer supplies, merged only for tokens the caller did not bind

        private static Dictionary<string, double> D(params (string, double)[] b)
        {
            var d = new Dictionary<string, double>();
            foreach (var (k, v) in b) d[k] = v;
            return d;
        }

        [Test] public void BuiltInsFillTokensTheCallerDidNotBind() =>
            Assert.That(ExpressionSubstitution.Apply("$Now.Day + $v_X", D(("$v_X", 1)), D(("$Now.Day", 5))),
                Is.EqualTo("5 + 1"));

        [Test] public void ACallerBindingWinsOverABuiltIn() =>
            Assert.That(ExpressionSubstitution.Apply("$Now.Day * 2", D(("$Now.Day", 9)), D(("$Now.Day", 5))),
                Is.EqualTo("9 * 2"));

        [Test] public void ACallerBindingWinsOverABuiltInCaseInsensitively() =>
            // A plain (case-sensitive) dictionary: the merge itself must compare ignoring case.
            Assert.That(ExpressionSubstitution.Apply("$NOW.day * 2", D(("$now.DAY", 9)), D(("$Now.Day", 5))),
                Is.EqualTo("9 * 2"));

        [Test] public void BuiltInsMatchWholeTokensCaseInsensitively() =>
            Assert.That(ExpressionSubstitution.Apply("$NOW.DAY + $Now.DayOfYear + $A.Now.Day", null, D(("$Now.Day", 5))),
                Is.EqualTo("5 + $Now.DayOfYear + $A.Now.Day"));

        [Test] public void BuiltInsAreNotSubstitutedInsideComments() =>
            Assert.That(ExpressionSubstitution.Apply("/* $Start.Year */ $Start.Year", null, D(("$Start.Year", 2000))),
                Is.EqualTo("  2000"));

        [Test] public void ANegativeBuiltInIsParenthesised() =>
            Assert.That(ExpressionSubstitution.Apply("1 - $ActiveInputSet", null, D(("$ActiveInputSet", -1))),
                Is.EqualTo("1 - (-1)"));

        [Test] public void NullOrEmptyBuiltInsLeaveApplyUnchanged()
        {
            Assert.That(ExpressionSubstitution.Apply("$v_X + $Now.Day", D(("$v_X", 1)), null), Is.EqualTo("1 + $Now.Day"));
            Assert.That(ExpressionSubstitution.Apply("$v_X + $Now.Day", D(("$v_X", 1)), D()), Is.EqualTo("1 + $Now.Day"));
        }

        [Test] public void TheActiveInputSetBuiltInLeavesSourcesComparisonFormAlone() =>
            // Source rewrites `$ActiveInputSet == "name"` (or `=`) itself during Parse; a number
            // in its place would no longer parse.
            Assert.That(ExpressionSubstitution.Apply(
                    "$ActiveInputSet + ($ActiveInputSet == \"Wet\") + ($activeinputset=\"Dry\") + ($ActiveInputSet > 1)",
                    null, D(("$ActiveInputSet", 2))),
                Is.EqualTo("2 + ($ActiveInputSet == \"Wet\") + ($activeinputset=\"Dry\") + (2 > 1)"));

        [Test] public void Unbound_KeepsOnlyBuiltInsTheCallerDidNotBindInOrder()
        {
            var unbound = ExpressionSubstitution.Unbound(
                D(("$now.day", 1), ("$v_X", 2)),
                D(("$Now.Day", 5), ("$Start.Year", 2000), ("", 3), ("$End.Day", 31)));
            Assert.That(unbound.Select(kv => kv.Key), Is.EqualTo(new[] { "$Start.Year", "$End.Day" }));
            Assert.That(unbound.Select(kv => kv.Value), Is.EqualTo(new[] { 2000.0, 31.0 }));
        }

        [Test] public void Unbound_ToleratesNulls()
        {
            Assert.That(ExpressionSubstitution.Unbound(null, null), Is.Empty);
            Assert.That(ExpressionSubstitution.Unbound(null, D(("$Now.Day", 5))).Count, Is.EqualTo(1));
        }
    }
}
