using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using RiverSystem;
using TIME.DataTypes;
using TIME.DataTypes.Polygons;

namespace FlowMatters.Source.WebServer
{
    [DataContract]
    public class GeoJSONGeometry
    {
        [DataMember]
        public string type { get; set; }

        [DataMember]
        public object coordinates { get; set; }

        public GeoJSONGeometry(Coordinate c)
        {
            type = GeoJSONGeometryType.Point;
            coordinates = GeoJSONPoint(c);
        }

        public GeoJSONGeometry(PolyLine line)
        {
            type = GeoJSONGeometryType.LineString;
            coordinates = GeoJSONLineString(line);
        }

        public GeoJSONGeometry(Arc arc)
        {
            type = GeoJSONGeometryType.MultiLineString;
            coordinates = arc.items.Select(GeoJSONLineString).ToArray();
        }

        public GeoJSONGeometry(Link link)
        {
            type = GeoJSONGeometryType.LineString;
            var cTemp = new double[2][];
            cTemp[0] = GeoJSONPoint(link.from.location);
            cTemp[1] = GeoJSONPoint(link.to.location);
        }

        public GeoJSONGeometry(Polygon polygon)
        {
            type = GeoJSONGeometryType.Polygon;
            coordinates = GeoJSONPolygon(polygon);
        }

        private static double[][][] GeoJSONPolygon(Polygon polygon)
        {
            List<double[][]> coordTemp = new List<double[][]>();
            for (int i = 0; i < polygon.lineCount(); i++)
            {
                PolyLineDescriptor pld = polygon.item(i);
                if (pld.usage == PolyLineUsage.Normal)
                    coordTemp.Add(GeoJSONLineString(pld.polyLine));
                else
                    coordTemp.Add(GeoJSONLineString(pld.polyLine.Reverse()));
            }

            double[][][] result = coordTemp.ToArray();
            return result;
        }

        public GeoJSONGeometry(GEORegion region)
        {
            type = GeoJSONGeometryType.MultiPolygon;
            
            List<double[][][]> coordTemp = new List<double[][][]>();
            for(int i = 0; i < region.count(); i++)
                coordTemp.Add(GeoJSONPolygon(region.item(i)));
            coordinates = coordTemp.ToArray();
        }

        private static double[] GeoJSONPoint(Coordinate c)
        {
            return new[] {c.E,c.N};
        }

        private static double[][] GeoJSONLineString(PolyLine line)
        {
            var coords = new List<double[]>();
            for (int i = 0; i < line.count(); i++)
                coords.Add(GeoJSONPoint(line.item(i)));
            return coords.ToArray();
        }
    }

    public static class PolyLineExtensions
    {
        public static PolyLine Reverse(this PolyLine original)
        {
            List<Coordinate> points = new List<Coordinate>();
            for(int i = 0; i < original.count(); i++)
                points.Add(original.item(i));
            points.Reverse();
            PolyLine result = new PolyLine();
            foreach(Coordinate c in points)
                result.add(c);

            return result;
        }
    }
}