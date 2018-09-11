#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5
#define BEFORE_RECORDING_ATTRIBUTES_REFACTOR
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RiverSystem;
using TIME.DataTypes;
using TIME.ScenarioManagement;

namespace FlowMatters.Source.WebServer.ProcessRequests
{
    class ParseDataRequests
    {
        public ParseDataRequests(RiverSystemScenario scenario)
        {
            Scenario = scenario;
        }

        public RiverSystemScenario Scenario { get; private set; }

        public TimeSeries GetResults(string query)
        {
            string[] queryComponents = query.Trim('/').Split('/');
            if (queryComponents.Length < 2) return null;

            string networkElement = queryComponents[0];
            string property = queryComponents[1];
            string parameter = (queryComponents.Length>2)?queryComponents[2]:"";

            var latestRun = Scenario.Project.ResultManager.AllRuns().Last();
            var rows = latestRun.RunParameters.Where(x => (x.NetworkElementName == networkElement) && (x.ElementName == property)).ToArray();

#if BEFORE_RECORDING_ATTRIBUTES_REFACTOR
            var results = rows[0].ElementRecorder.GetResultList();
            var matchingKey = (parameter=="")?null:results.Keys.FirstOrDefault(k => k.KeyString.Contains(parameter));
#else
            var results = rows[0].ElementRecorder.GetResultsLookup();
            var matchingKey = (parameter == "") ? null : results.Keys.FirstOrDefault(k => RecordableItemTransitionUtil.GetLegacyKeyString(k).Contains(parameter));
#endif

            return (matchingKey==null)?null:results[matchingKey];
        }
    }
}
