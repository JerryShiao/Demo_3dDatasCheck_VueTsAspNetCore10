namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityDoctor2 拓撲預處理介面
    /// </summary>
    public interface ICityDoctor2Adapter
    {
        /// <summary>
        /// 嘗試以 CityDoctor2 修復 CityGML；失敗時回退原始內容
        /// </summary>
        /// <param name="xmlContent">原始 CityGML/XML 內容</param>
        /// <param name="detection">CityGML 偵測結果</param>
        /// <returns>預處理結果（含修復後 XML、是否套用修復與提示訊息）</returns>
        XmlImportPreprocessResult TryRepairCityGml(string xmlContent, CityGmlDetectionResult detection);
    }
}
