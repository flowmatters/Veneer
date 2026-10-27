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
