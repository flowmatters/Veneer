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
            Number = r.RunNumber;
            Results = BuildResultsArray(r.RunParameters.GetRowsForScenario(r.Scenario.id).ToArray());
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
                        result.Add(new TimeSeriesLink
                            {
                                TimeSeriesName = ts.name,
                                TimeSeriesUrl = BuildTimeSeriesUrl(row,key),
                                NetworkElement = row.NetworkElementName,
                                RecordingElement = row.ElementName,
                                RecordingVariable = key.KeyString
                            });
                }
            }

            return result.ToArray();
        }

        private string BuildTimeSeriesUrl(ProjectViewRow row, AttributeRecordingState key)
        {
            return string.Format(UriTemplates.TimeSeries.Replace("{runId}", "{0}").Replace("{networkElement}", "{1}").Replace("{recordingElement}","{2}").Replace("{variable}", "{3}"), 
                Number, SourceService.URLSafeString(row.NetworkElementName), SourceService.URLSafeString(row.ElementName), SourceService.URLSafeString(key.KeyString));
        }

        [DataMember]
        public string DateRun;

        [DataMember]
        public string Name;

        [DataMember]
        public int Number;

        [DataMember]
        public TimeSeriesLink[] Results;
    }
}