using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem.Controls.Converters;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class SlimTimeSeries : TimeSeriesReponseMeta
    {
        public SlimTimeSeries(TimeSeriesLink link, TimeSeries source) : base(source)
        {
            RunNumber = link.RunNumber;
            SingleURL = link.TimeSeriesUrl;
            NetworkElement = link.NetworkElement;
            RecordingElement = link.RecordingElement;
            RecordingVariable = link.RecordingVariable;
            Values = source.ToArray();
        }

        [DataMember]
        public int RunNumber;

        [DataMember]
        public string SingleURL;

        [DataMember]
        public double[] Values;

        [DataMember]
        public string NetworkElement, RecordingElement, RecordingVariable;
    }
}
