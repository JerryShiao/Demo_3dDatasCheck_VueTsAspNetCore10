namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options
{
    /// <summary>
    /// CityDoctor2 預處理設定
    /// </summary>
    public class CityDoctor2Options
    {
        public const string SectionName = "CityDoctor2";

        /// <summary>
        /// 是否啟用 CityDoctor2 預處理
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// CityDoctor2 可執行檔或包裝腳本路徑
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// CityDoctor2 驗證計畫檔路徑
        /// </summary>
        public string ValidationPlanPath { get; set; } = string.Empty;

        /// <summary>
        /// 執行工作目錄；未設定時使用系統暫存資料夾
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 執行逾時秒數
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 是否保留輸入/輸出/報告檔案
        /// </summary>
        public bool KeepArtifacts { get; set; }
    }
}
