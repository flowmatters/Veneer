using System;
using System.Collections.Generic;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// The SelectedRecorderSets run parameter's validation and name matching, over the values the
    /// request deserialiser delivers and plain set names. Applying a selection to a scenario needs
    /// Source and is not covered here.
    /// </summary>
    [TestFixture]
    public class RecorderSetSelectionTests
    {
        private const string Tree = "Current Recorder Tree";

        private static readonly List<string> Available = new List<string> { Tree, "SetA", "SetB" };

        private static string Refusal(TestDelegate action) =>
            Assert.Throws<InvalidRunParameterException>(action).Message;

        // ParseNames: the deserialiser delivers a JSON array as object[], a string as string, a
        // number as int, an object as a bare object and null as null.

        [Test] public void AnArrayOfStringsYieldsTheNamesInOrder()
        {
            Assert.That(RecorderSetSelection.ParseNames(new object[] { "SetB", Tree, "SetA" }),
                Is.EqualTo(new[] { "SetB", Tree, "SetA" }));
        }

        [Test] public void AnEmptyArrayYieldsNoNames()
        {
            Assert.That(RecorderSetSelection.ParseNames(new object[0]), Is.Empty);
        }

        [Test] public void AStringIsRefused()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames("not-a-list")),
                Is.EqualTo("SelectedRecorderSets must be a JSON array of recorder set names, not a string."));
        }

        [Test] public void NullIsRefused()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(null)),
                Is.EqualTo("SelectedRecorderSets must be a JSON array of recorder set names, not null."));
        }

        [Test] public void AnObjectIsRefused()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(new object())),
                Is.EqualTo("SelectedRecorderSets must be a JSON array of recorder set names, not an object."));
        }

        [Test] public void ANumberIsRefused()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(1)),
                Is.EqualTo("SelectedRecorderSets must be a JSON array of recorder set names, not a number."));
        }

        [Test] public void ANumberElementIsRefusedByIndex()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(new object[] { 1, "SetB" })),
                Is.EqualTo("SelectedRecorderSets[0] must be a recorder set name (a string), not a number."));
        }

        [Test] public void ANestedArrayElementIsRefusedByIndex()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(new object[] { "SetA", new object[] { "SetB" } })),
                Is.EqualTo("SelectedRecorderSets[1] must be a recorder set name (a string), not an array."));
        }

        [Test] public void ANullElementIsRefusedByIndex()
        {
            Assert.That(Refusal(() => RecorderSetSelection.ParseNames(new object[] { "SetA", null })),
                Is.EqualTo("SelectedRecorderSets[1] must be a recorder set name (a string), not null."));
        }

        // Resolve: indices into the manager's sets, in the requested order. The tree is at index 0
        // of Available, as Source keeps it.

        private static int[] Resolve(IList<string> available, params string[] requested) =>
            RecorderSetSelection.Resolve(available, 0, requested);

#if !BEFORE_V5
        // Source 4.x has no CurrentTreeRecorderSet: its tree is a RecorderSet constructed with the
        // same literal name.
        [Test] public void TheTreeNameIsSourcesConstant()
        {
            Assert.That(RecorderSetSelection.CURRENT_TREE_NAME,
                Is.EqualTo(RiverSystem.ScenarioSets.RecorderSets.CurrentTreeRecorderSet.CurrentRecorderTreeName));
        }
