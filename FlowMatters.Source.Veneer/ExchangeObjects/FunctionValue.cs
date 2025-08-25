using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class FunctionValue
    {
        [DataMember] public string FullName;
        [DataMember] public string Name;
        [DataMember] public string Expression;
    }
}