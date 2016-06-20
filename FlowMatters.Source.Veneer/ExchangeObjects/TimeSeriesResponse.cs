using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer.ExchangeObjects;
using IronPython.Modules;
using RiverSystem.Reporting;
using TIME.DataTypes;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    [KnownType(typeof(TimeSeriesReponseMeta))]
    [KnownType(typeof(SlimTimeSeries))]
    [KnownType(typeof(SimpleTimeSeries))]
    [KnownType(typeof(MultipleTimeSeries))]
    public class TimeSeriesResponse : VeneerResponse
    {
        public DateTime AsDate(string text)
        {
            return DateTime.Parse(text, CultureInfo.InvariantCulture.DateTimeFormat);
        }
    }

    [DataContract]
    [KnownType(typeof(SlimTimeSeries))]
    [KnownType(typeof(SimpleTimeSeries))]
    public class TimeSeriesReponseMeta : TimeSeriesResponse
    {
        public TimeSeriesReponseMeta(TimeSeries source)
        {
            if (source == null)
            {
                Name = "No Data";
                return;
            }

            Name = source.name;
            Units = source.units.ShortName;
            NoDataValue = source.NullValue;
            Min = source.Min;
            Max = source.Max;
            Mean = source.average();
            Sum = source.total();
            StartDate = source.Start.ToString(CultureInfo.InvariantCulture);
            EndDate = source.End.ToString(CultureInfo.InvariantCulture);
            TimeStep = source.timeStep.Name;
        }

        [DataMember]
        public string Name;

        [DataMember]
        public string Units;

        [DataMember]
        public string StartDate, EndDate;

        [DataMember]
        public double NoDataValue;

        [DataMember]
        public double Min, Max, Mean, Sum;

        [DataMember]
        public string TimeStep;
    }
}
