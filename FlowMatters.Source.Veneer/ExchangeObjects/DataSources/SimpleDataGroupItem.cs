using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.DataManagement.DataManager.DataSources;
using RiverSystem.ManagedExtensions;

namespace FlowMatters.Source.Veneer.ExchangeObjects.DataSources
{
    [DataContract]
    public class SimpleDataGroupItem
    {
        public SimpleDataGroupItem()
        {
            
        }
        public SimpleDataGroupItem(DataGroupItem dgi,bool summary=true)
        {
            Name = dgi.Name;
            Path = ExpandPath(dgi);
            FullName = MakeFullName(dgi);
            id = MakeID(dgi);
            ReloadMatchesOnName = dgi.DataDetailsMatchMethod == DataDetailsMatchMethod.ByName;
            Items = dgi.InputSetItems.Select(isi => new SimpleDataItem(isi,summary)).ToArray();
        }

        public DataGroupItem AddToScenario(RiverSystemScenario scenario)
        {
            var dm = scenario.Network.DataManager;
            var asFile = Items.All(i => i.DetailsAsCSV == null);
            var dataGroupType = asFile
                ? typeof(FileCentralDataSource)
                : typeof(GeneratedCentralDataSource);

            var dataGroup = DataGroupItem.CreateGroup(dataGroupType,scenario.Network.InputSets);
            dataGroup.Name = Name;
            if (asFile)
            {
                dataGroup.InputSetItems.ForEachItem(i =>
                {
                    FileCentralDataSource ds = i.DataSource.SourceInformation as FileCentralDataSource;
                    ds.Filename = Name;
                });
            }
            dataGroup.DataDetailsMatchMethod = ReloadMatchesOnName ? DataDetailsMatchMethod.ByName : DataDetailsMatchMethod.ByPosition;

            dm.DataGroups.Add(dataGroup);

            if (Items == null)
                return dataGroup;

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                item.AddToGroup(scenario, dataGroup, i);
            }
            return dataGroup;
        }

        [DataMember]
        public string id;

        [DataMember]
        public string Path;

        [DataMember]
        public string Name;

        [DataMember]
        public string FullName;

        [DataMember] public bool ReloadMatchesOnName;

        [DataMember]
        public SimpleDataItem[] Items;

        public static string ExpandPath(DataGroupItem i)
        {
            string path = "/";
            var parent = i.Parent;
            while (parent != null)
            {
                path = "/" + parent.Name + path;
                parent = parent.Parent;
            }
            return path;
        }

        public static string MakeFullName(DataGroupItem i)
        {
            return ExpandPath(i) + i.Name;
        }
        public static string MakeID(DataGroupItem i)
        {
            return UriTemplates.DataSources + "/" + SourceService.URLSafeString(MakeFullName(i).Substring(1)).Replace("%2F","/");
        }

        public void ReplaceInScenario(RiverSystemScenario scenario, DataGroupItem existing)
        {
            var dm = scenario.Network.DataManager;
            var name = Name;
            Name = string.Format("TMP___{0}", name);

            var newItem = AddToScenario(scenario);

            foreach (var origDetail in existing.DataDetails)
            {
                var destDetail = newItem.DataDetails.First(dd => dd.Name == origDetail.Name);
                foreach (var usage in origDetail.Usages)
                {
                    destDetail.Usages.Add(usage);
                }

                origDetail.Usages.Clear();
            }
            Name = name;
            newItem.Name = name;
            dm.RemoveGroup(existing);
            dm.Refresh();
        }
    }
}
