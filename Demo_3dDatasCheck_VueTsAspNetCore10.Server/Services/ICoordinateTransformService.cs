namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 座標軸順序
    /// </summary>
    public enum CoordinateAxisOrder
    {
        EastNorth,
        NorthEast,
    }

    /// <summary>
    /// XML/GML 幾何座標參考資訊
    /// </summary>
    public sealed class CoordinateReferenceContext
    {
        public string? SourceCrsId { get; init; }

        public bool IsGeographic { get; init; }

        public bool IsHeuristic { get; init; }

        public CoordinateAxisOrder AxisOrder { get; init; } = CoordinateAxisOrder.EastNorth;

        public string? DiagnosticMessage { get; init; }
    }

    /// <summary>
    /// 座標正規化結果
    /// </summary>
    public sealed class CoordinateNormalizationResult
    {
        public required List<List<double>> Points { get; init; }

        public bool WasTransformed { get; init; }

        public bool UsedHeuristic { get; init; }

        public List<string> Messages { get; init; } = [];
    }

    /// <summary>
    /// 將 XML/GML 幾何座標正規化為 WGS84 lon/lat/z
    /// </summary>
    public interface ICoordinateTransformService
    {
        CoordinateNormalizationResult NormalizeToWgs84(
            IReadOnlyList<List<double>> rawPoints,
            CoordinateReferenceContext context);
    }
}
