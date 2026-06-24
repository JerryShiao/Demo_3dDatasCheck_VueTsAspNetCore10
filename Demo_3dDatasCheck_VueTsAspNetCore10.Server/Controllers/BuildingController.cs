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

        // 端點 2：連接 URL 取得 XML 資料
        [HttpGet("import-url")]
        public async Task<IActionResult> ImportUrl([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url)) return BadRequest("網址不可為空");
            try
            {
                var content = await _httpClient.GetStringAsync(url);
                var report = _processor.ProcessXml(content);
                return Ok(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"無法從網址取得資料: {ex.Message}");
            }
        }

    }//class end
}//namespace end
