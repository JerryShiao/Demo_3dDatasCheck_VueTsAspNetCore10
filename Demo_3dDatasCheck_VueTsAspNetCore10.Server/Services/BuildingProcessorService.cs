using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;
using System.Text.Json;
using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 建物資料處理服務
    /// </summary>

    public class BuildingProcessorService
    {
        // XML 命名空間（XML Namespace）的識別碼
        private static readonly XNamespace XmlNs =
            "http://schemas.datacontract.org/2004/07/ModelOfBuilding_WebAPI.Models";

        #region -- 異常檢測閾值（公尺）
        /// <summary>
        /// 地面層底部高度閾值
        /// </summary>
        private const double GroundFloorBottomThreshold = 5.0;
        /// <summary>
        /// 層間高度容許誤差（公尺）
        /// </summary>
        private const double FloorGapTolerance = 0.5;
        /// <summary>
        /// 最大合理層間高度（公尺）
        /// </summary>
        private const double MaxFloorGap = 3.0;
        /// <summary>
        /// 最小合理層高（公尺）
        /// </summary>
        private const double MinFloorHeight = 2.0;
        /// <summary>
        /// 最大合理層高（公尺）
        /// </summary>
        private const double MaxFloorHeight = 8.0;
        #endregion

        #region ◆依內容格式自動選擇 XML 或 JSON 解析 [ProcessContent]
        /// <summary>
        /// 依內容格式自動選擇 XML 或 JSON 解析
        /// </summary>
        public List<BuildingData> ProcessContent(string content)
        {
            var trimmed = content.TrimStart(); // 去除前置空白以判斷格式
            // 嘗試解析為 JSON
            if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
            {
                return ProcessJson(content);
            }
            // 嘗試解析為 XML
            else if (trimmed.StartsWith('<'))
            {
                return ProcessXml(content);
            }
            else
            {
                throw new FormatException("無法識別的資料格式，請提供有效的 JSON 或 XML 內容。");
            }
        }
        #endregion

        #region ◆解析建物資料（XML） [ProcessXml]
        /// <summary>
        /// 解析建物資料（XML）
        /// </summary>
        public List<BuildingData> ProcessXml(string xmlContent)
        {
            // 解析 XML 字串
            var doc = XDocument.Parse(xmlContent);

            // 查找建物元素（支援 ConsistsOfBuildingPart 與 BuildingRegistration 兩種 XML 格式）
            var elements = doc.Descendants(XmlNs + "ConsistsOfBuildingPart");
            if (!elements.Any())
            {
                elements = doc.Descendants(XmlNs + "BuildingRegistration");
            }

            // 將 XML 元素轉換為 BuildingData 物件列表
            var buildings = elements.Select(el => ValidateAndFix(new BuildingData
            {
                Mid = el.Element(XmlNs + "MID")?.Value ?? "",               // 唯一識別符
                Oid = el.Element(XmlNs + "OID")?.Value ?? "",               // 原始識別符
                BuildingNo = el.Element(XmlNs + "建號母號")?.Value ?? "",   // 建號母號
                Floor = el.Element(XmlNs + "層次")?.Value ?? "",            // 層次
                BoundedByRaw = el.Element(XmlNs + "boundedBy")?.Value ?? "" // 原始坐標字串
            })).ToList();

            // 解析坐標字串並進行異常檢測
            DetectAbnormalIssues(buildings);

            // 返回處理後的建物資料列表
            return buildings;
        }
        #endregion

        #region ◆解析建物資料（JSON） [ProcessJson]
        /// <summary>
        /// 解析建物資料（JSON）
        /// </summary>
        public List<BuildingData> ProcessJson(string jsonContent)
        {
            var trimmed = jsonContent.TrimStart();
            if (trimmed.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "FeatureCollection" &&
                    root.TryGetProperty("features", out var features) &&
                    features.ValueKind == JsonValueKind.Array)
                {
                    var geoBuildings = ProcessGeoJsonFeatures(features);
                    DetectAbnormalIssues(geoBuildings);
                    return geoBuildings;
                }
            }

            // 反序列化 JSON 字串為 BuildingJsonRecord 物件列表（舊版陣列格式）
            var records = JsonSerializer.Deserialize<List<BuildingJsonRecord>>(jsonContent)
                ?? new List<BuildingJsonRecord>();

            // 將 JSON 記錄轉換為 BuildingData 物件列表
            var buildings = records.Select(r => ValidateAndFix(new BuildingData
            {
                Mid = r.MID.ToString(),          // 唯一識別符
                Oid = r.OID.ToString(),          // 原始識別符
                BuildingNo = r.建號母號 ?? "",   // 建號母號
                Floor = r.層次 ?? "",            // 層次
                BoundedByRaw = r.boundedBy ?? "" // 原始坐標字串
            })).ToList();

            DetectAbnormalIssues(buildings); // 解析坐標字串並進行異常檢測
            return buildings; // 返回處理後的建物資料列表
        }

        /// <summary>
        /// 解析 GeoJSON FeatureCollection 的 features 陣列
        /// </summary>
        private static List<BuildingData> ProcessGeoJsonFeatures(JsonElement features)
        {
            var drafts = GeoJsonSolidInflator.ProcessFeatures(features);
            var buildings = drafts
                .Select(draft => ValidateAndFix(new BuildingData
                {
                    Mid = draft.Mid,
                    Oid = draft.Oid,
                    BuildingNo = draft.BuildingNo,
                    Floor = draft.Floor,
                    BoundedByRaw = draft.BoundedByRaw,
                }))
                .ToList();

            GeoJsonSolidInflator.ApplyInflateFixMessages(drafts, buildings);
            return buildings;
        }

        /// <summary>
        /// 從 JsonElement 讀取屬性並轉為字串（支援字串與數字型別）
        /// </summary>
        private static string GetJsonPropertyAsString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var value))
            {
                return "";
            }
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText()
            };
        }
        #endregion

        #region ◆批次檢測垂直幾何異常 [DetectAbnormalIssues]
        /// <summary>
        /// 批次檢測垂直幾何異常：單樓層高度合理性與跨樓層垂直連續性
        /// </summary>
        private static void DetectAbnormalIssues(List<BuildingData> buildings)
        {
            // 對每個建物進行檢測
            foreach (var dto in buildings)
            {
                // 單樓層高度合理性檢測
                DetectSinglePartAbnormal(dto); 
            }

            // 依 BuildingNo 分組
            var groups = buildings
                .Where(b => b.BuildingNo != "UNKNOWN_NO" && b.MinHeight.HasValue && b.MaxHeight.HasValue)
                .GroupBy(b => b.BuildingNo);

            // 對每個建物群組進行跨樓層檢測
            foreach (var group in groups)
            {
                // 將建物群組按層次排序（支援 B1 / 001 / R01 等命名）
                var floors = group
                    .OrderBy(b => b.Floor, Comparer<string>.Create(GeoJsonFloorOrdering.Compare))
                    .ToList();

                // 逐層檢測相鄰樓層的高度差異
                for (var i = 1; i < floors.Count; i++)
                {
                    var prev = floors[i - 1];
                    var curr = floors[i];
                    CompareAdjacentFloors(
                        prev,
                        curr,
                        GeoJsonFloorOrdering.GetDisplayLabel(prev.Floor),
                        GeoJsonFloorOrdering.GetDisplayLabel(curr.Floor));
                }
            }
        }
        #endregion

        #region ◆單樓層高度合理性檢測 [DetectSinglePartAbnormal]
        /// <summary>
        /// 單樓層高度合理性檢測
        /// </summary>
        /// <param name="dto">建物資料</param>
        private static void DetectSinglePartAbnormal(BuildingData dto)
        {
            // 如果最小高度或最大高度為 null，則無法進行檢測
            if (!dto.MinHeight.HasValue || !dto.MaxHeight.HasValue)
            {
                return;
            }

            // 計算樓層高度
            var height = dto.MaxHeight.Value - dto.MinHeight.Value;

            // 檢測樓層高度是否低於最小合理層高
            if (height < MinFloorHeight)
            {
                MarkAbnormal(dto, $"樓層高度異常偏低（{height:F1}m，低於 {MinFloorHeight}m）");
            }

            // 檢測樓層高度是否高於最大合理層高
            else if (height > MaxFloorHeight)
            {
                MarkAbnormal(dto, $"樓層高度異常偏高（{height:F1}m，高於 {MaxFloorHeight}m）");
            }

            // 僅一般地上 1 樓進行地面層底部高度檢測（排除 R01 / B1 等特殊樓層）
            var floorKey = GeoJsonFloorOrdering.Parse(dto.Floor);
            if (floorKey.Category == GeoJsonFloorOrdering.FloorCategory.Regular
                && floorKey.Number == 1
                && dto.MinHeight.Value > GroundFloorBottomThreshold)
            {
                // 樓層底部高度異常，標記為異常
                MarkAbnormal(dto, $"疑似浮空：1 樓底部高度 {dto.MinHeight.Value:F1}m，離地超過 {GroundFloorBottomThreshold}m");
            }
        }
        #endregion

        #region ◆檢測相鄰樓層 [CompareAdjacentFloors]
        /// <summary>
        /// 檢測相鄰樓層
        /// </summary>
        /// <param name="lowerFloor">前一層建物</param>
        /// <param name="upperFloor">當前層建物</param>
        /// <param name="lowerFloorNo">前一層樓層號碼</param>
        /// <param name="upperFloorNo">當前層樓層號碼</param>
        private static void CompareAdjacentFloors(
            BuildingData lowerFloor,
            BuildingData upperFloor,
            string lowerFloorLabel,
            string upperFloorLabel)
        {
            // 計算樓層高度差
            var gap = upperFloor.MinHeight!.Value - lowerFloor.MaxHeight!.Value;

            // 樓層高度差 > 最大合理層高，標記為垂直斷層
            if (gap > MaxFloorGap)
            {
                // 樓層高度差異過大，標記為垂直斷層
                MarkAbnormal(lowerFloor,
                    $"與 {upperFloorLabel} 樓之間垂直斷層（落差 {gap:F1}m，超過 {MaxFloorGap}m）");

                // 標記上層建物為垂直斷層
                MarkAbnormal(upperFloor,
                    $"與 {lowerFloorLabel} 樓之間垂直斷層（落差 {gap:F1}m，超過 {MaxFloorGap}m）");
            }
            // 高度差 < 層間高度容許誤差，則標記為垂直重疊
            else if (gap < -FloorGapTolerance)
            {
                // 樓層高度差異過小，標記為垂直重疊
                MarkAbnormal(lowerFloor,
                    $"與 {upperFloorLabel} 樓垂直重疊（重疊 {Math.Abs(gap):F1}m）");

                // 標記上層建物為垂直重疊
                MarkAbnormal(upperFloor,
                    $"與 {lowerFloorLabel} 樓垂直重疊（重疊 {Math.Abs(gap):F1}m）");
            }

            if (upperFloor.MinHeight!.Value < lowerFloor.MinHeight!.Value)
            {
                MarkAbnormal(upperFloor,
                    $"樓層高度倒置：{upperFloorLabel} 樓底部低於 {lowerFloorLabel} 樓");
            }
        }
        #endregion

        #region ◆標記垂直幾何異常 [MarkAbnormal]
        /// <summary>
        /// 標記垂直幾何異常
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="message"></param>
        private static void MarkAbnormal(BuildingData dto, string message)
        {
            dto.IsAbnormal = true; // 標記為異常
            dto.IsValid = false;   // 標記為無效
            // 將異常訊息加入到錯誤訊息列表中
            if (!dto.ErrorMessages.Contains(message))
            {
                dto.ErrorMessages.Add(message);
            }               
        }
        #endregion

        #region ◆解析樓層號碼 [ParseFloorNumber]
        /// <summary>
        /// 解析樓層號碼
        /// </summary>
        /// <param name="floor">樓層</param>
        /// <returns></returns>

        private static int? ParseFloorNumber(string floor)
        {
            if (string.IsNullOrWhiteSpace(floor))
            {
                return null;
            }
            var digits = new string(floor.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) && n > 0 ? n : null;
        }
        #endregion

        #region ◆計算高度邊界 [ComputeHeightBounds]
        /// <summary>
        /// 計算高度邊界
        /// </summary>
        /// <param name="dto"></param>
        private static void ComputeHeightBounds(BuildingData dto)
        {
            double? minZ = null; // 最小高度
            double? maxZ = null; // 最大高度
            // 遍歷所有幾何圖形，計算最小高度和最大高度
            foreach (var polygon in dto.Coordinates)
            {
                foreach (var pt in polygon)
                {
                    if (pt == null || pt.Count < 3)
                    {
                        continue;
                    }                      
                    var z = pt[2]; // 取得高度值
                    minZ = minZ.HasValue ? Math.Min(minZ.Value, z) : z; // 更新最小高度
                    maxZ = maxZ.HasValue ? Math.Max(maxZ.Value, z) : z; // 更新最大高度
                }
            }
            dto.MinHeight = minZ; // 設定最小高度
            dto.MaxHeight = maxZ; // 設定最大高度
        }
        #endregion

        #region ◆過濾無效座標 [SanitizeCoordinates]
        /// <summary>
        /// 過濾 null polygon、null 點與點數不足的多邊形
        /// </summary>
        private static List<List<List<double>>> SanitizeCoordinates(List<List<List<double>>> rawCoords)
        {
            var sanitized = new List<List<List<double>>>();
            foreach (var polygon in rawCoords)
            {
                if (polygon == null)
                {
                    continue;
                }
                var validPoints = polygon
                    .Where(pt => pt != null && pt.Count >= 3)
                    .ToList();
                if (validPoints.Count >= 3)
                {
                    sanitized.Add(validPoints);
                }
            }
            return sanitized;
        }
        #endregion

        #region ◆建物資料驗證和修復 [ValidateAndFix]
        /// <summary>
        /// 建物資料驗證和修復
        /// </summary>
        /// <param name="dto">建物資料</param>
        /// <returns></returns>
        private static BuildingData ValidateAndFix(BuildingData dto)
        {
            // 建號缺漏修復
            if (string.IsNullOrWhiteSpace(dto.BuildingNo))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("建號缺漏");
                dto.BuildingNo = "UNKNOWN_NO";
                dto.IsFixed = true;
                dto.FixMessages.Add("已自動預設建號為 UNKNOWN_NO");
            }

            // 樓層缺漏修復
            if (string.IsNullOrWhiteSpace(dto.Floor))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("層次缺漏");
                dto.Floor = "001";
                dto.IsFixed = true;
                dto.FixMessages.Add("已自動預設層次為 001 樓");
            }

            // 樓層高度缺漏修復
            if (string.IsNullOrWhiteSpace(dto.BoundedByRaw))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("座標完全缺漏，無法進行 3D 繪製");
                return dto;
            }

            try
            {
                // 解析座標資料
                var rawCoords = JsonSerializer.Deserialize<List<List<List<double>>>>(dto.BoundedByRaw);
                if (rawCoords != null && rawCoords.Count > 0)
                {
                    var hadInvalidEntries = rawCoords.Any(p =>
                        p == null || p.Any(pt => pt == null || pt.Count < 3));
                    var sanitized = SanitizeCoordinates(rawCoords);
                    if (sanitized.Count == 0)
                    {
                        dto.IsValid = false;
                        dto.ErrorMessages.Add("座標資料無效（僅含空值或點數不足）");
                        dto.Coordinates = new();
                        return dto;
                    }
                    if (hadInvalidEntries)
                    {
                        dto.IsValid = false;
                        dto.ErrorMessages.Add("座標含空值或無效點，已過濾無效幾何");
                    }

                    dto.Coordinates = sanitized; // 設定已過濾的座標資料
                    ComputeHeightBounds(dto);    // 計算高度邊界

                    // 逐一檢測3D網格座標
                    foreach (var polygon in dto.Coordinates)
                    {
                        // 若座標點數不足3個，則無法形成多邊形，跳過不檢測
                        if (polygon.Count == 0)
                        {
                            continue;
                        }
                        // 若座標為空，則無法形成多邊形，跳過不檢測
                        bool isValidPolygon = true;
                        foreach (var pt in polygon)
                        {
                            if (pt == null || pt.Count < 3)
                            {
                                isValidPolygon = false;
                                break;
                            }
                        }
                        if (!isValidPolygon)
                        {
                            continue;
                        }

                        // 第1個點的座標
                        var firstPoint = polygon[0];

                        // 最後1個點的座標
                        var lastPoint = polygon[^1];

                        // 若第1個點與最後1個點的座標差異過大，表示座標未封閉，則進行修復
                        if (Math.Abs(firstPoint[0] - lastPoint[0]) > 0.000001 ||
                            Math.Abs(firstPoint[1] - lastPoint[1]) > 0.000001)
                        {
                            dto.IsValid = false; // 標記為無效
                            dto.ErrorMessages.Add("座標未閉合（幾何邊界破面異常）"); // 錯誤訊息
                            polygon.Add(new List<double> { firstPoint[0], firstPoint[1], firstPoint[2] }); // 將第1個點的座標加入到最後，形成閉合多邊形
                            dto.IsFixed = true; // 標記為已修復
                            dto.FixMessages.Add("幾何修復：已自動追加閉合端點"); // 修復訊息
                        }
                    }
                    ComputeHeightBounds(dto); // 再次計算高度邊界，因為可能已經修復了座標
                }
            }
            catch
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("座標 JSON 格式解析失敗");
            }
            return dto;
        }
        #endregion

    }//class end
}//namespace end