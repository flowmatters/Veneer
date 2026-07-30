using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonCommandLine
    {
        private static readonly char[] WhitespaceOrQuote = { ' ', '\t', '"' };
        private static readonly char[] CmdMetacharacters = { '&', '|', '<', '>', '^', '(', ')' };

        public static bool IsBatch(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        public static string QuoteArgument(string arg)
        {
            arg = arg ?? string.Empty;
            if (arg.Length > 0 && arg.IndexOfAny(WhitespaceOrQuote) < 0)
                return arg;
            return Quote(arg);
        }

        /// <summary>
        /// As QuoteArgument, but also quotes shell metacharacters. Required on the
        /// cmd /C path, where an unquoted &amp; or | truncates the argument and
        /// executes the tail as a command.
        /// </summary>
        public static string QuoteArgumentForCmd(string arg)
        {
            arg = arg ?? string.Empty;
            if (arg.Length > 0
                && arg.IndexOfAny(WhitespaceOrQuote) < 0
                && arg.IndexOfAny(CmdMetacharacters) < 0)
                return arg;
            return Quote(arg);
        }

        public static void Compose(string path, IEnumerable<string> args,
                                   out string fileName, out string arguments)
        {
            var list = (args ?? Enumerable.Empty<string>()).ToList();

            if (IsBatch(path))
            {
                fileName = "cmd.exe";
                // The path is ALWAYS quoted, even when it contains no whitespace.
                // The doubled-outer-quote trick relies on there being three or more
                // quotes present, and an unquoted path would make the composed line
                // depend on whether the path happened to contain a space.
                var parts = new List<string> { Quote(path) };
                parts.AddRange(list.Select(QuoteArgumentForCmd));
                arguments = "/D /V:OFF /C \"" + string.Join(" ", parts) + "\"";
            }
            else
            {
                fileName = path;
                arguments = string.Join(" ", list.Select(QuoteArgument));
            }
        }

        private static string Quote(string arg)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    backslashes = 0;
                    sb.Append('"');
                    continue;
                }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
