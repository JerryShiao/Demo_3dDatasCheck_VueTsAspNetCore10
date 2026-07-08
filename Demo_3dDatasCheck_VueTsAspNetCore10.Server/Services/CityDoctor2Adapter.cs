using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 封裝 CityDoctor2 外部工具呼叫與 fallback 邏輯
    /// </summary>
    public sealed class CityDoctor2Adapter(
        IOptions<CityDoctor2Options> options,
        ICityDoctor2ProcessRunner processRunner) : ICityDoctor2Adapter
    {
        private readonly CityDoctor2Options _options = options.Value;

        public XmlImportPreprocessResult TryRepairCityGml(string xmlContent, CityGmlDetectionResult detection)
        {
            if (!_options.Enabled)
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection, ["CityDoctor2 未啟用，略過 CityGML 拓撲預處理。"]);
            }

            if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || string.IsNullOrWhiteSpace(_options.ValidationPlanPath))
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection, ["CityDoctor2 設定不完整，略過 CityGML 拓撲預處理。"]);
            }

            var workingRoot = ResolveWorkingRoot();
            var runId = Guid.NewGuid().ToString("N");
            var workDir = Path.Combine(workingRoot, "citydoctor2", runId);
            Directory.CreateDirectory(workDir);

            var inputPath = Path.Combine(workDir, "input.gml");
            var outputPath = Path.Combine(workDir, "output.gml");
            var xmlReportPath = Path.Combine(workDir, "report.xml");
            File.WriteAllText(inputPath, xmlContent);

            try
            {
                var processResult = processRunner.Run(new CityDoctor2ProcessRequest
                {
                    FileName = _options.ExecutablePath,
                    Arguments = BuildArguments(inputPath, outputPath, xmlReportPath),
                    WorkingDirectory = workDir,
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)),
                });

                if (processResult.TimedOut)
                {
                    return CreateAttemptedFallback(
                        xmlContent,
                        detection,
                        "CityDoctor2 執行逾時，已回退至原始 CityGML 匯入流程。");
                }

                if (processResult.ExitCode != 0)
                {
                    var message = BuildFailureMessage(processResult);
                    return CreateAttemptedFallback(xmlContent, detection, message);
                }

                if (!File.Exists(outputPath))
                {
                    return CreateAttemptedFallback(
                        xmlContent,
                        detection,
                        "CityDoctor2 未產出修復後 XML，已回退至原始 CityGML 匯入流程。");
                }

                var repairedXml = File.ReadAllText(outputPath);
                if (string.IsNullOrWhiteSpace(repairedXml))
                {
                    return CreateAttemptedFallback(
                        xmlContent,
                        detection,
                        "CityDoctor2 產出空白 XML，已回退至原始 CityGML 匯入流程。");
                }

                return new XmlImportPreprocessResult
                {
                    XmlContent = repairedXml,
                    Detection = detection,
                    AttemptedPreprocess = true,
                    RepairApplied = !string.Equals(repairedXml, xmlContent, StringComparison.Ordinal),
                    Messages = ["CityDoctor2 已完成 CityGML 拓撲預處理。"],
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return CreateAttemptedFallback(
                    xmlContent,
                    detection,
                    $"CityDoctor2 預處理失敗，已回退至原始 CityGML 匯入流程：{ex.Message}");
            }
            finally
            {
                if (!_options.KeepArtifacts && Directory.Exists(workDir))
                {
                    try
                    {
                        Directory.Delete(workDir, recursive: true);
                    }
                    catch
                    {
                        // Ignore temp cleanup failures.
                    }
                }
            }
        }

        private string ResolveWorkingRoot()
        {
            if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
            {
                Directory.CreateDirectory(_options.WorkingDirectory);
                return _options.WorkingDirectory;
            }

            return Path.GetTempPath();
        }

        private string BuildArguments(string inputPath, string outputPath, string xmlReportPath)
        {
            return string.Join(' ',
            [
                "-in", Quote(inputPath),
                "-config", Quote(_options.ValidationPlanPath),
                "-out", Quote(outputPath),
                "-xmlReport", Quote(xmlReportPath),
            ]);
        }

        private static string Quote(string path) => $"\"{path}\"";

        private static XmlImportPreprocessResult CreateAttemptedFallback(
            string xmlContent,
            CityGmlDetectionResult detection,
            string message)
        {
            return new XmlImportPreprocessResult
            {
                XmlContent = xmlContent,
                Detection = detection,
                AttemptedPreprocess = true,
                RepairApplied = false,
                Messages = [message],
            };
        }

        private static string BuildFailureMessage(CityDoctor2ProcessResult processResult)
        {
            var detail = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? processResult.StandardError.Trim()
                : processResult.StandardOutput.Trim();

            if (string.IsNullOrWhiteSpace(detail))
            {
                return $"CityDoctor2 執行失敗（ExitCode={processResult.ExitCode ?? -1}），已回退至原始 CityGML 匯入流程。";
            }

            return $"CityDoctor2 執行失敗（ExitCode={processResult.ExitCode ?? -1}）：{detail}";
        }
    }
}
