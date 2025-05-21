using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FlowMatters.Source.WebServer;

[DataContract]
public class GeoJSONProperties
{
    public GeoJSONProperties()
    {
            
    }

    [DataMember]
    public Dictionary<string, object> properties = new Dictionary<string, object>();

    public void Add(string key, object value)
    {
        properties[key] = value;
    }
}