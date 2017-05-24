#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3
#define BeforeCaseRefactor
#endif

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
#if BeforeCaseRefactor
            for (int i = 0; i < source.count(); i++)
#else
            for (int i = 0; i < source.Count; i++)
#endif
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