using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using NUnit.Framework;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// Creating data sources from POST /dataSources bodies, against a data manager outside any
    /// scenario. The UI-thread runner is a stand-in that records whether a change happened
    /// inside it: in the Source GUI, changes to the data manager's groups, and to the columns of
    /// a group already in it, must happen on the UI thread.
    /// </summary>
    [TestFixture]
    public class DataSourceCreationTests
    {
        private DataManager _dm;
        private List<InputSet> _inputSets;
        private bool _onUiThread;
        private List<string> _changesOffUiThread;

        private void RunOnUiThread(Action action)
        {
            _onUiThread = true;
            try { action(); }
            finally { _onUiThread = false; }
        }

        private void Watch(string what) { if (!_onUiThread) _changesOffUiThread.Add(what); }

        [SetUp] public void CreateDataManager()
        {
            _dm = new DataManager();
            _inputSets = new List<InputSet> { new InputSet("Default Input Set") };
            _changesOffUiThread = new List<string>();
            _dm.DataGroups.CollectionChanged += (s, e) =>
            {
                Watch("DataGroups." + e.Action);
                if (e.NewItems != null)
                    foreach (DataGroupItem g in e.NewItems)
                        g.DataDetails.CollectionChanged += (s2, e2) => Watch(g.Name + ".DataDetails." + e2.Action);
            };
        }

        [TearDown] public void DisposeDataManager() => _dm.Dispose();

        private static SimpleDataGroupItem InMemory(string name, params string[] columns)
        {
            var csv = "Date," + string.Join(",", columns) + "\n" +
                      "2000-01-01," + string.Join(",", columns.Select(c => "1")) + "\n" +
                      "2000-01-02," + string.Join(",", columns.Select(c => "2")) + "\n";
            return new SimpleDataGroupItem
            {
                Name = name,
                Items = new[]
                {
                    new SimpleDataItem
                    {
                        Name = "Item for " + name,
                        InputSets = new[] { "Default Input Set" },
                        DetailsAsCSV = csv,
                        UnitsForNewTS = "ML/d",
                    }
                }
            };
        }

        private DataGroupItem Group(string name) => _dm.DataGroups.Single(g => g.Name == name);

        // A2: on the UI thread

        [Test] public void ANewDataSourceJoinsTheDataManagerOnTheUiThread()
        {
            InMemory("flows", "A", "B").AddTo(_dm, _inputSets, RunOnUiThread);

            Assert.That(Group("flows").DataDetails.Select(d => d.Name), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(_changesOffUiThread, Is.Empty);
        }

        // A1: built before it is added

        [Test] public void ADataSourceJoinsTheDataManagerWithItsColumnsAlreadyLoaded()
        {
            var columnsWhenAdded = new List<int>();
            _dm.DataGroups.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                    columnsWhenAdded.AddRange(e.NewItems.Cast<DataGroupItem>().Select(g => g.DataDetails.Count));
            };

            InMemory("flows", "A", "B").AddTo(_dm, _inputSets, RunOnUiThread);

            Assert.That(columnsWhenAdded, Is.EqualTo(new[] { 2 }));
        }

        [Test] public void OnlyTheAddingIsDoneOnTheUiThread()
        {
            // Loading a large file on the UI thread would freeze Source's interface.
            // The columns are loaded before the group joins (see above), so the UI thread is
            // asked once, for the adding alone.
            int runs = 0;
            InMemory("flows", "A", "B").AddTo(_dm, _inputSets, change => { runs++; RunOnUiThread(change); });
            Assert.That(runs, Is.EqualTo(1));
        }

        [Test] public void AFileThatWillNotLoadLeavesNoDataSourceBehind()
        {
            var fromFile = new SimpleDataGroupItem
            {
                Name = @"C:\no\such\folder\flows.csv",
                Items = new[] { new SimpleDataItem { InputSets = new[] { "Default Input Set" } } }
            };

            Assert.Catch((Action)(() => fromFile.AddTo(_dm, _inputSets, RunOnUiThread)));
            Assert.That(_dm.DataGroups, Is.Empty);
        }

        // B: a file data source's name and filename

        private string _folder;

        private string WriteCsv(string fileName, params string[] columns)
        {
            if (_folder == null)
            {
                _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "veneer-tests-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(_folder);
            }
            var path = System.IO.Path.Combine(_folder, fileName);
            System.IO.File.WriteAllText(path,
                "Date," + string.Join(",", columns) + "\n" +
                "2000-01-01," + string.Join(",", columns.Select(c => "1")) + "\n" +
                "2000-01-02," + string.Join(",", columns.Select(c => "2")) + "\n");
            return path;
        }

        [TearDown] public void RemoveFiles()
        {
            if (_folder != null && System.IO.Directory.Exists(_folder))
                System.IO.Directory.Delete(_folder, true);
            _folder = null;
        }

        private static SimpleDataGroupItem FromFile(string name, string filename = null) => new SimpleDataGroupItem
        {
            Name = name,
            Items = new[] { new SimpleDataItem { InputSets = new[] { "Default Input Set" }, Filename = filename } }
        };

        private static string FilenameOf(DataGroupItem group) =>
            ((RiverSystem.DataManagement.DataManager.DataSources.FileCentralDataSource)
                group.InputSetItems.Single().DataSource.SourceInformation).Filename;

        [Test] public void AFileDataSourceCanBeNamedApartFromItsFile()
        {
            var path = WriteCsv("inflows.csv", "A", "B");
            FromFile("inflow_series", path).AddTo(_dm, _inputSets, RunOnUiThread);

            var group = Group("inflow_series");
            Assert.That(FilenameOf(group), Is.EqualTo(path));
            Assert.That(group.DataDetails.Select(d => d.Name), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test] public void WithNoFilenameTheNameIsTheFile()
        {
            // As every client before Filename was honoured sends it.
            var path = WriteCsv("inflows.csv", "A");
            FromFile(path).AddTo(_dm, _inputSets, RunOnUiThread);

            Assert.That(FilenameOf(_dm.DataGroups.Single()), Is.EqualTo(path));
            Assert.That(_dm.DataGroups.Single().DataDetails.Select(d => d.Name), Is.EqualTo(new[] { "A" }));
        }

        // C: names Source keeps

        [Test] public void ADataSourceIsNamedAsSourceWillKeepIt()
        {
            // The Source GUI renames data sources with DataManager.RestrictName; doing it here
            // gives the same name with or without the GUI.
            var created = InMemory("fire.tss@v2", "A").CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(created.Name, Is.EqualTo("fire_tss_v2"));
            Assert.That(_dm.DataGroups.Select(g => g.Name), Is.EqualTo(new[] { "fire_tss_v2" }));
        }

        [Test] public void AFileNamedOnlyByItsPathStillLoadsFromThePath()
        {
            var path = WriteCsv("inflows.csv", "A");
            var created = FromFile(path).CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(created.Name, Is.EqualTo(DataManager.RestrictName(path)));
            Assert.That(FilenameOf(created), Is.EqualTo(path));
            Assert.That(created.DataDetails.Count, Is.EqualTo(1));
        }

        [Test] public void CreatingUnderTheSameNameAgainReplacesTheDataSource()
        {
            var first = InMemory("fire.tss", "A").CreateIn(_dm, _inputSets, RunOnUiThread);
            var second = InMemory("fire.tss", "A").CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_dm.DataGroups, Is.EqualTo(new[] { second }));
        }

        [Test] public void ADataSourceAnOlderVeneerNamedUnrestrictedIsReplaced()
        {
            // Created before names were restricted, on a command line, which keeps any name.
            var older = InMemory("fire.tss", "A").CreateIn(_dm, _inputSets, RunOnUiThread);
            older.Name = "fire.tss";

            var replacement = InMemory("fire.tss", "A").CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(_dm.DataGroups, Is.EqualTo(new[] { replacement }));
            Assert.That(replacement.Name, Is.EqualTo("fire_tss"));
        }

        [Test] public void ADataSourceNeedsAName()
        {
            Assert.Throws<ArgumentException>((Action)(() => InMemory(" ", "A").CreateIn(_dm, _inputSets, RunOnUiThread)));
            Assert.That(_dm.DataGroups, Is.Empty);
        }

        // D: replacing a data source

        [Test] public void AFileNamedOnlyByItsPathCanBeCreatedAgain()
        {
            // The replacement is built under a temporary name, which must not become its file.
            var path = WriteCsv("inflows.csv", "A", "B");
            FromFile(path).CreateIn(_dm, _inputSets, RunOnUiThread);
            var again = FromFile(path).CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(_dm.DataGroups, Is.EqualTo(new[] { again }));
            Assert.That(FilenameOf(again), Is.EqualTo(path));
            Assert.That(again.DataDetails.Select(d => d.Name), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test] public void AReplacementTakesOverTheUsagesOfEachColumn()
        {
            var first = InMemory("flows", "A", "B").CreateIn(_dm, _inputSets, RunOnUiThread);
            var usage = new DataUsage();
            first.DataDetails.Single(d => d.Name == "A").Usages.Add(usage);

            var second = InMemory("flows", "A", "B").CreateIn(_dm, _inputSets, RunOnUiThread);

            Assert.That(second.DataDetails.Single(d => d.Name == "A").Usages, Is.EqualTo(new[] { usage }));
            Assert.That(second.DataDetails.Single(d => d.Name == "B").Usages, Is.Empty);
        }

        [Test] public void AColumnTheReplacementLacksDropsItsUsagesWithAWarning()
        {
            var first = InMemory("flows", "A", "Gone").CreateIn(_dm, _inputSets, RunOnUiThread);
            first.DataDetails.Single(d => d.Name == "Gone").Usages.Add(new DataUsage());
            var warnings = new List<string>();

            var second = InMemory("flows", "A").CreateIn(_dm, _inputSets, RunOnUiThread, warnings.Add);

            Assert.That(_dm.DataGroups, Is.EqualTo(new[] { second }));
            Assert.That(warnings, Has.Exactly(1).Contains("Gone"));
        }

        [Test] public void AReplacementIsMadeOnTheUiThread()
        {
            InMemory("flows", "A", "B").AddTo(_dm, _inputSets, RunOnUiThread);
            var original = Group("flows");

            InMemory("flows", "A", "B").ReplaceIn(_dm, _inputSets, original, RunOnUiThread);

            Assert.That(Group("flows"), Is.Not.SameAs(original));
            Assert.That(_changesOffUiThread, Is.Empty);
        }
    }
}
