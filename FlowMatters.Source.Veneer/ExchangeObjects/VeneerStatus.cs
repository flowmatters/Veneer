using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using RiverSystem;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class VeneerStatus
    {
        public const int PROTOCOL_VERSION = 20180501;
        public VeneerStatus(RiverSystemScenario s)
        {
            Version = PROTOCOL_VERSION;
            SourceVersion = new Constants.ProductVersion().GetFullVersionString();
            ProjectFile = s.Project.FileName;
            Scenario = s.Name;
        }

        [DataMember]
        public int Version { get; set; }

        [DataMember]
        public string SourceVersion { get; set; }

        [DataMember]
        public string ProjectFile { get; set; }

        [DataMember]
        public string Scenario{ get; set; }
    }
}
