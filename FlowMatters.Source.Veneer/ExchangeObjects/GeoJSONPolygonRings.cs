using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowMatters.Source.WebServer
{
    /// <summary>
    /// Arranges a region's rings into GeoJSON polygons.
    /// </summary>
    /// <remarks>
    /// Source keeps a region (a GEORegion, such as a catchment outline) as a flat list of
    /// closed rings that do not cross, and reads it with the even-odd rule: a point is in the
    /// region if an odd number of the rings contain it (GEORegion.pointInRegion). Whether a
    /// ring is an outer boundary or a hole is therefore decided by how deeply it is nested
    /// within the other rings, not by its orientation. GeoJSON instead needs each hole listed
    /// inside the polygon it belongs to, so the rings are grouped here by nesting:
    /// a ring inside an even number of others is an exterior ring, and a ring inside an odd
    /// number is a hole in the innermost exterior ring that contains it. Rings are oriented as
    /// RFC 7946 asks: exterior rings counter-clockwise, holes clockwise.
    /// </remarks>
    public static class GeoJSONPolygonRings
    {
        /// <summary>
        /// Group rings into MultiPolygon coordinates: one polygon per exterior ring, each
        /// followed by its holes.
        /// </summary>
        /// <param name="rings">Rings as [x, y] positions. A ring need not be closed, and
        /// rings with fewer than three distinct positions are dropped.</param>
        public static double[][][][] ToMultiPolygon(IEnumerable<double[][]> rings)
        {
            var closed = rings.Select(Close).Where(r => r != null).ToList();
            int n = closed.Count;

            var areas = closed.Select(SignedArea).ToArray();
            var bounds = closed.Select(Bounds).ToArray();

            // containers[i]: the rings that contain ring i.
            var containers = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                containers[i] = new List<int>();
                for (int j = 0; j < n; j++)
                {
                    if (i == j || !Within(bounds[i], bounds[j]))
                        continue;
                    if (Math.Abs(areas[i]) > Math.Abs(areas[j]))
                        continue;
                    if (RingInsideRing(closed[i], closed[j]))
                        containers[i].Add(j);
                }
            }

            var depth = containers.Select(c => c.Count).ToArray();
            var parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = -1;
                if (depth[i] % 2 == 0)
                    continue;
                // The innermost exterior ring that contains this hole.
                var candidates = containers[i].Where(j => depth[j] == depth[i] - 1).ToList();
                if (candidates.Count > 0)
                    parent[i] = candidates.OrderBy(j => Math.Abs(areas[j])).First();
            }

            var polygons = new List<List<double[][]>>();
            var polygonOf = new Dictionary<int, List<double[][]>>();
            for (int i = 0; i < n; i++)
            {
                // A ring with no consistent parent is kept as an exterior ring rather than lost.
                if (depth[i] % 2 == 0 || parent[i] < 0)
                {
                    var polygon = new List<double[][]> { Oriented(closed[i], areas[i], true) };
                    polygons.Add(polygon);
                    polygonOf[i] = polygon;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (depth[i] % 2 == 1 && parent[i] >= 0)
                    polygonOf[parent[i]].Add(Oriented(closed[i], areas[i], false));
            }

            return polygons.Select(p => p.ToArray()).ToArray();
        }

        private static double[][] Close(double[][] ring)
        {
            if (ring == null)
                return null;
            var points = ring.ToList();
            if (points.Count > 0 && !SamePoint(points[0], points[points.Count - 1]))
                points.Add(points[0]);

            int distinct = points.Take(points.Count - 1)
                .Select(p => Tuple.Create(p[0], p[1])).Distinct().Count();
            if (distinct < 3)
                return null;
            return points.ToArray();
        }

        private static bool SamePoint(double[] a, double[] b)
        {
            return a[0] == b[0] && a[1] == b[1];
        }

        // Shoelace formula: positive for a counter-clockwise ring.
        private static double SignedArea(double[][] ring)
        {
            double sum = 0.0;
            for (int i = 0; i < ring.Length - 1; i++)
                sum += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
            return sum / 2.0;
        }

        private static double[][] Oriented(double[][] ring, double signedArea, bool counterClockwise)
        {
            if ((signedArea > 0) == counterClockwise)
                return ring;
            return ring.Reverse().ToArray();
        }

        private static double[] Bounds(double[][] ring)
        {
            return new[]
            {
                ring.Min(p => p[0]), ring.Min(p => p[1]),
                ring.Max(p => p[0]), ring.Max(p => p[1])
            };
        }

        private static bool Within(double[] inner, double[] outer)
        {
            return inner[0] >= outer[0] && inner[1] >= outer[1] &&
                   inner[2] <= outer[2] && inner[3] <= outer[3];
        }

        // Rings do not cross, so one is inside another if any of its points that is not on
        // the other's boundary is inside it. Vertices are tried first, then edge midpoints,
        // for rings that share every vertex with the other.
        private static bool RingInsideRing(double[][] inner, double[][] outer)
        {
            var candidates = inner.Take(inner.Length - 1).Concat(
                Enumerable.Range(0, inner.Length - 1).Select(k => new[]
                {
                    (inner[k][0] + inner[k + 1][0]) / 2.0,
                    (inner[k][1] + inner[k + 1][1]) / 2.0
                }));

            foreach (var p in candidates)
            {
                if (OnBoundary(p, outer))
                    continue;
                return PointInRing(p, outer);
            }
            return false;
        }

        private static bool OnBoundary(double[] p, double[][] ring)
        {
            for (int k = 0; k < ring.Length - 1; k++)
            {
                double[] a = ring[k], b = ring[k + 1];
                double cross = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]);
                double scale = Math.Max(Math.Abs(b[0] - a[0]) + Math.Abs(b[1] - a[1]), 1.0);
                if (Math.Abs(cross) > 1e-9 * scale * scale)
                    continue;
                if (p[0] >= Math.Min(a[0], b[0]) && p[0] <= Math.Max(a[0], b[0]) &&
                    p[1] >= Math.Min(a[1], b[1]) && p[1] <= Math.Max(a[1], b[1]))
                    return true;
            }
            return false;
        }

        // Even-odd ray casting, as Source's own Polygon.pointInPoly.
        private static bool PointInRing(double[] p, double[][] ring)
        {
            bool inside = false;
            for (int k = 0, prev = ring.Length - 2; k < ring.Length - 1; prev = k++)
            {
                double[] a = ring[k], b = ring[prev];
                if ((a[1] > p[1]) != (b[1] > p[1]) &&
                    p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0])
                    inside = !inside;
            }
            return inside;
        }
    }
}
