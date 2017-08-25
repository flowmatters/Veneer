using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class DictResponse : VeneerResponse
    {
        [DataMember] public IEnumerable<KeyValueResponse> Entries;
    }

    [DataContract]
    public class KeyValueResponse : VeneerResponse
    {
        [DataMember] public object Key;
        [DataMember] public object Value;
    }
}
