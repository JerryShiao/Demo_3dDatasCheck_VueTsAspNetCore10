using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 同建號內依水平 footprint 關聯分子棟，避免多構件被當成單一垂直柱體。
    /// </summary>
    internal static class BuildingFootprintClusterer
    {
        private const double MinClusterOverlapRatio = 0.2;

        internal static List<List<BuildingData>> Cluster(IReadOnlyList<BuildingData> buildings)
        {
            if (buildings.Count <= 1)
            {
                return [buildings.ToList()];
            }

            var footprints = buildings
                .Select(TryBuildFootprint)
                .ToList();

            var parent = Enumerable.Range(0, buildings.Count).ToArray();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                var ra = Find(a);
                var rb = Find(b);
                if (ra != rb)
                {
                    parent[rb] = ra;
                }
            }

            for (var i = 0; i < buildings.Count; i++)
            {
                for (var j = i + 1; j < buildings.Count; j++)
                {
                    if (!ShouldCluster(footprints[i], footprints[j]))
                    {
                        continue;
                    }

                    Union(i, j);
                }
            }

            var byRoot = new Dictionary<int, List<BuildingData>>();
            for (var i = 0; i < buildings.Count; i++)
            {
                var root = Find(i);
                if (!byRoot.TryGetValue(root, out var list))
                {
                    list = [];
                    byRoot[root] = list;
                }

                list.Add(buildings[i]!);
            }

            return byRoot.Values.ToList();
        }

        private static bool ShouldCluster(FootprintInfo? left, FootprintInfo? right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (!left.Bbox.Intersects(right.Bbox))
            {
                return false;
            }

            var intersection = PolygonIntersectionArea(left.Ring, right.Ring);
            if (intersection <= 0)
            {
                return false;
            }

            var minArea = Math.Min(left.Area, right.Area);
            if (minArea <= 0)
            {
                return false;
            }

            return intersection / minArea >= MinClusterOverlapRatio;
        }

        private sealed record FootprintInfo(
            List<(double Lon, double Lat)> Ring,
            BBox Bbox,
            double Area);

        private readonly record struct BBox(double MinLon, double MinLat, double MaxLon, double MaxLat)
        {
            public bool Intersects(BBox other)
            {
                return !(other.MinLon > MaxLon
                    || other.MaxLon < MinLon
                    || other.MinLat > MaxLat
                    || other.MaxLat < MinLat);
            }
        }

        private static FootprintInfo? TryBuildFootprint(BuildingData building)
        {
            if (building.Coordinates == null || building.Coordinates.Count == 0)
            {
                return null;
            }

            List<(double Lon, double Lat)>? bestRing = null;
            var bestScore = double.NegativeInfinity;

            foreach (var polygon in building.Coordinates)
            {
                var ring = RingTo2D(polygon);
                if (ring.Count < 3)
                {
                    continue;
                }

                var area = Math.Abs(ShoelaceArea(ring));
                if (area <= 0)
                {
                    continue;
                }

                var zs = polygon
                    .Where(pt => pt != null && pt.Count >= 3 && double.IsFinite(pt[2]))
                    .Select(pt => pt[2])
                    .ToList();
                var zSpan = zs.Count > 0 ? zs.Max() - zs.Min() : double.PositiveInfinity;
                var score = area - zSpan * 1_000_000;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRing = ring;
                }
            }

            if (bestRing == null || bestRing.Count < 3)
            {
                return null;
            }

            var bbox = GetBbox(bestRing);
            var ringArea = Math.Abs(ShoelaceArea(bestRing));
            if (ringArea <= 0)
            {
                return null;
            }

            return new FootprintInfo(bestRing, bbox, ringArea);
        }

        private static List<(double Lon, double Lat)> RingTo2D(List<List<double>> polygon)
        {
            var ring = new List<(double Lon, double Lat)>();
            foreach (var pt in polygon)
            {
                if (pt == null || pt.Count < 2)
                {
                    continue;
                }

                if (!double.IsFinite(pt[0]) || !double.IsFinite(pt[1]))
                {
                    continue;
                }

                ring.Add((pt[0], pt[1]));
            }

            return ring;
        }

        private static BBox GetBbox(IReadOnlyList<(double Lon, double Lat)> ring)
        {
            var minLon = double.PositiveInfinity;
            var maxLon = double.NegativeInfinity;
            var minLat = double.PositiveInfinity;
            var maxLat = double.NegativeInfinity;

            foreach (var (lon, lat) in ring)
            {
                minLon = Math.Min(minLon, lon);
                maxLon = Math.Max(maxLon, lon);
                minLat = Math.Min(minLat, lat);
                maxLat = Math.Max(maxLat, lat);
            }

            return new BBox(minLon, minLat, maxLon, maxLat);
        }

        private static double ShoelaceArea(IReadOnlyList<(double Lon, double Lat)> ring)
        {
            if (ring.Count < 3)
            {
                return 0;
            }

            double area = 0;
            for (var i = 0; i < ring.Count; i++)
            {
                var j = (i + 1) % ring.Count;
                area += ring[i].Lon * ring[j].Lat;
                area -= ring[j].Lon * ring[i].Lat;
            }

            return area / 2.0;
        }

        private static double PolygonIntersectionArea(
            IReadOnlyList<(double Lon, double Lat)> subject,
            IReadOnlyList<(double Lon, double Lat)> clip)
        {
            if (subject.Count < 3 || clip.Count < 3)
            {
                return 0;
            }

            var output = subject.ToList();
            for (var i = 0; i < clip.Count; i++)
            {
                var input = output;
                output = [];
                if (input.Count == 0)
                {
                    break;
                }

                var edgeA = clip[i];
                var edgeB = clip[(i + 1) % clip.Count];

                for (var j = 0; j < input.Count; j++)
                {
                    var current = input[j];
                    var previous = input[(j + input.Count - 1) % input.Count];
                    var currInside = IsInsideEdge(current, edgeA, edgeB);
                    var prevInside = IsInsideEdge(previous, edgeA, edgeB);

                    if (currInside)
                    {
                        if (!prevInside)
                        {
                            var intersection = LineIntersection(previous, current, edgeA, edgeB);
                            if (intersection != null)
                            {
                                output.Add(intersection.Value);
                            }
                        }

                        output.Add(current);
                    }
                    else if (prevInside)
                    {
                        var intersection = LineIntersection(previous, current, edgeA, edgeB);
                        if (intersection != null)
                        {
                            output.Add(intersection.Value);
                        }
                    }
                }
            }

            return Math.Abs(ShoelaceArea(output));
        }

        private static bool IsInsideEdge(
            (double Lon, double Lat) point,
            (double Lon, double Lat) edgeA,
            (double Lon, double Lat) edgeB)
        {
            return (edgeB.Lon - edgeA.Lon) * (point.Lat - edgeA.Lat)
                - (edgeB.Lat - edgeA.Lat) * (point.Lon - edgeA.Lon) >= 0;
        }

        private static (double Lon, double Lat)? LineIntersection(
            (double Lon, double Lat) p1,
            (double Lon, double Lat) p2,
            (double Lon, double Lat) p3,
            (double Lon, double Lat) p4)
        {
            var x1 = p1.Lon;
            var y1 = p1.Lat;
            var x2 = p2.Lon;
            var y2 = p2.Lat;
            var x3 = p3.Lon;
            var y3 = p3.Lat;
            var x4 = p4.Lon;
            var y4 = p4.Lat;
            var denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-12)
            {
                return null;
            }

            var px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
            var py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
            return (px, py);
        }
    }
}
