using System;
using System.Collections.Generic;
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.Veneer.ExchangeObjects;

namespace FlowMatters.Source.Veneer.Functions
{
    /// <summary>
    /// The request-level checks for POST /functions/evaluate — the conditions that answer 400 for
    /// the whole batch rather than an error on one item. Pure and Source-free so it is
    /// unit-testable. Everything else (bindings, context, parse, evaluation) is a per-item error
    /// that never fails the batch.
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

            foreach (var item in items)
            {
                if (item == null)
                {
                    problem = "items must not contain null";
                    return null;
                }
                if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Expression))
                {
                    problem = "every item needs an id and an expression";
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

        private static bool TryParseDate(string text, out DateTime date, out string reason)
        {
            date = default(DateTime);
            reason = null;
            if (string.IsNullOrEmpty(text))
            {
                reason = "missing";
                return false;
            }
            try
            {
                date = TransientFunctionEvaluator.ParseIsoDate(text);
                return true;
            }
            catch (FormatException e)
            {
                reason = e.Message;
                return false;
            }
        }
    }
}
