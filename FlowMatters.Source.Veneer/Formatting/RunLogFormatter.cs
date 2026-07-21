using System;
using System.Globalization;
using TIME.Management;

namespace FlowMatters.Source.Veneer.Formatting
{
    /// <summary>
    /// Captured simulation log for a single Veneer-triggered run: the formatted log lines plus
    /// the last non-null stack trace seen during the run (null when no entry carried one).
    /// </summary>
    public class CapturedRunLog
    {
        public string[] Messages;
        public string LastStackTrace;
    }

    /// <summary>
    /// Formats a TIME <see cref="LogEntry"/> into a single human-readable log line carrying the
    /// log level and, when present, the simulation timestep.
    /// </summary>
    public static class RunLogFormatter
    {
        /// <summary>
        /// Format a log entry as "[Level] timestep &lt;sim-time&gt;: message". The
        /// "timestep &lt;sim-time&gt;" segment is omitted entirely when the entry has no timestep.
        /// The word "timestep" labels the value as simulation (model) time so it is not mistaken
        /// for a wall-clock log timestamp.
        /// </summary>
        public static string Format(LogEntry entry)
        {
            return FormatLine(entry?.Type.ToString(), entry?.TimeStep, entry?.Message);
        }

        /// <summary>
        /// Pure formatting logic over primitives, isolated from the TIME types so the branching
        /// (level brackets, optional timestep, smart date/datetime granularity) is easy to reason
        /// about and to test.
        /// </summary>
        internal static string FormatLine(string level, DateTime? timeStep, string message)
        {
            var levelPart = string.IsNullOrEmpty(level) ? "" : "[" + level + "] ";
            var timeStepPart = timeStep.HasValue ? "timestep " + FormatTimeStep(timeStep.Value) + ": " : "";
            return levelPart + timeStepPart + (message ?? "");
        }

        // Smart granularity: date only at midnight, full datetime otherwise (preserves sub-daily steps).
        private static string FormatTimeStep(DateTime timeStep)
        {
            return timeStep.TimeOfDay == TimeSpan.Zero
                ? timeStep.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : timeStep.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
