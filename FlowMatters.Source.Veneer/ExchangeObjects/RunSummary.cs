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
            LastStackTrace = null;

            var meta = r.MetaData;
            if (meta != null)
            {
                StartDate = meta.ContainsKey("ConfigurationStartDate") ? meta["ConfigurationStartDate"]?.ToString() : null;
                EndDate = meta.ContainsKey("ConfigurationEndDate") ? meta["ConfigurationEndDate"]?.ToString() : null;
                TimeStep = meta.ContainsKey("ConfigurationTimeStep") ? meta["ConfigurationTimeStep"]?.ToString() : null;
            }
        }

        /// <summary>
        /// Recorded results left out of <see cref="Results"/> because listing them threw. Not
        /// serialised; GET /runs/{runId} fails with a fault naming them.
        /// </summary>
        public List<SkippedResult> Skipped { get; } = new List<SkippedResult>();

        public class SkippedResult
        {
            public string Row;
            public Exception Error;
        }

        private TimeSeriesLink[] BuildResultsArray(ProjectViewRow[] rows)
        {
            return CollectLinks(rows,
#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
                row => row.ElementRecorder.GetResultList(),
#else
                row => row.ElementRecorder.GetResultsLookup(),
#endif
                (row, key, ts) => BuildLink(ts, row, key, Number),
                row => row.NetworkElementName + " / " + row.ElementName,
                Skipped).ToArray();
        }

        /// <summary>
        /// One link per recorded series. A row or series that throws is left out and added to
        /// <paramref name="skipped"/>, and listing carries on, so the caller can report every
        /// failure, not just the first. A row Source cannot give results for
        /// (ArgumentNullException) is left out quietly, as it always has been.
        /// </summary>
        internal static List<TimeSeriesLink> CollectLinks<TRow, TKey, TSeries>(IEnumerable<TRow> rows,
            Func<TRow, IEnumerable<KeyValuePair<TKey, TSeries>>> results,
            Func<TRow, TKey, TSeries, TimeSeriesLink> link,
            Func<TRow, string> describe,
            List<SkippedResult> skipped) where TSeries : class
        {
            var links = new List<TimeSeriesLink>();
            foreach (var row in rows)
            {
                try
                {
                    foreach (var result in results(row))
                    {
                        if (result.Value == null)
                            continue;
                        try
                        {
                            links.Add(link(row, result.Key, result.Value));
                        }
                        catch (Exception e)
                        {
                            skipped.Add(Skip(row, describe, e));
                        }
                    }
                }
                catch (ArgumentNullException)
                {
                }
                catch (Exception e)
                {
                    skipped.Add(Skip(row, describe, e));
                }
            }

            return links;
        }

        private static SkippedResult Skip<TRow>(TRow row, Func<TRow, string> describe, Exception e)
        {
            string name;
            try
            {
                name = describe(row);
            }
            catch (Exception)
            {
                name = "(row could not be named)";
            }
            return new SkippedResult { Row = name, Error = e };
        }

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
        private static string SelectRecordingVariable(AttributeRecordingState key, ProjectViewRow row)
        {
            return string.IsNullOrEmpty(key.KeyString) ? row.ElementName : key.KeyString;
        }
#else
        private static string SelectRecordingVariable(RecordableItem key, ProjectViewRow row)
        {
            var recordableItemDisplayString = RecordableItemTransitionUtil.GetLegacyKeyString(key);
            return string.IsNullOrEmpty(recordableItemDisplayString) ? row.ElementName : recordableItemDisplayString;
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
        [DataMember] public string LastStackTrace;

        [DataMember]
        public string StartDate;

        [DataMember]
        public string EndDate;

        [DataMember]
        public string TimeStep;
    }
}