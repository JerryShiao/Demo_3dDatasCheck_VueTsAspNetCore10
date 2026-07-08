using System.Diagnostics;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityDoctor2 程序執行器：以外部程序方式執行並收集輸出
    /// </summary>
    public sealed class CityDoctor2ProcessRunner : ICityDoctor2ProcessRunner
    {
        #region ◆執行 CityDoctor2 外部程序 [Run]
        /// <summary>
        /// 執行 CityDoctor2 外部程序並回傳結果；逾時時終止進程樹
        /// </summary>
        /// <param name="request">程序啟動參數</param>
        /// <returns>執行結果（ExitCode、stdout/stderr、是否逾時）</returns>
        public CityDoctor2ProcessResult Run(CityDoctor2ProcessRequest request)
        {
            // 建立無視窗、可重定向輸出的外部程序
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = request.FileName,
                    Arguments = request.Arguments,
                    WorkingDirectory = request.WorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            // 非同步讀取 stdout/stderr，避免緩衝區塞滿造成死鎖
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            // 逾時：終止進程樹並回傳 TimedOut
            if (!process.WaitForExit((int)request.Timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore kill failures; timeout state is enough for caller handling
                }

                Task.WaitAll(standardOutputTask, standardErrorTask);
                return new CityDoctor2ProcessResult
                {
                    TimedOut = true,
                    StandardOutput = standardOutputTask.Result,
                    StandardError = standardErrorTask.Result,
                };
            }

            // 正常結束：回傳 ExitCode 與完整輸出
            Task.WaitAll(standardOutputTask, standardErrorTask);
            return new CityDoctor2ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = standardOutputTask.Result,
                StandardError = standardErrorTask.Result,
            };
        }
        #endregion
    }
}
