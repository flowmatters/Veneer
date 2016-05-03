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
        [DataMember] public SlimTimeSeries[] TimeSeries;

        public MultipleTimeSeries(Tuple<TimeSeriesLink, TimeSeries>[] src)
        {
            TimeSeries = src.Select(item => new SlimTimeSeries(item.Item1.TimeSeriesUrl,item.Item2)).ToArray();
        }
    }
}
