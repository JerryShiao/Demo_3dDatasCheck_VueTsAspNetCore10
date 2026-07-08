using System.Diagnostics;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 以外部程序方式執行 CityDoctor2
    /// </summary>
    public sealed class CityDoctor2ProcessRunner : ICityDoctor2ProcessRunner
    {
        public CityDoctor2ProcessResult Run(CityDoctor2ProcessRequest request)
        {
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
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

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

            Task.WaitAll(standardOutputTask, standardErrorTask);
            return new CityDoctor2ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = standardOutputTask.Result,
                StandardError = standardErrorTask.Result,
            };
        }
    }
}
