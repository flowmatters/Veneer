using System.Threading;
using TIME.Management;

namespace FlowMatters.Source.Veneer
{
    /// <summary>
    /// Keeps <c>TIME.Management.Log.MessageRecieved</c> permanently non-null.
    /// </summary>
    /// <remarks>
    /// TIME.Management.Log.OnMessageRecieved null-checks MessageRecieved on the logging
    /// thread but re-reads the field inside the work item it queues to the thread pool.
    /// If the last subscriber detaches between those two points (TriggerRun's finally
    /// block racing a message logged near run completion), the queued work item invokes
    /// a null delegate: a NullReferenceException on a thread-pool thread, which
    /// terminates the process. A permanent no-op subscriber keeps the field non-null,
    /// so a message that loses the race is dropped instead of fatal.
    ///
    /// This lives outside SourceService so that it can be installed before anything
    /// logs. Registering it from SourceService's static constructor left plugin and
    /// project loading — both of which log heavily — exposed to the race, because that
    /// constructor does not run until the web server is being configured.
    /// Proper fix (capture the delegate before queueing) reported to eWater.
    /// </remarks>
    public static class LogDispatchGuard
    {
        private static readonly LogAction KeepAlive = (sender, args) => { };

        private static int _installed;

        /// <summary>
        /// Subscribes the keep-alive. Idempotent and safe to call from any thread.
        /// Call as early as possible in each host: every message written before the
        /// first call is still exposed to the race.
        /// </summary>
        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0)
                return;

            TIME.Management.Log.MessageRecieved += KeepAlive;
        }
    }
}
