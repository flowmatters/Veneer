using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonScript
    {
        public const string Guard = "if errorlevel 1 exit %errorlevel%";
        public const string Terminator = "exit 0";
        public const string EchoOff = "@echo off";

        public static string Marker(string nonce, int step)
        {
            return "##VENEER:" + nonce + ":" + step;
        }

        /// <summary>
        /// @echo off suppresses the prompt (setting PROMPT empty does not, and /Q
        /// is inert for a stdin-fed session). The guard after every line gives
        /// stop-on-first-failure inside cmd, with no round-tripping to .NET.
        /// Per-line %errorlevel% expansion is correct because each line is parsed
        /// as it is read, after the previous line has run.
        /// </summary>
        public static IEnumerable<string> Generate(IEnumerable<string> lines, string nonce)
        {
            yield return EchoOff;
            int step = 0;
            foreach (var line in lines)
            {
                step++;
                yield return "echo " + Marker(nonce, step);
                yield return line;
                yield return Guard;
            }
            yield return Terminator;
        }
    }

    /// <summary>
    /// Strips Veneer's own scaffolding from cmd's stdout. The command echo is
    /// deliberately kept -- it cannot be reliably stripped (an echoed input line
    /// is byte-identical to a program printing the same text) and reads usefully
    /// as a transcript. Note the echo is pre-expansion: it shows
    /// "cd %VENEER_PROJECT_DIR%", not the resolved path.
    /// </summary>
    internal sealed class ScriptOutputFilter
    {
        private readonly Regex _markerEcho;
        private readonly Regex _markerOutput;
        private readonly List<string> _preSentinel = new List<string>();
        private bool _sentinelSeen;

        public int CurrentStep { get; private set; }

        public ScriptOutputFilter(string nonce)
        {
            var n = Regex.Escape(nonce);
            _markerEcho = new Regex(@"^echo ##VENEER:" + n + @":(\d+)$");
            _markerOutput = new Regex(@"^##VENEER:" + n + @":(\d+)$");
        }

        /// <summary>
        /// Deliberately eager rather than a yield iterator. State transitions
        /// (sentinel detection, pre-sentinel buffering, CurrentStep) must happen
        /// on the call itself: a caller that invoked this and ignored the result
        /// would otherwise apply no transition at all, so the sentinel would never
        /// be seen and every line would end up flushed as though cmd had failed.
        /// </summary>
        public IEnumerable<string> Accept(string rawLine)
        {
            var none = new string[0];
            var line = (rawLine ?? string.Empty).TrimEnd('\r', '\n');

            if (!_sentinelSeen)
            {
                if (line.Contains(">" + AddonScript.EchoOff))
                {
                    _sentinelSeen = true;
                    _preSentinel.Clear();
                }
                else
                {
                    _preSentinel.Add(line);
                }
                return none;
            }

            if (line == AddonScript.Guard) return none;
            if (line == AddonScript.Terminator) return none;
            if (_markerEcho.IsMatch(line)) return none;

            var output = _markerOutput.Match(line);
            if (output.Success)
            {
                CurrentStep = int.Parse(output.Groups[1].Value);
                return none;
            }

            return new[] { line };
        }

        /// <summary>
        /// If the sentinel never arrived, cmd failed pathologically -- surface
        /// what it did say rather than discarding the whole stream.
        /// </summary>
        public IEnumerable<string> Flush()
        {
            if (_sentinelSeen) return new string[0];
            var buffered = _preSentinel.ToArray();
            _preSentinel.Clear();
            return buffered;
        }
    }
}
