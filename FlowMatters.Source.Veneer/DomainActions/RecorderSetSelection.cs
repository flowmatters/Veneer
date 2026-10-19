using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// A run parameter whose value cannot be applied. Thrown before the configuration is changed;
    /// TriggerRun answers it with 400 and the exception's message.
    /// </summary>
    public class InvalidRunParameterException : Exception
    {
        public InvalidRunParameterException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Validation and name matching for the SelectedRecorderSets run parameter, over the
    /// deserialised value and the recorder set names only. Every method throws
    /// <see cref="InvalidRunParameterException"/> on a bad request.
    /// </summary>
    internal static class RecorderSetSelection
    {
        public const string KEY = "SelectedRecorderSets";

        /// <summary>
        /// The requested names, in the caller's order. The request deserialiser delivers a JSON
        /// array as object[] and a JSON string as string; anything other than an object[] whose
        /// elements are all non-null strings is refused.
        /// </summary>
        public static string[] ParseNames(object value)
        {
            var items = value as object[];
            if (items == null)
                throw new InvalidRunParameterException(
                    $"{KEY} must be a JSON array of recorder set names, not {Describe(value)}.");

            var names = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var name = items[i] as string;
                if (name == null)
                    throw new InvalidRunParameterException(
                        $"{KEY}[{i}] must be a recorder set name (a string), not {Describe(items[i])}.");
                names[i] = name;
            }
            return names;
        }

        /// <summary>
        /// The index in <paramref name="available"/> of each requested name, in the requested
        /// order. <paramref name="available"/> is the manager's set names in its order, or null
        /// when the scenario has no recorder set manager. An empty request resolves to an empty
        /// array whether or not there is a manager. Names match exactly (ordinal); a name that
        /// matches no set, or more than one, is refused.
        /// </summary>
        public static int[] Resolve(IList<string> available, IList<string> requested)
        {
            if (requested == null || requested.Count == 0)
                return new int[0];

            if (available == null)
                throw new InvalidRunParameterException(
                    $"{KEY} cannot select {Quote(requested.Distinct())}: the scenario has no recorder sets.");

            var unknown = new List<string>();
            var ambiguous = new List<string>();
            var indices = new int[requested.Count];
            for (int i = 0; i < requested.Count; i++)
            {
                var matches = Enumerable.Range(0, available.Count)
                    .Where(j => string.Equals(available[j], requested[i], StringComparison.Ordinal))
                    .ToList();
                if (matches.Count == 0)
                {
                    if (!unknown.Contains(requested[i])) unknown.Add(requested[i]);
                }
                else if (matches.Count > 1)
                {
                    if (!ambiguous.Contains(requested[i])) ambiguous.Add(requested[i]);
                }
                else
                {
                    indices[i] = matches[0];
                }
            }

            if (unknown.Count > 0)
                throw new InvalidRunParameterException(
                    $"{KEY} names {(unknown.Count == 1 ? "an unknown recorder set" : "unknown recorder sets")}: " +
                    $"{Quote(unknown)}. Available recorder sets: {Available(available)}.");

            if (ambiguous.Count > 0)
                throw new InvalidRunParameterException(
                    $"{KEY} names {(ambiguous.Count == 1 ? "a recorder set name" : "recorder set names")} " +
                    $"shared by more than one set: {Quote(ambiguous)}. Available recorder sets: {Available(available)}.");

            return indices;
        }

        private static string Available(IList<string> available) =>
            available.Count == 0 ? "(none)" : Quote(available);

        private static string Quote(IEnumerable<string> names) =>
            string.Join(", ", names.Select(name => "'" + name + "'"));

        private static string Describe(object value)
        {
            if (value == null) return "null";
            if (value is string) return "a string";
            if (value is object[]) return "an array";
            if (value is bool) return "a boolean";
            if (value is int || value is long || value is double || value is decimal || value is float)
                return "a number";
            if (value.GetType() == typeof(object)) return "an object";
            return "a " + value.GetType().Name;
        }
    }
}
