using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityDoctor2 適配器：封裝外部工具呼叫與失敗回退邏輯
    /// </summary>
    public sealed class CityDoctor2Adapter(
        IOptions<CityDoctor2Options> options,
        ICityDoctor2ProcessRunner processRunner) : ICityDoctor2Adapter
    {
        // CityDoctor2 設定（啟用狀態、路徑、逾時等）
        private readonly CityDoctor2Options _options = options.Value;

        #region ◆嘗試以 CityDoctor2 修復 CityGML [TryRepairCityGml]
        /// <summary>
        /// 嘗試以 CityDoctor2 修復 CityGML；設定不完整、逾時或失敗時回退原始內容
        /// </summary>
        /// <param name="xmlContent">原始 CityGML/XML 內容</param>
        /// <param name="detection">CityGML 偵測結果</param>
        /// <returns>預處理結果（修復後 XML 或原始內容 + 提示訊息）</returns>
        public XmlImportPreprocessResult TryRepairCityGml(string xmlContent, CityGmlDetectionResult detection)
        {
            // 未啟用時直接略過預處理
            if (!_options.Enabled)
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection, ["CityDoctor2 未啟用，略過 CityGML 拓撲預處理。"]);
            }

            // 可執行檔或驗證計畫路徑缺失時略過
            if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || string.IsNullOrWhiteSpace(_options.ValidationPlanPath))
            {
                return XmlImportPreprocessResult.Passthrough(xmlContent, detection, ["CityDoctor2 設定不完整，略過 CityGML 拓撲預處理。"]);
            }

            // 建立本次執行專屬工作目錄，避免並行衝突
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
                // 呼叫外部 CityDoctor2 程序
                var processResult = processRunner.Run(new CityDoctor2ProcessRequest
                {
                    FileName = _options.ExecutablePath,
                    Arguments = BuildArguments(inputPath, outputPath, xmlReportPath),
                    WorkingDirectory = workDir,
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)),
                });

                // 逾時：回退原始內容
                if (processResult.TimedOut)
                {
                    return CreateAttemptedFallback(
                        xmlContent,
                        detection,
                        "CityDoctor2 執行逾時，已回退至原始 CityGML 匯入流程。");
                }

                // 非 0 結束代碼：回退並帶回錯誤細節
                if (processResult.ExitCode != 0)
                {
                    var message = BuildFailureMessage(processResult);
                    return CreateAttemptedFallback(xmlContent, detection, message);
                }

                // 未產出 output 檔：回退
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

                // 成功：回傳修復後 XML，並標記內容是否實際變更
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
                // IO / 權限 / 程序啟動失敗：回退並附上例外訊息
                return CreateAttemptedFallback(
                    xmlContent,
                    detection,
                    $"CityDoctor2 預處理失敗，已回退至原始 CityGML 匯入流程：{ex.Message}");
            }
            finally
            {
                // 預設清臨時檔；設定 KeepArtifacts 時保留供除錯
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
        #endregion

        #region ◆解析工作目錄根路徑 [ResolveWorkingRoot]
        /// <summary>
        /// 解析工作目錄根路徑：有設定則使用設定值，否則使用系統暫存目錄
        /// </summary>
        private string ResolveWorkingRoot()
        {
            if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
            {
                Directory.CreateDirectory(_options.WorkingDirectory);
                return _options.WorkingDirectory;
            }

            return Path.GetTempPath();
        }
        #endregion

        #region ◆組裝 CityDoctor2 命令列引數 [BuildArguments]
        /// <summary>
        /// 組裝 CityDoctor2 命令列引數（輸入、驗證計畫、輸出、XML 報告）
        /// </summary>
        /// <param name="inputPath">輸入 CityGML 路徑</param>
        /// <param name="outputPath">修復後輸出路徑</param>
        /// <param name="xmlReportPath">XML 報告輸出路徑</param>
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

        /// <summary>
        /// 以雙引號包住路徑，避免空白字元造成參數解析錯誤
        /// </summary>
        private static string Quote(string path) => $"\"{path}\"";
        #endregion

        #region ◆建立「已嘗試但失敗」的回退結果 [CreateAttemptedFallback]
        /// <summary>
        /// 建立「已嘗試預處理但失敗」的回退結果，保留原始 XML
        /// </summary>
        /// <param name="xmlContent">原始 XML 內容</param>
        /// <param name="detection">CityGML 偵測結果</param>
        /// <param name="message">回退原因訊息</param>
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
        #endregion

        #region ◆組裝執行失敗訊息 [BuildFailureMessage]
        /// <summary>
        /// 由程序輸出組裝執行失敗訊息，優先使用 stderr
        /// </summary>
        /// <param name="processResult">外部程序執行結果</param>
        private static string BuildFailureMessage(CityDoctor2ProcessResult processResult)
        {
            // 優先取 stderr，否則退回 stdout
            var detail = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? processResult.StandardError.Trim()
                : processResult.StandardOutput.Trim();

            if (string.IsNullOrWhiteSpace(detail))
            {
                return $"CityDoctor2 執行失敗（ExitCode={processResult.ExitCode ?? -1}），已回退至原始 CityGML 匯入流程。";
            }

            return $"CityDoctor2 執行失敗（ExitCode={processResult.ExitCode ?? -1}）：{detail}";
        }
        #endregion
    }
}
