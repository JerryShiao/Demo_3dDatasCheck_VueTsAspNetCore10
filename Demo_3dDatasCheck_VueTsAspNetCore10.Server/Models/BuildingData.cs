namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models
{
    /// <summary>
    /// 建物模型資料
    /// </summary>
    public class BuildingData
    {
        #region -- 建物基本資料
        /// <summary>
        /// 唯一識別符
        /// </summary>
        public string Mid { get; set; } = string.Empty;
        /// <summary>
        /// 原始識別符
        /// </summary>
        public string Oid { get; set; } = string.Empty;
        /// <summary>
        /// 建號母號
        /// </summary>
        public string BuildingNo { get; set; } = string.Empty;
        /// <summary>
        /// 層次
        /// </summary>
        public string Floor { get; set; } = string.Empty;

        /// <summary>
        /// 原始坐標字串
        /// </summary>
        public string BoundedByRaw { get; set; } = string.Empty;

        /// <summary>
        /// 解析後的3D坐標
        /// </summary>
        public List<List<List<double>>> Coordinates { get; set; } = new();
        #endregion

        #region -- 異常狀態記錄
        /// <summary>
        /// 資料是否有效
        /// </summary>
        public bool IsValid { get; set; } = true;
        /// <summary>
        /// 錯誤訊息列表
        /// </summary>
        public List<string> ErrorMessages { get; set; } = new();
        /// <summary>
        /// 是否已修正
        /// </summary>
        public bool IsFixed { get; set; } = false;
        /// <summary>
        /// 修正訊息列表
        /// </summary>
        public List<string> FixMessages { get; set; } = new();
        #endregion

    }//class end
}//namespace end
