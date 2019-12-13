using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    class SimulationFault
    {
        public SimulationFault(Exception e)
        {
            Message = e.Message;
            StackTrace = e.StackTrace;
        }

        [DataMember] public string Message { get; set; }
        [DataMember] public string StackTrace { get; set; }
    }
}
