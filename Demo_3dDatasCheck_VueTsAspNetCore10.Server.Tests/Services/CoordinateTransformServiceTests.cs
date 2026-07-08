using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Tests.Services;

public class CoordinateTransformServiceTests
{
    private readonly CoordinateTransformService _service = new();

    [Fact]
    public void NormalizeToWgs84_ExplicitTaiwanTm2_TransformsToTaiwanLonLat()
    {
        var result = _service.NormalizeToWgs84(
            [
                [251582.6776065464, 2742712.2278096904, 0],
                [251587.1797726064, 2742721.7136149006, 0],
            ],
            new CoordinateReferenceContext
            {
                SourceCrsId = "EPSG:3826",
            });

        Assert.True(result.WasTransformed);
        Assert.All(result.Points, point =>
        {
            Assert.InRange(point[0], 119d, 123d);
            Assert.InRange(point[1], 21d, 26d);
        });
    }

    [Fact]
    public void NormalizeToWgs84_HeuristicTaiwanTm2_AddsInferenceMessage()
    {
        var result = _service.NormalizeToWgs84(
            [
                [251582.6776065464, 2742712.2278096904, 0],
            ],
            new CoordinateReferenceContext());

        Assert.True(result.WasTransformed);
        Assert.True(result.UsedHeuristic);
        Assert.Contains(result.Messages, message => message.Contains("座標系統推定"));
    }

    [Fact]
    public void NormalizeToWgs84_UnsupportedCrs_LeavesCoordinatesUnchanged()
    {
        var result = _service.NormalizeToWgs84(
            [
                [1000, 2000, 3],
            ],
            new CoordinateReferenceContext
            {
                SourceCrsId = "EPSG:9999",
            });

        Assert.False(result.WasTransformed);
        Assert.Equal(1000, result.Points[0][0]);
        Assert.Equal(2000, result.Points[0][1]);
        Assert.Contains(result.Messages, message => message.Contains("未支援"));
    }
}
