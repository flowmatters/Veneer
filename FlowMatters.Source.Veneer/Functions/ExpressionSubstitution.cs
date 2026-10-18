using System;
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
        private static readonly Regex BlockComment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex LineComment = new Regex(@"//[^\n]*", RegexOptions.Compiled);

        public static string StripComments(string expression) =>
            LineComment.Replace(BlockComment.Replace(expression ?? string.Empty, " "), " ");

        /// <summary>Whole-token, case-insensitive replacement; unknown keys are ignored.</summary>
        public static string Apply(string expression, IDictionary<string, double> bindings)
        {
            var text = StripComments(expression);
            if (bindings == null || bindings.Count == 0) return text;
            foreach (var kv in bindings)
            {
                // Not preceded by a word char, '.', or '$'; not followed by a word char or '.'.
                var pattern = @"(?<![\w.$])" + Regex.Escape(kv.Key) + @"(?![\w.])";
                var literal = kv.Value.ToString("R", CultureInfo.InvariantCulture);
                if (kv.Value < 0) literal = "(" + literal + ")";
                text = Regex.Replace(text, pattern, literal, RegexOptions.IgnoreCase);
            }
            return text;
        }
    }
}
