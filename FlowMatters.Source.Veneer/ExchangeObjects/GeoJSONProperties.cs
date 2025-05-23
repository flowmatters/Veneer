using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FlowMatters.Source.WebServer
{
    [Serializable]
    public class GeoJSONProperties : ISerializable
    {
        public GeoJSONProperties()
        {

        }

        public GeoJSONProperties(SerializationInfo info, StreamingContext context)
        {
        }

        private Dictionary<string, object> properties = new Dictionary<string, object>();

        public void Add(string key, object value)
        {
            properties[key] = value;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            foreach (KeyValuePair<string, object> kvp in properties)
                info.AddValue(kvp.Key, kvp.Value);
        }
    }
}