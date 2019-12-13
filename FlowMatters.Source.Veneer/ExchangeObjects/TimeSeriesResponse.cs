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
    [KnownType(typeof(TimeSeriesFullSummary))]
    [KnownType(typeof(SlimTimeSeries))]
    [KnownType(typeof(SimpleTimeSeries))]
    public class TimeSeriesReponseMeta : TimeSeriesResponse
    {
        public TimeSeriesReponseMeta()
        {
            
        }

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
            refTimeSeries = source;
        }

        public TimeSeriesReponseMeta(TimeSeriesReponseMeta source)
        {
            Name = source.Name;
            Units = source.Units;
            NoDataValue = source.NoDataValue;
            Min = source.Min;
            Max = source.Max;
            Mean = source.Mean;
            Sum = source.Sum;
            StartDate = source.StartDate;
            EndDate = source.EndDate;
            TimeStep = source.TimeStep;
            refTimeSeries = source.refTimeSeries;
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

        private TimeSeries refTimeSeries;

        public string DateForTimeStep(int i)
        {
            return refTimeSeries.timeForItem(i).ToString(CultureInfo.InvariantCulture);
        }
    }

    [DataContract]
    [KnownType(typeof (SlimTimeSeries))]
    public class TimeSeriesFullSummary : TimeSeriesReponseMeta
    {
        [DataMember]
        public int RunNumber;

        [DataMember]
        public string SingleURL;

        [DataMember]
        public string NetworkElement;

        [DataMember]
        public string RecordingElement;

        [DataMember]
        public string RecordingVariable;

        [DataMember]
        public string FunctionalUnit;

        public TimeSeriesFullSummary(TimeSeriesLink link, TimeSeries source) : base(source)
        {
            RunNumber = link.RunNumber;
            SingleURL = link.TimeSeriesUrl;
            NetworkElement = link.NetworkElement;
            RecordingElement = link.RecordingElement;
            RecordingVariable = link.RecordingVariable;
            FunctionalUnit = link.FunctionalUnit;
        }

        public TimeSeriesFullSummary(TimeSeriesReponseMeta source) : base(source)
        {
            if (source is TimeSeriesFullSummary)
            {
                var full = source as TimeSeriesFullSummary;
                RunNumber = full.RunNumber;
                SingleURL = full.SingleURL;
                NetworkElement = full.NetworkElement;
                RecordingElement = full.RecordingElement;
                RecordingVariable = full.RecordingVariable;
                FunctionalUnit = full.FunctionalUnit;
            }
        }
    }

}
