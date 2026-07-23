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
        // Time-of-evaluation flags-enum in its comma form, e.g. "EndOfTimeStep, StartOfTimeStep".
        // Additive (SIFT A2): null on write and ignored by SetFunction. Older consumers unaffected.
        [DataMember] public string TimeOfEvaluation;
    }
}