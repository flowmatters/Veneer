using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.WebServer;
using RiverSystem.ManagedExtensions;
using Console = System.Console;

namespace FlowMatters.Source.Veneer.RemoteScripting
{
    public class CustomEndPoint
    {
        public string endpoint { get; set; }

        public string[] script { get; set; }
        public string[] parameters { get; set; }

        public string GetScript(string[] parameterValues)
        {
            var fullScript = String.Join(Environment.NewLine, script);
            for (var i = 0; i < parameters.Length; i++)
            {
                var paramName = parameters[i];
                var paramValue = (parameterValues.Length > i) ? parameterValues[i] : "";
                var key = "{{" + paramName + "}}";
                fullScript = fullScript.Replace(key, paramValue);
            }

            return fullScript;
        }
    }
}
