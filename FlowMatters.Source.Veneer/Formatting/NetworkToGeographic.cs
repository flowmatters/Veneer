using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.WebServer;
using RiverSystem.ManagedExtensions;
using RiverSystem.PreProcessing.ProjectionInfo;
using TIME.DataTypes;
using Network = RiverSystem.Network;

namespace FlowMatters.Source.Veneer.Formatting
{
    public class NetworkToGeographic
    {
        protected NetworkToGeographic()
        {

        }

        public static GeoJSONNetwork ToGeographic(Network current, AbstractProjectionInfo currentProjection)
        {
            var result = new GeoJSONNetwork(current);
            result.features.ForEachItem(f => ConvertToGeographic(f, currentProjection));
            return result;
        }

        public static void ConvertToGeographic(GeoJSONFeature f, AbstractProjectionInfo currentProjection)
        {
            f.geometry.coordinates = ConvertGeometry(f.geometry.coordinates, currentProjection);
        }

        private static object ConvertGeometry(object coords, AbstractProjectionInfo currentProjection)
        {
            if ((coords as double[])!=null)
            {
                return ConvertPoint(coords as double[], currentProjection);
            }

            if((coords as double[][])!=null)
            {
                double[][] poly = (double[][])coords;
                double[][]  result = poly.Select(p => ConvertPoint(p, currentProjection)).ToArray();
                return result;
            }

            if ((coords as double[][][]) != null)
            {
                double[][][] multiPoly = (double[][][])coords;
                double[][][] result = multiPoly.Select(ln => ln.Select(p=>ConvertPoint(p, currentProjection)).ToArray()).ToArray();
                return result;
            }

            if ((coords as double[][][][]) != null)
            {
                double[][][][] multiPoly = (double[][][][])coords;
                double[][][][] result = multiPoly.Select(poly =>
                    poly.Select(ln => ln.Select(pt => ConvertPoint(pt, currentProjection)).ToArray()).ToArray()).ToArray();
                return result;
            }

            throw new NotImplementedException();
        }

        private static double[] ConvertPoint(double[] coords, AbstractProjectionInfo currentProjection)
        {
            var c = new Coordinate(coords[0], coords[1]);
            var result = currentProjection.ToLatLong(c);
            double[] arr = {GeoJSONGeometry.RoundCoordinate( result.E), GeoJSONGeometry.RoundCoordinate(result.N) };
            if (Double.IsNaN(result.E) || (Double.IsNaN(result.N)))
            {
                throw new ArgumentException(String.Format("Invalid coordinates E:{0}, N:{1}",coords[0],coords[1]));
            }
            return arr;
        }
    }
}
