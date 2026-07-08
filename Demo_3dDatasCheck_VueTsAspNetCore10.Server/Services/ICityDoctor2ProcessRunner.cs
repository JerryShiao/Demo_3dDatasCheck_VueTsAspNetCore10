namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 抽象化外部程序執行器，方便測試 CityDoctor2 adapter
    /// </summary>
    public interface ICityDoctor2ProcessRunner
    {
        /// <summary>
        /// 執行 CityDoctor2 外部程序並回傳結果
        /// </summary>
        /// <param name="request">程序啟動參數（檔名、引數、工作目錄、逾時）</param>
        /// <returns>執行結果（ExitCode、stdout/stderr、是否逾時）</returns>
        CityDoctor2ProcessResult Run(CityDoctor2ProcessRequest request);
    }

    /// <summary>
    /// CityDoctor2 外部程序啟動參數
    /// </summary>
    public sealed class CityDoctor2ProcessRequest
    {
        /// <summary>
        /// 可執行檔路徑
        /// </summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>
        /// 命令列引數
        /// </summary>
        public string Arguments { get; init; } = string.Empty;

        /// <summary>
        /// 工作目錄
        /// </summary>
        public string WorkingDirectory { get; init; } = string.Empty;

        /// <summary>
        /// 執行逾時時間
        /// </summary>
        public TimeSpan Timeout { get; init; }
    }

    /// <summary>
    /// CityDoctor2 外部程序執行結果
    /// </summary>
    public sealed class CityDoctor2ProcessResult
    {
        /// <summary>
        /// 是否因逾時而被中止
        /// </summary>
        public bool TimedOut { get; init; }

        /// <summary>
        /// 程序結束代碼；逾時時可能為 null
        /// </summary>
        public int? ExitCode { get; init; }

        /// <summary>
        /// 標準輸出內容
        /// </summary>
        public string StandardOutput { get; init; } = string.Empty;

        /// <summary>
        /// 標準錯誤輸出內容
        /// </summary>
        public string StandardError { get; init; } = string.Empty;
    }
}
