using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Tests.Services;

public class CityDoctor2AdapterTests
{
    [Fact]
    public void TryRepairCityGml_Disabled_ReturnsPassthroughWithoutAttempt()
    {
        var adapter = CreateAdapter(new CityDoctor2Options
        {
            Enabled = false,
        });

        var result = adapter.TryRepairCityGml("<CityModel />", CreateDetection());

        Assert.False(result.AttemptedPreprocess);
        Assert.False(result.RepairApplied);
        Assert.Equal("<CityModel />", result.XmlContent);
        Assert.Contains(result.Messages, message => message.Contains("未啟用"));
    }

    [Fact]
    public void TryRepairCityGml_Success_ReturnsRepairedXmlAndBuildsExpectedArguments()
    {
        var runner = new StubProcessRunner(request =>
        {
            var outPath = ExtractArgumentValue(request.Arguments, "-out");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, "<repaired />");
            return new CityDoctor2ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
            };
        });

        var adapter = CreateAdapter(new CityDoctor2Options
        {
            Enabled = true,
            ExecutablePath = "citydoctor2.cmd",
            ValidationPlanPath = "validation.yml",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "citydoctor2-tests"),
            TimeoutSeconds = 5,
        }, runner);

        var result = adapter.TryRepairCityGml("<CityModel />", CreateDetection());

        Assert.True(result.AttemptedPreprocess);
        Assert.True(result.RepairApplied);
        Assert.Contains(result.Messages, message => message.Contains("已完成"));
        Assert.Equal("<repaired />", result.XmlContent);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("citydoctor2.cmd", runner.LastRequest!.FileName);
        Assert.Contains("-config \"validation.yml\"", runner.LastRequest.Arguments);
        Assert.Contains("-in ", runner.LastRequest.Arguments);
        Assert.Contains("-out ", runner.LastRequest.Arguments);
        Assert.Contains("-xmlReport ", runner.LastRequest.Arguments);
    }

    [Fact]
    public void TryRepairCityGml_ProcessFailure_FallsBackToOriginalXml()
    {
        var adapter = CreateAdapter(new CityDoctor2Options
        {
            Enabled = true,
            ExecutablePath = "citydoctor2.cmd",
            ValidationPlanPath = "validation.yml",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "citydoctor2-tests"),
        }, new StubProcessRunner(_ => new CityDoctor2ProcessResult
        {
            ExitCode = 2,
            StandardError = "broken topology",
        }));

        var result = adapter.TryRepairCityGml("<original />", CreateDetection());

        Assert.True(result.AttemptedPreprocess);
        Assert.False(result.RepairApplied);
        Assert.Equal("<original />", result.XmlContent);
        Assert.Contains(result.Messages, message => message.Contains("broken topology"));
    }

    [Fact]
    public void TryRepairCityGml_Timeout_FallsBackToOriginalXml()
    {
        var adapter = CreateAdapter(new CityDoctor2Options
        {
            Enabled = true,
            ExecutablePath = "citydoctor2.cmd",
            ValidationPlanPath = "validation.yml",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "citydoctor2-tests"),
        }, new StubProcessRunner(_ => new CityDoctor2ProcessResult
        {
            TimedOut = true,
        }));

        var result = adapter.TryRepairCityGml("<original />", CreateDetection());

        Assert.True(result.AttemptedPreprocess);
        Assert.False(result.RepairApplied);
        Assert.Equal("<original />", result.XmlContent);
        Assert.Contains(result.Messages, message => message.Contains("逾時"));
    }

    private static CityGmlDetectionResult CreateDetection() => new()
    {
        IsCityGml = true,
        HasTopologyRelevantGeometry = true,
        Reason = "test",
    };

    private static CityDoctor2Adapter CreateAdapter(
        CityDoctor2Options options,
        StubProcessRunner? runner = null)
    {
        runner ??= new StubProcessRunner(request =>
        {
            var outPath = ExtractArgumentValue(request.Arguments, "-out");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllText(outPath, "<repaired />");
            }

            return new CityDoctor2ProcessResult
            {
                ExitCode = 0,
            };
        });

        return new CityDoctor2Adapter(Microsoft.Extensions.Options.Options.Create(options), runner);
    }

    private static string ExtractArgumentValue(string arguments, string key)
    {
        var marker = $"{key} \"";
        var start = arguments.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += marker.Length;
        var end = arguments.IndexOf('"', start);
        return end < 0 ? string.Empty : arguments[start..end];
    }

    private sealed class StubProcessRunner(Func<CityDoctor2ProcessRequest, CityDoctor2ProcessResult> callback) : ICityDoctor2ProcessRunner
    {
        public CityDoctor2ProcessRequest? LastRequest { get; private set; }

        public CityDoctor2ProcessResult Run(CityDoctor2ProcessRequest request)
        {
            LastRequest = request;
            return callback(request);
        }
    }
}
