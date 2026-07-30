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
        Warning,
        Error
    }

    internal interface IAddonLog
    {
        void Write(string message, AddonLogLevel level);
    }
}
