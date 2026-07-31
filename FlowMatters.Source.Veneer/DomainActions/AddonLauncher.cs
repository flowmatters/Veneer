using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    internal static class AddonLauncher
    {
        /// <summary>
        /// Entry point. Launch runs on the WinForms menu Click handler, so nothing
        /// here may throw: an escaping exception becomes an unhandled-exception
        /// dialog in Source rather than a logged addon failure.
        /// </summary>
        public static void Launch(VeneerAddon addon, AddonContext context, IAddonLog log)
        {
            // Re-validate even though VeneerMenu disables invalid entries. Without
            // this, a null path reaches Path.Combine(dir, null) and throws
            // ArgumentNullException outside every guard below.
            var invalid = VeneerAddon.Validate(addon);
            if (invalid != null)
            {
                log.Write(string.Format("Addon '{0}' {1}", addon.name, invalid),
                          AddonLogLevel.Error);
                return;
            }

            // Path.Combine throws ArgumentNullException on a null segment, and an
            // empty project directory would leave a *relative* FileName, which
            // Windows resolves against the parent's cwd and PATH -- so an addon
            // named e.g. python.exe could silently launch something off PATH.
            if (string.IsNullOrEmpty(context.ProjectDirectory))
            {
                log.Write(string.Format(
                    "Addon '{0}' cannot run: no project directory is available. " +
                    "Load a project before launching addons.", addon.name),
                    AddonLogLevel.Error);
                return;
            }

            try
            {
                var env = AddonEnvironment.BuildEffective(context, addon.env);

                if (addon.script != null && addon.script.Length > 0)
                    LaunchScript(addon, context, env, log);
                else
                    LaunchExe(addon, context, env, log);
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not be launched: {1}",
                                        addon.name, ex.Message),
                          AddonLogLevel.Error);
            }
        }

        /// <summary>
        /// Separate entry point from Launch, not a branch inside it. Launch opens
        /// with a hard ProjectDirectory guard and builds a full child-process
        /// environment; a URL needs neither, and a wiki link routed through Launch
        /// would fail with "no project directory is available".
        /// </summary>
        public static void LaunchUrl(VeneerAddon addon, AddonContext context, IAddonLog log)
        {
            // Re-validated for the same reason Launch re-validates: VeneerMenu
            // disables invalid entries, but this is a public entry point and must
            // not assume its caller did. Here the concrete risk is a null url.
            var invalid = VeneerAddon.Validate(addon);
            if (invalid != null)
            {
                log.Write(string.Format("Addon '{0}' {1}", addon.name, invalid),
                          AddonLogLevel.Error);
                return;
            }

            try
            {
                var env = AddonEnvironment.BuildEffective(context, addon.env);

                // Normalised again after expansion: a variable whose value carries
                // whitespace would otherwise reintroduce the leading space the
                // validation-time trim removed.
                var url = AddonUrl.Normalise(
                    AddonEnvironment.Expand(AddonUrl.Normalise(addon.url), env));

                // No scheme re-check. None of the allowed prefixes contains a '%',
                // and Expand only replaces %NAME% spans, so expansion cannot alter
                // the scheme Validate already accepted.
                string error;
                if (!ShellLink.TryOpen(url, out error))
                {
                    log.Write(string.Format("Addon '{0}' could not open {1}: {2}",
                                            addon.name, url, error),
                              AddonLogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not be launched: {1}",
                                        addon.name, ex.Message),
                          AddonLogLevel.Error);
            }
        }

        /// <summary>
        /// One cmd.exe per launch, script lines written to its redirected stdin.
        /// Nothing is written to disk, so a blocked temp directory cannot stop it,
        /// and because it is a single shell session set/cd/&amp;&amp; persist across lines.
        /// </summary>
        private static void LaunchScript(VeneerAddon addon, AddonContext context,
                                         IDictionary<string, string> env, IAddonLog log)
        {
            // Default "D" format: interpolated into the filter's regexes, so it must
            // contain no metacharacters. "B"/"P" would inject braces or parens.
            var nonce = Guid.NewGuid().ToString();
            var filter = new ScriptOutputFilter(nonce);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // /V:OFF makes ! immune to a machine-wide DelayedExpansion registry
                // setting; /D skips AutoRun, which could otherwise cd or set
                // variables under the script's feet in a policy-managed deployment.
                Arguments = "/D /V:OFF",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ResolveWorkingDirectory(addon, context, env)
            };
            ApplyEnvironment(startInfo, env);

            // Fed through Run's callback rather than after it returns, so the
            // completion watcher starts only once every line is written -- a script
            // whose first line fails cannot have its Process disposed mid-write.
            Run(startInfo, addon, log, filter, stdin =>
            {
                foreach (var line in AddonScript.Generate(addon.script, nonce))
                    stdin.WriteLine(line);
            });
        }

        private static string ResolveWorkingDirectory(VeneerAddon addon, AddonContext context,
                                                      IDictionary<string, string> env)
        {
            if (string.IsNullOrEmpty(addon.workingDirectory))
                return context.ProjectDirectory;

            var expanded = AddonEnvironment.Expand(addon.workingDirectory, env);
            return Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(context.ProjectDirectory, expanded);
        }

        private static void LaunchExe(VeneerAddon addon, AddonContext context,
                                      IDictionary<string, string> env, IAddonLog log)
        {
            var path = AddonEnvironment.Expand(addon.path, env);
            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(context.ProjectDirectory, path);
            var args = (addon.args ?? new string[0])
                .Select(a => AddonEnvironment.Expand(a, env))
                .ToList();

            string fileName, arguments;
            AddonCommandLine.Compose(fullPath, args, out fileName, out arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ResolveWorkingDirectory(addon, context, env)
            };
            ApplyEnvironment(startInfo, env);

            // filter is null: exe mode has no banner and no @echo off sentinel, so
            // applying the script-mode rules would buffer the whole stream.
            // feedStdin is null: nothing is written to an exe's stdin.
            Run(startInfo, addon, log, null, null);
        }

        private static void ApplyEnvironment(ProcessStartInfo startInfo,
                                             IDictionary<string, string> env)
        {
            foreach (var kv in env)
                startInfo.Environment[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Starts the process, pumps its output, and reports completion. In exe mode
        /// filter is null and output passes through unchanged -- the script-mode
        /// filter rules assume a banner and an @echo off sentinel that an exe launch
        /// never produces, and applying them would buffer a long-running addon's
        /// entire output.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT use the Exited event. Exited can fire before the
        /// async output pump has delivered its final lines, which would drop
        /// trailing output, run filter.Flush() before the last Accept(), and read
        /// filter.CurrentStep on one thread while a threadpool thread writes it --
        /// so "failed at line N" could name the wrong line. The parameterless
        /// WaitForExit() also waits for the output handlers to finish, which is
        /// exactly the guarantee needed here.
        ///
        /// feedStdin runs before the completion watcher starts, so a fast-exiting
        /// process cannot be disposed while script lines are still being written.
        /// Both output pumps are already running by then, so the child can never
        /// block on a full stdout buffer while we are blocked writing its stdin.
        /// </remarks>
        private static void Run(ProcessStartInfo startInfo, VeneerAddon addon,
                                IAddonLog log, ScriptOutputFilter filter,
                                Action<StreamWriter> feedStdin)
        {
            var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                if (filter == null) log.Write(e.Data, AddonLogLevel.Debug);
                else foreach (var line in filter.Accept(e.Data))
                    log.Write(line, AddonLogLevel.Debug);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) log.Write(e.Data, AddonLogLevel.Warning);
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not start: {1}", addon.name, ex.Message),
                          AddonLogLevel.Error);
                process.Dispose();
                return;
            }

            // Separate guard, deliberately. If a Begin*ReadLine throws, the child
            // IS already running -- disposing and returning here would orphan it:
            // unmonitored, exit code never reported, and its pipes closed under it.
            // Log the loss of output and fall through to the watcher so it is still
            // waited on and disposed.
            try
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                log.Write(string.Format(
                    "Addon '{0}' started but its output could not be captured: {1}",
                    addon.name, ex.Message), AddonLogLevel.Warning);
            }

            // Must be guarded. If cmd exits before every line is written -- a script
            // whose first line calls exit, or cmd being killed by EDR -- WriteLine or
            // the flush inside Dispose throws IOException ("pipe has been ended").
            // Launch runs on the WinForms Click handler, so an unguarded throw would
            // surface as an unhandled-exception dialog in Source AND skip the watcher
            // below, leaking the process. Report it and still let the watcher run.
            if (feedStdin != null)
            {
                try
                {
                    using (var stdin = process.StandardInput)
                        feedStdin(stdin);
                }
                catch (Exception ex)
                {
                    // IOException is the only expected case (broken pipe), but this
                    // catches broadly for symmetry with the Start() guard above --
                    // anything escaping here becomes a dialog in Source.
                    log.Write(string.Format("Addon '{0}' script was cut short: {1}",
                                            addon.name, ex.Message),
                              AddonLogLevel.Error);
                }
            }

            Task.Run(() =>
            {
                // MUST stay the parameterless overload. It waits for EOF on both
                // redirected streams as well as process exit, which is the only
                // reason Flush() and CurrentStep can be read here without locking:
                // every Accept() on the pump thread has already returned. In .NET 5+
                // WaitForExit(int) does NOT wait for the output pumps, so adding a
                // timeout here would silently introduce a data race on the filter's
                // state and could report the wrong step number. Use
                // WaitForExitAsync() if a timeout is ever needed -- it preserves the
                // guarantee and frees this threadpool thread.
                process.WaitForExit();

                if (filter != null)
                    foreach (var line in filter.Flush())
                        log.Write(line, AddonLogLevel.Debug);

                if (process.ExitCode != 0)
                {
                    var where = filter != null && filter.CurrentStep > 0
                        ? string.Format(" at line {0}", filter.CurrentStep)
                        : string.Empty;
                    log.Write(string.Format("Addon '{0}' failed{1} with exit code {2}",
                                            addon.name, where, process.ExitCode),
                              AddonLogLevel.Error);
                }

                process.Dispose();
            });
        }
    }
}
