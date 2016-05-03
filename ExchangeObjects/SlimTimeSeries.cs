using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class SlimTimeSeries : TimeSeriesReponseMeta
    {
        public SlimTimeSeries(string url, TimeSeries source) : base(source)
        {
            SingleURL = url;
            Values = source.ToArray();
        }

        [DataMember]
        public string SingleURL;

        [DataMember]
        public double[] Values;
    }
}
