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
            var results = rows[0].ElementRecorder.GetResultsLookup();

            var matchingKey = (parameter=="")?null:results.Keys.FirstOrDefault(k => RecordableItemTransitionUtil.GetLegacyKeyString(k).Contains(parameter));

            return (matchingKey==null)?null:results[matchingKey];
        }
    }
}
