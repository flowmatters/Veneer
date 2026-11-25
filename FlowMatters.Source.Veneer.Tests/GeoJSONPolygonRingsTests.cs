using System;
using System.Collections.Generic;
using System.Linq;
using FlowMatters.Source.WebServer;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class GeoJSONPolygonRingsTests
    {
        // A closed, axis-aligned square ring. Clockwise unless ccw is set.
        private static double[][] Square(double x0, double y0, double size, bool ccw = false)
        {
            var pts = new List<double[]>
            {
                new[] {x0, y0},
                new[] {x0, y0 + size},
                new[] {x0 + size, y0 + size},
                new[] {x0 + size, y0},
                new[] {x0, y0}
            };
            if (ccw)
                pts.Reverse();
            return pts.ToArray();
        }

        private static double SignedArea(double[][] ring)
        {
            double sum = 0.0;
            for (int i = 0; i < ring.Length - 1; i++)
                sum += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
            return sum / 2.0;
        }

        private static void AssertSameRing(double[][] actual, double[][] expected)
        {
            // Same closed ring, irrespective of start point and direction.
            Assert.That(actual.First(), Is.EqualTo(actual.Last()), "ring is not closed");
            var a = actual.Take(actual.Length - 1).Select(p => (p[0], p[1])).ToList();
            var e = expected.Take(expected.Length - 1).Select(p => (p[0], p[1])).ToList();
            Assert.That(a, Is.EquivalentTo(e));
        }

        [Test]
        public void SingleRing_IsOnePolygonWithOneRing()
        {
            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { Square(0, 0, 10) });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Length, Is.EqualTo(1));
            AssertSameRing(result[0][0], Square(0, 0, 10));
        }

        [Test]
        public void SquareWithSquareHole_IsOnePolygonWithAnInteriorRing()
        {
            var shell = Square(0, 0, 10);
            var hole = Square(4, 4, 2);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { shell, hole });

            Assert.That(result.Length, Is.EqualTo(1), "the hole must not become a polygon of its own");
            Assert.That(result[0].Length, Is.EqualTo(2));
            AssertSameRing(result[0][0], shell);
            AssertSameRing(result[0][1], hole);
        }

        [Test]
        public void HoleListedBeforeItsShell_IsStillAttachedToTheShell()
        {
            var shell = Square(0, 0, 10);
            var hole = Square(4, 4, 2);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { hole, shell });

            Assert.That(result.Length, Is.EqualTo(1));
            AssertSameRing(result[0][0], shell);
            AssertSameRing(result[0][1], hole);
        }

        [Test]
        public void TwoSeparateParts_AreTwoPolygons()
        {
            var a = Square(0, 0, 10);
            var b = Square(20, 0, 5);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { a, b });

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result.All(p => p.Length == 1));
            AssertSameRing(result[0][0], a);
            AssertSameRing(result[1][0], b);
        }

        [Test]
        public void PartWithTwoHoles_AndASecondPart()
        {
            var shell = Square(0, 0, 10);
            var holeA = Square(1, 1, 2);
            var holeB = Square(6, 6, 2);
            var other = Square(20, 0, 5);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { shell, other, holeB, holeA });

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Length, Is.EqualTo(3));
            AssertSameRing(result[0][0], shell);
            AssertSameRing(result[0][1], holeB);
            AssertSameRing(result[0][2], holeA);
            Assert.That(result[1].Length, Is.EqualTo(1));
            AssertSameRing(result[1][0], other);
        }

        [Test]
        public void IslandInsideAHole_IsAPolygonOfItsOwn()
        {
            var shell = Square(0, 0, 20);
            var hole = Square(5, 5, 10);
            var island = Square(8, 8, 4);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { shell, hole, island });

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Length, Is.EqualTo(2));
            AssertSameRing(result[0][0], shell);
            AssertSameRing(result[0][1], hole);
            Assert.That(result[1].Length, Is.EqualTo(1));
            AssertSameRing(result[1][0], island);
        }

        [Test]
        public void HoleInAnIsland_AttachesToTheIslandNotTheOuterShell()
        {
            var shell = Square(0, 0, 30);
            var hole = Square(5, 5, 20);
            var island = Square(8, 8, 14);
            var islandHole = Square(12, 12, 4);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { islandHole, island, hole, shell });

            Assert.That(result.Length, Is.EqualTo(2));
            var outer = result.Single(p => p[0].Any(c => c[0] == 30.0));
            var inner = result.Single(p => !p[0].Any(c => c[0] == 30.0));
            Assert.That(outer.Length, Is.EqualTo(2));
            AssertSameRing(outer[1], hole);
            Assert.That(inner.Length, Is.EqualTo(2));
            AssertSameRing(inner[0], island);
            AssertSameRing(inner[1], islandHole);
        }

        [Test]
        public void HoleTouchingItsShellAtAVertex_IsStillAHole()
        {
            var shell = Square(0, 0, 10);
            // A triangle that shares the shell's corner (0,0).
            var hole = new[] { new[] {0.0, 0.0}, new[] {2.0, 4.0}, new[] {4.0, 2.0}, new[] {0.0, 0.0} };

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { shell, hole });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Length, Is.EqualTo(2));
        }

        [Test]
        public void Rings_FollowRfc7946Orientation()
        {
            // Supplied the wrong way round: the shell clockwise and the hole
            // counter-clockwise.
            var shell = Square(0, 0, 10);
            var hole = Square(4, 4, 2, ccw: true);
            var other = Square(20, 0, 5);

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { shell, hole, other });

            Assert.That(SignedArea(result[0][0]), Is.GreaterThan(0), "exterior ring must be counter-clockwise");
            Assert.That(SignedArea(result[0][1]), Is.LessThan(0), "interior ring must be clockwise");
            Assert.That(SignedArea(result[1][0]), Is.GreaterThan(0), "exterior ring must be counter-clockwise");
        }

        [Test]
        public void OpenRing_IsClosed()
        {
            var open = Square(0, 0, 10).Take(4).ToArray();

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { open });

            var ring = result[0][0];
            Assert.That(ring.Length, Is.EqualTo(5));
            Assert.That(ring.First(), Is.EqualTo(ring.Last()));
        }

        [Test]
        public void DegenerateRings_AreDropped()
        {
            var line = new[] { new[] {0.0, 0.0}, new[] {1.0, 1.0}, new[] {0.0, 0.0} };

            var result = GeoJSONPolygonRings.ToMultiPolygon(new[] { Square(0, 0, 10), line });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Length, Is.EqualTo(1));
        }

        [Test]
        public void NoRings_IsAnEmptyMultiPolygon()
        {
            Assert.That(GeoJSONPolygonRings.ToMultiPolygon(new double[0][][]), Is.Empty);
        }
    }
}
