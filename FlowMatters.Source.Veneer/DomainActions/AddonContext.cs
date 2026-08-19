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

    // Every call site compares these with == only, never <, >, <= or >= (verified by
    // grep, not just assumed). That is what makes it safe to insert a member and shift
    // the ordinals below it, as Info did here between Debug and Warning -- a >= or <=
    // comparison would silently reorder which levels it matches.
    //
    // That is only half the question, though: AddonLogLevel itself is fine, but its one
    // consumer is not. VeneerMenu.ControlAddonLog.Write (VeneerMenu.cs:277-279) is a
    // three-way == chain -- Error, Warning, else -- so Info falls into the "else" and is
    // folded into LogLevel.Debug. LogLevel (a different, unrelated enum) IS ordinal
    // compared, at WebServerStatusControl.xaml.cs:223 ("if (level < _minimumLogLevel)"),
    // so an operator who has raised their panel's minimum level above Debug will not see
    // an addon's Info message at all. At this commit Info has no consumer that maps it
    // correctly -- Task 6 is expected to add one. Until then, an Info(...) log call added
    // by Task 4 will compile, run, and be silently invisible in the panel, which would
    // read as "the feature doesn't report" rather than "the mapping isn't wired yet".
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
