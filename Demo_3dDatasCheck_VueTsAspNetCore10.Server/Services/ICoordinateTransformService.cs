namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 座標軸順序
    /// </summary>
    public enum CoordinateAxisOrder
    {
        /// <summary>
        /// 先東後北（lon/easting, lat/northing）
        /// </summary>
        EastNorth,

        /// <summary>
        /// 先北後東（lat/northing, lon/easting）
        /// </summary>
        NorthEast,
    }

    /// <summary>
    /// XML/GML 幾何座標參考資訊
    /// </summary>
    public sealed class CoordinateReferenceContext
    {
        /// <summary>
        /// 來源 CRS 識別碼（例如 EPSG:3826、CRS:84）
        /// </summary>
        public string? SourceCrsId { get; init; }

        /// <summary>
        /// 是否為已可直接使用的地理座標（WGS84/CRS84 等）
        /// </summary>
        public bool IsGeographic { get; init; }

        /// <summary>
        /// 是否以啟發式（座標範圍推定）推得 CRS
        /// </summary>
        public bool IsHeuristic { get; init; }

        /// <summary>
        /// 來源座標軸順序
        /// </summary>
        public CoordinateAxisOrder AxisOrder { get; init; } = CoordinateAxisOrder.EastNorth;

        /// <summary>
        /// 診斷/提示訊息（例如未支援的 CRS）
        /// </summary>
        public string? DiagnosticMessage { get; init; }
    }

    /// <summary>
    /// 座標正規化結果
    /// </summary>
    public sealed class CoordinateNormalizationResult
    {
        /// <summary>
        /// 正規化後座標點列表（lon, lat, z）
        /// </summary>
        public required List<List<double>> Points { get; init; }

        /// <summary>
        /// 是否實際進行投影轉換
        /// </summary>
        public bool WasTransformed { get; init; }

        /// <summary>
        /// 是否使用啟發式推定 CRS
        /// </summary>
        public bool UsedHeuristic { get; init; }

        /// <summary>
        /// 相關提示訊息列表
        /// </summary>
        public List<string> Messages { get; init; } = [];
    }

    /// <summary>
    /// 座標轉換服務介面：將 XML/GML 幾何座標正規化為 WGS84 lon/lat/z
    /// </summary>
    public interface ICoordinateTransformService
    {
        /// <summary>
        /// 依 CRS 脈絡將原始座標點正規化為 WGS84
        /// </summary>
        /// <param name="rawPoints">原始座標點列表</param>
        /// <param name="context">座標參考系統脈絡</param>
        /// <returns>正規化結果（點位、轉換狀態、提示訊息）</returns>
        CoordinateNormalizationResult NormalizeToWgs84(
            IReadOnlyList<List<double>> rawPoints,
            CoordinateReferenceContext context);
    }
}
