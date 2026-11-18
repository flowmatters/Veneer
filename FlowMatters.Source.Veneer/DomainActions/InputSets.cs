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
            if (!String.IsNullOrEmpty(summary.HierarchicalName))
            {
                var hierarchy = summary.HierarchicalName.Substring(0,summary.HierarchicalName.Length-(summary.Name.Length+1));
                var parentInputSet = Scenario.Network.InputSets.FirstOrDefault(s=>s.HierarchicalName== hierarchy);
                set.ParentInputSet = parentInputSet;
            }
            UpdateInstructions(set, summary.Configuration);
        }

        public InputSet Find(string urlSafeInputSetName)
        {
            return All.FirstOrDefault(inputSet => SourceService.URLSafeString(inputSet.Name) == urlSafeInputSetName);
        }

        public void Delete(InputSet inputSet)
        {
            if (inputSet == null)
                return;

            var manager = ParameterSetManager();
            var inputSetParameterSet = manager.ParameterSets.FirstOrDefault(x => x.InputSet == inputSet);
            if (inputSetParameterSet != null)
                manager.ParameterSets.Remove(inputSetParameterSet);

            Scenario.Network.InputSets.Remove(inputSet);
        }

        public void Delete(string urlSafeInputSetName)
        {
            Delete(Find(urlSafeInputSetName));
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
            var parameterSet = LinkedParameterSet(ParameterSetManager(), inputSet).Parameters;
            parameterSet.Configuration.Instructions = String.Join(Environment.NewLine, newInstructions);
        }

        /// <summary>
        /// The input set's InputSetParameterSet, created if missing, with its Parent linked to the
        /// parent input set's (itself created and linked if missing). Source's
        /// InputSetParameterSet.Apply applies Parent first and then the set's own lines, so a child
        /// left with Parent = null silently loses everything it should inherit when a run selects
        /// it. Source's own ParameterSetManager.AddParamterSetForInputSet links the chain the same
        /// way, but throws if a parent has no parameter set yet; this creates the parent's instead.
        /// Re-linking on every call also repairs children an earlier Veneer created unlinked.
        /// </summary>
        public static InputSetParameterSet LinkedParameterSet(ParameterSetManager manager, InputSet inputSet)
        {
            return LinkedParameterSet(manager, inputSet, new HashSet<InputSet>());
        }

        private static InputSetParameterSet LinkedParameterSet(ParameterSetManager manager, InputSet inputSet, HashSet<InputSet> visiting)
        {
            var entry = manager.ParameterSets.FirstOrDefault(x => x.InputSet == inputSet);
            if (entry == null)
            {
                entry = new InputSetParameterSet
                {
                    InputSet = inputSet,
                    Parameters = new ParameterSet()
                };
                manager.ParameterSets.Add(entry);
            }

            visiting.Add(inputSet);
            var parent = inputSet.ParentInputSet;
            // A parent chain that loops back (never valid in Source) is cut rather than recursed.
            entry.Parent = parent == null || visiting.Contains(parent)
                ? null
                : LinkedParameterSet(manager, parent, visiting);
            return entry;
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
