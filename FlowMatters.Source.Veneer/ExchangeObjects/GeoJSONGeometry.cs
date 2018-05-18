#if V3 || V4_0 || V4_1 || V4_2_0 || V4_2_1 || V4_2_2 || V4_2_3 || GBRSource
#define BeforeCaseRefactor
#endif

using System;
using System.Collections.Generic;
using System.Drawing;
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
        public static double RoundCoordinate(double v,int dp=-1)
        {
            if (dp < 0)
            {
                if (Math.Abs(v) > 1000.0)
                {
                    dp = 0;
                }
                else
                {
                    dp = 4;
                }
            }
            return Math.Round(v, dp);
        }

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

        public GeoJSONGeometry(Link link,bool useSchematicLocation)
        {
            type = GeoJSONGeometryType.LineString;
            var cTemp = new double[2][];
            cTemp[0] = GeoJSONPoint(link.from.location);
            cTemp[1] = GeoJSONPoint(link.to.location);
            coordinates = new[] {cTemp[0], cTemp[1]};
        }

        public GeoJSONGeometry(PointF from, PointF to)
        {
            type = GeoJSONGeometryType.LineString;
            var cTemp = new double[2][];
            cTemp[0] = GeoJSONPoint(new Coordinate(from));
            cTemp[1] = GeoJSONPoint(new Coordinate(to));
            coordinates = new[] { cTemp[0], cTemp[1] };
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
#if BeforeCaseRefactor
            for(int i = 0; i < region.count(); i++)
#else
            for (int i = 0; i < region.Count; i++)
#endif
                coordTemp.Add(GeoJSONPolygon(region.item(i)));
            coordinates = coordTemp.ToArray();
        }

        private static double[] GeoJSONPoint(Coordinate c)
        {
            return new[] { RoundCoordinate(c.E), RoundCoordinate(c.N)};
        }

        private static double[][] GeoJSONLineString(PolyLine line)
        {
            var coords = new List<double[]>();
            double[] last = null;
#if BeforeCaseRefactor
            int nPoints = line.count();
#else
            int nPoints = line.Count;
#endif
            for (int i = 0; i < nPoints; i++)
            {
                var newPoint = GeoJSONPoint(line.item(i));
                if ((nPoints==2)||
                    (last == null)||
                    (last[0] != newPoint[0])||
                    (last[1] != newPoint[1]))
                {
                    coords.Add(newPoint);
                }
                last = newPoint;
            }
            return coords.ToArray();
        }
    }

    public static class PolyLineExtensions
    {
        public static PolyLine Reverse(this PolyLine original)
        {
            List<Coordinate> points = new List<Coordinate>();
#if BeforeCaseRefactor
            for(int i = 0; i < original.count(); i++)
#else
            for (int i = 0; i < original.Count; i++)
#endif
                points.Add(original.item(i));
            points.Reverse();
            PolyLine result = new PolyLine();
            foreach(Coordinate c in points)
                result.add(c);

            return result;
        }
    }
}