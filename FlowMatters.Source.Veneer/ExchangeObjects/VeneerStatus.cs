using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using RiverSystem;
using RiverSystem.PreProcessing.ProjectionInfo;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class VeneerStatus
    {
        public const int PROTOCOL_VERSION = 20220601;
        public VeneerStatus(RiverSystemScenario s)
        {
            Version = PROTOCOL_VERSION;
            SourceVersion = new Constants.ProductVersion().GetFullVersionString();
            ProjectFile = s.Project.FileName;
            ProjectFullFilename = (s.Project.FullFilename==null) ?
                null:
                Path.GetFullPath(s.Project.FullFilename);
            Scenario = s.Name;
            Projection = new ProjectionInfo(s.GeographicData?.Projection as AbstractProjectionInfo);
            var process = Process.GetCurrentProcess();
            PID = process.Id;
            HostExe = process.MainModule.FileName;
            User = Environment.UserName;
        }

        [DataMember]
        public int Version { get; set; }

        [DataMember]
        public string SourceVersion { get; set; }

        [DataMember]
        public string ProjectFile { get; set; }

        [DataMember]
        public string ProjectFullFilename { get; set; }

        [DataMember]
        public string Scenario{ get; set; }

        [DataMember]
        public ProjectionInfo Projection { get; set; }

        [DataMember]
        public int PID { get; set; }

        [DataMember]
        public string HostExe { get; set; }

        [DataMember]
        public string User { get; set; }

    }
}
