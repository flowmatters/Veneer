using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using FlowMatters.Source.Veneer.ExchangeObjects;
using RiverSystem;
using RiverSystem.Catchments;
using RiverSystem.Controls.Icons;
using TIME.ManagedExtensions;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.WebServer
{
    [DataContract]
    public class GeoJSONNetwork : GeoJSONCoverage
    {
        public GeoJSONNetwork(Network source)
        {
            IList<ICatchment> catchments = new List<ICatchment>(source.Catchments);
            bool schematicAsCoordinates =
                source.Nodes.OfType<Node>()
                    .All(n => n.location.E.EqualWithTolerance(0.0) && n.location.N.EqualWithTolerance(0.0));

            List<GeoJSONFeature> featureList = new List<GeoJSONFeature>();
            featureList.AddRange(from Node n in source.nodes select new GeoJSONFeature(n,source.Scenario,schematicAsCoordinates));
            foreach (Link link in source.links)
            {
                if (link.Network == null)
                    link.Network = source;
            }

            featureList.AddRange(from Link l in source.links select new GeoJSONFeature(l,source.Scenario,schematicAsCoordinates));
            featureList.AddRange(catchments.Select(c => new GeoJSONFeature((Catchment)c,source.Scenario.BoundaryForCatchment(c))));
            features = featureList.ToArray();
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