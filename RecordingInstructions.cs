using System.Runtime.Serialization;
using FlowMatters.Source.WebServer.ExchangeObjects;

namespace FlowMatters.Source.WebServer
{
    [DataContract]
    public class RecordingInstructions
    {
        [DataMember] public string[] RecordNone;
        [DataMember] public string[] RecordAll;

        public TimeSeriesLink Parse(string partialURL)
        {
            string[] bits = partialURL.Split('/');
            return new TimeSeriesLink
            {
                NetworkElement = bits[1],
                RecordingElement = bits[3],
                RecordingVariable = bits[5]
            };
        }
    }
}