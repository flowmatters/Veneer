using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class ModelTableIndexItem
    {
        [DataMember] public string Name;
        [DataMember] public string Url;

        public ModelTableIndexItem(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }
    [DataContract]
    public class ModelTableIndex
    {
        public ModelTableIndex()
        {
        }

        [DataMember] public ModelTableIndexItem[] Tables;
    }
}