#endif

        [Test] public void AnEmptyListResolvesToTheTree()
        {
            Assert.That(Resolve(Available), Is.EqualTo(new[] { 0 }));
        }

        [Test] public void AnEmptyListResolvesToTheTreeWhereverItIs()
        {
            var available = new List<string> { "SetA", Tree };
            Assert.That(RecorderSetSelection.Resolve(available, 1, new string[0]), Is.EqualTo(new[] { 1 }));
        }

        [Test] public void AnEmptyListResolvesToNothingWhenTheTreeIsNotAmongTheSets()
        {
            Assert.That(RecorderSetSelection.Resolve(new List<string> { "SetA" }, -1, new string[0]), Is.Empty);
        }

        [Test] public void AnEmptyListResolvesToNothingWithoutAManager()
        {
            Assert.That(RecorderSetSelection.Resolve(null, -1, new string[0]), Is.Empty);
        }

        [Test] public void ANonEmptyListIsRefusedWithoutAManager()
        {
            Assert.That(Refusal(() => RecorderSetSelection.Resolve(null, -1, new[] { "SetA", "SetA" })),
                Is.EqualTo("SelectedRecorderSets cannot select 'SetA': the scenario has no recorder sets."));
        }

        [Test] public void RequireEmptyPassesAnEmptyListAndRefusesOthersWithTheReason()
        {
            RecorderSetSelection.RequireEmpty(new string[0], "no selection here");
            Assert.That(Refusal(() => RecorderSetSelection.RequireEmpty(new[] { "SetA", "SetB" }, "no selection here")),
                Is.EqualTo("SelectedRecorderSets cannot select 'SetA', 'SetB': no selection here."));
        }

        [Test] public void ANamedSetResolvesToItsIndex()
        {
            Assert.That(Resolve(Available, "SetB"), Is.EqualTo(new[] { 2 }));
        }

        [Test] public void AMixedListOfTheTreeAndNamedSetsIsAccepted()
        {
            Assert.That(Resolve(Available, Tree, "SetA"), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test] public void TheRequestedOrderIsPreserved()
        {
            Assert.That(Resolve(Available, "SetB", Tree, "SetA"), Is.EqualTo(new[] { 2, 0, 1 }));
        }

        [Test] public void RepeatedNamesKeepTheFirstOccurrence()
        {
            Assert.That(Resolve(Available, "SetB", "SetA", "SetB"), Is.EqualTo(new[] { 2, 1 }));
        }

        [Test] public void TheTreeTwiceIsTheTreeOnce()
        {
            Assert.That(Resolve(Available, Tree, Tree), Is.EqualTo(new[] { 0 }));
        }

        [Test] public void TheTreeByItsOwnNameAndByTheConstantIsTheTreeOnce()
        {
            var available = new List<string> { "Renamed tree", "SetA" };
            Assert.That(Resolve(available, "Renamed tree", "SetA", Tree), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test] public void TheTreeNameMeansTheTreeEvenWhenTheTreeIsRenamed()
        {
            var available = new List<string> { "Renamed tree", "SetA" };
            Assert.That(Resolve(available, Tree, "SetA"), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test] public void TheTreeNameMeansTheTreeEvenWhenANamedSetSharesIt()
        {
            var available = new List<string> { Tree, "SetA", Tree };
            Assert.That(Resolve(available, Tree, "SetA"), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test] public void TheTreeNameIsMatchedByNameWhenTheTreeIsNotAmongTheSets()
        {
            var available = new List<string> { "SetA", Tree };
            Assert.That(RecorderSetSelection.Resolve(available, -1, new[] { Tree }), Is.EqualTo(new[] { 1 }));
        }

        [Test] public void NamesMatchExactly()
        {
            Assert.That(Refusal(() => Resolve(Available, "seta")),
                Is.EqualTo("SelectedRecorderSets names an unknown recorder set: 'seta'. " +
                           "Available recorder sets: 'Current Recorder Tree', 'SetA', 'SetB'."));
        }

        [Test] public void UnknownNamesAreNamedOnceEachWithTheAvailableSets()
        {
            Assert.That(Refusal(() => Resolve(Available, "Nope", "SetA", "Gone", "Nope")),
                Is.EqualTo("SelectedRecorderSets names unknown recorder sets: 'Nope', 'Gone'. " +
                           "Available recorder sets: 'Current Recorder Tree', 'SetA', 'SetB'."));
        }

        [Test] public void AnUnknownNameWithNoSetsSaysThereAreNone()
        {
            Assert.That(Refusal(() => RecorderSetSelection.Resolve(new List<string>(), -1, new[] { "SetA" })),
                Is.EqualTo("SelectedRecorderSets names an unknown recorder set: 'SetA'. Available recorder sets: (none)."));
        }

        [Test] public void ANameSharedByTwoNamedSetsIsRefused()
        {
            var available = new List<string> { Tree, "SetA", "SetA" };
            Assert.That(Refusal(() => Resolve(available, "SetA")),
                Is.EqualTo("SelectedRecorderSets names a recorder set name shared by more than one set: " +
                           "'SetA'. Available recorder sets: 'Current Recorder Tree', 'SetA', 'SetA'."));
        }
    }
}
