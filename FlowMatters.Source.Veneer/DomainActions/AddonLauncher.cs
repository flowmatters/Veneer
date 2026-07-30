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
        public static void Launch(VeneerAddon addon, AddonContext context, IAddonLog log)
        {
            var env = AddonEnvironment.BuildEffective(context, addon.env);

            if (addon.script != null && addon.script.Length > 0)
            {
                LaunchScript(addon, context, env, log);
                return;
            }

            LaunchExe(addon, context, env, log);
        }

        private static void LaunchScript(VeneerAddon addon, AddonContext context,
                                         IDictionary<string, string> env, IAddonLog log)
        {
            throw new NotImplementedException("Task 9");
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
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                log.Write(string.Format("Addon '{0}' could not start: {1}", addon.name, ex.Message),
                          AddonLogLevel.Error);
                process.Dispose();
                return;
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
