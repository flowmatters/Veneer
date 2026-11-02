using System;
using System.Diagnostics;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// The one place that hands a URL to the shell. Returns false rather than
    /// throwing because every caller is a WinForms Click handler, where an
    /// escaping exception becomes an unhandled-exception dialog in Source.
    /// </summary>
    internal static class ShellLink
    {
        public static bool TryOpen(string url, out string error)
        {
            error = null;
            try
            {
                // .NET Core flipped the UseShellExecute default from true to
                // false, so Process.Start(string) tries to execute the URL as a
                // file and throws Win32Exception. Setting it explicitly restores
                // the browser/handler behaviour .NET Framework had by default,
                // and is a harmless no-op on legacy_ci where it already is.
                var startInfo = new ProcessStartInfo(url) { UseShellExecute = true };

                // Null when an already-running handler absorbs the request -- a
                // browser opening a tab starts no new process.
                var process = Process.Start(startInfo);
                if (process != null)
                    process.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
