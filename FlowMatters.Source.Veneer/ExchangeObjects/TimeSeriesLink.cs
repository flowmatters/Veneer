using System.Runtime.Serialization;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class TimeSeriesLink
    {
        [DataMember]
        public int RunNumber;

        [DataMember]
        public string TimeSeriesName;

        [DataMember]
        public string TimeSeriesUrl;

        [DataMember] public string NetworkElement, RecordingElement, RecordingVariable;

        [DataMember] public string FunctionalUnit;
    }
}