using System;
using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    [DataContract]
    public class IronPythonScript
    {
        [DataMember] public string Script { get; set; }
    }
}