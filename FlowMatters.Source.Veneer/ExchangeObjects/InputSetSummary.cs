using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class InputSetSummary
    {
        [DataMember] public string HierarchicalName;
        [DataMember] public string URL;
        [DataMember] public string Name;
        [DataMember] public string[] Configuration;
        [DataMember] public bool ReloadOnRun;
        [DataMember] public bool RelativePath;
        [DataMember] public string Filename;
    }
}