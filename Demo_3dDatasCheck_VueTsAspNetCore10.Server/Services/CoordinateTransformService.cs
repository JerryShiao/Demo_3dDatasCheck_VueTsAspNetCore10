using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 將投影座標轉為 WGS84 經緯度
    /// </summary>
    public sealed class CoordinateTransformService : ICoordinateTransformService
    {
        private const string Epsg4326 = "EPSG:4326";
        private const string Epsg3826 = "EPSG:3826";
        private const string Epsg3825 = "EPSG:3825";
        private const string Crs84 = "CRS:84";

        private readonly Dictionary<string, ICoordinateTransformation> _transformCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly CoordinateTransformationFactory _transformationFactory = new();

        public CoordinateNormalizationResult NormalizeToWgs84(
            IReadOnlyList<List<double>> rawPoints,
            CoordinateReferenceContext context)
        {
            var messages = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.DiagnosticMessage))
            {
                messages.Add(context.DiagnosticMessage);
            }

            if (rawPoints.Count == 0)
            {
                return new CoordinateNormalizationResult
                {
                    Points = [],
                    Messages = messages,
                };
            }

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

            if (string.IsNullOrWhiteSpace(effectiveCrs))
            {
                effectiveCrs = InferProjectedCrs(rawPoints, out var heuristicMessage);
                if (!string.IsNullOrWhiteSpace(heuristicMessage))
                {
                    messages.Add(heuristicMessage);
                    usedHeuristic = true;
                }
            }

            if (string.IsNullOrWhiteSpace(effectiveCrs))
            {
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    UsedHeuristic = usedHeuristic,
                    Messages = messages,
                };
            }

            if (IsGeographicCrs(effectiveCrs))
            {
                return new CoordinateNormalizationResult
                {
                    Points = NormalizeAxisOnly(rawPoints, context.AxisOrder),
                    UsedHeuristic = usedHeuristic,
                    Messages = messages,
                };
            }

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

        private static bool LooksLikeGeographic(IReadOnlyList<List<double>> points)
        {
            return points.All(point =>
                point.Count >= 2
                && point[0] >= -180 && point[0] <= 180
                && point[1] >= -90 && point[1] <= 90);
        }

        private static bool LooksLikeTaiwanTm2(IReadOnlyList<List<double>> points)
        {
            return points.All(point =>
                point.Count >= 2
                && point[0] is >= 100000 and <= 400000
                && point[1] is >= 2400000 and <= 3200000);
        }

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

        private static bool TryCreateSourceCoordinateSystem(string sourceCrsId, out CoordinateSystem source)
        {
            var normalized = sourceCrsId.Trim().ToUpperInvariant();

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

        private static ProjectedCoordinateSystem ParseProjectedCoordinateSystem(string wkt)
        {
            var factory = new CoordinateSystemFactory();
            return (ProjectedCoordinateSystem)factory.CreateFromWkt(wkt);
        }

        private static bool IsGeographicCrs(string sourceCrsId)
        {
            var normalized = sourceCrsId.Trim().ToUpperInvariant();
            return normalized is Epsg4326 or Crs84;
        }
    }
}
