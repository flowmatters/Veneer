using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer;
using Microsoft.CSharp.RuntimeBinder;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystem.Forms.SchematicBuilder;
using TIME.DataTypes;
using TIME.DataTypes.Polygons;
using Network = RiverSystem.Network;

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

        public GeoJSONFeature(Node n,RiverSystemScenario scenario,bool useSchematicLocation)
        {           
            id = NodeURL(n,scenario.Network);

            properties.Add("name",n.Name);
            properties.Add(FeatureTypeProperty, "node");
            Coordinate loc = n.location;
            var schematic = ScriptHelpers.GetSchematic(scenario);
            if (schematic != null)
            {
                PointF schematicLocation = SchematicLocationForNode(n, schematic);
                properties.Add("schematic_location",new double[] {schematicLocation.X,schematicLocation.Y});
                if (useSchematicLocation)
                    loc = new Coordinate(schematicLocation);
            }
            properties.Add(ResourceProperty,
                UriTemplates.Resources.Replace("{resourceName}", ResourceName(n)));
            properties.Add("elevation", n.elevation);

            geometry = new GeoJSONGeometry(loc);
        }

        private static PointF SchematicLocationForNode(TIME.DataTypes.NodeLinkNetwork.Node n, SchematicNetworkConfigurationPersistent schematic)
        {
            return schematic.ExistingFeatureShapeProperties.Where(shape=>shape.Feature==n).Select(shape=>shape.Location).FirstOrDefault();
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

        private static string NodeURL(Node n,Network network)
        {
            return UriTemplates.Node.Replace("{nodeId}", network.nodes.indexOf(n).ToString());//n.id.ToString());
        }

        private static string LinkURL(ILink l)
        {
            Link link = (Link) l;
            return UriTemplates.Link.Replace("{linkId}", link.Network.links.indexOf(link).ToString());
        }

        public GeoJSONFeature(Link l, RiverSystemScenario scenario, bool useSchematicLocation)
        {
            id = LinkURL(l);
            properties.Add("name", l.Name);
            properties.Add(FeatureTypeProperty, "link");
            properties.Add("from_node", NodeURL((Node)l.UpstreamNode,l.Network));
            properties.Add("to_node", NodeURL((Node)l.DownstreamNode, l.Network));
            properties.Add("length", l.Length);

            if (useSchematicLocation)
            {
                var schematic = ScriptHelpers.GetSchematic(scenario);
                if (scenario != null)
                {

                    geometry = new GeoJSONGeometry(SchematicLocationForNode(l.from,schematic),SchematicLocationForNode(l.to,schematic));
                    return;
                }
            }
            geometry = new GeoJSONGeometry(l, useSchematicLocation);
        }

        public GeoJSONFeature(Catchment c,GEORegion region)
        {
            id = UriTemplates.Catchment.Replace("{catchmentId}", c.id.ToString());
            properties.Add("name", c.Name);
            properties.Add(FeatureTypeProperty,"catchment");
            properties.Add("link", c.DownstreamLink != null ? LinkURL(c.DownstreamLink) : null);
            properties.Add("areaInSquareMeters", c.characteristics.areaInSquareMeters);
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