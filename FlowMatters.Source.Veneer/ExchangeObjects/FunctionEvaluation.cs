using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    /// <summary>
    /// One token -> literal. `bindings` is a LIST of these, never a Dictionary or an ISerializable
    /// flat object: the request path is the WebHttpBinding endpoint set up in
    /// SourceRESTfulService (no custom request formatter for this operation in
    /// ReplyFormatSwitchBehaviour), which uses the default DataContractJsonSerializer; that
    /// serializer reads a Dictionary member as [{"Key":…,"Value":…}] and silently DROPS object
    /// members whose name is not a legal XML name — every `$`-prefixed token (verified with a
    /// .NET 8 probe during planning). A list of DataContract pairs deserialises exactly and ports
    /// to classic WCF unchanged.
    /// </summary>
    [DataContract] public class EvaluateBinding
    {
        [DataMember(Name = "token", IsRequired = true)] public string Token { get; set; }
        [DataMember(Name = "value", IsRequired = true)] public double Value { get; set; }
    }

    [DataContract] public class EvaluateItem
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "date")] public string Date { get; set; }               // ISO yyyy-MM-dd
        [DataMember(Name = "context", EmitDefaultValue = false)] public string Context { get; set; }
        [DataMember(Name = "expression")] public string Expression { get; set; }
        [DataMember(Name = "bindings", EmitDefaultValue = false)] public List<EvaluateBinding> Bindings { get; set; }

        /// <summary>
        /// The bindings as ExpressionSubstitution wants them. Case-insensitive, because
        /// substitution matches tokens case-insensitively: case-only duplicates collapse to one
        /// entry, so Apply substitutes a single value. Empty/null tokens are dropped, and a null
        /// element in the list (e.g. a "bindings":[null] body) is skipped rather than throwing.
        /// Does NOT validate that tokens start with '$' or that values are finite — the evaluator
        /// (TransientFunctionEvaluator) checks both and turns failures into item errors.
        /// </summary>
        public Dictionary<string, double> BindingsByToken()
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Bindings ?? new List<EvaluateBinding>())
                if (b != null && !string.IsNullOrEmpty(b.Token)) d[b.Token] = b.Value;
            return d;
        }

        /// <summary>
        /// The first reason <paramref name="bindings"/> (as from BindingsByToken) cannot be
        /// substituted, or null when they all can: every token must begin with '$' and every value
        /// must be finite — ExpressionSubstitution.Apply's preconditions, which it does not check.
        /// </summary>
        public static string BindingError(IDictionary<string, double> bindings)
        {
            if (bindings == null) return null;
            foreach (var kv in bindings)
            {
                if (kv.Key == null || !kv.Key.StartsWith("$", StringComparison.Ordinal))
                    return "binding token must start with '$': " + kv.Key;
                if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value))
                    return "binding " + kv.Key + ": non-finite value";
            }
            return null;
        }
    }

    [DataContract] public class EvaluateRequest
    {
        public const int MAX_ITEMS = 2000;
        [DataMember(Name = "items")] public List<EvaluateItem> Items { get; set; }
    }

    [DataContract] public class EvaluateResultItem
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "value", EmitDefaultValue = false)] public double? Value { get; set; }
        [DataMember(Name = "error", EmitDefaultValue = false)] public string Error { get; set; }
    }

    [DataContract] public class EvaluateResponse : VeneerResponse
    {
        [DataMember(Name = "results")] public List<EvaluateResultItem> Results { get; set; } = new List<EvaluateResultItem>();
    }
}
