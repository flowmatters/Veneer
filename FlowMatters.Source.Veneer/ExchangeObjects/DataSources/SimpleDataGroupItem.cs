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
            Items = dgi.InputSetItems.Select(isi => new SimpleDataItem(isi,summary)).ToArray();
        }

        public void AddToScenario(RiverSystemScenario scenario)
        {
            var dm = scenario.Network.DataManager;
            var dataGroup = DataGroupItem.CreateGroup<GeneratedCentralDataSource>(scenario.Network.DefaultInputSet);
            dataGroup.Name = Name;
            dm.DataGroups.Add(dataGroup);

            if(Items==null)
                return;

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                item.AddToGroup(scenario, dataGroup,i);
            }
        }

        [DataMember]
        public string id;

        [DataMember]
        public string Path;

        [DataMember]
        public string Name;

        [DataMember]
        public string FullName;

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
            return UriTemplates.DataSources + "/" + SourceService.URLSafeString(MakeFullName(i).Substring(1));
        }
    }
}
