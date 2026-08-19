using FlowMatters.Source.Veneer.Addons;

namespace FlowMatters.Source.Veneer.DomainActions
{
    /// <summary>
    /// Everything an addon launch needs from the host, as primitives.
    /// Deliberately carries no RiverSystem or TIME types so that the launch
    /// logic can be unit tested without a loaded scenario.
    /// </summary>
    internal class AddonContext
    {
        public string ProjectDirectory { get; set; }
        public string ProjectFile { get; set; }
        public int Port { get; set; }
    }

    internal enum AddonLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    internal interface IAddonLog
    {
        void Write(string message, AddonLogLevel level);
    }

    /// <summary>
    /// Reports that an addon launch has finished -- the process exited, or never
    /// started. NOT that Launch() returned: Launch returns as soon as the completion
    /// watcher is queued, and cannot even distinguish a failed Start() from a running
    /// process, so firing on its return would clear the running state immediately on
    /// every successful launch.
    /// </summary>
    internal interface IAddonLifecycle
    {
        void Finished(VeneerAddon addon);
    }
}
