namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 抽象化外部程序執行器，方便測試 CityDoctor2 adapter
    /// </summary>
    public interface ICityDoctor2ProcessRunner
    {
        CityDoctor2ProcessResult Run(CityDoctor2ProcessRequest request);
    }

    public sealed class CityDoctor2ProcessRequest
    {
        public string FileName { get; init; } = string.Empty;

        public string Arguments { get; init; } = string.Empty;

        public string WorkingDirectory { get; init; } = string.Empty;

        public TimeSpan Timeout { get; init; }
    }

    public sealed class CityDoctor2ProcessResult
    {
        public bool TimedOut { get; init; }

        public int? ExitCode { get; init; }

        public string StandardOutput { get; init; } = string.Empty;

        public string StandardError { get; init; } = string.Empty;
    }
}
