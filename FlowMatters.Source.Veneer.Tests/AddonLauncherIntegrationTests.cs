using FlowMatters.Source.Veneer.Addons;
using FlowMatters.Source.Veneer.DomainActions;
using System.Diagnostics;

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
            private readonly List<KeyValuePair<string, AddonLogLevel>> _entries =
                new List<KeyValuePair<string, AddonLogLevel>>();

            public void Write(string message, AddonLogLevel level)
            {
                lock (_entries) _entries.Add(new KeyValuePair<string, AddonLogLevel>(message, level));
            }

            public string[] Lines
            {
                get { lock (_entries) return _entries.Select(e => e.Key).ToArray(); }
            }

            /// <summary>The level of the first line containing `substring`, or null.</summary>
            public AddonLogLevel? LevelOf(string substring)
            {
                lock (_entries)
                {
                    foreach (var e in _entries)
                        if (e.Key != null && e.Key.Contains(substring)) return e.Value;
                    return null;
                }
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

        private sealed class FakeLifecycle : IAddonLifecycle
        {
            private int _count;

            public int Count { get { return System.Threading.Volatile.Read(ref _count); } }

            public void Finished(VeneerAddon addon)
            {
                System.Threading.Interlocked.Increment(ref _count);
            }

            public bool WaitForFinish(int timeoutMs = 30000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (Count > 0) return true;
                    System.Threading.Thread.Sleep(25);
                }
                return false;
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
                name = "show",
                type = "exe",
                path = "show.bat",
                args = new[] { "arg with space", "%VENEER_PORT%" },
                env = new Dictionary<string, string> { { "RUN_LABEL", "nightly" } }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("ARG1="))), Is.True,
                        "no output captured. Log was:\n" + log.Dump());

            var line = log.Lines.First(x => x.Contains("ARG1="));
            AddonAssert.Contains(line, "ARG1=[arg with space]", "argument quoting failed");
            AddonAssert.Contains(line, "ARG2=[9876]", "%VENEER_PORT% was not expanded in args");
            AddonAssert.Contains(line, "LABEL=[nightly]", "addon env did not reach the child");
            AddonAssert.Contains(line, "PORT=[9876]", "VENEER_PORT was not injected");
        }

        [Test]
        public void ScriptMode_PersistsStateAcrossLinesAndStripsScaffolding()
        {
            var addon = new VeneerAddon
            {
                name = "nightly",
                type = "script",
                script = new[]
                {
                    "set STAGE=one",
                    "echo stage is %STAGE%"
                }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

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
                name = "failing",
                type = "script",
                script = new[]
                {
                    "echo first line ran",
                    "cmd /c exit 7",
                    "echo THIRD_SHOULD_NOT_RUN"
                }
            };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("exit code 7"))), Is.True,
                        "no failure was reported. Log was:\n" + log.Dump());

            var failure = log.Lines.First(x => x.Contains("exit code 7"));
            AddonAssert.Contains(failure, "at line 2",
                                 "the failure was not attributed to the failing line");
            Assert.That(log.Lines.Any(x => x.Contains("THIRD_SHOULD_NOT_RUN")), Is.False,
                        "execution continued past the failing line");
        }

        [Test]
        public void InvalidAddon_IsReportedAndNothingRuns()
        {
            var addon = new VeneerAddon { name = "broken", type = "exe" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.Lines.Length, Is.EqualTo(1), log.Dump());
            AddonAssert.Contains(log.Lines[0], "neither 'path', 'script' nor 'url'");
        }

        [Test]
        public void MissingProjectDirectory_IsReportedRatherThanThrowing()
        {
            var addon = new VeneerAddon { name = "x", type = "exe", path = "tool.exe" };
            var context = new AddonContext { ProjectDirectory = null, Port = 1 };

            var log = new FakeLog();
            Assert.DoesNotThrow(() => AddonLauncher.Launch(addon, context, log, new FakeLifecycle()),
                                "Launch runs on the menu Click handler and must never throw");
            Assert.That(log.Lines.Length, Is.EqualTo(1), log.Dump());
            AddonAssert.Contains(log.Lines[0], "no project directory");
        }

        [Test]
        public void Lifecycle_FiresOnceOnCleanExit()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            System.Threading.Thread.Sleep(250);   // catch a late second fire
            Assert.That(life.Count, Is.EqualTo(1));
        }

        // The failure mode this whole design turns on: Launch returns as soon as the
        // watcher is queued. If Finished were fired at Launch's return, the count would
        // clear before the child had even run and the menu item would never disable.
        [Test]
        public void Lifecycle_FiresAfterTheProcessExitsNotWhenLaunchReturns()
        {
            WriteBat("slow.bat", "ping -n 3 127.0.0.1 > nul\r\necho done");
            var addon = new VeneerAddon { name = "slow", type = "exe", path = "slow.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.Count, Is.EqualTo(0), "Finished fired before the child exited");
            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
        }
        // ^ The one mildly timing-dependent test here: it leans on `ping -n 3` giving
        //   ~2s of headroom. If it fails, check the log first -- if cmd.exe is blocked in
        //   the environment, Start() throws and Finished fires synchronously, which is a
        //   broken environment rather than a broken guarantee.

        [Test]
        public void Lifecycle_FiresOnceOnNonZeroExit()
        {
            WriteBat("bad.bat", "exit /b 3");
            var addon = new VeneerAddon { name = "bad", type = "exe", path = "bad.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            System.Threading.Thread.Sleep(250);
            Assert.That(life.Count, Is.EqualTo(1));
        }

        // Must be a NON-batch bogus path. AddonCommandLine.Compose routes .bat/.cmd
        // through cmd.exe, which always starts, and script mode always launches cmd.exe --
        // so WriteBat cannot produce a Start() failure.
        [Test]
        public void Lifecycle_FiresOnceWhenStartFails()
        {
            var addon = new VeneerAddon { name = "nope", type = "exe", path = "nosuch.exe" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(5000), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            Assert.That(life.Count, Is.EqualTo(1));
        }

        [Test]
        public void Lifecycle_FiresOnceOnValidationFailure()
        {
            var addon = new VeneerAddon { name = "broken", type = "script" };   // no script lines

            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), new FakeLog(), life);

            Assert.That(life.Count, Is.EqualTo(1));   // synchronous, no wait needed
        }

        [Test]
        public void Lifecycle_FiresOnceWhenThereIsNoProjectDirectory()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };
            var context = new AddonContext { ProjectDirectory = null, Port = 9876 };

            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, context, new FakeLog(), life);

            Assert.That(life.Count, Is.EqualTo(1));
        }

        [Test]
        public void CleanExitLogsAFinishedLineAtInfo()
        {
            WriteBat("ok.bat", "echo done");
            var addon = new VeneerAddon { name = "ok", type = "exe", path = "ok.bat" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("finished"))), Is.True,
                        "no finished line. Log was:\n" + log.Dump());
            Assert.That(log.LevelOf("finished"), Is.EqualTo(AddonLogLevel.Info));
        }

        [Test]
        public void ChildOutputStaysAtDebugSoAChattyAddonCannotDrownTheLifecycleLines()
        {
            WriteBat("noisy.bat", "echo CHATTER");
            var addon = new VeneerAddon { name = "noisy", type = "exe", path = "noisy.bat" };

            var log = new FakeLog();
            AddonLauncher.Launch(addon, Context(), log, new FakeLifecycle());

            Assert.That(log.WaitFor(l => l.Any(x => x.Contains("CHATTER"))), Is.True,
                        "no child output. Log was:\n" + log.Dump());
            Assert.That(log.LevelOf("CHATTER"), Is.EqualTo(AddonLogLevel.Debug));
        }

        [Test]
        public void AFailedRunLogsNoFinishedLine()
        {
            WriteBat("bad.bat", "exit /b 3");
            var addon = new VeneerAddon { name = "bad", type = "exe", path = "bad.bat" };

            var log = new FakeLog();
            var life = new FakeLifecycle();
            AddonLauncher.Launch(addon, Context(), log, life);

            Assert.That(life.WaitForFinish(), Is.True, "lifecycle never fired. Log was:\n" + log.Dump());
            Assert.That(log.Lines.Any(x => x.Contains("finished")), Is.False,
                        "a failure was reported as finished. Log was:\n" + log.Dump());
        }
    }
}
