using System;
using System.Collections.Generic;
using System.Linq;
using FlowMatters.Source.Veneer.DomainActions;
using FlowMatters.Source.Veneer.ExchangeObjects.DataSources;
using NUnit.Framework;
using RiverSystem;
using RiverSystem.DataManagement.DataManager;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// The Source GUI's data sources tree never drops the entry of a data source removed from
    /// the data manager by code (only its own Delete command does), so after Veneer removes one
    /// the tree is rebuilt. DataSourcesTree.ChangeThenRefreshIfRemoved decides when; rebuilding
    /// the GUI's panels themselves needs the GUI and is not covered here.
    /// </summary>
    [TestFixture]
    public class DataSourcesTreeTests
    {
        private DataManager _dm;
        private List<InputSet> _inputSets;
        private int _refreshes;
        private List<string> _warnings;

        [SetUp] public void CreateDataManager()
        {
            _dm = new DataManager();
            _inputSets = new List<InputSet> { new InputSet("Default Input Set") };
            _refreshes = 0;
            _warnings = new List<string>();
        }

        [TearDown] public void DisposeDataManager() => _dm.Dispose();

        private void Change(Action change) =>
            DataSourcesTree.ChangeThenRefreshIfRemoved(_dm, change, () => _refreshes++, _warnings.Add);

        private static SimpleDataGroupItem InMemory(string name) => new SimpleDataGroupItem
        {
            Name = name,
            Items = new[]
            {
                new SimpleDataItem
                {
                    Name = "Item for " + name,
                    InputSets = new[] { "Default Input Set" },
                    DetailsAsCSV = "Date,A\n2000-01-01,1\n2000-01-02,2\n",
                    UnitsForNewTS = "ML/d",
                }
            }
        };

        [Test] public void AddingADataSourceLeavesTheTreeAlone()
        {
            InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Assert.That(_refreshes, Is.EqualTo(0));
        }

        [Test] public void ReplacingADataSourceRefreshesTheTreeOnce()
        {
            InMemory("flows").CreateIn(_dm, _inputSets, Change);
            InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Assert.That(_refreshes, Is.EqualTo(1));
        }

        [Test] public void RemovingADataSourceRefreshesTheTreeOnce()
        {
            var group = InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Change(() => _dm.RemoveGroup(group));
            Assert.That(_refreshes, Is.EqualTo(1));
        }

        [Test] public void SeveralRemovalsInOneChangeRefreshItOnce()
        {
            var a = InMemory("a").CreateIn(_dm, _inputSets, Change);
            var b = InMemory("b").CreateIn(_dm, _inputSets, Change);
            Change(() => { _dm.RemoveGroup(a); _dm.RemoveGroup(b); });
            Assert.That(_refreshes, Is.EqualTo(1));
        }

        [Test] public void AFailedRefreshIsReportedButDoesNotFailTheChange()
        {
            var group = InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Action failingRefresh = () => throw new InvalidOperationException("tree unavailable");

            Assert.DoesNotThrow((TestDelegate)(() =>
                DataSourcesTree.ChangeThenRefreshIfRemoved(_dm, () => _dm.RemoveGroup(group), failingRefresh, _warnings.Add)));
            Assert.That(_dm.DataGroups, Is.Empty);
            Assert.That(_warnings, Has.Exactly(1).Contains("tree unavailable"));
        }

        [Test] public void AChangeThatFailsAfterARemovalStillRefreshesAndStillFails()
        {
            var group = InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Assert.Throws<InvalidOperationException>((TestDelegate)(() => Change(() =>
            {
                _dm.RemoveGroup(group);
                throw new InvalidOperationException("part-way");
            })));
            Assert.That(_refreshes, Is.EqualTo(1));
        }

        [Test] public void WithNoRefreshNothingIsAttempted()
        {
            // As on the command line, which has no tree.
            var group = InMemory("flows").CreateIn(_dm, _inputSets, Change);
            Assert.DoesNotThrow((TestDelegate)(() => DataSourcesTree.ChangeThenRefreshIfRemoved(_dm, () => _dm.RemoveGroup(group), null, _warnings.Add)));
            Assert.That(_warnings, Is.Empty);
        }

        [Test] public void ALaterChangeIsNotWatched()
        {
            var group = InMemory("flows").CreateIn(_dm, _inputSets, Change);
            _dm.RemoveGroup(group);
            Assert.That(_refreshes, Is.EqualTo(0));
        }
    }
}
