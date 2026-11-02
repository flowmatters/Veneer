using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    /// <summary>
    /// GET /recorderSets: the names of the recorder sets the scenario defines, in the manager's
    /// order, and the names of the sets selected in its current configuration, in order.
    /// </summary>
    [DataContract]
    public class RecorderSetsSummary
    {
        [DataMember] public string[] Sets;
        [DataMember] public string[] Selected;
    }
}
