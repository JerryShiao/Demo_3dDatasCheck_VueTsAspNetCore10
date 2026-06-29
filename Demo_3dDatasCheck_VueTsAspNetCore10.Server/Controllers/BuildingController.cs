using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Controllers
{
    /// <summary>
    /// 建物資料控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BuildingController : ControllerBase
    {
        private readonly BuildingProcessorService _processor;
        private readonly HttpClient _httpClient;

        public BuildingController(BuildingProcessorService processor, HttpClient httpClient)
        {
            _processor = processor;
            _httpClient = httpClient;
        }

        // 端點 1：匯入本地 XML 檔案
        [HttpPost("import-file")]
        public IActionResult ImportFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("請上傳有效的 XML 檔案");

            using var reader = new StreamReader(file.OpenReadStream());
            var content = reader.ReadToEnd();
            var report = _processor.ProcessXml(content);
            return Ok(report);
        }

        // 端點 2：連接 URL 取得建物資料（支援 XML 或 JSON）
        [HttpGet("import-url")]
        public async Task<IActionResult> ImportUrl([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url)) return BadRequest("網址不可為空");
            try
            {
                var content = await _httpClient.GetStringAsync(url);
                var report = _processor.ProcessContent(content);
                return Ok(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"無法從網址取得資料: {ex.Message}");
            }
        }

        // 端點 3：測試 URL 連線是否有效
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

    }//class end
}//namespace end
