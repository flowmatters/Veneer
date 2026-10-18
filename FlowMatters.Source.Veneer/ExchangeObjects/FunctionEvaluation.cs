using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    /// <summary>
    /// One token -> literal. `bindings` is a LIST of these, never a Dictionary or an ISerializable
    /// flat object: the request path is CoreWCF WebHttpBinding with the default
    /// DataContractJsonSerializer (SourceRESTfulService.cs:116-140), which reads a Dictionary
    /// member as [{"Key":…,"Value":…}] and silently DROPS object members whose name is not a legal
    /// XML name — every `$`-prefixed token (verified with a .NET 8 probe during planning). A list
    /// of DataContract pairs deserialises exactly and ports to classic WCF unchanged.
    /// </summary>
    [DataContract] public class EvaluateBinding
    {
        [DataMember(Name = "token")] public string Token { get; set; }
        [DataMember(Name = "value")] public double Value { get; set; }
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
        /// substitution matches tokens case-insensitively: two bindings differing only in case are
        /// the same token, and the LAST one wins (so the rule holds whichever the regex meets first).
        /// Empty/null tokens are dropped.
        /// </summary>
        public Dictionary<string, double> BindingsByToken()
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Bindings ?? new List<EvaluateBinding>())
                if (!string.IsNullOrEmpty(b.Token)) d[b.Token] = b.Value;
            return d;
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
