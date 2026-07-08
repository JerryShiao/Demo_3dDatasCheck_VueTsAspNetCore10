using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 建物資料處理服務
    /// </summary>

    public class BuildingProcessorService(
        IOptions<BuildingAbnormalDetectionOptions> detectionOptions,
        IXmlImportPreprocessor xmlImportPreprocessor,
        ICoordinateTransformService coordinateTransformService)
    {
        // XML 候選節點與欄位別名
        private static readonly string[] XmlBuildingElementNames =
        [
            "ConsistsOfBuildingPart",
            "consistsOfBuildingPart",
            "BuildingRegistration",
            "產權建物",
            "建物產權空間",
        ];
        private static readonly string[] XmlMidNames = ["MID", "mid"];
        private static readonly string[] XmlOidNames = ["OID", "oid"];
        private static readonly string[] XmlBuildingNoNames = ["建號母號", "buildingNo", "BuildingNo"];
        private static readonly string[] XmlFloorNames = ["層次", "floor", "Floor"];
        private static readonly string[] XmlBoundedByNames = ["boundedBy", "BoundedBy"];

        private readonly BuildingAbnormalDetectionOptions _detection = detectionOptions.Value;
        private readonly IXmlImportPreprocessor _xmlImportPreprocessor = xmlImportPreprocessor;
        private readonly ICoordinateTransformService _coordinateTransformService = coordinateTransformService;

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
            var preprocessResult = PreprocessXmlIfNeeded(xmlContent);
            return ProcessXmlDocument(preprocessResult);
        }

        /// <summary>
        /// CityGML 預處理入口；非 CityGML 直接 passthrough
        /// </summary>
        internal XmlImportPreprocessResult PreprocessXmlIfNeeded(string xmlContent)
        {
            return _xmlImportPreprocessor.Preprocess(xmlContent);
        }

        /// <summary>
        /// 解析 XML 文件並沿用既有正規化、驗證與異常檢測流程
        /// </summary>
        internal List<BuildingData> ProcessXmlDocument(XmlImportPreprocessResult preprocessResult)
        {
            // 解析 XML 字串
            var doc = XDocument.Parse(preprocessResult.XmlContent);

            // 先依常見建物節點名搜尋，若找不到再退回欄位導向的寬鬆搜尋
            var elements = FindCandidateElements(doc).ToList();

            // 將 XML 元素轉換為 BuildingData 物件列表
            var buildings = elements
                .Select((el, index) => ValidateAndFix(CreateBuildingFromXmlElement(el, index)))
                .ToList();

            ApplyXmlPreprocessMessages(buildings, preprocessResult);

            // 解析坐標字串並進行異常檢測
            DetectAbnormalIssues(buildings);

            // 返回處理後的建物資料列表
            return buildings;
        }
        #endregion

        /// <summary>
        /// 將 XML 預處理結果映射到既有狀態欄位，避免改動前端契約
        /// </summary>
        private static void ApplyXmlPreprocessMessages(
            List<BuildingData> buildings,
            XmlImportPreprocessResult preprocessResult)
        {
            if (buildings.Count == 0 || preprocessResult.Messages.Count == 0)
            {
                return;
            }

            foreach (var building in buildings)
            {
                foreach (var message in preprocessResult.Messages)
                {
                    if (preprocessResult.RepairApplied)
                    {
                        building.IsFixed = true;
                        AddUniqueMessage(building.FixMessages, $"CityGML 拓撲預處理：{message}");
                    }
                    else
                    {
                        AddUniqueMessage(building.FixMessages, $"CityGML 匯入提示：{message}");
                    }
                }
            }
        }

        private static void AddUniqueMessage(List<string> messages, string message)
        {
            if (!messages.Contains(message))
            {
                messages.Add(message);
            }
        }

        #region ◆XML local-name 動態辨識輔助 [XML Helpers]
        /// <summary>
        /// 比對 XML 節點 local-name，忽略 prefix 與大小寫差異
        /// </summary>
        private static bool MatchesLocalName(XName? name, params string[] candidates)
        {
            if (name == null)
            {
                return false;
            }

            return candidates.Any(candidate =>
                string.Equals(name.LocalName, candidate, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 查找候選建物節點；找不到已知節點名時，退回搜尋同時包含關鍵欄位的區塊
        /// </summary>
        private static IEnumerable<XElement> FindCandidateElements(XDocument doc)
        {
            var directMatches = doc
                .Descendants()
                .Where(el => MatchesLocalName(el.Name, XmlBuildingElementNames))
                .Where(el => !el.Descendants().Any(child => MatchesLocalName(child.Name, XmlBuildingElementNames)))
                .ToList();

            if (directMatches.Count > 0)
            {
                return directMatches;
            }

            return doc
                .Descendants()
                .Where(HasRecognizableBuildingPayload)
                .Where(el => !el.Elements().Any(HasRecognizableBuildingPayload))
                .ToList();
        }

        /// <summary>
        /// 判斷節點是否含有可辨識的建物欄位，用於 fallback 搜尋
        /// </summary>
        private static bool HasRecognizableBuildingPayload(XElement element)
        {
            var hasIdentity = HasAnyElement(element, XmlMidNames)
                || HasAnyElement(element, XmlOidNames)
                || HasAttribute(element, "id");
            var hasFloorOrNo = HasAnyElement(element, XmlBuildingNoNames)
                || HasAnyElement(element, XmlFloorNames);
            var hasGeometry = HasAnyElement(element, XmlBoundedByNames);

            return hasGeometry && (hasIdentity || hasFloorOrNo);
        }

        /// <summary>
        /// 建立 XML 匯入後的建物資料，並先完成幾何正規化
        /// </summary>
        private BuildingData CreateBuildingFromXmlElement(XElement element, int index)
        {
            var buildingNo = GetFirstNonEmpty(element, XmlBuildingNoNames);
            var floor = GetFirstNonEmpty(element, XmlFloorNames);
            var xmlId = GetFirstNonEmptyAttribute(element, "id");
            var mid = GetFirstNonEmpty(element, XmlMidNames);
            var oid = GetFirstNonEmpty(element, XmlOidNames);

            if (string.IsNullOrWhiteSpace(mid))
            {
                mid = !string.IsNullOrWhiteSpace(xmlId)
                    ? xmlId
                    : BuildFallbackIdentifier(buildingNo, floor, index, "MID");
            }

            if (string.IsNullOrWhiteSpace(oid))
            {
                oid = !string.IsNullOrWhiteSpace(xmlId)
                    ? xmlId
                    : mid;
            }

            var geometry = ExtractXmlGeometry(element);

            var building = new BuildingData
            {
                Mid = mid,
                Oid = oid,
                BuildingNo = buildingNo,
                Floor = floor,
                BoundedByRaw = geometry.BoundedByRaw,
                Coordinates = geometry.Coordinates,
            };

            foreach (var message in geometry.Messages)
            {
                building.IsFixed = true;
                AddUniqueMessage(building.FixMessages, message);
            }

            return building;
        }

        /// <summary>
        /// 取得欄位別名中的第一個有效值
        /// </summary>
        private static string GetFirstNonEmpty(XElement element, params string[] localNames)
        {
            foreach (var localName in localNames)
            {
                var value = GetElementValue(element, localName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        /// <summary>
        /// 取得指定 local-name 屬性的第一個有效值
        /// </summary>
        private static string GetFirstNonEmptyAttribute(XElement element, params string[] localNames)
        {
            foreach (var localName in localNames)
            {
                var value = GetAttributeValue(element, localName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        /// <summary>
        /// 先找直接子節點，必要時再找後代節點，取得指定欄位值
        /// </summary>
        private static string GetElementValue(XElement parent, string localName)
        {
            var direct = parent.Elements()
                .FirstOrDefault(el => MatchesLocalName(el.Name, localName));
            if (direct != null)
            {
                return ExtractElementText(direct);
            }

            var nested = parent.Descendants()
                .FirstOrDefault(el => MatchesLocalName(el.Name, localName));
            return nested == null ? "" : ExtractElementText(nested);
        }

        /// <summary>
        /// 先找目前節點，再找後代節點上的屬性值
        /// </summary>
        private static string GetAttributeValue(XElement parent, string localName)
        {
            var self = parent.Attributes()
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
            if (self != null)
            {
                return self.Value.Trim();
            }

            var nested = parent
                .DescendantsAndSelf()
                .SelectMany(el => el.Attributes())
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

            return nested?.Value.Trim() ?? "";
        }

        /// <summary>
        /// 檢查目前節點或其後代是否存在指定欄位
        /// </summary>
        private static bool HasAnyElement(XElement parent, params string[] localNames)
        {
            return parent
                .DescendantsAndSelf()
                .Any(el => MatchesLocalName(el.Name, localNames));
        }

        /// <summary>
        /// 檢查目前節點或其後代是否存在指定 local-name 的屬性
        /// </summary>
        private static bool HasAttribute(XElement parent, params string[] localNames)
        {
            return parent
                .DescendantsAndSelf()
                .SelectMany(el => el.Attributes())
                .Any(attr => localNames.Any(localName =>
                    string.Equals(attr.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// 取出元素文字內容；若只有單一文字節點則保留原始值，否則串接內文
        /// </summary>
        private static string ExtractElementText(XElement element)
        {
            if (!element.HasElements)
            {
                return element.Value.Trim();
            }

            var textNode = element.Nodes().OfType<XText>().Select(text => text.Value).ToList();
            if (textNode.Count > 0)
            {
                var merged = string.Concat(textNode).Trim();
                if (!string.IsNullOrWhiteSpace(merged))
                {
                    return merged;
                }
            }

            return string.Concat(element.DescendantNodes().OfType<XText>().Select(text => text.Value)).Trim();
        }

        /// <summary>
        /// 萃取 XML 幾何並正規化為既有 coordinates 結構
        /// </summary>
        private XmlGeometryExtractionResult ExtractXmlGeometry(XElement element)
        {
            var boundedByElements = element
                .DescendantsAndSelf()
                .Where(el => MatchesLocalName(el.Name, XmlBoundedByNames))
                .ToList();

            if (boundedByElements.Count == 0)
            {
                return new XmlGeometryExtractionResult
                {
                    BoundedByRaw = "",
                    Coordinates = new(),
                };
            }

            foreach (var boundedByElement in boundedByElements)
            {
                var text = ExtractElementText(boundedByElement);
                if (TryParseCoordinateJson(text, out var jsonCoordinates))
                {
                    return new XmlGeometryExtractionResult
                    {
                        BoundedByRaw = text,
                        Coordinates = jsonCoordinates,
                    };
                }
            }

            var polygons = new List<List<List<double>>>();
            var messages = new List<string>();
            foreach (var boundedByElement in boundedByElements)
            {
                polygons.AddRange(ExtractPolygonsFromBoundedBy(boundedByElement, messages));
            }

            if (polygons.Count == 0)
            {
                return new XmlGeometryExtractionResult
                {
                    BoundedByRaw = ExtractElementText(boundedByElements[0]),
                    Coordinates = new(),
                    Messages = messages,
                };
            }

            return new XmlGeometryExtractionResult
            {
                BoundedByRaw = JsonSerializer.Serialize(polygons),
                Coordinates = polygons,
                Messages = messages,
            };
        }

        /// <summary>
        /// 解析 boundedBy 內的 GML Polygon / posList
        /// </summary>
        private List<List<List<double>>> ExtractPolygonsFromBoundedBy(
            XElement boundedByElement,
            List<string> messages)
        {
            return boundedByElement
                .Descendants()
                .Where(el => MatchesLocalName(el.Name, "Polygon"))
                .Select(polygon => ExtractPolygonCoordinates(polygon, messages))
                .Where(polygon => polygon.Count >= 3)
                .ToList();
        }

        /// <summary>
        /// 解析單一 Polygon 的 exterior ring
        /// </summary>
        private List<List<double>> ExtractPolygonCoordinates(XElement polygonElement, List<string> messages)
        {
            var exterior = polygonElement
                .Elements()
                .FirstOrDefault(el => MatchesLocalName(el.Name, "exterior"));
            if (exterior == null)
            {
                return new();
            }

            var linearRing = exterior
                .Descendants()
                .FirstOrDefault(el => MatchesLocalName(el.Name, "LinearRing"));
            if (linearRing == null)
            {
                return new();
            }

            var posList = linearRing
                .Elements()
                .FirstOrDefault(el => MatchesLocalName(el.Name, "posList"));
            if (posList != null)
            {
                return ParsePosList(posList, messages);
            }

            var positions = linearRing
                .Elements()
                .Where(el => MatchesLocalName(el.Name, "pos"))
                .Select(pos => ParsePos(pos, messages))
                .Where(point => point.Count >= 3)
                .ToList();

            return positions;
        }

        /// <summary>
        /// 解析 GML posList；支援 2D/3D，2D 時自動補 z=0
        /// </summary>
        private List<List<double>> ParsePosList(XElement posListElement, List<string> messages)
        {
            var tokens = posListElement
                .Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length < 6)
            {
                return new();
            }

            var values = new List<double>(tokens.Length);
            foreach (var token in tokens)
            {
                if (!double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
                {
                    return new();
                }
                values.Add(value);
            }

            var dimension = ResolveCoordinateDimension(posListElement, values.Count);
            if (dimension < 2 || values.Count % dimension != 0)
            {
                return new();
            }

            var points = new List<List<double>>();
            for (var i = 0; i < values.Count; i += dimension)
            {
                var point = new List<double> { values[i], values[i + 1] };
                point.Add(dimension >= 3 ? values[i + 2] : 0);
                points.Add(point);
            }

            var context = XmlCoordinateReferenceResolver.ResolveForGeometry(posListElement);
            var normalized = _coordinateTransformService.NormalizeToWgs84(points, context);
            foreach (var message in normalized.Messages)
            {
                AddUniqueMessage(messages, message);
            }

            return normalized.Points;
        }

        /// <summary>
        /// 解析單一 GML pos 點
        /// </summary>
        private List<double> ParsePos(XElement posElement, List<string> messages)
        {
            var tokens = posElement
                .Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length < 2)
            {
                return new();
            }

            var values = new List<double>();
            foreach (var token in tokens)
            {
                if (!double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
                {
                    return new();
                }
                values.Add(value);
            }

            while (values.Count < 3)
            {
                values.Add(0);
            }

            var context = XmlCoordinateReferenceResolver.ResolveForGeometry(posElement);
            var normalized = _coordinateTransformService.NormalizeToWgs84([values.Take(3).ToList()], context);
            foreach (var message in normalized.Messages)
            {
                AddUniqueMessage(messages, message);
            }

            return normalized.Points.FirstOrDefault() ?? [];
        }

        /// <summary>
        /// 判斷座標維度，優先使用 srsDimension / dimension，其次依數量推論
        /// </summary>
        private static int ResolveCoordinateDimension(XElement posListElement, int valueCount)
        {
            var dimensionText = posListElement.Attributes()
                .FirstOrDefault(attr =>
                    string.Equals(attr.Name.LocalName, "srsDimension", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(attr.Name.LocalName, "dimension", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2)
            {
                return parsedDimension;
            }

            if (valueCount % 3 == 0)
            {
                return 3;
            }

            if (valueCount % 2 == 0)
            {
                return 2;
            }

            return 0;
        }

        /// <summary>
        /// 產生識別欄位 fallback 值
        /// </summary>
        private static string BuildFallbackIdentifier(string buildingNo, string floor, int index, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(buildingNo) && !string.IsNullOrWhiteSpace(floor))
            {
                return $"{prefix}_{buildingNo}_{floor}";
            }

            return $"{prefix}_XML_{index + 1:0000}";
        }

        /// <summary>
        /// 嘗試將舊格式 JSON 座標字串轉成 3D polygon 清單
        /// </summary>
        private static bool TryParseCoordinateJson(
            string raw,
            out List<List<List<double>>> coordinates)
        {
            coordinates = new();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<List<List<double>>>>(raw);
                if (parsed == null || parsed.Count == 0)
                {
                    return false;
                }

                coordinates = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class XmlGeometryExtractionResult
        {
            public string BoundedByRaw { get; init; } = string.Empty;

            public List<List<List<double>>> Coordinates { get; init; } = new();

            public List<string> Messages { get; init; } = [];
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
        private void DetectAbnormalIssues(List<BuildingData> buildings)
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
        private void DetectSinglePartAbnormal(BuildingData dto)
        {
            // 如果最小高度或最大高度為 null，則無法進行檢測
            if (!dto.MinHeight.HasValue || !dto.MaxHeight.HasValue)
            {
                return;
            }

            // 計算樓層高度
            var height = dto.MaxHeight.Value - dto.MinHeight.Value;

            // 檢測樓層高度是否低於最小合理層高
            if (height < _detection.MinFloorHeight)
            {
                MarkAbnormal(dto, $"樓層高度異常偏低（{height:F1}m，低於 {_detection.MinFloorHeight}m）");
            }

            // 檢測樓層高度是否高於最大合理層高
            else if (height > _detection.MaxFloorHeight)
            {
                MarkAbnormal(dto, $"樓層高度異常偏高（{height:F1}m，高於 {_detection.MaxFloorHeight}m）");
            }

            // 僅一般地上 1 樓進行地面層底部高度檢測（排除 R01 / B1 等特殊樓層）
            var floorKey = GeoJsonFloorOrdering.Parse(dto.Floor);
            if (floorKey.Category == GeoJsonFloorOrdering.FloorCategory.Regular
                && floorKey.Number == 1
                && dto.MinHeight.Value > _detection.GroundFloorBottomThreshold)
            {
                // 樓層底部高度異常，標記為異常
                MarkAbnormal(dto, $"疑似浮空：1 樓底部高度 {dto.MinHeight.Value:F1}m，離地超過 {_detection.GroundFloorBottomThreshold}m");
            }
        }
        #endregion

        #region ◆檢測相鄰樓層 [CompareAdjacentFloors]
        /// <summary>
        /// 檢測相鄰樓層
        /// </summary>
        /// <param name="lowerFloor">前一層建物</param>
        /// <param name="upperFloor">當前層建物</param>
        /// <param name="lowerFloorLabel">前一層樓層標籤</param>
        /// <param name="upperFloorLabel">當前層樓層標籤</param>
        private void CompareAdjacentFloors(
            BuildingData lowerFloor,
            BuildingData upperFloor,
            string lowerFloorLabel,
            string upperFloorLabel)
        {
            // 計算樓層高度差
            var gap = upperFloor.MinHeight!.Value - lowerFloor.MaxHeight!.Value;

            // 樓層高度差 > 最大合理層高，標記為垂直斷層
            if (gap > _detection.MaxFloorGap)
            {
                // 樓層高度差異過大，標記為垂直斷層
                MarkAbnormal(lowerFloor,
                    $"與 {upperFloorLabel} 樓之間垂直斷層（落差 {gap:F1}m，超過 {_detection.MaxFloorGap}m）");

                // 標記上層建物為垂直斷層
                MarkAbnormal(upperFloor,
                    $"與 {lowerFloorLabel} 樓之間垂直斷層（落差 {gap:F1}m，超過 {_detection.MaxFloorGap}m）");
            }
            // 高度差 < 層間高度容許誤差，則標記為垂直重疊
            else if (gap < -_detection.FloorGapTolerance)
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
            AddUniqueMessage(dto.ErrorMessages, message);
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
                AddUniqueMessage(dto.ErrorMessages, "建號缺漏");
                dto.BuildingNo = "UNKNOWN_NO";
                dto.IsFixed = true;
                AddUniqueMessage(dto.FixMessages, "已自動預設建號為 UNKNOWN_NO");
            }

            // 樓層缺漏修復
            if (string.IsNullOrWhiteSpace(dto.Floor))
            {
                dto.IsValid = false;
                AddUniqueMessage(dto.ErrorMessages, "層次缺漏");
                dto.Floor = "001";
                dto.IsFixed = true;
                AddUniqueMessage(dto.FixMessages, "已自動預設層次為 001 樓");
            }

            // 樓層高度缺漏修復
            if (string.IsNullOrWhiteSpace(dto.BoundedByRaw))
            {
                if (dto.Coordinates.Count == 0)
                {
                    dto.IsValid = false;
                    AddUniqueMessage(dto.ErrorMessages, "座標完全缺漏，無法進行 3D 繪製");
                    return dto;
                }
            }

            if (dto.Coordinates.Count == 0)
            {
                if (!TryParseCoordinateJson(dto.BoundedByRaw, out var rawCoords))
                {
                    dto.IsValid = false;
                    AddUniqueMessage(dto.ErrorMessages, "座標 JSON 格式解析失敗");
                    return dto;
                }

                dto.Coordinates = rawCoords;
            }

            var hadInvalidEntries = dto.Coordinates.Any(p =>
                p == null || p.Any(pt => pt == null || pt.Count < 3));
            var sanitized = SanitizeCoordinates(dto.Coordinates);
            if (sanitized.Count == 0)
            {
                dto.IsValid = false;
                AddUniqueMessage(dto.ErrorMessages, "座標資料無效（僅含空值或點數不足）");
                dto.Coordinates = new();
                return dto;
            }
            if (hadInvalidEntries)
            {
                dto.IsValid = false;
                AddUniqueMessage(dto.ErrorMessages, "座標含空值或無效點，已過濾無效幾何");
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
                    AddUniqueMessage(dto.ErrorMessages, "座標未閉合（幾何邊界破面異常）"); // 錯誤訊息
                    polygon.Add(new List<double> { firstPoint[0], firstPoint[1], firstPoint[2] }); // 將第1個點的座標加入到最後，形成閉合多邊形
                    dto.IsFixed = true; // 標記為已修復
                    AddUniqueMessage(dto.FixMessages, "幾何修復：已自動追加閉合端點"); // 修復訊息
                }
            }
            ComputeHeightBounds(dto); // 再次計算高度邊界，因為可能已經修復了座標
            return dto;
        }
        #endregion

    }//class end
}//namespace end