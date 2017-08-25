using System.Runtime.Serialization;
using FlowMatters.Source.WebServer.ExchangeObjects;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    [KnownType(typeof(TimeSeriesReponseMeta))]
    [KnownType(typeof(SlimTimeSeries))]
    [KnownType(typeof(SimpleTimeSeries))]
    [KnownType(typeof(MultipleTimeSeries))]
    [KnownType(typeof(StringResponse))]
    [KnownType(typeof(NumericResponse))]
    [KnownType(typeof(SimplePiecewise))]
    [KnownType(typeof(BooleanResponse))]
    [KnownType(typeof(ListResponse))]
    [KnownType(typeof(GeoJSONCoverage))]
    [KnownType(typeof(DictResponse))]
    [KnownType(typeof(KeyValueResponse))]
    public class VeneerResponse
    {
    }
}
