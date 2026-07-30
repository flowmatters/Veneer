using System.Collections.Generic;
using System.Linq;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class AddonScriptTests
    {
        private const string Nonce = "abc-123";

        [Test]
        public void Generate_EmitsEchoOffMarkersGuardsAndTerminator()
        {
            var lines = AddonScript.Generate(new[] { "one", "two" }, Nonce).ToList();
            Assert.That(lines, Is.EqualTo(new[]
            {
                "@echo off",
                "echo ##VENEER:abc-123:1",
                "one",
                "if errorlevel 1 exit %errorlevel%",
                "echo ##VENEER:abc-123:2",
                "two",
                "if errorlevel 1 exit %errorlevel%",
                "exit 0"
            }));
        }

        private static List<string> Run(ScriptOutputFilter filter, IEnumerable<string> raw)
        {
            var kept = new List<string>();
            foreach (var line in raw) kept.AddRange(filter.Accept(line));
            kept.AddRange(filter.Flush());
            return kept;
        }

        [Test]
        public void Filter_DropsBannerPromptScaffolding_KeepsEchoAndOutput()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[]
            {
                "Microsoft Windows [Version 10.0.26200.8875]",
                "(c) Microsoft Corporation. All rights reserved.",
                "",
                @"C:\models\catchment>@echo off",
                "echo ##VENEER:abc-123:1",
                "##VENEER:abc-123:1",
                "python calibrate.py",
                "calibration complete",
                "if errorlevel 1 exit %errorlevel%",
                "exit 0"
            });

            Assert.That(kept, Is.EqualTo(new[] { "python calibrate.py", "calibration complete" }));
        }

        [Test]
        public void Filter_RecordsCurrentStep()
        {
            var filter = new ScriptOutputFilter(Nonce);
            Run(filter, new[]
            {
                @"C:\x>@echo off",
                "echo ##VENEER:abc-123:1",
                "##VENEER:abc-123:1",
                "first",
                "echo ##VENEER:abc-123:3",
                "##VENEER:abc-123:3",
                "third"
            });
            Assert.That(filter.CurrentStep, Is.EqualTo(3));
        }

        [Test]
        public void Filter_TrimsLineEndingsBeforeMatching()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[]
            {
                "C:\\x>@echo off\r",
                "if errorlevel 1 exit %errorlevel%\r",
                "real output\r"
            });
            Assert.That(kept, Is.EqualTo(new[] { "real output" }));
        }

        [Test]
        public void Filter_FlushesWhenSentinelNeverArrives()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[] { "catastrophic cmd failure", "second line" });
            Assert.That(kept, Is.EqualTo(new[] { "catastrophic cmd failure", "second line" }),
                        "a stream with no sentinel must not be silently discarded");
        }

        [Test]
        public void Filter_DoesNotDropOutputResemblingAMarkerWithADifferentNonce()
        {
            var filter = new ScriptOutputFilter(Nonce);
            var kept = Run(filter, new[] { @"C:\x>@echo off", "##VENEER:other:7" });
            Assert.That(kept, Is.EqualTo(new[] { "##VENEER:other:7" }));
        }

        [Test]
        public void Filter_AppliesStateEvenWhenTheResultIsIgnored()
        {
            var filter = new ScriptOutputFilter(Nonce);

            // Result deliberately discarded. Accept must not be a lazy iterator:
            // if it were, the sentinel would go unseen and the guard below would
            // be buffered and flushed instead of dropped.
            filter.Accept(@"C:\x>@echo off");

            var kept = Run(filter, new[] { AddonScript.Guard, "real output" });
            Assert.That(kept, Is.EqualTo(new[] { "real output" }),
                        "Accept must apply its state transition on the call, not on enumeration");
        }
    }
}
