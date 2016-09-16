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
using RiverSystem.Catchments.Models.ContaminantFilteringModels;
using RiverSystem.Catchments;
using RiverSystem.Catchments.Constituents;
using RiverSystem.Catchments.Models.ContaminantGenerationModels;
using RiverSystem.Constituents;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.DataManagement.DataManager.DataDetails;
using RiverSystem.DataManagement.DataManager.DataSources;
using RiverSystem.ManagedExtensions;
using RiverSystem.Quality.SourceSinkModels;
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
            var member = t.GetMember(element, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
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

            foreach (var name in dm.DataGroups.Select(g => g.GetFullPath(ri,theInputSet)).Where(name => !string.IsNullOrEmpty(name)))
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
            });

            foreach (var catchment in s.Network.Catchments.OfType<Catchment>())
            {
                foreach (var functionalUnit in catchment.FunctionalUnits.OfType<StandardFunctionalUnit>())
                {
                    InitialiseConstituentSources(s,catchment, functionalUnit,c);
                }
            }
        }

        public static void EnsureElementsHaveConstituentProviders(RiverSystemScenario scenario)
        {
            RiverSystem.Network network = scenario.Network;
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

            if (constituentModel.ConstituentSources.Count > 0) return;

            var defaultConstituentSource = scenario.SystemConfiguration.ConstituentSources.First(cs => cs.IsDefault);
            constituentModel.ConstituentSources.Add(new ConstituentSourceContainer(defaultConstituentSource, new NilConstituent(), new PassThroughFilter()));
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
    }
}
