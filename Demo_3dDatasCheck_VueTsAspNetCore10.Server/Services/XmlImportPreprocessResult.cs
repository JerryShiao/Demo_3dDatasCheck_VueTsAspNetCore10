namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理結果
    /// </summary>
    public sealed class XmlImportPreprocessResult
    {
        /// <summary>
        /// 預處理後（或原始）的 XML 內容
        /// </summary>
        public string XmlContent { get; init; } = string.Empty;

        /// <summary>
        /// CityGML 偵測結果
        /// </summary>
        public CityGmlDetectionResult Detection { get; init; } = new();

        /// <summary>
        /// 是否已嘗試執行預處理（含失敗回退）
        /// </summary>
        public bool AttemptedPreprocess { get; init; }

        /// <summary>
        /// 是否實際套用修復（內容與原始不同）
        /// </summary>
        public bool RepairApplied { get; init; }

        /// <summary>
        /// 預處理相關提示/錯誤訊息
        /// </summary>
        public List<string> Messages { get; init; } = [];

        #region ◆建立不變更內容的 passthrough 結果 [Passthrough]
        /// <summary>
        /// 建立不變更內容的 passthrough 結果（未預處理或略過時使用）
        /// </summary>
        /// <param name="xmlContent">原始 XML 內容</param>
        /// <param name="detection">可選的 CityGML 偵測結果</param>
        /// <param name="messages">可選的提示訊息</param>
        public static XmlImportPreprocessResult Passthrough(
            string xmlContent,
            CityGmlDetectionResult? detection = null,
            IEnumerable<string>? messages = null)
        {
            return new XmlImportPreprocessResult
            {
                XmlContent = xmlContent,
                Detection = detection ?? new CityGmlDetectionResult(),
                Messages = messages?.ToList() ?? [],
            };
        }
        #endregion
    }
}
