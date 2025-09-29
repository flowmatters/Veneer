using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class FunctionValue
    {
        [DataMember] public string FullName;
        [DataMember] public string Name;
        [DataMember] public string Expression;
        [DataMember] public string Units;
        [DataMember] public double InitialValue;
    }
}