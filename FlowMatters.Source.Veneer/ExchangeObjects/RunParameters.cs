using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [Serializable]
    public class RunParameters : ISerializable
    {
        public RunParameters()
        {

        }

        public RunParameters(SerializationInfo info, StreamingContext context)
        {
            foreach (var entry in info)
            {
                properties.Add(entry.Name, entry.Value);
            }
        }

        private Dictionary<string, object> properties = new Dictionary<string, object>();
        public Dictionary<string, object> Params { get { return properties; } }

        public void Add(string key, object value)
        {
            properties[key] = value;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            foreach (KeyValuePair<string, object> kvp in properties)
                info.AddValue(kvp.Key, kvp.Value);
        }

        //[DataMember] public string Start;
        //[DataMember] public string End;
        //[DataMember] public int ForecastLength;
        //[DataMember] public string InputSet;

    }
}
