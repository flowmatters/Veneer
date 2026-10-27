using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using RiverSystem.Forms;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Runs a change to the model on Source's UI thread when Veneer is hosted in the Source GUI.
    /// </summary>
    /// <remarks>
    /// Veneer serves requests on worker threads. In the GUI, some changes to the model are
    /// observed by view models that build WPF objects in response, and WPF objects can only be
    /// created on the (STA) UI thread: adding a data source to DataManager.DataGroups, say,
    /// makes the data sources tree build a node, and off the UI thread Source throws "The calling
    /// thread must be STA". Changes like that go through Run.
    ///
    /// Only the change itself should go through Run, not slow work around it (loading a file,
    /// say), so that the GUI stays responsive. VeneerCmd has no UI thread, and the action runs
    /// directly there.
    /// </remarks>
    internal static class GuiThread
    {
        /// <summary>
        /// How long to wait for the UI thread to start a change, before failing the request.
        /// A modal dialog left open in Source, for one, keeps it from starting.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        public static void Run(bool runningInGui, Action action)
        {
            Run(runningInGui ? MainForm.Instance : null, action, DefaultTimeout);
        }

        public static T Run<T>(bool runningInGui, Func<T> func)
        {
            return Run(runningInGui ? MainForm.Instance : null, func, DefaultTimeout);
        }

        internal static T Run<T>(ISynchronizeInvoke uiThread, Func<T> func, TimeSpan timeout)
        {
            T result = default(T);
            Run(uiThread, () => { result = func(); }, timeout);
            return result;
        }

        /// <summary>
        /// Run action on uiThread (directly, if there is none or this is it), and return once it
        /// has finished, raising any exception it raised.
        /// </summary>
        /// <exception cref="TimeoutException">
        /// The UI thread did not start the action within timeout. The action is then dropped, and
        /// never runs: the request has failed, and must not go on to change the model later.
        /// An action that has started is always waited for.
        /// </exception>
        internal static void Run(ISynchronizeInvoke uiThread, Action action, TimeSpan timeout)
        {
            if (uiThread == null || !uiThread.InvokeRequired)
            {
                action();
                return;
            }

            var gate = new object();
            var started = false;
            var abandoned = false;
            Exception failure = null;

            // Exceptions are caught here, not left to EndInvoke: WinForms passes on an exception
            // from BeginInvoke only as its base exception, or (under a debugger) not at all.
            var pending = uiThread.BeginInvoke(new Action(() =>
            {
                lock (gate)
                {
                    if (abandoned)
                        return;
                    started = true;
                }
                try { action(); }
                catch (Exception e) { failure = e; }
            }), null);

            if (!pending.AsyncWaitHandle.WaitOne(timeout))
            {
                lock (gate)
                {
                    if (!started)
                    {
                        abandoned = true;
                        throw new TimeoutException(string.Format(
                            "Source's user interface did not respond within {0:0} seconds, so the change was not made. " +
                            "Close any dialog open in Source and try again.", timeout.TotalSeconds));
                    }
                }
                pending.AsyncWaitHandle.WaitOne();
            }
            uiThread.EndInvoke(pending);

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
