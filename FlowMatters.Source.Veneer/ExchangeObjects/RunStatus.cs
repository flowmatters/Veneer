using System.Runtime.Serialization;

namespace FlowMatters.Source.WebServer.ExchangeObjects
{
    [DataContract]
    public class RunStatus
    {
        [DataMember]
        public bool IsRunning { get; set; }
        
        [DataMember]
        public bool CanCancel { get; set; }

        [DataMember]
        public string StartDate { get; set; }

        [DataMember]
        public string EndDate { get; set; }

        [DataMember]
        public string CurrentDate { get; set; }

        [DataMember]
        public double PercentComplete { get; set; }

        [DataMember]
        public string Scenario { get; set; }

        //[DataMember]
        //public string[] Logs { get; set; }
    }
}