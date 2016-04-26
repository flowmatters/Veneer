using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using FlowMatters.Source.WebServer;
using Microsoft.CSharp.RuntimeBinder;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystemGUI_II.SchematicBuilder;
using TIME.DataTypes.Polygons;

namespace FlowMatters.Source.Veneer.ExchangeObjects
{
    [DataContract]
    public class GeoJSONFeature
    {
        private const string FeatureTypeProperty = "feature_type";
        private const string ResourceProperty = "icon";

        [DataMember]
        public string type
        {
            get { return _type; }
            private set { _type = value; }
        }
        private string _type = "Feature";
        private GeoJSONProperties _properties = new GeoJSONProperties();


        [DataMember]
        public string id { get; private set; }

        [DataMember]
        public GeoJSONGeometry geometry { get; private set; }

        [DataMember]
        public GeoJSONProperties properties
        {
            get { return _properties; }
            private set { _properties = value; }
        }

        public GeoJSONFeature(Node n,RiverSystemScenario scenario)
        {           
            id = NodeURL(n);

            properties.Add("name",n.Name);
            properties.Add(FeatureTypeProperty, "node");
            object tmp = null;
            scenario.AuxiliaryInformation.TryGetValue(SchematicNetworkControl.AUX_CONFIG, out tmp);
            SchematicNetworkConfigurationPersistent schematic = tmp as SchematicNetworkConfigurationPersistent;
            if (schematic != null)
            {
                PointF schematicLocation = schematic.ExistingFeatureShapeProperties.Where(shape=>shape.Feature==n).Select(shape=>shape.Location).First();
                properties.Add("schematic_location",new double[] {schematicLocation.X,schematicLocation.Y});
            }
            properties.Add(ResourceProperty,
                UriTemplates.Resources.Replace("{resourceName}", ResourceName(n)));

            geometry = new GeoJSONGeometry(n.location);
        }

        private static string ResourceName(Node n)
        {
            object o = RetrieveNodeModel(n) ?? (object) n.FlowPartitioning;
            return o.GetType().Name;
        }

        private static NodeModel RetrieveNodeModel(dynamic n)
        {
            try
            {
                return n.NodeModel;
            }
            catch (RuntimeBinderException)
            {
                return n.NodeModels[0];
            }
        }

        private static string NodeURL(INode n)
        {
            return UriTemplates.Node.Replace("{nodeId}", n.id.ToString());
        }

        private static string LinkURL(ILink l)
        {
            Link link = (Link) l;
            return UriTemplates.Link.Replace("{linkId}", link.Network.links.indexOf(link).ToString());
        }

        public GeoJSONFeature(Link l)
        {
            id = LinkURL(l);
            properties.Add("name",l.Name);
            properties.Add(FeatureTypeProperty,"link");
            properties.Add("from_node", NodeURL(l.UpstreamNode));
            properties.Add("to_node", NodeURL(l.DownstreamNode));
            
            geometry = new GeoJSONGeometry(l);
        }

        public GeoJSONFeature(Catchment c,GEORegion region)
        {
            id = UriTemplates.Catchment.Replace("{catchmentId}", c.id.ToString());
            properties.Add("name", c.Name);
            properties.Add(FeatureTypeProperty,"catchment");
            properties.Add("link",LinkURL(c.DownstreamLink));
            geometry = new GeoJSONGeometry(region);
        }

        public GeoJSONFeature(GEORegion region, Dictionary<string,object> attributes)
        {
            geometry = new GeoJSONGeometry(region);
            foreach (var kvp in attributes)
            {
                properties.Add(kvp.Key,kvp.Value);
            }
        }
    }
}