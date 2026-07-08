namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// CityDoctor2 執行介面
    /// </summary>
    public interface ICityDoctor2Adapter
    {
        XmlImportPreprocessResult TryRepairCityGml(string xmlContent, CityGmlDetectionResult detection);
    }
}
