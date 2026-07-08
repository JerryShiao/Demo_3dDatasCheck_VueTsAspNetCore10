namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理結果
    /// </summary>
    public sealed class XmlImportPreprocessResult
    {
        public string XmlContent { get; init; } = string.Empty;

        public CityGmlDetectionResult Detection { get; init; } = new();

        public bool AttemptedPreprocess { get; init; }

        public bool RepairApplied { get; init; }

        public List<string> Messages { get; init; } = [];

        public static XmlImportPreprocessResult Passthrough(
            string xmlContent,
            CityGmlDetectionResult? detection = null,
            IEnumerable<string>? messages = null)
        {
            return new XmlImportPreprocessResult
            {
                XmlContent = xmlContent,
                Detection = detection ?? new CityGmlDetectionResult(),
                Messages = messages?.ToList() ?? [],
            };
        }
    }
}
