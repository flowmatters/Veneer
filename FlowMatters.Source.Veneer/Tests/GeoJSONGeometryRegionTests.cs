using NUnit.Framework;
using System.Linq;
using FlowMatters.Source.WebServer;
using TIME.DataTypes;
using TIME.DataTypes.Polygons;

namespace FlowMatters.Source.Veneer.Tests
{
    // GeoJSON for Source's own polygon types, as /network writes a catchment's outline.
    [TestFixture]
    public class GeoJSONGeometryRegionTests
    {
        private static PolyLine Line(params double[] xy)
        {
            var line = new PolyLine();
            for (int i = 0; i < xy.Length; i += 2)
                line.add(new Coordinate(xy[i], xy[i + 1]));
            return line;
        }

        // A closed square ring as a single polyline.
        private static Polygon Square(double x0, double y0, double size)
        {
            return new Polygon(Line(
                x0, y0,
                x0, y0 + size,
                x0 + size, y0 + size,
                x0 + size, y0,
                x0, y0));
        }

        private static double[][][][] MultiPolygonOf(GEORegion region)
        {
            var geometry = new GeoJSONGeometry(region);
            Assert.That(geometry.type, Is.EqualTo(GeoJSONGeometryType.MultiPolygon));
            return (double[][][][])geometry.coordinates;
        }

        [Test]
        public void RegionWithAHole_IsOnePolygonWithAnInteriorRing()
        {
            var region = new GEORegion();
            region.add(Square(1000, 1000, 100));
            region.add(Square(1040, 1040, 20));

            var polygons = MultiPolygonOf(region);

            Assert.That(polygons.Length, Is.EqualTo(1), "the hole was written as a polygon of its own");
            Assert.That(polygons[0].Length, Is.EqualTo(2));
            Assert.That(polygons[0][1].Max(p => p[0]), Is.EqualTo(1060.0));
        }

        [Test]
        public void RegionWithTwoParts_IsTwoPolygons()
        {
            var region = new GEORegion();
            region.add(Square(1000, 1000, 100));
            region.add(Square(2000, 1000, 50));

            var polygons = MultiPolygonOf(region);

            Assert.That(polygons.Length, Is.EqualTo(2));
            Assert.That(polygons.All(p => p.Length == 1));
        }

        [Test]
        public void PartWithTwoHoles_AndASecondPart()
        {
            var region = new GEORegion();
            region.add(Square(1000, 1000, 100));
            region.add(Square(1010, 1010, 20));
            region.add(Square(1060, 1060, 20));
            region.add(Square(2000, 1000, 50));

            var polygons = MultiPolygonOf(region);

            Assert.That(polygons.Select(p => p.Length).OrderBy(c => c), Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void RingBuiltFromSeveralPolylines_IsOneClosedRing()
        {
            // One ring made of two arcs, the second stored the other way round, as Source
            // keeps rings that share boundaries with their neighbours.
            var polygon = new Polygon();
            polygon.addLine(Line(1000, 1000, 1000, 1100, 1100, 1100), PolyLineUsage.Normal);
            polygon.addLine(Line(1000, 1000, 1100, 1000, 1100, 1100), PolyLineUsage.Reverse);
            var region = new GEORegion(polygon);

            var polygons = MultiPolygonOf(region);

            Assert.That(polygons.Length, Is.EqualTo(1));
            Assert.That(polygons[0].Length, Is.EqualTo(1), "each polyline was written as a ring of its own");
            var ring = polygons[0][0];
            Assert.That(ring.Length, Is.EqualTo(5));
            Assert.That(ring.First(), Is.EqualTo(ring.Last()));
            Assert.That(ring.Select(p => (p[0], p[1])).Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void SinglePolygon_IsAPolygonWithOneCounterClockwiseRing()
        {
            var geometry = new GeoJSONGeometry(Square(1000, 1000, 100));

            Assert.That(geometry.type, Is.EqualTo(GeoJSONGeometryType.Polygon));
            var rings = (double[][][])geometry.coordinates;
            Assert.That(rings.Length, Is.EqualTo(1));
            var ring = rings[0];
            double twiceArea = 0.0;
            for (int i = 0; i < ring.Length - 1; i++)
                twiceArea += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
            Assert.That(twiceArea, Is.GreaterThan(0));
        }
    }
}
