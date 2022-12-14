#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5
#define BEFORE_RECORDING_ATTRIBUTES_REFACTOR
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using TIME.DataTypes;
using TIME.ScenarioManagement;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class RunSummary
    {
        public RunSummary(Run r)
        {
            DateRun = r.DateRun.ToString(CultureInfo.InvariantCulture);
            Name = r.Name;
            Scenario = r.Scenario.Name;
            Number = r.RunNumber;
            Results = BuildResultsArray(r.RunParameters.GetRowsForScenario(r.Scenario.id).ToArray());
            Status = r.RunResultIndicator.ToString();
            RunLog = new string[0];
        }

        private TimeSeriesLink[] BuildResultsArray(ProjectViewRow[] rows)
        {
            List<TimeSeriesLink> result = new List<TimeSeriesLink>();
            foreach (ProjectViewRow row in rows)
            {
#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
                                Dictionary<AttributeRecordingState, TimeSeries> rowResults = row.ElementRecorder.GetResultList();
#else
                var rowResults = row.ElementRecorder.GetResultsLookup();
#endif
                //try
                //{
                foreach (var key in rowResults.Keys)
                {
                    TimeSeries ts = rowResults[key];
                    if (ts != null)
                        result.Add(BuildLink(ts, row, key, Number));
                }
                //}
                //catch (ArgumentNullException) { }
            }

            return result.ToArray();
        }

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
        private static string SelectRecordingVariable(AttributeRecordingState key, ProjectViewRow row)
        {
            return (key.KeyString == "") ? row.ElementName : key.KeyString;
        }
#else
        private static string SelectRecordingVariable(RecordableItem key, ProjectViewRow row)
        {
            var recordableItemDisplayString = RecordableItemTransitionUtil.GetLegacyKeyString(key);
            return (recordableItemDisplayString == "") ? row.ElementName : recordableItemDisplayString;
        }
#endif

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
        public static TimeSeriesLink BuildLink(TimeSeries ts, ProjectViewRow row, AttributeRecordingState key, int runNumber)
#else
        public static TimeSeriesLink BuildLink(TimeSeries ts, ProjectViewRow row, RecordableItem key, int runNumber)
#endif
        {
            var result = new TimeSeriesLink
            {
                TimeSeriesName = ts.name,
                RunNumber = runNumber,
                TimeSeriesUrl = BuildTimeSeriesUrl(row,key, runNumber),
                NetworkElement = row.NetworkElementName,
                RecordingElement = row.ElementName,
                RecordingVariable = SelectRecordingVariable(key, row)
            };

            if (row.NetworkElementTypeInstance == ProjectViewRow.NetworkElementType.Catchment)
            {
                result.FunctionalUnit = row.WaterFeatureType;
            }
            return result;
        }

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
        public static string BuildTimeSeriesUrl(ProjectViewRow row, AttributeRecordingState key, int runNumber)
#else
        public static string BuildTimeSeriesUrl(ProjectViewRow row, RecordableItem key, int runNumber)
#endif
        {
            string networkElementSuffix = "";
            if (row.NetworkElementTypeInstance == ProjectViewRow.NetworkElementType.Catchment)
            {
                networkElementSuffix = UriTemplates.NETWORK_ELEMENT_FU_DELIMITER + row.WaterFeatureType;
            }
            return string.Format(UriTemplates.TimeSeriesBase.Replace("{runId}", "{0}").Replace("{networkElement}", "{1}").Replace("{recordingElement}","{2}").Replace("{variable}", "{3}"), 
                runNumber, SourceService.URLSafeString(row.NetworkElementName+networkElementSuffix), SourceService.URLSafeString(row.ElementName), SourceService.URLSafeString(SelectRecordingVariable(key,row)));
        }

        [DataMember]
        public string DateRun;

        [DataMember]
        public string Name;

        [DataMember]
        public string Scenario;

        [DataMember]
        public int Number;

        [DataMember]
        public TimeSeriesLink[] Results;

        [DataMember]
        public string Status;

        [DataMember] public string[] RunLog;
    }
}