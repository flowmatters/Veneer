using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.Functions
{
    /// <summary>
    /// Substitutes literal values for reference tokens in a Source function expression — the
    /// text half of POST /functions/evaluate. Pure and Source-free so it is unit-testable.
    /// </summary>
    public static class ExpressionSubstitution
    {
        // A single alternation, scanned left to right, so whichever comment form starts first
        // "wins" the match. This matters: with two separate regex passes (block comment first,
        // then line comment), a "//" line comment containing a literal "/*" would be left alone
        // by the first pass and then have its "/*" picked up by a later, unrelated "*/" on a
        // following line, deleting code that was never actually commented out. Scanning the two
        // forms together, in textual order, avoids that.
        private static readonly Regex Comment = new Regex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Strips "//" line comments and "/* ... */" block comments from
        /// <paramref name="expression"/>, replacing each one with a single space. A null
        /// expression yields "". Block comments may span multiple lines. Comment forms are
        /// recognised lexically rather than parsed, so a "//" or "/*" that appears inside a
        /// string literal is stripped as if it were a real comment; this is an accepted
        /// limitation, since Source function expressions essentially never contain string
        /// literals.
        /// </summary>
        public static string StripComments(string expression) =>
            Comment.Replace(expression ?? string.Empty, " ");

        /// <summary>
        /// Replaces each reference token named in <paramref name="bindings"/> with its value,
        /// formatted as an invariant-culture numeric literal. Comments are stripped first (see
        /// <see cref="StripComments"/>), so a token that only appears inside a comment is never
        /// substituted. A match must be a whole token — not preceded by a word character, '.',
        /// or '$', and not followed by a word character or '.' — so "$v_X" does not touch
        /// "$v_X2" or the "v_X" inside "$A.v_X". Matching is case-insensitive and
        /// culture-invariant. A negative value (including negative zero) is wrapped in
        /// parentheses, e.g. "2 - $a" with $a = -3 becomes "2 - (-3)". Null or empty keys, and
        /// keys that don't appear in the expression, are ignored. Preconditions: every key in
        /// <paramref name="bindings"/> begins with '$', and every value is finite; this method
        /// does not check, and a NaN or infinity would be emitted as text Source cannot parse.
        /// </summary>
        public static string Apply(string expression, IDictionary<string, double> bindings)
        {
            var text = StripComments(expression);
            if (bindings == null || bindings.Count == 0) return text;
            foreach (var kv in bindings)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;

                // Not preceded by a word char, '.', or '$'; not followed by a word char or '.'.
                var pattern = @"(?<![\w.$])" + Regex.Escape(kv.Key) + @"(?![\w.])";
                var literal = kv.Value.ToString("R", CultureInfo.InvariantCulture);
                // Parenthesise from the formatted text, not kv.Value < 0: -0.0 formats as "-0"
                // but is not < 0, so checking the sign of the double would miss it.
                // Indexer, not StartsWith(char): that overload doesn't exist on .NET Framework
                // 4.8 / C# 7.3, which the legacy_ci port (Task 6) compiles against. "R" output
                // is never empty, so literal[0] is safe.
                if (literal[0] == '-') literal = "(" + literal + ")";
                text = Regex.Replace(text, pattern, literal, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return text;
        }
    }
}
