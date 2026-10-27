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
#if !BEFORE_V5
            ReloadMatchesOnName = dgi.DataDetailsMatchMethod == DataDetailsMatchMethod.ByName;
#endif
            Items = dgi.InputSetItems.Select(isi => new SimpleDataItem(isi,summary)).ToArray();
        }

        /// <summary>
        /// Create this data source in scenario. onUiThread runs a change the Source GUI observes
        /// (see GuiThread); null runs it directly.
        /// </summary>
        public DataGroupItem AddToScenario(RiverSystemScenario scenario, Action<Action> onUiThread = null)
        {
            return AddTo(scenario.Network.DataManager, scenario.Network.InputSets, onUiThread);
        }

        internal DataGroupItem AddTo(DataManager dm, IEnumerable<InputSet> inputSets, Action<Action> onUiThread = null)
        {
            // Built in full - its file loaded - before it joins the data manager: whatever
            // observes the data manager sees a complete data source, a file that will not load
            // leaves nothing behind, and only the adding itself needs the UI thread.
            var dataGroup = Build(inputSets);
            Directly(onUiThread)(() => dm.DataGroups.Add(dataGroup));
            return dataGroup;
        }

        private static Action<Action> Directly(Action<Action> onUiThread)
        {
            return onUiThread ?? (action => action());
        }

        /// <summary>
        /// The data source this describes, loaded, but not yet in any data manager.
        /// </summary>
        private DataGroupItem Build(IEnumerable<InputSet> inputSets)
        {
            var asFile = Items.All(i => i.DetailsAsCSV == null);
            var dataGroupType = asFile
                ? typeof(FileCentralDataSource)
                : typeof(GeneratedCentralDataSource);

            var dataGroup = DataGroupItem.CreateGroup(dataGroupType,inputSets);
            dataGroup.Name = Name;
            if (asFile)
            {
                dataGroup.InputSetItems.ForEachItem(i =>
                {
                    FileCentralDataSource ds = i.DataSource.SourceInformation as FileCentralDataSource;
                    ds.Filename = Name;
                });
            }
#if !BEFORE_V5
            dataGroup.DataDetailsMatchMethod = ReloadMatchesOnName ? DataDetailsMatchMethod.ByName : DataDetailsMatchMethod.ByPosition;
#endif

            if (Items == null)
                return dataGroup;

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                item.AddToGroup(dataGroup, i);
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

#if !BEFORE_V5
        [DataMember] public bool ReloadMatchesOnName;
#endif

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

        /// <summary>
        /// Replace existing, a data source of the same name, with this one, moving its usages
        /// across. onUiThread is as for AddToScenario.
        /// </summary>
        public DataGroupItem ReplaceInScenario(RiverSystemScenario scenario, DataGroupItem existing,
                                               Action<Action> onUiThread = null)
        {
            return ReplaceIn(scenario.Network.DataManager, scenario.Network.InputSets, existing, onUiThread);
        }

        internal DataGroupItem ReplaceIn(DataManager dm, IEnumerable<InputSet> inputSets, DataGroupItem existing,
                                         Action<Action> onUiThread = null)
        {
            var name = Name;
            Name = string.Format("TMP___{0}", name);
            DataGroupItem newItem;
            try
            {
                newItem = Build(inputSets);
            }
            finally
            {
                Name = name;
            }

            // The swap is one change on the UI thread, so the GUI never sees it half made.
            Directly(onUiThread)(() =>
            {
                dm.DataGroups.Add(newItem);
                foreach (var origDetail in existing.DataDetails)
                {
                    var destDetail = newItem.DataDetails.First(dd => dd.Name == origDetail.Name);
                    foreach (var usage in origDetail.Usages)
                    {
                        destDetail.Usages.Add(usage);
                    }

                    origDetail.Usages.Clear();
                }
                newItem.Name = name;
                dm.RemoveGroup(existing);
                dm.Refresh();
            });
            return newItem;
        }
    }
}
