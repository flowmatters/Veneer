using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using TIME.DataTypes;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class SimpleTimeSeries
    {
        public SimpleTimeSeries() { }

        public SimpleTimeSeries(TimeSeries source)
        {
            Name = source.name;
            Units = source.units.ShortName;
            NoDataValue = source.NullValue;
            Min = source.Min;
            Max = source.Max;
            Mean = source.average();
            Sum = source.total();
            StartDate = source.Start.ToString(CultureInfo.InvariantCulture);
            EndDate = source.End.ToString(CultureInfo.InvariantCulture);

            IList<TimeSeriesEvent> eventList = new List<TimeSeriesEvent>();
            for (int i = 0; i < source.count(); i++)
                eventList.Add(new TimeSeriesEvent
                    {
                        Date = source.timeForItem(i).ToString(CultureInfo.InvariantCulture),
                        Value = source[i]
                    });

            Events = eventList.ToArray();
        }

        public DateTime AsDate(string text)
        {
            return DateTime.Parse(text, CultureInfo.InvariantCulture.DateTimeFormat);
        }

        [DataMember]
        public string Name;

        [DataMember]
        public string Units;

        [DataMember] public string StartDate, EndDate;

        [DataMember] public double NoDataValue;

        [DataMember] public double Min, Max, Mean, Sum;

        [DataMember] public TimeSeriesEvent[] Events;
    }
}