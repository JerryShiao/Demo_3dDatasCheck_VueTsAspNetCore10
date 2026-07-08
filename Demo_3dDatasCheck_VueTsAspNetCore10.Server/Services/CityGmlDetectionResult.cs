namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityGML 偵測結果
    /// </summary>
    public sealed class CityGmlDetectionResult
    {
        /// <summary>
        /// 是否偵測為 CityGML
        /// </summary>
        public bool IsCityGml { get; init; }

        /// <summary>
        /// 是否含需拓撲預處理的幾何（LOD / Polygon / Solid 等）
        /// </summary>
        public bool HasTopologyRelevantGeometry { get; init; }

        /// <summary>
        /// 偵測原因說明
        /// </summary>
        public string Reason { get; init; } = string.Empty;
    }
}
