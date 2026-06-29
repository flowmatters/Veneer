using System.Runtime.Serialization;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class SchematicTagMap
    {
        [DataMember(Name = "viewBox")]
        public double[] ViewBox { get; set; }

        [DataMember(Name = "nodes")]
        public SchematicNodeTag[] Nodes { get; set; }

        [DataMember(Name = "links")]
        public SchematicLinkTag[] Links { get; set; }
    }

    [DataContract]
    public class SchematicNodeTag
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "tags")]
        public string[] Tags { get; set; }

        [DataMember(Name = "icon_kind")]
        public string IconKind { get; set; }  // "svg" or "png"

        [DataMember(Name = "icon_shape", EmitDefaultValue = false)]
        public string IconShape { get; set; }  // null for icon_kind == "png"

        [DataMember(Name = "hg_value")]
        public string HgValue { get; set; }
    }

    [DataContract]
    public class SchematicLinkTag
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "tags")]
        public string[] Tags { get; set; }

        [DataMember(Name = "hg_value")]
        public string HgValue { get; set; }
    }
}
