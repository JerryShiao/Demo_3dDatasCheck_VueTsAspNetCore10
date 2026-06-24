namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models
{
    /// <summary>
    /// 建物模型資料 (JSON 解析後的資料結構)
    /// </summary>
    public class BuildingJsonRecord
    {
        public int OID { get; set; }
        public int MID { get; set; }
        public string? 建號母號 { get; set; }
        public string? 層次 { get; set; }
        public string? boundedBy { get; set; }

    }//class end
}//namespace end
