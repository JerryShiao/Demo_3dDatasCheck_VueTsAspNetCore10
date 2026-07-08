using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理器：僅在 CityGML 時才委派 CityDoctor2
    /// </summary>
    public sealed class XmlImportPreprocessor(ICityDoctor2Adapter cityDoctor2Adapter) : IXmlImportPreprocessor
    {
        private static readonly string[] CityGmlNamespaces =
        [
            "http://www.opengis.net/citygml",
            "http://www.opengis.net/citygml/1.0",
            "http://www.opengis.net/citygml/2.0",
            "http://www.opengis.net/citygml/3.0",
        ];

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

        public XmlImportPreprocessResult Preprocess(string xmlContent)
        {
            var doc = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace);
            var detection = DetectCityGml(doc);

            if (!detection.IsCityGml || !detection.HasTopologyRelevantGeometry)
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection);
            }

            return cityDoctor2Adapter.TryRepairCityGml(xmlContent, detection);
        }

        internal static CityGmlDetectionResult DetectCityGml(XDocument doc)
        {
            var root = doc.Root;
            if (root == null)
            {
                return new CityGmlDetectionResult { Reason = "XML 缺少根節點" };
            }

            var namespaces = root
                .DescendantsAndSelf()
                .Attributes()
                .Where(attr => attr.IsNamespaceDeclaration)
                .Select(attr => attr.Value)
                .Append(root.Name.NamespaceName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasCityGmlNamespace = namespaces.Any(ns =>
                CityGmlNamespaces.Any(prefix => ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

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
    }
}
