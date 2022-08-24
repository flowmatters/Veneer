using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using FlowMatters.Source.WebServer;
using IronPython.Modules;
using IronPython.Runtime.Operations;
using RiverSystem;
using RiverSystem.Api;
using RiverSystem.Assurance;
using RiverSystem.Catchments.Models.ContaminantFilteringModels;
using RiverSystem.Catchments;
using RiverSystem.Catchments.Constituents;
using RiverSystem.Catchments.Models.ContaminantGenerationModels;
using RiverSystem.Constituents;
using RiverSystem.DataManagement;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.DataManagement.DataManager.DataDetails;
using RiverSystem.DataManagement.DataManager.DataSources;
using RiverSystem.ManagedExtensions;
using RiverSystem.Quality.SourceSinkModels;
#if BEFORE_V4 || BEFORE_V5 || BEFORE_V5_13
using RiverSystemGUI_II.SchematicBuilder;
#else
using RiverSystem.Forms.SchematicBuilder;
#endif
using TIME.Core;
using TimeSeries = TIME.DataTypes.TimeSeries;
using TIME.Management;
using TIME.Tools.Reflection;
using TIME.Core.Metadata;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    public static class ScriptHelpers
    {
        public static object Deref(object target, string accessor)
        {
            if (accessor.Contains('.'))
            {
                var bits = accessor.Split('.');
                var first = bits[0];
                var next = target.GetMemberValue(first);
                return Deref(next, String.Join(".", bits.TakeLast(bits.Length - 1)));
            }
            return target.GetMemberValue(accessor);
        }

        //public static string FindMemberFromAka(Type t, string aka)
        //{
        //    foreach (MemberInfo memberInfo in t.GetMembers())
        //    {
        //        if (AkaAttribute.FindAka(memberInfo).ToLower() == aka.ToLower())
        //            return memberInfo.Name;
        //    }
        //    return null;
        //}

        public static void AssignTimeSeries(RiverSystemScenario scenario, object target, string element,
            string dataGroupName, string dataItem, int column = 0)
        {
            var ri = GetReflectedItem(target, element);

            var dm = scenario.Network.DataManager;
            dm.RemoveUsage(ri);

            var dataGroup = dm.DataGroups.Where(dg => dg.Name == dataGroupName).FirstOrDefault();
            if (dataGroup == null)
            {
                dataGroup = DataGroupItem.CreateGroup<GeneratedCentralDataSource>(scenario.Network.DefaultInputSet);
                dataGroup.Name = dataGroupName;
                dm.DataGroups.Add(dataGroup);
            }

            var dataGroupItem = dataGroup.DataDetails.Where(dgi => dgi.Name == dataItem).FirstOrDefault();
            if (dataGroupItem == null)
            {
                Data[] loaded = NonInteractiveIO.Load(dataItem);
                TimeSeries ts = loaded[column] as TimeSeries;
                ts.name = dataItem;
                dataGroup.CreateUsage<GeneratedDataDetails>(ri, ts);
            }
            else
            {
                dataGroupItem.Usages.Add(new DataUsage {ReflectedItem = ri});
            }
        }

        private static ReflectedItem GetReflectedItem(object target, string element)
        {
            if (element.Contains("."))
            {
                var bits = element.Split('.');
                target = Deref(target, String.Join(".", bits.Take(bits.Length - 1)));
                element = bits[bits.Length - 1];
            }

            Type t = target.GetType();
            var members = t.GetMember(element, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (members.Length == 0)
            {
                throw new Exception("No type member found");
            }
            var member = members[0];
            return ReflectedItem.NewItem(member, target);
        }

        public static TimeSeries FindInputTimeSeries(RiverSystemScenario scenario, object target, string element, string inputSet=null)
        {
            var ri = GetReflectedItem(target, element);

            Network theNetwork = scenario.Network;
            InputSet theInputSet = null;

            if (inputSet != null)
            {
                IList<InputSet> inputSets = theNetwork.InputSets;
                theInputSet = inputSets.FirstOrDefault(i => i.Name == inputSet);
            }

            if(theInputSet==null)
                theInputSet= theNetwork.DefaultInputSet;
            
            DataManager dm = theNetwork.DataManager;
            return dm.GetUsedTimeSeries(theInputSet,ri);
        }

        public static string GetFullPath(this DataGroupItem dgi, ReflectedItem ri,InputSet inputSet)
        {
            var gdd = dgi.DataDetails.FirstOrDefault(dd => dd.Usages.Any(u => u.ReflectedItem.Equals(ri)));
            return gdd == null ? "" : SimpleDataGroupItem.MakeID(dgi) + "/" + inputSet.Name + "/" + SourceService.URLSafeString(gdd.Name);
        }

        public static string FindDataSource(RiverSystemScenario scenario, object target, string element,
            string inputSet = null)
        {
            var ri = GetReflectedItem(target, element);

            Network theNetwork = scenario.Network;
            InputSet theInputSet = null;

            if (inputSet != null)
            {
                IList<InputSet> inputSets = theNetwork.InputSets;
                theInputSet = inputSets.FirstOrDefault(i => i.Name == inputSet);
            }

            if (theInputSet == null)
                theInputSet = theNetwork.DefaultInputSet;


            DataManager dm = theNetwork.DataManager;

            foreach (var name in dm.DataGroups.Select(g => g.GetFullPath(ri,theInputSet)).Where(name => !String.IsNullOrEmpty(name)))
            {
                return name;
            }
            return "";
        }

        public static bool ListContainsInstance(IEnumerable<object> theList, object example)
        {
            return theList.Any(o => o.GetType() == example.GetType());
        }

        public static void InitialiseModelsForConstituent(RiverSystemScenario s, Constituent c)
        {
            ConstituentsManagement cm = s.Network.ConstituentsManagement;
            
            cm.Elements.OfType<NetworkElementConstituentData>().ForEachItem(d =>
            {
                if (d.Data == null)
                    d.Data = new ConstituentsModel();

                d.Data.GetModel(c, DefaultSourceSinkType(d));
                if (!d.Data.ConstituentPlayedValues.Any(cpv => cpv.Constituent == c))
                {
                    d.Data.ConstituentPlayedValues.Add(new ConstituentPlayedValue(c) { PlayedType = ConstituentPlayedValue.ConstituentPlayedType.varConcentration });
                }
                d.Reset(cm,false,null,ScenarioType.RiverManager);
            });

            foreach (var catchment in s.Network.Catchments.OfType<Catchment>())
            {
                foreach (var functionalUnit in catchment.FunctionalUnits.OfType<StandardFunctionalUnit>())
                {
                    InitialiseConstituentSources(s,catchment, functionalUnit,c);
                }
            }
        }

        public static void InitialiseModelsForConstituentSource(RiverSystemScenario s)
        {
            foreach (var catchment in s.Network.Catchments.OfType<Catchment>())
            {
                foreach (var functionalUnit in catchment.FunctionalUnits.OfType<StandardFunctionalUnit>())
                {
                    foreach (var constituent in s.SystemConfiguration.Constituents)
                        InitialiseConstituentSources(s, catchment, functionalUnit, constituent);
                }
            }
        }

        public static void EnsureElementsHaveConstituentProviders(RiverSystemScenario scenario)
        {
            Network network = scenario.Network;
            ConstituentsManagement cm = network.ConstituentsManagement;

            Action<INetworkElement> ensure = element =>
            {
                cm.NetworkElementAdded(element,null);
            };

            network.Nodes.ForEachItem(ensure);
            network.Links.ForEachItem(ensure);
            /* 
                            var element = sender as INetworkElement;
            if (element != null && !_elementsDictionary.ContainsKey(element))
                _elementsDictionary.Add(element, ElementDataFactory.GetNetworkElementConstituentData(element));
*/
        }

        private static void InitialiseConstituentSources(RiverSystemScenario scenario, Catchment catchment, StandardFunctionalUnit fu, Constituent constituent)
        {
            ConstituentsManagement cm = scenario.Network.ConstituentsManagement;
            FunctionalUnitConstituentData model = cm.GetConstituentData<CatchmentElementConstituentData>(catchment).GetFunctionalUnitData(fu);
            ConstituentContainer constituentModel = model.ConstituentModels.SingleOrDefault(f => f.Constituent.Equals(constituent));
            if (constituentModel == null)
            {
                constituentModel = new ConstituentContainer(constituent);
                model.ConstituentModels.Add(constituentModel);
            }

#if V3 || V4_0 || V4_1 || V4_2 || V4_3
            if (constituentModel.ConstituentSources.Count == scenario.SystemConfiguration.ConstituentSources.Count) return;
#else
            if (constituentModel.ConstituentSources.Length == scenario.SystemConfiguration.ConstituentSources.Count) return;
#endif

            scenario.SystemConfiguration.ConstituentSources.ForEachItem(cs =>
            {
                if (constituentModel.ConstituentSources.Any(csc => csc.ConstituentSource == cs))
                {
                    return;
                }
#if V3 || V4_0 || V4_1 || V4_2 || V4_3
                constituentModel.ConstituentSources.Add(new ConstituentSourceContainer(cs, new NilConstituent(), new PassThroughFilter()));
#else
                constituentModel.AddConstituentSources(new ConstituentSourceContainer(cs, new NilConstituent(), new PassThroughFilter()));
#endif

            });
            /*
            var defaultConstituentSource = scenario.SystemConfiguration.ConstituentSources.First(cs => cs.IsDefault);
            #if V3 || V4_0 || V4_1 || V4_2 || V4_3_0
                        constituentModel.ConstituentSources.Add(new ConstituentSourceContainer(defaultConstituentSource, new NilConstituent(), new PassThroughFilter()));
            #else
                        constituentModel.AddConstituentSources(new ConstituentSourceContainer(defaultConstituentSource, new NilConstituent(), new PassThroughFilter()));
            #endif
            */
        }

        private static Type DefaultSourceSinkType(NetworkElementConstituentData data)
        {
            if (data is LinkElementConstituentData)
            {
                return typeof (NullLinkInstreamModel);
            }

            if (data is StorageElementConstituentData)
            {
                return typeof (NullStorageInstreamModel);
            }

            return null;
        }

        public static SchematicNetworkConfigurationPersistent GetSchematic(RiverSystemScenario scenario)
        {
            object tmp;
            scenario.AuxiliaryInformation.TryGetValue(SchematicNetworkControl.AUX_CONFIG, out tmp);
            SchematicNetworkConfigurationPersistent schematic = tmp as SchematicNetworkConfigurationPersistent;
            return schematic;
        }

        public static void ConfigureAssuranceRule(RiverSystemScenario scenario, string level = "Off", string name=null, string category = null)
        {
            var config = scenario.GetScenarioConfiguration<AssuranceConfiguration>();
            LogLevel logLevel;
            if (!Enum.TryParse<LogLevel>(level, true, out logLevel))
            {
                throw new Exception("Unknown log level");
            }

            if (name == null)
            {
                scenario.Network.AssuranceManager.DefaultLogLevels.ForEachItem(ar =>
                {
                    ConfigureAssuranceRule(scenario,level,ar.Name,ar.Category);
                });
                return;
            }

            bool needToAdd = false;
            AssuranceRule rule = GetAssuranceRule(name, category, config.Entries);

            if (rule == null)
            {
                rule = GetAssuranceRule(name, category, scenario.Network.AssuranceManager.DefaultLogLevels);
                needToAdd = true;
            }
            if (rule == null)
            {
                throw new Exception("Unknown assurance rule");
            }
            rule.LogLevel = logLevel;
            if (needToAdd)
            {
                config.Entries.Add(rule);
            }
        }

        private static AssuranceRule GetAssuranceRule(string name, string category,IEnumerable<AssuranceRule> config)
        {
            if (category == null)
            {
                return config.FirstOrDefault(r => r.Name == name);
            }
                return config.FirstOrDefault(r => (r.Name == name) && (r.Category == category));
        }

        //public static object FindProjectViewRow(RiverSystemScenario scenario, string path)
        //{
        //    var pathElements = path.split("/");
        //    var pvt = scenario.ProjectViewTable();
        //    //pvt.Where()
        //    scenario.Network.FunctionManager.Variables.Wh
        //}
    }
}
