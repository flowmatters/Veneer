﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.CSharp.RuntimeBinder;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystem.Controls.Icons;
using TIME.DataTypes.Polygons;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.WebServer
{
    [DataContract]
    public class GeoJSONNetwork
    {
        public GeoJSONNetwork(Network source)
        {
            IList<ICatchment> catchments = new List<ICatchment>(source.Catchments);

            List<GeoJSONFeature> featureList = new List<GeoJSONFeature>();
            featureList.AddRange(from Node n in source.nodes select new GeoJSONFeature(n));
            foreach (Link link in source.links)
            {
                if (link.Network == null)
                    link.Network = source;
            }

            featureList.AddRange(from Link l in source.links select new GeoJSONFeature(l));
            featureList.AddRange(catchments.Select(c => new GeoJSONFeature((Catchment)c,source.Scenario.BoundaryForCatchment(c))));
            features = featureList.ToArray();
        }

        [DataMember]
        public string type
        {
            get { return _type; }
            private set { _type = value; }
        }
        private string _type = "FeatureCollection";

        [DataMember]
        public GeoJSONFeature[] features;

    }

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

        public GeoJSONFeature(Node n)
        {           
            id = NodeURL(n);

            properties.Add("name",n.Name);
            properties.Add(FeatureTypeProperty,"node");

            properties.Add(ResourceProperty,
                           UriTemplates.Resources.Replace("{resourceName}", RetrieveNodeModel(n).GetType().Name));

            geometry = new GeoJSONGeometry(n.location);
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
    }

    public static class GeoJSONGeometryType
    {
        public const string Point = "Point";
        public const string LineString = "LineString";
        public const string Polygon = "Polygon";
        public const string MultiPoint = "MultiPoint";
        public const string MultiLineString = "MultiLineString";
        public const string MultiPolygon = "MultiPolygon";
        public const string GeometryCollection = "GeometryCollection";
    }

    [Serializable]
    public class GeoJSONProperties : ISerializable
    {
        public GeoJSONProperties()
        {
            
        }

        public GeoJSONProperties(SerializationInfo info, StreamingContext context)
        {
        }

        private Dictionary<string,object> properties = new Dictionary<string, object>();

        public void Add(string key, object value)
        {
            properties[key] = value;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            foreach (KeyValuePair<string, object> kvp in properties)
                info.AddValue(kvp.Key,kvp.Value);
        }
    }
}