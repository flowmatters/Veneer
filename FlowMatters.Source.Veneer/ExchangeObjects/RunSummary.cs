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
                Dictionary<AttributeRecordingState, TimeSeries> rowResults = row.ElementRecorder.GetResultList();
                foreach (var key in rowResults.Keys)
                {
                    TimeSeries ts = rowResults[key];
                    if(ts != null)
                        result.Add(BuildLink(ts, row, key, Number));
                }
            }

            return result.ToArray();
        }

        private static string SelectRecordingVariable(AttributeRecordingState key, ProjectViewRow row)
        {
            return (key.KeyString == "") ? row.ElementName : key.KeyString;
        }

        public static TimeSeriesLink BuildLink(TimeSeries ts, ProjectViewRow row, AttributeRecordingState key, int runNumber)
        {
            return new TimeSeriesLink
            {
                TimeSeriesName = ts.name,
                RunNumber = runNumber,
                TimeSeriesUrl = BuildTimeSeriesUrl(row,key, runNumber),
                NetworkElement = row.NetworkElementName,
                RecordingElement = row.ElementName,
                RecordingVariable = SelectRecordingVariable(key, row)
            };
        }

        public static string BuildTimeSeriesUrl(ProjectViewRow row, AttributeRecordingState key, int runNumber)
        {
            return string.Format(UriTemplates.TimeSeries.Replace("{runId}", "{0}").Replace("{networkElement}", "{1}").Replace("{recordingElement}","{2}").Replace("{variable}", "{3}"), 
                runNumber, SourceService.URLSafeString(row.NetworkElementName), SourceService.URLSafeString(row.ElementName), SourceService.URLSafeString(SelectRecordingVariable(key,row)));
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