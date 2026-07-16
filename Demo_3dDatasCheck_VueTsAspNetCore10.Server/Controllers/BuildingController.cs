using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Controllers
{
    /// <summary>
    /// 建物資料控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BuildingController : ControllerBase
    {
        /// <summary>
        /// 建物資料處理服務
        /// </summary>
        private readonly BuildingProcessorService _processorService;
        /// <summary>
        /// HTTP 客戶端
        /// </summary>
        private readonly HttpClient _httpClient;
        /// <summary>
        /// 異常檢測閾值設定
        /// </summary>
        private readonly BuildingAbnormalDetectionOptions _detectionOptions;
        /// <summary>
        /// 資料寫回服務
        /// </summary>
        private readonly WriteBackService _writeBackService;

        public BuildingController(
            BuildingProcessorService processorService,
            HttpClient httpClient,
            IOptions<BuildingAbnormalDetectionOptions> detectionOptions,
            WriteBackService writeBackService)
        {
            _processorService = processorService;
            _httpClient = httpClient;
            _detectionOptions = detectionOptions.Value;
            _writeBackService = writeBackService;
        }

        #region ◆匯入本地檔案（支援 XML 或 JSON） [ImportFile]
        /// <summary>
        /// 匯入本地檔案（支援 XML 或 JSON）
        /// </summary>
        /// <param name="file">匯入檔案</param>
        /// <returns></returns>
        [HttpPost("import-file")]
        public IActionResult ImportFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("請上傳有效的 XML 或 JSON 檔案");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var content = reader.ReadToEnd();
            var report = _processorService.ProcessContent(content);
            return Ok(report);
        }
        #endregion

        #region ◆連接 URL 取得建物資料（支援 XML 或 JSON）[ImportUrl]
        /// <summary>
        /// 連接 URL 取得建物資料（支援 XML 或 JSON）
        /// </summary>
        /// <param name="url">網址</param>
        /// <returns></returns>
        [HttpGet("import-url")]
        public async Task<IActionResult> ImportUrl([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url)) return BadRequest("網址不可為空");
            try
            {
                var content = await _httpClient.GetStringAsync(url);
                var report = _processorService.ProcessContent(content);
                return Ok(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"無法從網址取得資料: {ex.Message}");
            }
        }
        #endregion

        #region ◆測試 URL 連線是否有效 [TestUrl]
        /// <summary>
        /// 測試 URL 連線是否有效
        /// </summary>
        /// <param name="url">網址</param>
        /// <returns></returns>
        [HttpGet("test-url")]
        public async Task<IActionResult> TestUrl([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
                return BadRequest(new { success = false, message = "網址不可為空" });

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return BadRequest(new { success = false, message = "無效的 URL 格式" });

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                    return Ok(new { success = true, message = $"連線成功 (HTTP {(int)response.StatusCode})" });

                return Ok(new { success = false, message = $"連線失敗 (HTTP {(int)response.StatusCode})" });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = $"連線失敗: {ex.Message}" });
            }
        }
        #endregion

        #region ◆取得異常檢測閾值設定 [GetDetectionSettings]
        /// <summary>
        /// 取得異常檢測閾值設定（供前端同步使用）
        /// </summary>
        [HttpGet("detection-settings")]
        public IActionResult GetDetectionSettings()
        {
            return Ok(_detectionOptions);
        }
        #endregion

        #region ◆寫回修復後資料至 ModelOfBuilding [WriteBack]
        /// <summary>
        /// 將已修復樓層寫回 ConsistsOfBuildingParts（失敗時補償還原）
        /// </summary>
        [HttpPost("write-back")]
        public async Task<IActionResult> WriteBack([FromBody] WriteBackRequest request, CancellationToken cancellationToken)
        {
            var (success, error, statusCode) = await _writeBackService.WriteBackAsync(request, cancellationToken);
            if (success != null)
            {
                return Ok(success);
            }

            return StatusCode(statusCode, error);
        }
        #endregion

    }//class end
}//namespace end
