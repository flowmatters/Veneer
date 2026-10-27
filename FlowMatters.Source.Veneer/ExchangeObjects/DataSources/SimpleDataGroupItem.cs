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
        /// The name a data source called name is given: Source's DataManager.RestrictName, which
        /// replaces '.', '@', '>', ',' and backslash with '_', cuts it to 200 characters and
        /// trims it. The Source GUI renames every data source it shows that way, so applying it
        /// here gives the same name with or without the GUI.
        /// </summary>
        public static string KeptName(string name)
        {
            return DataManager.RestrictName(name);
        }

        /// <summary>
        /// Create this data source in scenario (POST /dataSources): replace the data source
        /// already of its name if there is one, else add it. Returns the data source created.
        /// onUiThread runs a change the Source GUI observes (see GuiThread); null runs it directly.
        /// warn, if given, is told of anything a replacement could not carry over.
        /// </summary>
        public DataGroupItem CreateInScenario(RiverSystemScenario scenario, Action<Action> onUiThread = null,
                                              Action<string> warn = null)
        {
            return CreateIn(scenario.Network.DataManager, scenario.Network.InputSets, onUiThread, warn);
        }

        internal DataGroupItem CreateIn(DataManager dm, IEnumerable<InputSet> inputSets, Action<Action> onUiThread = null,
                                        Action<string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new ArgumentException("A data source needs a Name.");

            // A data source an older Veneer created under the name as given (on a command line,
            // which keeps any name) is replaced too, by one named as Source keeps it.
            var kept = KeptName(Name);
            var existing = dm.DataGroups.FirstOrDefault(g => g.Name == kept)
                           ?? dm.DataGroups.FirstOrDefault(g => g.Name == Name);

            return existing != null
                ? ReplaceIn(dm, inputSets, existing, onUiThread, warn)
                : AddTo(dm, inputSets, onUiThread);
        }

        /// <summary>
        /// Add this data source to scenario, whatever else it holds. onUiThread is as for
        /// CreateInScenario.
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
        /// The file item i loads from: its Filename, or failing that the data source's Name.
        /// </summary>
        private string FilenameFor(int i)
        {
            var item = (Items != null && i < Items.Length) ? Items[i] : null;
            return string.IsNullOrEmpty(item?.Filename) ? Name : item.Filename;
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
            dataGroup.Name = KeptName(Name);
            if (asFile)
            {
                // Each item may name its file. Clients that predate Filename send the file as
                // the data source's Name, so that is the fall-back.
                var inputSetItems = dataGroup.InputSetItems.ToList();
                for (int i = 0; i < inputSetItems.Count; i++)
                {
                    FileCentralDataSource ds = inputSetItems[i].DataSource.SourceInformation as FileCentralDataSource;
                    ds.Filename = FilenameFor(i);
                }
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
                                               Action<Action> onUiThread = null, Action<string> warn = null)
        {
            return ReplaceIn(scenario.Network.DataManager, scenario.Network.InputSets, existing, onUiThread, warn);
        }

        internal DataGroupItem ReplaceIn(DataManager dm, IEnumerable<InputSet> inputSets, DataGroupItem existing,
                                         Action<Action> onUiThread = null, Action<string> warn = null)
        {
            var name = Name;
            // Built under a temporary name, so that it does not clash with the one it replaces.
            // A file named only by Name must still load from Name, not from the temporary name.
            var filenames = Items?.Select(item => item.Filename).ToArray();
            if (Items != null)
                for (int i = 0; i < Items.Length; i++)
                    Items[i].Filename = FilenameFor(i);
            Name = string.Format("TMP___{0}", name);
            DataGroupItem newItem;
            try
            {
                newItem = Build(inputSets);
            }
            finally
            {
                Name = name;
                if (Items != null)
                    for (int i = 0; i < Items.Length; i++)
                        Items[i].Filename = filenames[i];
            }

            // The swap is one change on the UI thread, so the GUI never sees it half made.
            Directly(onUiThread)(() =>
            {
                dm.DataGroups.Add(newItem);
                foreach (var origDetail in existing.DataDetails)
                {
                    var destDetail = newItem.DataDetails.FirstOrDefault(dd => dd.Name == origDetail.Name);
                    if (destDetail == null)
                    {
                        if (origDetail.Usages.Count > 0)
                            warn?.Invoke(string.Format(
                                "Data source '{0}' was replaced by one with no column '{1}': " +
                                "{2} usage(s) of that column were dropped.",
                                existing.Name, origDetail.Name, origDetail.Usages.Count));
                    }
                    else
                    {
                        foreach (var usage in origDetail.Usages)
                        {
                            destDetail.Usages.Add(usage);
                        }
                    }

                    origDetail.Usages.Clear();
                }
                newItem.Name = KeptName(name);
                dm.RemoveGroup(existing);
                dm.Refresh();
            });
            return newItem;
        }
    }
}
