using System;
using System.Collections.Generic;
using System.Net;
using CoreWCF.Web;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer.ExchangeObjects;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// GET /runs/{runId} lists every recorded series. When a recorder row cannot be listed, the
    /// request fails with a fault that names every row that failed and why, not with CoreWCF's
    /// opaque HTTP 400. CollectLinks carries on past each failure so the fault can name them all.
    /// </summary>
    [TestFixture]
    public class RunSummaryResultsTests
    {
        private static KeyValuePair<string, object> Result(string key, object series = null) =>
            new KeyValuePair<string, object>(key, series ?? new object());

        private static TimeSeriesLink Link(string row, string key) =>
            new TimeSeriesLink { RecordingElement = row, RecordingVariable = key };

        [Test] public void URLSafeString_TreatsNullAsEmpty()
        {
            Assert.That(SourceService.URLSafeString(null), Is.EqualTo(""));
        }

        [Test] public void CollectLinks_SkipsAResultThatCannotBeLinked_AndKeepsTheRowsOthers()
        {
            var rows = new[] { "row1" };
            var skipped = new List<RunSummary.SkippedResult>();

            var links = RunSummary.CollectLinks(rows,
                row => new[] { Result("good1"), Result("bad"), Result("good2") },
                (row, key, series) => key == "bad" ? throw new NullReferenceException() : Link(row, key),
                row => row, skipped);

            Assert.That(links, Has.Count.EqualTo(2));
            Assert.That(links[0].RecordingVariable, Is.EqualTo("good1"));
            Assert.That(links[1].RecordingVariable, Is.EqualTo("good2"));
            Assert.That(skipped, Has.Count.EqualTo(1));
            Assert.That(skipped[0].Row, Is.EqualTo("row1"));
            Assert.That(skipped[0].Error, Is.InstanceOf<NullReferenceException>());
        }

        [Test] public void CollectLinks_SkipsARowWhoseResultsCannotBeRead_AndKeepsTheOtherRows()
        {
            var rows = new[] { "row1", "broken", "row3" };
            var skipped = new List<RunSummary.SkippedResult>();

            var links = RunSummary.CollectLinks(rows,
                row => row == "broken" ? throw new InvalidOperationException("no recorder") : new[] { Result("v") },
                (row, key, series) => Link(row, key),
                row => row, skipped);

            Assert.That(links, Has.Count.EqualTo(2));
            Assert.That(links[0].RecordingElement, Is.EqualTo("row1"));
            Assert.That(links[1].RecordingElement, Is.EqualTo("row3"));
            Assert.That(skipped, Has.Count.EqualTo(1));
            Assert.That(skipped[0].Row, Is.EqualTo("broken"));
            Assert.That(skipped[0].Error.Message, Is.EqualTo("no recorder"));
        }

        [Test] public void CollectLinks_LeavesOutAResultWithNoSeries_WithoutReportingIt()
        {
            var rows = new[] { "row1" };
            var skipped = new List<RunSummary.SkippedResult>();

            var links = RunSummary.CollectLinks(rows,
                row => new[] { new KeyValuePair<string, object>("empty", null), Result("v") },
                (row, key, series) => Link(row, key),
                row => row, skipped);

            Assert.That(links, Has.Count.EqualTo(1));
            Assert.That(skipped, Is.Empty);
        }

        [Test] public void CollectLinks_StillReportsASkip_WhenTheRowCannotBeDescribed()
        {
            var rows = new[] { "row1" };
            var skipped = new List<RunSummary.SkippedResult>();

            RunSummary.CollectLinks<string, string, object>(rows,
                row => throw new InvalidOperationException("no recorder"),
                (row, key, series) => Link(row, key),
                row => throw new NullReferenceException(), skipped);

            Assert.That(skipped, Has.Count.EqualTo(1));
            Assert.That(skipped[0].Error.Message, Is.EqualTo("no recorder"));
        }

        /// <summary>A row Source cannot give results for (ArgumentNullException) has always been
        /// left out quietly; reporting it would log the same skip on every request.</summary>
        [Test] public void CollectLinks_LeavesOutARowThatCannotBeAccessed_WithoutReportingIt()
        {
            var rows = new[] { "inaccessible", "row2" };
            var skipped = new List<RunSummary.SkippedResult>();

            var links = RunSummary.CollectLinks(rows,
                row => row == "inaccessible" ? throw new ArgumentNullException() : new[] { Result("v") },
                (row, key, series) => Link(row, key),
                row => row, skipped);

            Assert.That(links, Has.Count.EqualTo(1));
            Assert.That(skipped, Is.Empty);
        }

        private static List<RunSummary.SkippedResult> Skips(int count)
        {
            var skipped = new List<RunSummary.SkippedResult>();
            for (var i = 1; i <= count; i++)
            {
                Exception error;
                try { throw new InvalidOperationException("failure " + i); }
                catch (Exception e) { error = e; }
                skipped.Add(new RunSummary.SkippedResult { Row = "row" + i, Error = error });
            }
            return skipped;
        }

        /// <summary>
        /// A summary with results that could not be listed fails, rather than return a partial
        /// list the client cannot tell from a complete one; the fault says what failed.
        /// </summary>
        [Test] public void SkippedResultsFault_NamesTheRunTheCountAndTheFirstFailure()
        {
            var fault = SourceService.SkippedResultsFault(7, Skips(3));

            Assert.That(fault.Message, Does.Contain("3 recorded result(s) of run 7"));
            Assert.That(fault.Message, Does.Contain("row1: InvalidOperationException: failure 1"));
        }

        [Test] public void SkippedResultsFault_ListsEachFailure()
        {
            var fault = SourceService.SkippedResultsFault(1, Skips(3));

            Assert.That(fault.Log, Is.EqualTo(new[]
            {
                "row1: InvalidOperationException: failure 1",
                "row2: InvalidOperationException: failure 2",
                "row3: InvalidOperationException: failure 3",
            }));
        }

        [Test] public void SkippedResultsFault_ListsAtMostTwentyFailures_AndCountsTheRest()
        {
            var fault = SourceService.SkippedResultsFault(1, Skips(25));

            Assert.That(fault.Log, Has.Length.EqualTo(21));
            Assert.That(fault.Log[19], Is.EqualTo("row20: InvalidOperationException: failure 20"));
            Assert.That(fault.Log[20], Is.EqualTo("... and 5 more"));
        }

        [Test] public void SkippedResultsFault_CarriesTheFirstFailureInFull()
        {
            var fault = SourceService.SkippedResultsFault(1, Skips(2));

            Assert.That(fault.StackTrace, Does.Contain("System.InvalidOperationException: failure 1"));
            Assert.That(fault.StackTrace, Does.Contain(nameof(Skips)), "the stack trace, not just the message");
        }

        /// <summary>
        /// An unexpected failure building the summary is reported as a JSON fault carrying its
        /// message, not left to CoreWCF, which answers 400 with an HTML page and logs nothing.
        /// (With no scenario loaded, reading the runs throws.)
        /// </summary>
        [Test] public void GetRunResults_ReportsAnUnexpectedFailureAsAFault()
        {
            var service = new SourceService { Scenario = null };

            var fault = Assert.Throws<WebFaultException<SimulationFault>>(
                (Action)(() => service.GetRunResults("1")));

            Assert.That(fault.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(fault.Detail.Message, Is.Not.Empty);
        }
    }
}
