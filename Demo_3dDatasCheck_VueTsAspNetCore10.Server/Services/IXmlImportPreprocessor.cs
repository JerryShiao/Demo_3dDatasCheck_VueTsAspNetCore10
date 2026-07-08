namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理介面
    /// </summary>
    public interface IXmlImportPreprocessor
    {
        /// <summary>
        /// 對 XML 內容進行匯入前處理（CityGML 時可委派拓撲修復）
        /// </summary>
        /// <param name="xmlContent">原始 XML 內容</param>
        /// <returns>預處理結果（可能為原始內容 passthrough 或修復後內容）</returns>
        XmlImportPreprocessResult Preprocess(string xmlContent);
    }
}
