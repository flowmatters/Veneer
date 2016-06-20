using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class NumericResponse : VeneerResponse
    {
        [DataMember] public double Value;
    }
}
