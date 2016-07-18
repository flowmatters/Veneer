using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem.Api.DataSources;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class MultipleTimeSeries : TimeSeriesResponse
    {
        [DataMember] public TimeSeriesReponseMeta[] TimeSeries;

        public MultipleTimeSeries(TimeSeriesFullSummary tsMeta)
        {
            TimeSeries = new[] {tsMeta};
        }

        public MultipleTimeSeries(Tuple<TimeSeriesLink, TimeSeries>[] src,bool includeValues = true)
        {
            if(includeValues)
                TimeSeries = src.Select(item => new SlimTimeSeries(item.Item1,item.Item2)).ToArray();
            else
                TimeSeries = src.Select(item => new TimeSeriesFullSummary(item.Item1, item.Item2)).ToArray();
        }
    }
}
