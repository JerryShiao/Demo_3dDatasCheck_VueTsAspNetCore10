using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 座標轉換服務：將投影座標轉為 WGS84 經緯度
    /// </summary>
    public sealed class CoordinateTransformService : ICoordinateTransformService
    {
        // 常見 CRS 識別碼常數
        private const string Epsg4326 = "EPSG:4326"; // WGS84 地理座標
        private const string Epsg3826 = "EPSG:3826"; // TWD97 / TM2 zone 121
        private const string Epsg3825 = "EPSG:3825"; // TWD97 / TM2 zone 119
        private const string Crs84 = "CRS:84"; // lon/lat 軸序的 WGS84

        // 轉換器快取，避免重複建立相同 CRS → WGS84 轉換
        private readonly Dictionary<string, ICoordinateTransformation> _transformCache = new(StringComparer.OrdinalIgnoreCase);
        // ProjNET 轉換工廠
        private readonly CoordinateTransformationFactory _transformationFactory = new();

        #region ◆將原始座標正規化為 WGS84 [NormalizeToWgs84]
        /// <summary>
        /// 依 CRS 脈絡將原始座標點正規化為 WGS84 lon/lat/z
        /// </summary>
        /// <param name="rawPoints">原始座標點列表</param>
        /// <param name="context">座標參考系統脈絡</param>
        /// <returns>正規化結果</returns>
        public CoordinateNormalizationResult NormalizeToWgs84(
            IReadOnlyList<List<double>> rawPoints,
            CoordinateReferenceContext context)
        {
            var messages = new List<string>();
            // 先帶入上游診斷訊息（若有）
            if (!string.IsNullOrWhiteSpace(context.DiagnosticMessage))
            {
                messages.Add(context.DiagnosticMessage);
            }

            // 空點列表直接回傳
            if (rawPoints.Count == 0)
            {
                return new CoordinateNormalizationResult
                {
                    Points = [],
                    Messages = messages,
                };
            }

            // 已是地理座標：只做軸序正規化
            if (context.IsGeographic)
            {
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    Messages = messages,
                };
            }

            var effectiveCrs = context.SourceCrsId;
            var usedHeuristic = context.IsHeuristic;

            // 未提供 CRS：嘗試依台灣投影座標範圍啟發式推定
            if (string.IsNullOrWhiteSpace(effectiveCrs))
            {
                effectiveCrs = InferProjectedCrs(rawPoints, out var heuristicMessage);
                if (!string.IsNullOrWhiteSpace(heuristicMessage))
                {
                    messages.Add(heuristicMessage);
                    usedHeuristic = true;
                }
            }

            // 仍無法判定：只做軸序正規化後回傳
            if (string.IsNullOrWhiteSpace(effectiveCrs))
            {
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    UsedHeuristic = usedHeuristic,
                    Messages = messages,
                };
            }

            // 推定結果其實是地理 CRS：同樣只正規化軸序
            if (IsGeographicCrs(effectiveCrs))
            {
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    UsedHeuristic = usedHeuristic,
                    Messages = messages,
                };
            }

            // 不支援的投影 CRS：沿用原始座標（僅軸序）
            if (!TryGetTransformation(effectiveCrs, out var transformation))
            {
                messages.Add($"座標系統未支援：{effectiveCrs}，已沿用原始座標。");
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    UsedHeuristic = usedHeuristic,
                    Messages = messages,
                };
            }

            // 逐點做軸序調整後投影轉換為 WGS84
            var normalized = new List<List<double>>(rawPoints.Count);
            foreach (var point in rawPoints)
            {
                if (point.Count < 2)
                {
                    continue;
                }

                var first = point[0];
                var second = point[1];
                if (context.AxisOrder == CoordinateAxisOrder.NorthEast)
                {
                    (first, second) = (second, first);
                }

                var transformed = transformation.MathTransform.Transform([first, second]);
                var normalizedPoint = new List<double>
                {
                    transformed[0],
                    transformed[1],
                    point.Count >= 3 ? point[2] : 0,
                };
                normalized.Add(normalizedPoint);
            }

            if (usedHeuristic)
            {
                messages.Add("座標系統推定：未提供 srsName，已依台灣投影座標特徵自動轉為 WGS84。");
            }

            return new CoordinateNormalizationResult
            {
                Points = normalized,
                WasTransformed = true,
                UsedHeuristic = usedHeuristic,
                Messages = messages,
            };
        }
        #endregion

        #region ◆僅正規化軸序為 lon/lat [NormalizeAxisOnly]
        /// <summary>
        /// 僅依軸序將座標正規化為 lon/lat/z，不做投影轉換
        /// </summary>
        private static List<List<double>> NormalizeAxisOnly(
            IReadOnlyList<List<double>> rawPoints,
            CoordinateAxisOrder axisOrder)
        {
            return rawPoints
                .Where(point => point.Count >= 2)
                .Select(point =>
                {
                    var lon = axisOrder == CoordinateAxisOrder.EastNorth ? point[0] : point[1];
                    var lat = axisOrder == CoordinateAxisOrder.EastNorth ? point[1] : point[0];
                    return new List<double> { lon, lat, point.Count >= 3 ? point[2] : 0 };
                })
                .ToList();
        }
        #endregion

        #region ◆啟發式推定台灣投影 CRS [InferProjectedCrs]
        /// <summary>
        /// 啟發式推定投影 CRS；看起來像地理座標則不推定
        /// </summary>
        private static string? InferProjectedCrs(IReadOnlyList<List<double>> points, out string? heuristicMessage)
        {
            heuristicMessage = null;
            if (LooksLikeGeographic(points))
            {
                return null;
            }

            if (LooksLikeTaiwanTm2(points))
            {
                heuristicMessage = "未提供 srsName，已依座標範圍推定為台灣 TM2 投影座標。";
                return Epsg3826;
            }

            return null;
        }

        /// <summary>
        /// 判斷座標是否落在經緯度合理範圍
        /// </summary>
        private static bool LooksLikeGeographic(IReadOnlyList<List<double>> points)
        {
            return points.All(point =>
                point.Count >= 2
                && point[0] >= -180 && point[0] <= 180
                && point[1] >= -90 && point[1] <= 90);
        }

        /// <summary>
        /// 判斷座標是否落在台灣 TM2（EPSG:3826）常見範圍
        /// </summary>
        private static bool LooksLikeTaiwanTm2(IReadOnlyList<List<double>> points)
        {
            return points.All(point =>
                point.Count >= 2
                && point[0] is >= 100000 and <= 400000
                && point[1] is >= 2400000 and <= 3200000);
        }
        #endregion

        #region ◆取得或建立 CRS → WGS84 轉換器 [TryGetTransformation]
        /// <summary>
        /// 取得或建立來源 CRS → WGS84 轉換器（含快取）
        /// </summary>
        private bool TryGetTransformation(string sourceCrsId, out ICoordinateTransformation transformation)
        {
            if (_transformCache.TryGetValue(sourceCrsId, out transformation!))
            {
                return true;
            }

            if (!TryCreateSourceCoordinateSystem(sourceCrsId, out var source))
            {
                transformation = default!;
                return false;
            }

            transformation = _transformationFactory.CreateFromCoordinateSystems(
                source,
                GeographicCoordinateSystem.WGS84);
            _transformCache[sourceCrsId] = transformation;
            return true;
        }

        /// <summary>
        /// 依 CRS 識別碼建立來源座標系統定義
        /// </summary>
        private static bool TryCreateSourceCoordinateSystem(string sourceCrsId, out CoordinateSystem source)
        {
            var normalized = sourceCrsId.Trim().ToUpperInvariant();

            // TWD97 / TM2 zone 121
            if (normalized == Epsg3826)
            {
                source = ParseProjectedCoordinateSystem("""
                    PROJCS["TWD97 / TM2 zone 121",
                    GEOGCS["TWD97",
                    DATUM["Taiwan_Datum_1997",
                    SPHEROID["GRS 1980",6378137,298.257222101]],
                    PRIMEM["Greenwich",0],
                    UNIT["degree",0.0174532925199433]],
                    PROJECTION["Transverse_Mercator"],
                    PARAMETER["latitude_of_origin",0],
                    PARAMETER["central_meridian",121],
                    PARAMETER["scale_factor",0.9999],
                    PARAMETER["false_easting",250000],
                    PARAMETER["false_northing",0],
                    UNIT["metre",1]]
                    """);
                return true;
            }

            // TWD97 / TM2 zone 119
            if (normalized == Epsg3825)
            {
                source = ParseProjectedCoordinateSystem("""
                    PROJCS["TWD97 / TM2 zone 119",
                    GEOGCS["TWD97",
                    DATUM["Taiwan_Datum_1997",
                    SPHEROID["GRS 1980",6378137,298.257222101]],
                    PRIMEM["Greenwich",0],
                    UNIT["degree",0.0174532925199433]],
                    PROJECTION["Transverse_Mercator"],
                    PARAMETER["latitude_of_origin",0],
                    PARAMETER["central_meridian",119],
                    PARAMETER["scale_factor",0.9999],
                    PARAMETER["false_easting",250000],
                    PARAMETER["false_northing",0],
                    UNIT["metre",1]]
                    """);
                return true;
            }

            source = default!;
            return false;
        }

        /// <summary>
        /// 由 WKT 字串解析投影座標系統
        /// </summary>
        private static ProjectedCoordinateSystem ParseProjectedCoordinateSystem(string wkt)
        {
            var factory = new CoordinateSystemFactory();
            return (ProjectedCoordinateSystem)factory.CreateFromWkt(wkt);
        }

        /// <summary>
        /// 判斷是否為已知地理 CRS（EPSG:4326 / CRS:84）
        /// </summary>
        private static bool IsGeographicCrs(string sourceCrsId)
        {
            var normalized = sourceCrsId.Trim().ToUpperInvariant();
            return normalized is Epsg4326 or Crs84;
        }
        #endregion
    }
}
