namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options
{
    /// <summary>
    /// 建物垂直幾何異常檢測閾值設定
    /// </summary>
    public class BuildingAbnormalDetectionOptions
    {
        public const string SectionName = "BuildingAbnormalDetection";

        /// <summary>
        /// 地面層底部高度閾值（公尺）
        /// </summary>
        public double GroundFloorBottomThreshold { get; set; } = 5.0;

        /// <summary>
        /// 層間高度容許誤差（公尺）
        /// </summary>
        public double FloorGapTolerance { get; set; } = 0.5;

        /// <summary>
        /// 最大合理層間高度（公尺）
        /// </summary>
        public double MaxFloorGap { get; set; } = 3.0;

        /// <summary>
        /// 最小合理層高（公尺）
        /// </summary>
        public double MinFloorHeight { get; set; } = 2.0;

        /// <summary>
        /// 最大合理層高（公尺）
        /// </summary>
        public double MaxFloorHeight { get; set; } = 8.0;
    }
}
