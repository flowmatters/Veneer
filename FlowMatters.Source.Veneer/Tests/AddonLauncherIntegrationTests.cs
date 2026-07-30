using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.Veneer.DomainActions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    /// <summary>
    /// Drives AddonLauncher against real child processes. These cover what the
    /// unit tests cannot: that the composed command line actually runs, that the
    /// injected environment reaches the child, and that script mode's filtering
    /// and failure attribution work against genuine cmd.exe output.
    ///
    /// The project directory deliberately contains a space, exercising the quoting
    /// bug that existed before this feature (an unquoted "/C " + path).
    /// </summary>
    [TestFixture]
    public class AddonLauncherIntegrationTests
    {
        private string _dir;

        private sealed class FakeLog : IAddonLog
        {
            private readonly List<string> _lines = new List<string>();

            public void Write(string message, AddonLogLevel level)
            {
                lock (_lines) _lines.Add(message);
            }

            public string[] Lines
            {
                get { lock (_lines) return _lines.ToArray(); }
            }

            /// <summary>Polls until the predicate holds, so tests need no sleeps.</summary>
            public bool WaitFor(Func<string[], bool> predicate, int timeoutMs = 30000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (predicate(Lines)) return true;
                    System.Threading.Thread.Sleep(25);
                }
                return false;
            }

            public string Dump()
            {
                return string.Join(Environment.NewLine, Lines);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(),
                                "veneer addon tests " + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { /* a child may still hold a handle; harmless in temp */ }
        }

        private AddonContext Context()
        {
            return new AddonContext
            {
                ProjectDirectory = _dir,
                ProjectFile = Path.Combine(_dir, "model.rsproj"),
                Port = 9876
            };
        }

        private string WriteBat(string name, string body)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
            return path;
        }

        [Test]
        public void ExeMode_PassesArgumentsAndInjectedEnvironment()
        {
            WriteBat("show.bat", "echo ARG1=[%~1] ARG2=[%~2] LABEL=[%RUN_LABEL%] PORT=[%VENEER_PORT%]");

            var addon = new VeneerAddon
            {
                name = "show", type = "exe", path = "show.bat",
                args = new[] { "arg with space", "%VENEER_PORT%" },
                env = new Dictionary<string, string> { { "RUN_LABEL", "nightly" } }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log);

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("ARG1="))), Is.True,
                        "no output captured. Log was:\n" + log.Dump());

            var line = log.Lines.First(x => x.Contains("ARG1="));
            Assert.That(line, Does.Contain("ARG1=[arg with space]"), "argument quoting failed");
            Assert.That(line, Does.Contain("ARG2=[9876]"), "%VENEER_PORT% was not expanded in args");
            Assert.That(line, Does.Contain("LABEL=[nightly]"), "addon env did not reach the child");
            Assert.That(line, Does.Contain("PORT=[9876]"), "VENEER_PORT was not injected");
        }

        [Test]
        public void ScriptMode_PersistsStateAcrossLinesAndStripsScaffolding()
        {
            var addon = new VeneerAddon
            {
                name = "nightly", type = "script",
                script = new[]
                {
                    "set STAGE=one",
                    "echo stage is %STAGE%"
                }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log);

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("stage is one"))), Is.True,
                        "set did not persist to a later line. Log was:\n" + log.Dump());

            var lines = log.Lines;
            Assert.That(lines.Any(x => x.Contains("Microsoft Windows [Version")), Is.False,
                        "cmd's banner leaked into the log");
            Assert.That(lines.Any(x => x.Contains("##VENEER:")), Is.False,
                        "a step marker leaked into the log");
            Assert.That(lines.Any(x => x.Contains("if errorlevel 1 exit")), Is.False,
                        "an injected guard leaked into the log");
            Assert.That(lines.Any(x => x.Trim() == "exit 0"), Is.False,
                        "the terminator leaked into the log");
            Assert.That(lines.Any(x => x.Contains(">@echo off")), Is.False,
                        "the prompt line leaked into the log");
        }

        [Test]
        public void ScriptMode_StopsAtFirstFailureAndAttributesTheLine()
        {
            var addon = new VeneerAddon
            {
                name = "failing", type = "script",
                script = new[]
                {
                    "echo first line ran",
                    "cmd /c exit 7",
                    "echo THIRD_SHOULD_NOT_RUN"
                }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log);

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("exit code 7"))), Is.True,
                        "no failure was reported. Log was:\n" + log.Dump());

            var failure = log.Lines.First(x => x.Contains("exit code 7"));
            Assert.That(failure, Does.Contain("at line 2"),
                        "the failure was not attributed to the failing line");
            Assert.That(log.Lines.Any(x => x.Contains("THIRD_SHOULD_NOT_RUN")), Is.False,
                        "execution continued past the failing line");
        }

        [Test]
        public void InvalidAddon_IsReportedAndNothingRuns()
        {
            var addon = new VeneerAddon { name = "broken", type = "exe" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log);

            Assert.That(log.Lines.Length, Is.EqualTo(1), log.Dump());
            Assert.That(log.Lines[0], Does.Contain("neither 'path' nor 'script'"));
        }

        [Test]
        public void MissingProjectDirectory_IsReportedRatherThanThrowing()
        {
            var addon = new VeneerAddon { name = "x", type = "exe", path = "tool.exe" };
            var context = new AddonContext { ProjectDirectory = null, Port = 1 };

            var log = new FakeLog();
            Assert.DoesNotThrow(() => AddonLauncher.Launch(addon, context, log),
                                "Launch runs on the menu Click handler and must never throw");
            Assert.That(log.Lines.Length, Is.EqualTo(1), log.Dump());
            Assert.That(log.Lines[0], Does.Contain("no project directory"));
        }
    }
}
