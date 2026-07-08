using System.Text.Json;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// GeoJSON 立體膨脹器：將平面樓層 polygon 補成立體樓層（底面、頂面、側牆）
    /// </summary>
    internal static class GeoJsonSolidInflator
    {
        // 預設樓層净高（公尺）；無法由相鄰樓層或高度欄位推得時使用
        private const double DefaultFloorHeight = 3.2;
        // 判斷「平面」的 Z 差容差（公尺）
        private const double FlatZTolerance = 0.01;
        // 判定既有幾何已是立體的最小高度差（公尺）
        private const double MinExistingSolidHeight = 2.0;

        /// <summary>
        /// GeoJSON 樓層草稿：解析 feature 後、膨脹前後的中介資料
        /// </summary>
        internal sealed class GeoJsonFloorDraft
        {
            /// <summary>
            /// 建物唯一識別碼
            /// </summary>
            public string Mid { get; init; } = string.Empty;

            /// <summary>
            /// 建物舊 ID
            /// </summary>
            public string Oid { get; init; } = string.Empty;

            /// <summary>
            /// 建號母號（同建物分組鍵）
            /// </summary>
            public string BuildingNo { get; init; } = string.Empty;

            /// <summary>
            /// 層次字串
            /// </summary>
            public string Floor { get; init; } = string.Empty;

            /// <summary>
            /// properties 中的「高度」欄位原始值
            /// </summary>
            public string HeightProperty { get; init; } = string.Empty;

            /// <summary>
            /// 外輪廓環座標
            /// </summary>
            public List<List<double>> Ring { get; init; } = new();

            /// <summary>
            /// 平面 Z（通常為環的最小 Z）
            /// </summary>
            public double PlaneZ { get; set; }

            /// <summary>
            /// 膨脹後底面 Z
            /// </summary>
            public double MinZ { get; set; }

            /// <summary>
            /// 膨脹後頂面 Z
            /// </summary>
            public double MaxZ { get; set; }

            /// <summary>
            /// 是否為平面幾何（Z 差在容差內）
            /// </summary>
            public bool IsFlat { get; set; }

            /// <summary>
            /// 是否已具足夠高度的立體幾何，無需膨脹
            /// </summary>
            public bool AlreadySolid { get; set; }

            /// <summary>
            /// 原始 boundedBy JSON
            /// </summary>
            public string OriginalBoundedBy { get; init; } = string.Empty;

            /// <summary>
            /// 目前（可能已膨脹）的 boundedBy JSON
            /// </summary>
            public string BoundedByRaw { get; set; } = string.Empty;

            /// <summary>
            /// 是否已執行平面→立體膨脹
            /// </summary>
            public bool WasInflated { get; set; }

            /// <summary>
            /// 膨脹修復訊息（寫入 BuildingData.FixMessages）
            /// </summary>
            public string? InflateFixMessage { get; set; }
        }

        #region ◆解析 features 並依建號補成立體 [ProcessFeatures]
        /// <summary>
        /// 解析 GeoJSON features 並依建號補成立體樓層
        /// </summary>
        /// <param name="features">GeoJSON Feature 陣列</param>
        /// <returns>樓層草稿列表</returns>
        internal static List<GeoJsonFloorDraft> ProcessFeatures(JsonElement features)
        {
            var drafts = CollectDrafts(features);
            InflateByBuilding(drafts);
            return drafts;
        }
        #endregion

        #region ◆套用膨脹修復訊息 [ApplyInflateFixMessages]
        /// <summary>
        /// 套用補立體修復訊息至 ValidateAndFix 後的結果
        /// </summary>
        /// <param name="drafts">樓層草稿（與 buildings 同序）</param>
        /// <param name="buildings">驗證後的建物資料</param>
        internal static void ApplyInflateFixMessages(
            IReadOnlyList<GeoJsonFloorDraft> drafts,
            IReadOnlyList<BuildingData> buildings)
        {
            // 長度不一致時略過，避免對錯樓層
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
        #endregion

        #region ◆由 features 蒐集樓層草稿 [CollectDrafts]
        /// <summary>
        /// 由 GeoJSON features 蒐集樓層草稿（欄位、外環、平面/立體判斷）
        /// </summary>
        private static List<GeoJsonFloorDraft> CollectDrafts(JsonElement features)
        {
            var drafts = new List<GeoJsonFloorDraft>();

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props))
                {
                    continue;
                }

                // 取出外輪廓；不足 3 點則略過
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
                    // Z 差足夠大視為已是立體，不要再膨脹
                    AlreadySolid = !isFlat && maxZ - minZ >= MinExistingSolidHeight,
                    OriginalBoundedBy = originalBoundedBy,
                    BoundedByRaw = originalBoundedBy,
                });
            }

            return drafts;
        }
        #endregion

        #region ◆依建號分組並膨脹平面樓層 [InflateByBuilding]
        /// <summary>
        /// 依建號分組、排序後決定垂直跨距，並將平面樓層補成立體
        /// </summary>
        private static void InflateByBuilding(List<GeoJsonFloorDraft> drafts)
        {
            var groups = drafts
                .GroupBy(d => d.BuildingNo)
                .ToList();

            foreach (var group in groups)
            {
                // 同建物依樓層規則由下而上排序
                var sorted = group
                    .OrderBy(d => d.Floor, Comparer<string>.Create(GeoJsonFloorOrdering.Compare))
                    .ToList();

                DecideVerticalSpans(sorted);

                foreach (var draft in sorted)
                {
                    // 已是立體或非平面：跳過
                    if (draft.AlreadySolid)
                    {
                        continue;
                    }

                    if (!draft.IsFlat)
                    {
                        continue;
                    }

                    // 依底/頂 Z 建立底面、頂面、側牆
                    var solid = BuildSolidFaces(draft.Ring, draft.MinZ, draft.MaxZ);
                    draft.BoundedByRaw = JsonSerializer.Serialize(solid);
                    draft.WasInflated = true;
                    draft.InflateFixMessage =
                        $"GeoJSON 修復：已將平面樓層補成立體幾何（底面 {draft.MinZ:F1}m ~ 頂面 {draft.MaxZ:F1}m）";
                }
            }
        }
        #endregion

        #region ◆決定各樓層垂直跨距 [DecideVerticalSpans]
        /// <summary>
        /// 決定各平面樓層的底/頂 Z：優先採下一層平面 Z，其次高度欄位，最後預設净高
        /// </summary>
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

                // 有上一層 → 以上一層平面 Z 作為本層頂部
                if (i + 1 < sorted.Count)
                {
                    topZ = sorted[i + 1].PlaneZ;
                }
                // 否則嘗試以「高度」欄位作為絕對頂部高程
                else if (TryParseHeightAsTopZ(current.HeightProperty, baseZ, out var propertyTopZ))
                {
                    topZ = propertyTopZ;
                }
                // 最後退回預設净高
                else
                {
                    topZ = baseZ + DefaultFloorHeight;
                }

                // 頂部與底部過近時強制使用預設净高，避免退化體
                if (topZ <= baseZ + FlatZTolerance)
                {
                    topZ = baseZ + DefaultFloorHeight;
                }

                current.MinZ = baseZ;
                current.MaxZ = topZ;
            }
        }

        /// <summary>
        /// 將「高度」屬性解析為絕對頂部高程；須大於 baseZ 才有效
        /// </summary>
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
        #endregion

        #region ◆建立立體面（底面、頂面、側牆）[BuildSolidFaces]
        /// <summary>
        /// 由平面 footprint 建立立體面：底面、頂面與各側牆
        /// </summary>
        private static List<List<List<double>>> BuildSolidFaces(
            List<List<double>> ring,
            double baseZ,
            double topZ)
        {
            var footprint = NormalizeFootprint(ring);
            var solids = new List<List<List<double>>>();

            // 底面與頂面
            solids.Add(CreateHorizontalFace(footprint, baseZ));
            solids.Add(CreateHorizontalFace(footprint, topZ));

            // 沿 footprint 邊建立垂直側牆（閉合四邊形）
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

        /// <summary>
        /// 以固定 Z 建立水平面
        /// </summary>
        private static List<List<double>> CreateHorizontalFace(List<List<double>> footprint, double z)
        {
            return footprint
                .Select(point => new List<double> { point[0], point[1], z })
                .ToList();
        }

        /// <summary>
        /// 正規化 footprint：僅保留 XY，並確保首尾閉合
        /// </summary>
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

            // 未閉合則補上起點
            if (!IsSamePoint(footprint[0], footprint[^1]))
            {
                footprint.Add(new List<double> { footprint[0][0], footprint[0][1] });
            }

            return footprint;
        }
        #endregion

        #region ◆幾何與 JSON 輔助 [Geometry / JSON Helpers]
        /// <summary>
        /// 分析環的最小/最大 Z，並判斷是否為平面
        /// </summary>
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

        /// <summary>
        /// 依 geometry type 取出外輪廓環（Polygon / MultiPolygon / 舊格式）
        /// </summary>
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
                // type 缺失時嘗試舊格式座標巢狀
                null => TryParseLegacyCoordinates(coordinates),
                _ => null,
            };
        }

        /// <summary>
        /// 嘗試解析缺少 geometry.type 的舊版座標結構
        /// </summary>
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
            // [ [x,y,z], ... ] 形式的單一環
            if (firstPoint.ValueKind == JsonValueKind.Array
                && firstPoint.GetArrayLength() >= 2
                && firstPoint[0].ValueKind == JsonValueKind.Number)
            {
                return ParseRing(first);
            }

            // 再包一層的 polygon 形式
            if (firstPoint.ValueKind == JsonValueKind.Array)
            {
                return ParseRing(coordinates[0]);
            }

            return null;
        }

        /// <summary>
        /// 解析環座標，不足 3 維的點補 z=0
        /// </summary>
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

                // 保證至少 lon/lat/z 三個值
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

        /// <summary>
        /// 以小容差比較 XY 是否為同一點
        /// </summary>
        private static bool IsSamePoint(List<double> a, List<double> b)
        {
            return Math.Abs(a[0] - b[0]) <= 0.000001
                && Math.Abs(a[1] - b[1]) <= 0.000001;
        }

        /// <summary>
        /// 將 JSON 屬性轉為字串（支援 string / number / bool）
        /// </summary>
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
        #endregion
    }
}
