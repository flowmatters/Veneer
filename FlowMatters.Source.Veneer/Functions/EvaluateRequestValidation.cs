using System;
using System.Collections.Generic;
using System.Globalization;
using FlowMatters.Source.Veneer.ExchangeObjects;

namespace FlowMatters.Source.Veneer.Functions
{
    /// <summary>
    /// The request-level checks for POST /functions/evaluate — the conditions that answer 400 for
    /// the whole batch rather than an error on one item. Pure and Source-free so it is
    /// unit-testable. Everything else (bindings, context, parse, evaluation) is a per-item error
    /// that never fails the batch. Every per-item error names the item: by its zero-based index
    /// when it is null or lacks an id or an expression, otherwise (a bad date) by its id.
    /// Duplicate ids are accepted; results come back in request order, so a client can tell
    /// duplicates apart by position.
    /// </summary>
    public static class EvaluateRequestValidation
    {
        public sealed class ParsedItem
        {
            public EvaluateItem Item { get; set; }
            public DateTime Date { get; set; }
        }

        /// <summary>
        /// The items paired with their parsed dates, in request order; or null with
        /// <paramref name="problem"/> set when the request must be rejected. A null request or a
        /// null/empty item list is valid and yields an empty list.
        /// </summary>
        public static List<ParsedItem> Parse(EvaluateRequest request, out string problem)
        {
            problem = null;
            var items = request?.Items;
            var parsed = new List<ParsedItem>();
            if (items == null) return parsed;

            if (items.Count > EvaluateRequest.MAX_ITEMS)
            {
                problem = "too many items: " + items.Count + " > " + EvaluateRequest.MAX_ITEMS;
                return null;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    problem = "item[" + i + "] must not be null";
                    return null;
                }
                if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Expression))
                {
                    problem = "item[" + i + "] needs an id and an expression";
                    return null;
                }
                DateTime date;
                string reason;
                if (!TryParseDate(item.Date, out date, out reason))
                {
                    problem = "item " + item.Id + ": date must be yyyy-MM-dd (" + reason + ")";
                    return null;
                }
                parsed.Add(new ParsedItem { Item = item, Date = date });
            }
            return parsed;
        }

        /// <summary>Exactly yyyy-MM-dd, invariant culture; throws FormatException otherwise.</summary>
        public static DateTime ParseIsoDate(string text) =>
            DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);

        /// <summary>The reason is a fixed English string, never a (localised) exception message:
        /// "missing", "not yyyy-MM-dd" or "not a calendar date".</summary>
        private static bool TryParseDate(string text, out DateTime date, out string reason)
        {
            date = default(DateTime);
            reason = null;
            if (string.IsNullOrEmpty(text))
            {
                reason = "missing";
                return false;
            }
            if (!HasIsoDateShape(text))
            {
                reason = "not yyyy-MM-dd";
                return false;
            }
            try
            {
                date = ParseIsoDate(text);
                return true;
            }
            catch (FormatException)
            {
                // The right shape but no such day (e.g. 2025-02-30, month 13, year 0000).
                reason = "not a calendar date";
                return false;
            }
        }

        /// <summary>Four ASCII digits, '-', two digits, '-', two digits.</summary>
        private static bool HasIsoDateShape(string text)
        {
            if (text.Length != 10) return false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (i == 4 || i == 7)
                {
                    if (c != '-') return false;
                }
                else if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
