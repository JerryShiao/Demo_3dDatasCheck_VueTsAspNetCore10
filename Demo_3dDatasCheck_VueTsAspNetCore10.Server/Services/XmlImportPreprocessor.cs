using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理器：僅在 CityGML 且具拓撲相關幾何時才委派 CityDoctor2
    /// </summary>
    public sealed class XmlImportPreprocessor(ICityDoctor2Adapter cityDoctor2Adapter) : IXmlImportPreprocessor
    {
        // CityGML namespace 前綴候選
        private static readonly string[] CityGmlNamespaces =
        [
            "http://www.opengis.net/citygml",
            "http://www.opengis.net/citygml/1.0",
            "http://www.opengis.net/citygml/2.0",
            "http://www.opengis.net/citygml/3.0",
        ];

        // CityGML 典型節點 local-name 候選
        private static readonly string[] CityGmlLocalNames =
        [
            "CityModel",
            "cityObjectMember",
            "Building",
            "BuildingPart",
            "lod1Solid",
            "lod2Solid",
            "lod3Solid",
            "lod4Solid",
            "lod1MultiSurface",
            "lod2MultiSurface",
            "lod3MultiSurface",
            "lod4MultiSurface",
            "Polygon",
        ];

        #region ◆XML 匯入前處理入口 [Preprocess]
        /// <summary>
        /// 解析 XML 並偵測 CityGML；非 CityGML 或無需拓撲處理時直接 passthrough
        /// </summary>
        /// <param name="xmlContent">原始 XML 內容</param>
        /// <returns>預處理結果</returns>
        public XmlImportPreprocessResult Preprocess(string xmlContent)
        {
            var doc = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace);
            var detection = DetectCityGml(doc);

            // 非 CityGML，或沒有需拓撲處理的幾何：直接放行
            if (!detection.IsCityGml || !detection.HasTopologyRelevantGeometry)
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection);
            }

            // 委派 CityDoctor2 嘗試拓撲修復
            return cityDoctor2Adapter.TryRepairCityGml(xmlContent, detection);
        }
        #endregion

        #region ◆偵測是否為 CityGML 及是否含拓撲相關幾何 [DetectCityGml]
        /// <summary>
        /// 偵測文件是否為 CityGML，以及是否含需拓撲預處理的幾何節點
        /// </summary>
        /// <param name="doc">已解析的 XML 文件</param>
        /// <returns>CityGML 偵測結果</returns>
        internal static CityGmlDetectionResult DetectCityGml(XDocument doc)
        {
            var root = doc.Root;
            if (root == null)
            {
                return new CityGmlDetectionResult { Reason = "XML 缺少根節點" };
            }

            // 蒐集文件宣告與根節點的 namespace
            var namespaces = root
                .DescendantsAndSelf()
                .Attributes()
                .Where(attr => attr.IsNamespaceDeclaration)
                .Select(attr => attr.Value)
                .Append(root.Name.NamespaceName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 比對已知 CityGML namespace 前綴
            var hasCityGmlNamespace = namespaces.Any(ns =>
                CityGmlNamespaces.Any(prefix => ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

            // 比對典型 CityGML 節點名
            var hasCityGmlLocalNames = root
                .DescendantsAndSelf()
                .Any(el => CityGmlLocalNames.Contains(el.Name.LocalName, StringComparer.OrdinalIgnoreCase));

            var isCityGml = hasCityGmlNamespace || hasCityGmlLocalNames;
            if (!isCityGml)
            {
                return new CityGmlDetectionResult
                {
                    IsCityGml = false,
                    HasTopologyRelevantGeometry = false,
                    Reason = "未偵測到 CityGML namespace 或典型節點",
                };
            }

            // 檢查是否存在 LOD / Polygon / MultiSurface / Solid 等拓撲相關幾何
            var hasTopologyRelevantGeometry = root
                .DescendantsAndSelf()
                .Any(el =>
                    el.Name.LocalName.StartsWith("lod", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(el.Name.LocalName, "Polygon", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(el.Name.LocalName, "MultiSurface", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(el.Name.LocalName, "Solid", StringComparison.OrdinalIgnoreCase));

            return new CityGmlDetectionResult
            {
                IsCityGml = true,
                HasTopologyRelevantGeometry = hasTopologyRelevantGeometry,
                Reason = hasTopologyRelevantGeometry
                    ? "偵測到 CityGML 幾何節點"
                    : "偵測到 CityGML，但未找到需拓撲預處理的幾何節點",
            };
        }
        #endregion
    }
}
