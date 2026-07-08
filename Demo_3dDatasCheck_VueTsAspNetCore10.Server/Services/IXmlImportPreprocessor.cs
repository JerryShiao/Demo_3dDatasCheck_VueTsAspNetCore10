namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// XML 匯入前處理介面
    /// </summary>
    public interface IXmlImportPreprocessor
    {
        XmlImportPreprocessResult Preprocess(string xmlContent);
    }
}
