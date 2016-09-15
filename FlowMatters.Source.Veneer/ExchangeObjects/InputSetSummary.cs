using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class InputSetSummary
    {
        [DataMember] public string URL;
        [DataMember] public string Name;
        [DataMember] public string[] Configuration;
    }
}