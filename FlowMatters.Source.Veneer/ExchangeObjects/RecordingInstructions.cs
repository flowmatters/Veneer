using System;
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
            var result = new TimeSeriesLink
            {
                NetworkElement = bits[1],
                RecordingElement = bits[3],
                RecordingVariable = bits[5]
            };

            bool haveFU = UriTemplates.TryExtractFunctionalUnit(result.NetworkElement, out result.NetworkElement,
                out result.FunctionalUnit);
            if (!haveFU && (bits.Length >= 8))
            {
                result.FunctionalUnit = bits[7];
            }
            return result;
        }
    }
}