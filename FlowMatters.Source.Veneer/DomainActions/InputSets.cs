using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.ExchangeObjects;
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

        public void Create(InputSetSummary summary)
        {
            var set = new InputSet(summary.Name);
            Scenario.Network.InputSets.Add(set);
            UpdateInstructions(set, summary.Configuration);
        }

        public InputSet Find(string urlSafeInputSetName)
        {
            return All.FirstOrDefault(inputSet => SourceService.URLSafeString(inputSet.Name) == urlSafeInputSetName);
        }

        public string[] Instructions(InputSet inputSet)
        {
            ParameterSet parameterSet =
                ParameterSetManager().ParameterSets.FirstOrDefault(x => x.InputSet == inputSet)?.Parameters;
            if (parameterSet == null)
                return new string[0];

            try
            {
#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || V4_2_4 || V4_2_5 || V4_2_6
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
            {
                parameterSet = new ParameterSet();
                var inputSetParameterSet = new InputSetParameterSet
                {
                    InputSet = inputSet,
                    Parent = null,
                    Parameters = parameterSet
                };
                ParameterSetManager().ParameterSets.Add(inputSetParameterSet);
            }
            parameterSet.Configuration.Instructions = String.Join(Environment.NewLine, newInstructions);
        }

        private ParameterSet ParameterSet(InputSet inputSet)
        {
            return ParameterSetManager().ParameterSets.FirstOrDefault(x => x.InputSet == inputSet)?.Parameters;
        }

        private ParameterSetManager ParameterSetManager()
        {
#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || V4_2_4 || V4_2_5 || V4_2_6
            var manager = Scenario.PluginDataModels.OfType<ParameterSetManager>().FirstOrDefault();
            if (manager == null)
            {
                manager = new ParameterSetManager();
                Scenario.PluginDataModels.Add(manager);
            }
            return manager;
#else
            return Scenario.GetOrCreatePluginModel<ParameterSetManager>();
#endif
        }

        public void Run(InputSet inputSet)
        {
            ParameterSet parameterSet = ParameterSet(inputSet);
            if (parameterSet == null)
                return;
#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || V4_2_4 || V4_2_5 || V4_2_6
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
