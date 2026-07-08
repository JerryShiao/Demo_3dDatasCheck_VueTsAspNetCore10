namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityGML 偵測結果
    /// </summary>
    public sealed class CityGmlDetectionResult
    {
        public bool IsCityGml { get; init; }

        public bool HasTopologyRelevantGeometry { get; init; }

        public string Reason { get; init; } = string.Empty;
    }
}
