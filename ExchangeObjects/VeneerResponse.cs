using System.Runtime.Serialization;
using FlowMatters.Source.WebServer.ExchangeObjects;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    [KnownType(typeof(SimpleTimeSeries))]
    [KnownType(typeof(StringResponse))]
    [KnownType(typeof(NumericResponse))]
    [KnownType(typeof(SimplePiecewise))]
    [KnownType(typeof(BooleanResponse))]
    public class VeneerResponse
    {
    }
}
