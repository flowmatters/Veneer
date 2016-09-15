using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem.DataManagement.DataManager;

namespace FlowMatters.Source.Veneer.ExchangeObjects.DataSources
{
    [DataContract]
    public class SimpleDataDetails
    {
        public SimpleDataDetails(DataDetailsItem item,bool summary=true)
        {
            TimeSeries = summary
                ? new TimeSeriesReponseMeta(item.Data.TimeSeries)
                : new SimpleTimeSeries(item.Data.TimeSeries);
            Name = item.DataInformation.Name;
        }

        [DataMember] public TimeSeriesReponseMeta TimeSeries;
        [DataMember] public string Name;
    }
}
