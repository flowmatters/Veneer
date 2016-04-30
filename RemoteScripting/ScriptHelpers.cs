using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using IronPython.Runtime.Operations;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.DataManagement.DataManager.DataDetails;
using RiverSystem.DataManagement.DataManager.DataSources;
using RiverSystem.ManagedExtensions;
using TIME.Core;
using TIME.DataTypes;
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
            if (element.Contains("."))
            {
                var bits = element.Split('.');
                target = Deref(target,String.Join(".", bits.Take(bits.Length - 1)));
                element = bits[bits.Length - 1];
            }
            Type t = target.GetType();
            var member = t.GetMember(element, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
            var ri = ReflectedItem.NewItem(member, target);

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

        public static bool ListContainsInstance(IEnumerable<object> theList, object example)
        {
            return theList.Any(o => o.GetType() == example.GetType());
        }
    }
}
