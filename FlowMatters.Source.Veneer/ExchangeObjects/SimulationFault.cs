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

        public SimulationFault(string message, string[] log)
        {
            Message = message;
            Log = log;
        }

        [DataMember] public string Message { get; set; }
        [DataMember] public string StackTrace { get; set; }

        /// <summary>
        /// Diagnostic messages captured from the Source log during the run. Populated when a
        /// run fails (or completes without producing a result) so the client can see why.
        /// </summary>
        [DataMember(EmitDefaultValue = false)] public string[] Log { get; set; }
    }
}
