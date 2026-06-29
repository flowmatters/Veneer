using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using RiverSystem.DataManagement.DataManager;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.ExchangeObjects.DataSources
{
    [DataContract]
    public class SimpleDataDetails
    {
        public SimpleDataDetails()
        {
            
        }

        public SimpleDataDetails(DataDetailsItem item,bool summary=true,bool slim=false)
        {

            TimeSeries = summary
                ? new TimeSeriesReponseMeta(item.Data.TimeSeries)
                : new SimpleTimeSeries(item.Data.TimeSeries);

            if(slim && !summary)
                TimeSeries = new SlimTimeSeries(TimeSeries);

            _originalTimeSeries = item.Data.TimeSeries;
            Name = (item.DataInformation==null)?item.Data.TimeSeries.name : item.DataInformation.Name;
        }

        private TimeSeries _originalTimeSeries;
        [DataMember] public TimeSeriesReponseMeta TimeSeries;
        [DataMember] public string Name;

        public void Expand()
        {
            TimeSeries = new SimpleTimeSeries(_originalTimeSeries);
        }
    }
}
