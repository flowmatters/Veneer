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
        /// The Current Recorder Tree's name in Source 5.16 and later
        /// (CurrentTreeRecorderSet.CurrentRecorderTreeName), a constant that is not localised.
        /// </summary>
        public const string CURRENT_TREE_NAME = "Current Recorder Tree";

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
        /// The index in <paramref name="available"/> of each set to select, in the requested order
        /// with repeats dropped (the first occurrence is kept).
        /// </summary>
        /// <param name="available">The manager's set names in its order, or null when the scenario
        /// has no recorder set manager.</param>
        /// <param name="treeIndex">The index in <paramref name="available"/> of the manager's
        /// CurrentTree, found by reference; -1 when it is not there.</param>
        /// <param name="requested">The names asked for.</param>
        /// <remarks>
        /// <see cref="CURRENT_TREE_NAME"/> resolves to <paramref name="treeIndex"/> before any name
        /// is matched, so a named set that shares the tree's name does not make it ambiguous. Every
        /// other name matches exactly (ordinal); one that matches no set, or more than one, is
        /// refused. An empty request resolves to [<paramref name="treeIndex"/>]: an empty selection
        /// and the tree alone select the same, and Source's getter adds the tree to an empty
        /// selection when it next reads it. Without a manager, only an empty request resolves, to
        /// an empty array.
        /// </remarks>
        public static int[] Resolve(IList<string> available, int treeIndex, IList<string> requested)
        {
            requested = requested ?? new string[0];
            if (available == null)
            {
                RequireEmpty(requested, "the scenario has no recorder sets");
                return new int[0];
            }

            if (requested.Count == 0)
                return treeIndex >= 0 ? new[] { treeIndex } : new int[0];

            var unknown = new List<string>();
            var ambiguous = new List<string>();
            var indices = new List<int>();
            foreach (var name in requested)
            {
                List<int> matches;
                if (treeIndex >= 0 && string.Equals(name, CURRENT_TREE_NAME, StringComparison.Ordinal))
                    matches = new List<int> { treeIndex };
                else
                    matches = Enumerable.Range(0, available.Count)
                        .Where(j => string.Equals(available[j], name, StringComparison.Ordinal))
                        .ToList();

                if (matches.Count == 0)
                {
                    if (!unknown.Contains(name)) unknown.Add(name);
                }
                else if (matches.Count > 1)
                {
                    if (!ambiguous.Contains(name)) ambiguous.Add(name);
                }
                else if (!indices.Contains(matches[0]))
                {
                    indices.Add(matches[0]);
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

            return indices.ToArray();
        }

        /// <summary>
        /// Refuses a non-empty request, naming its sets and <paramref name="reason"/>; an empty one
        /// passes.
        /// </summary>
        public static void RequireEmpty(IList<string> requested, string reason)
        {
            if (requested != null && requested.Count > 0)
                throw new InvalidRunParameterException(
                    $"{KEY} cannot select {Quote(requested.Distinct())}: {reason}.");
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
