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
    public class SlimTimeSeries : TimeSeriesFullSummary
    {
        public SlimTimeSeries(TimeSeriesLink link, TimeSeries source) : base(link,source)
        {
            Values = source.ToArray();
        }

        public SlimTimeSeries(TimeSeriesReponseMeta source) : base(source)
        {
            if (source is SlimTimeSeries)
            {
                var slim = source as SlimTimeSeries;
                Values = (double[]) slim.Values.Clone();
            } else if (source is SimpleTimeSeries)
            {
                var simple = source as SimpleTimeSeries;
                Values = simple.Events.Select(evt => evt.Value).ToArray();
            }
        }

        [DataMember]
        public double[] Values;
    }
}
