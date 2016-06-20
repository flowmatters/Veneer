using System.Runtime.Serialization;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class TimeSeriesEvent
    {
        [DataMember] public string Date;
        [DataMember] public double Value;
    }
}