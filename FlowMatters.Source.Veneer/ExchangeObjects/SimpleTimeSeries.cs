using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using FlowMatters.Source.Veneer.ExchangeObjects;
using TIME.DataTypes;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class SimpleTimeSeries : TimeSeriesReponseMeta
    {
        public SimpleTimeSeries(TimeSeries source):base(source)
        {
            if (source == null)
            {
                Events = new TimeSeriesEvent[0];
                return;
            }

            IList<TimeSeriesEvent> eventList = new List<TimeSeriesEvent>();
            for (int i = 0; i < source.Count; i++)
                eventList.Add(new TimeSeriesEvent
                    {
                        Date = source.timeForItem(i).ToString(CultureInfo.InvariantCulture),
                        Value = source[i]
                    });

            Events = eventList.ToArray();
        }

        [DataMember] public TimeSeriesEvent[] Events;

        public SlimTimeSeries Slim()
        {
            return new SlimTimeSeries(this);
        }
    }
}