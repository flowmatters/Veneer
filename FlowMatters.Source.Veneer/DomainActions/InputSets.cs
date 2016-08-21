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
using TIME.Tools.Optimisation;
using ParameterSet = RiverSystem.ScenarioExplorer.ParameterSet.ParameterSet;

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
            ParameterSet parameterSet = ParameterSet(inputSet);
            if (parameterSet == null)
                return new string[0];

            IEnumerable<string> result = parameterSet.Configuration.GetInstructions(null);
            return result.ToArray();
        }

        public void UpdateInstructions(InputSet inputSet, string[] newInstructions)
        {
            ParameterSet parameterSet = ParameterSet(inputSet);
            if (parameterSet == null)
                return;
            parameterSet.Configuration.Instructions = String.Join("\n", newInstructions);
        }

        private ParameterSet ParameterSet(InputSet inputSet)
        {
            ParameterSetManager Manager = Scenario.PluginDataModels.OfType<ParameterSetManager>().FirstOrDefault();
            if (Manager == null)
                return null;
            return Manager.ParameterSets.First(x => x.InputSet == inputSet).Parameters;
        }

        public void Run(InputSet inputSet)
        {
            ParameterSet parameterSet = ParameterSet(inputSet);
            if (parameterSet == null)
                return;
            parameterSet.Reset(new Scenario(Scenario));
        }

        public void Run(string urlSafeInputSetName)
        {
            Run(Find(urlSafeInputSetName));
        }
    }
}
