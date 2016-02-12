using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class StringResponse : VeneerResponse
    {
        [DataMember] public string Value;
    }
}
