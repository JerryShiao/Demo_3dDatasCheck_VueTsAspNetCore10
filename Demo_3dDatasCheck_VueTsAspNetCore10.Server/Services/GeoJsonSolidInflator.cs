using System.Text.Json;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// GeoJSON 匯入時將平面樓層 polygon 補成立體樓層（底面、頂面、側牆）
    /// </summary>
    internal static class GeoJsonSolidInflator
    {
        private const double DefaultFloorHeight = 3.2;
        private const double FlatZTolerance = 0.01;
        private const double MinExistingSolidHeight = 2.0;

        internal sealed class GeoJsonFloorDraft
        {
            public string Mid { get; init; } = string.Empty;
            public string Oid { get; init; } = string.Empty;
            public string BuildingNo { get; init; } = string.Empty;
            public string Floor { get; init; } = string.Empty;
            public string HeightProperty { get; init; } = string.Empty;
            public List<List<double>> Ring { get; init; } = new();
            public double PlaneZ { get; set; }
            public double MinZ { get; set; }
            public double MaxZ { get; set; }
            public bool IsFlat { get; set; }
            public bool AlreadySolid { get; set; }
            public string OriginalBoundedBy { get; init; } = string.Empty;
            public string BoundedByRaw { get; set; } = string.Empty;
            public bool WasInflated { get; set; }
            public string? InflateFixMessage { get; set; }
        }

        /// <summary>
        /// 解析 GeoJSON features 並依建號補成立體樓層
        /// </summary>
        internal static List<GeoJsonFloorDraft> ProcessFeatures(JsonElement features)
        {
            var drafts = CollectDrafts(features);
            InflateByBuilding(drafts);
            return drafts;
        }

        /// <summary>
        /// 套用補立體修復訊息至 ValidateAndFix 後的結果
        /// </summary>
        internal static void ApplyInflateFixMessages(
            IReadOnlyList<GeoJsonFloorDraft> drafts,
            IReadOnlyList<BuildingData> buildings)
        {
            if (drafts.Count != buildings.Count)
            {
                return;
            }

            for (var i = 0; i < drafts.Count; i++)
            {
                var draft = drafts[i];
                var building = buildings[i];
                if (!draft.WasInflated || string.IsNullOrWhiteSpace(draft.InflateFixMessage))
                {
                    continue;
                }

                building.IsFixed = true;
                if (!building.FixMessages.Contains(draft.InflateFixMessage))
                {
                    building.FixMessages.Add(draft.InflateFixMessage);
                }
            }
        }

        private static List<GeoJsonFloorDraft> CollectDrafts(JsonElement features)
        {
            var drafts = new List<GeoJsonFloorDraft>();

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props))
                {
                    continue;
                }

                var ring = feature.TryGetProperty("geometry", out var geometry)
                    ? ExtractExteriorRing(geometry)
                    : null;

                if (ring == null || ring.Count < 3)
                {
                    continue;
                }

                var (minZ, maxZ, isFlat) = AnalyzeRingHeight(ring);
                var originalBoundedBy = JsonSerializer.Serialize(new List<List<List<double>>> { ring });

                drafts.Add(new GeoJsonFloorDraft
                {
                    Mid = GetJsonPropertyAsString(props, "MID"),
                    Oid = GetJsonPropertyAsString(props, "OID"),
                    BuildingNo = GetJsonPropertyAsString(props, "建號母號"),
                    Floor = GetJsonPropertyAsString(props, "層次"),
                    HeightProperty = GetJsonPropertyAsString(props, "高度"),
                    Ring = ring,
                    PlaneZ = minZ,
                    MinZ = minZ,
                    MaxZ = maxZ,
                    IsFlat = isFlat,
                    AlreadySolid = !isFlat && maxZ - minZ >= MinExistingSolidHeight,
                    OriginalBoundedBy = originalBoundedBy,
                    BoundedByRaw = originalBoundedBy,
                });
            }

            return drafts;
        }

        private static void InflateByBuilding(List<GeoJsonFloorDraft> drafts)
        {
            var groups = drafts
                .GroupBy(d => d.BuildingNo)
                .ToList();

            foreach (var group in groups)
            {
                var sorted = group
                    .OrderBy(d => d.Floor, Comparer<string>.Create(GeoJsonFloorOrdering.Compare))
                    .ToList();

                DecideVerticalSpans(sorted);

                foreach (var draft in sorted)
                {
                    if (draft.AlreadySolid)
                    {
                        continue;
                    }

                    if (!draft.IsFlat)
                    {
                        continue;
                    }

                    var solid = BuildSolidFaces(draft.Ring, draft.MinZ, draft.MaxZ);
                    draft.BoundedByRaw = JsonSerializer.Serialize(solid);
                    draft.WasInflated = true;
                    draft.InflateFixMessage =
                        $"GeoJSON 修復：已將平面樓層補成立體幾何（底面 {draft.MinZ:F1}m ~ 頂面 {draft.MaxZ:F1}m）";
                }
            }
        }

        private static void DecideVerticalSpans(List<GeoJsonFloorDraft> sorted)
        {
            for (var i = 0; i < sorted.Count; i++)
            {
                var current = sorted[i];
                if (current.AlreadySolid)
                {
                    continue;
                }

                if (!current.IsFlat)
                {
                    continue;
                }

                var baseZ = current.PlaneZ;
                double topZ;

                if (i + 1 < sorted.Count)
                {
                    topZ = sorted[i + 1].PlaneZ;
                }
                else if (TryParseHeightAsTopZ(current.HeightProperty, baseZ, out var propertyTopZ))
                {
                    topZ = propertyTopZ;
                }
                else
                {
                    topZ = baseZ + DefaultFloorHeight;
                }

                if (topZ <= baseZ + FlatZTolerance)
                {
                    topZ = baseZ + DefaultFloorHeight;
                }

                current.MinZ = baseZ;
                current.MaxZ = topZ;
            }
        }

        private static bool TryParseHeightAsTopZ(string heightProperty, double baseZ, out double topZ)
        {
            topZ = 0;
            if (!double.TryParse(heightProperty, out var parsed))
            {
                return false;
            }

            if (parsed <= baseZ + FlatZTolerance)
            {
                return false;
            }

            topZ = parsed;
            return true;
        }

        private static List<List<List<double>>> BuildSolidFaces(
            List<List<double>> ring,
            double baseZ,
            double topZ)
        {
            var footprint = NormalizeFootprint(ring);
            var solids = new List<List<List<double>>>();

            solids.Add(CreateHorizontalFace(footprint, baseZ));
            solids.Add(CreateHorizontalFace(footprint, topZ));

            for (var i = 0; i < footprint.Count - 1; i++)
            {
                var p0 = footprint[i];
                var p1 = footprint[i + 1];
                if (IsSamePoint(p0, p1))
                {
                    continue;
                }

                solids.Add(new List<List<double>>
                {
                    new() { p0[0], p0[1], baseZ },
                    new() { p1[0], p1[1], baseZ },
                    new() { p1[0], p1[1], topZ },
                    new() { p0[0], p0[1], topZ },
                    new() { p0[0], p0[1], baseZ },
                });
            }

            return solids;
        }

        private static List<List<double>> CreateHorizontalFace(List<List<double>> footprint, double z)
        {
            return footprint
                .Select(point => new List<double> { point[0], point[1], z })
                .ToList();
        }

        private static List<List<double>> NormalizeFootprint(List<List<double>> ring)
        {
            var footprint = ring
                .Where(point => point.Count >= 2)
                .Select(point => new List<double> { point[0], point[1] })
                .ToList();

            if (footprint.Count == 0)
            {
                return footprint;
            }

            if (!IsSamePoint(footprint[0], footprint[^1]))
            {
                footprint.Add(new List<double> { footprint[0][0], footprint[0][1] });
            }

            return footprint;
        }

        private static (double MinZ, double MaxZ, bool IsFlat) AnalyzeRingHeight(List<List<double>> ring)
        {
            var zs = ring
                .Where(point => point.Count >= 3)
                .Select(point => point[2])
                .ToList();

            if (zs.Count == 0)
            {
                return (0, 0, true);
            }

            var minZ = zs.Min();
            var maxZ = zs.Max();
            var isFlat = maxZ - minZ <= FlatZTolerance;
            return (minZ, maxZ, isFlat);
        }

        private static List<List<double>>? ExtractExteriorRing(JsonElement geometry)
        {
            if (!geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array
                || coordinates.GetArrayLength() == 0)
            {
                return null;
            }

            var geometryType = geometry.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString()
                : null;

            return geometryType switch
            {
                "Polygon" => ParseRing(coordinates[0]),
                "MultiPolygon" => coordinates[0].ValueKind == JsonValueKind.Array
                    && coordinates[0].GetArrayLength() > 0
                    ? ParseRing(coordinates[0][0])
                    : null,
                null => TryParseLegacyCoordinates(coordinates),
                _ => null,
            };
        }

        private static List<List<double>>? TryParseLegacyCoordinates(JsonElement coordinates)
        {
            if (coordinates.GetArrayLength() == 0)
            {
                return null;
            }

            var first = coordinates[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() == 0)
            {
                return null;
            }

            var firstPoint = first[0];
            if (firstPoint.ValueKind == JsonValueKind.Array
                && firstPoint.GetArrayLength() >= 2
                && firstPoint[0].ValueKind == JsonValueKind.Number)
            {
                return ParseRing(first);
            }

            if (firstPoint.ValueKind == JsonValueKind.Array)
            {
                return ParseRing(coordinates[0]);
            }

            return null;
        }

        private static List<List<double>>? ParseRing(JsonElement ringElement)
        {
            if (ringElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var ring = new List<List<double>>();
            foreach (var pointElement in ringElement.EnumerateArray())
            {
                if (pointElement.ValueKind != JsonValueKind.Array || pointElement.GetArrayLength() < 2)
                {
                    continue;
                }

                var values = new List<double>();
                foreach (var coordinate in pointElement.EnumerateArray())
                {
                    if (coordinate.ValueKind == JsonValueKind.Number)
                    {
                        values.Add(coordinate.GetDouble());
                    }
                }

                while (values.Count < 3)
                {
                    values.Add(0);
                }

                if (values.Count >= 2)
                {
                    ring.Add(values);
                }
            }

            return ring.Count >= 3 ? ring : null;
        }

        private static bool IsSamePoint(List<double> a, List<double> b)
        {
            return Math.Abs(a[0] - b[0]) <= 0.000001
                && Math.Abs(a[1] - b[1]) <= 0.000001;
        }

        private static string GetJsonPropertyAsString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText(),
            };
        }
    }
}
