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
        public string ConfigDirectory { get; set; }
        public int Port { get; set; }
    }

    // Every call site compares these with == only, never <, >, <= or >= (verified by
    // grep, not just assumed). That is what makes it safe to insert a member and shift
    // the ordinals below it, as Info did here between Debug and Warning -- a >= or <=
    // comparison would silently reorder which levels it matches.
    //
    // Info must be mapped EXPLICITLY by every consumer. VeneerMenu.ControlAddonLog.Write
    // maps it to LogLevel.Info; the trap is that an == chain which forgets Info drops it
    // into the "else" and folds it into LogLevel.Debug. LogLevel (a different, unrelated
    // enum) IS ordinal compared ("if (level < _minimumLogLevel)" in
    // WebServerStatusControl.Append), so an operator whose panel minimum is above Debug
    // would then not see the addon's Info message at all -- an Info(...) call that
    // compiles, runs, and is silently invisible, reading as "the feature doesn't report"
    // rather than "the mapping isn't wired".
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
