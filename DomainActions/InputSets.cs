using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer;
using RiverSystem;
using RiverSystem.Api;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.ScenarioExplorer.ParameterSet;

namespace FlowMatters.Source.Veneer.DomainActions
{
    public class InputSets
    {
        public InputSets(RiverSystemScenario scenario)
        {
            Scenario = scenario;
        }

        public RiverSystemScenario Scenario { get; set; }

        public IList<InputSet> All
        {
            get { return Scenario.Network.InputSets; }
        }

        public InputSet Find(string urlSafeInputSetName)
        {
            return All.FirstOrDefault(inputSet => SourceService.URLSafeString(inputSet.Name) == urlSafeInputSetName);
        }

        public string[] Instructions(InputSet inputSet)
        {
            IEnumerable<string> result = ParameterSet(inputSet).Configuration.GetInstructions(null);
            return result.ToArray();
        }

        public void UpdateInstructions(InputSet inputSet, string[] newInstructions)
        {
            ParameterSet(inputSet).Configuration.Instructions = String.Join("\n", newInstructions);
        }

        private ParameterSet ParameterSet(InputSet inputSet)
        {
            ParameterSetManager Manager = Scenario.PluginDataModels.OfType<ParameterSetManager>().First();
            return Manager.ParameterSets.First(x => x.InputSet == inputSet).Parameters;
        }

        public void Run(InputSet inputSet)
        {
            ParameterSet(inputSet).Reset(new Scenario(Scenario));
        }

        public void Run(string urlSafeInputSetName)
        {
            Run(Find(urlSafeInputSetName));
        }
    }
}
