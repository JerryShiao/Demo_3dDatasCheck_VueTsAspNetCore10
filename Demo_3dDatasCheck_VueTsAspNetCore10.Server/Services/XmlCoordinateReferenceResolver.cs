using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 解析 XML/GML 幾何的座標參考系統
    /// </summary>
    internal static class XmlCoordinateReferenceResolver
    {
        internal static CoordinateReferenceContext ResolveForGeometry(XElement element)
        {
            var srsName = FindFirstSrsName(element);
            if (!string.IsNullOrWhiteSpace(srsName))
            {
                return ResolveFromSrsName(srsName);
            }

            return new CoordinateReferenceContext();
        }

        private static string? FindFirstSrsName(XElement element)
        {
            var direct = element
                .DescendantsAndSelf()
                .Attributes()
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, "srsName", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            return element
                .Ancestors()
                .SelectMany(ancestor => ancestor.Attributes())
                .FirstOrDefault(attr => string.Equals(attr.Name.LocalName, "srsName", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        private static CoordinateReferenceContext ResolveFromSrsName(string srsName)
        {
            var trimmed = srsName.Trim();
            var upper = trimmed.ToUpperInvariant();

            if (upper.Contains("CRS84", StringComparison.Ordinal))
            {
                return new CoordinateReferenceContext
                {
                    SourceCrsId = "CRS:84",
                    IsGeographic = true,
                    AxisOrder = CoordinateAxisOrder.EastNorth,
                };
            }

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
                    _ => new CoordinateReferenceContext
                    {
                        SourceCrsId = $"EPSG:{epsgCode}",
                        IsGeographic = epsgCode == 4979,
                        AxisOrder = CoordinateAxisOrder.EastNorth,
                        DiagnosticMessage = $"偵測到 srsName={trimmed}，目前僅支援部分 CRS，自動轉換可能略過。",
                    },
                };
            }

            return new CoordinateReferenceContext
            {
                SourceCrsId = trimmed,
                DiagnosticMessage = $"偵測到 srsName={trimmed}，但無法解析為已知 CRS，將嘗試沿用原始座標。",
            };
        }

        private static bool TryExtractEpsgCode(string srsName, out int epsgCode)
        {
            var tokens = srsName
                .Split([':', '/', '#'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
    }
}
