using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML/GML 座標參考系統解析器：由 srsName 推得 CRS 脈絡
    /// </summary>
    internal static class XmlCoordinateReferenceResolver
    {
        #region ◆解析幾何節點的 CRS 脈絡 [ResolveForGeometry]
        /// <summary>
        /// 解析幾何節點（含祖先）上的 srsName，建立座標參考脈絡
        /// </summary>
        /// <param name="element">幾何相關 XML 元素</param>
        /// <returns>座標參考脈絡；找不到 srsName 時回傳空白脈絡</returns>
        internal static CoordinateReferenceContext ResolveForGeometry(XElement element)
        {
            var srsName = FindFirstSrsName(element);
            if (!string.IsNullOrWhiteSpace(srsName))
            {
                return ResolveFromSrsName(srsName);
            }

            return new CoordinateReferenceContext();
        }
        #endregion

        #region ◆查找 srsName 屬性 [FindFirstSrsName]
        /// <summary>
        /// 先找目前節點與後代的 srsName，再往祖先節點搜尋
        /// </summary>
        private static string? FindFirstSrsName(XElement element)
        {
            // 優先：節點本身或其後代上的 srsName
            var direct = element
                .DescendantsAndSelf()
                .Attributes()
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, "srsName", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            // 其次：祖先節點上的 srsName（常見於 CityGML 繼承宣告）
            return element
                .Ancestors()
                .SelectMany(ancestor => ancestor.Attributes())
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, "srsName", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }
        #endregion

        #region ◆由 srsName 對應已知 CRS [ResolveFromSrsName]
        /// <summary>
        /// 將 srsName 字串解析為已知 CRS 脈絡（軸序、地理/投影）
        /// </summary>
        private static CoordinateReferenceContext ResolveFromSrsName(string srsName)
        {
            var trimmed = srsName.Trim();
            var upper = trimmed.ToUpperInvariant();

            // CRS:84 → lon/lat 地理座標
            if (upper.Contains("CRS84", StringComparison.Ordinal))
            {
                return new CoordinateReferenceContext
                {
                    SourceCrsId = "CRS:84",
                    IsGeographic = true,
                    AxisOrder = CoordinateAxisOrder.EastNorth,
                };
            }

            // 嘗試擷取 EPSG 代碼並對應
            if (TryExtractEpsgCode(trimmed, out var epsgCode))
            {
                return epsgCode switch
                {
                    4326 => new CoordinateReferenceContext
                    {
                        SourceCrsId = "EPSG:4326",
                        IsGeographic = true,
                        AxisOrder = CoordinateAxisOrder.EastNorth,
                    },
                    3826 => new CoordinateReferenceContext
                    {
                        SourceCrsId = "EPSG:3826",
                        IsGeographic = false,
                        AxisOrder = CoordinateAxisOrder.EastNorth,
                    },
                    3825 => new CoordinateReferenceContext
                    {
                        SourceCrsId = "EPSG:3825",
                        IsGeographic = false,
                        AxisOrder = CoordinateAxisOrder.EastNorth,
                    },
                    // 其他 EPSG：保留代碼，標示可能略過自動轉換
                    _ => new CoordinateReferenceContext
                    {
                        SourceCrsId = $"EPSG:{epsgCode}",
                        IsGeographic = epsgCode == 4979,
                        AxisOrder = CoordinateAxisOrder.EastNorth,
                        DiagnosticMessage = $"偵測到 srsName={trimmed}，目前僅支援部分 CRS，自動轉換可能略過。",
                    },
                };
            }

            // 無法解析：保留原始字串並附加診斷訊息
            return new CoordinateReferenceContext
            {
                SourceCrsId = trimmed,
                DiagnosticMessage = $"偵測到 srsName={trimmed}，但無法解析為已知 CRS，將嘗試沿用原始座標。",
            };
        }

        /// <summary>
        /// 從 srsName 字串末段擷取 EPSG 數字代碼
        /// </summary>
        private static bool TryExtractEpsgCode(string srsName, out int epsgCode)
        {
            var tokens = srsName
                .Split([':', '/', '#'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 由後往前找第一個可解析的整數（相容 urn:ogc:def:crs:EPSG::3826 等形式）
            for (var i = tokens.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(tokens[i], out epsgCode))
                {
                    return true;
                }
            }

            epsgCode = 0;
            return false;
        }
        #endregion
    }
}
