namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options
{
    /// <summary>
    /// ModelOfBuilding_WebAPI 連線設定
    /// </summary>
    public class ModelOfBuildingApiOptions
    {
        public const string SectionName = "ModelOfBuildingApi";

        /// <summary>
        /// ConsistsOfBuildingParts API 基底 URL（POST 新增；GET/PUT/DELETE 附加 /{oid}）
        /// </summary>
        public string ConsistsOfBuildingPartsUrl { get; set; } = "http://localhost:240/api/ConsistsOfBuildingParts";
    }
}
