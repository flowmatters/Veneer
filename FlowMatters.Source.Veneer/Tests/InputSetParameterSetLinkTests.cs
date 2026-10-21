using System.Linq;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;
using RiverSystem.DataManagement.DataManager;
using RiverSystem.ScenarioExplorer.ParameterSet;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// How an input set's InputSetParameterSet is linked to its parent's. Source's
    /// InputSetParameterSet.Apply applies Parent first and then the set's own lines, so a child whose
    /// Parent is left null silently loses everything it should inherit when a run selects it.
    /// </summary>
    [TestFixture]
    public class InputSetParameterSetLinkTests
    {
        private static InputSet Child(string name, InputSet parent) => new InputSet(name) { ParentInputSet = parent };

        [Test]
        public void ATopLevelSetHasNoParent()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");

            var entry = InputSets.LinkedParameterSet(manager, p);

            Assert.That(entry.InputSet, Is.SameAs(p));
            Assert.That(entry.Parent, Is.Null);
            Assert.That(entry.Parameters, Is.Not.Null);
        }

        [Test]
        public void AChildIsLinkedToItsParentsParameterSet()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");
            var c = Child("C", p);
            var parentEntry = InputSets.LinkedParameterSet(manager, p);

            var childEntry = InputSets.LinkedParameterSet(manager, c);

            Assert.That(childEntry.Parent, Is.SameAs(parentEntry));
        }

        [Test]
        public void AParentWithNoParameterSetGetsOneRatherThanTheChildStayingUnlinked()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");
            var c = Child("C", p);

            var childEntry = InputSets.LinkedParameterSet(manager, c);

            Assert.That(childEntry.Parent, Is.Not.Null);
            Assert.That(childEntry.Parent.InputSet, Is.SameAs(p));
            Assert.That(manager.ParameterSets.Count(x => x.InputSet == p), Is.EqualTo(1));
        }

        [Test]
        public void AGrandchildInheritsTheWholeChain()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");
            var c = Child("C", p);
            var g = Child("G", c);

            var grandchildEntry = InputSets.LinkedParameterSet(manager, g);

            Assert.That(grandchildEntry.Parent.InputSet, Is.SameAs(c));
            Assert.That(grandchildEntry.Parent.Parent.InputSet, Is.SameAs(p));
            Assert.That(grandchildEntry.Parent.Parent.Parent, Is.Null);
        }

        [Test]
        public void AnUnlinkedChildFromAnEarlierVeneerIsLinkedOnItsNextUpdate()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");
            var c = Child("C", p);
            var parentEntry = InputSets.LinkedParameterSet(manager, p);
            var stale = new InputSetParameterSet { InputSet = c, Parent = null, Parameters = new ParameterSet() };
            manager.ParameterSets.Add(stale);

            var childEntry = InputSets.LinkedParameterSet(manager, c);

            Assert.That(childEntry, Is.SameAs(stale));
            Assert.That(childEntry.Parent, Is.SameAs(parentEntry));
        }

        [Test]
        public void LinkingNeverDuplicatesAnEntry()
        {
            var manager = new ParameterSetManager();
            var p = new InputSet("P");
            var c = Child("C", p);

            InputSets.LinkedParameterSet(manager, c);
            InputSets.LinkedParameterSet(manager, c);
            InputSets.LinkedParameterSet(manager, p);

            Assert.That(manager.ParameterSets.Count, Is.EqualTo(2));
        }
    }
}
