using System.Text.Json.Serialization;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models
{
    /// <summary>
    /// 前端送入的單筆寫回樓層資料
    /// </summary>
    public class WriteBackBuildingItem
    {
        public string Mid { get; set; } = string.Empty;
        public string Oid { get; set; } = string.Empty;
        public string BuildingNo { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public List<List<List<double>>> Coordinates { get; set; } = new();
        public double? MinHeight { get; set; }
        public double? MaxHeight { get; set; }
        public string? Gmlid { get; set; }
        public string? BuildingSubNo { get; set; }
        public string? IsMainBuilding { get; set; }
        public string? AnnexType { get; set; }
        public decimal? Height { get; set; }
        public decimal? Area { get; set; }
        /// <summary>
        /// 前端列唯一識別，寫回成功後用於對應新 OID
        /// </summary>
        public string? RowId { get; set; }
    }

    /// <summary>
    /// 前端寫回請求
    /// </summary>
    public class WriteBackRequest
    {
        public List<WriteBackBuildingItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 對齊 ModelOfBuilding_WebAPI ConsistsOfBuildingPart 欄位
    /// </summary>
    public class ConsistsOfBuildingPartPayload
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int OID { get; set; }

        public int? MID { get; set; }
        public string? gmlid { get; set; }
        public string? 建號母號 { get; set; }
        public string? 建號子號 { get; set; }
        public string? 是否為主要建物 { get; set; }
        public string? 附屬建物類型 { get; set; }
        public decimal? 高度 { get; set; }
        public decimal? 面積 { get; set; }
        public string? 層次 { get; set; }
        public string? boundedBy { get; set; }
    }

    /// <summary>
    /// 單筆寫回結果（成功時回傳）
    /// </summary>
    public class WriteBackItemResult
    {
        public string? RowId { get; set; }
        public string OriginalOid { get; set; } = string.Empty;
        public int? NewOid { get; set; }
        public bool IsInsert { get; set; }
    }

    /// <summary>
    /// 寫回成功回應
    /// </summary>
    public class WriteBackResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<WriteBackItemResult> Results { get; set; } = new();
    }

    /// <summary>
    /// 寫回失敗回應（含補償資訊）
    /// </summary>
    public class WriteBackErrorResponse
    {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public string? FailedOid { get; set; }
        public string? FailedRowId { get; set; }
        public bool CompensationSucceeded { get; set; }
        public string? CompensationMessage { get; set; }
    }
}
