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

            try
            {
#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5 || GBRSource
                IEnumerable<string> result = parameterSet.Configuration.GetInstructions(new Scenario(Scenario));
#else
                IEnumerable<string> result = parameterSet.Configuration.GetInstructions(Scenario);
#endif
                return result.ToArray();
            }
            catch
            {
                return new string[] { "// Couldn't read instructions." };
            }
        }

        public void UpdateInstructions(InputSet inputSet, string[] newInstructions)
        {
            ParameterSet parameterSet = ParameterSet(inputSet);
            if (parameterSet == null)
                return;
            parameterSet.Configuration.Instructions = String.Join(Environment.NewLine, newInstructions);
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
#if V3 || V4_0 || V4_1 || V4_2 || V4_3 || V4_4 || V4_5 || GBRSource
            parameterSet.Reset(new Scenario(Scenario));
#else
            parameterSet.Apply(Scenario);
#endif
        }

        public void Run(string urlSafeInputSetName)
        {
            Run(Find(urlSafeInputSetName));
        }

        public string Filename(InputSet inputSet)
        {
            var p = ParameterSet(inputSet);
            if (p == null)
                return null;

            if (p.Configuration is FileParameterSetConfiguration)
            {
                return ((FileParameterSetConfiguration)p.Configuration).Filename;
            }
            return null;
        }

        public bool ReloadOnRun(InputSet inputSet)
        {
            var p = ParameterSet(inputSet);
            if (p == null)
                return false;

            if (p.Configuration is FileParameterSetConfiguration)
            {
                return ((FileParameterSetConfiguration) p.Configuration).ReloadOnRun;
            }
            return false;
        }
    }
}
