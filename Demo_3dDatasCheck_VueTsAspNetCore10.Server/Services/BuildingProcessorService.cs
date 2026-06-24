using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;
using System.Text.Json;
using System.Xml.Linq;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 建物資料處理服務
    /// </summary>
    public class BuildingProcessorService
    {
        private static readonly XNamespace XmlNs =
            "http://schemas.datacontract.org/2004/07/ModelOfBuilding_WebAPI.Models";

        /// <summary>
        /// 依內容格式自動選擇 XML 或 JSON 解析
        /// </summary>
        public List<BuildingData> ProcessContent(string content)
        {
            var trimmed = content.TrimStart();
            if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
                return ProcessJson(content);
            return ProcessXml(content);
        }

        /// <summary>
        /// 解析建物資料（XML）
        /// </summary>
        public List<BuildingData> ProcessXml(string xmlContent)
        {
            var doc = XDocument.Parse(xmlContent);
            var elements = doc.Descendants(XmlNs + "ConsistsOfBuildingPart");

            return elements.Select(el => ValidateAndFix(new BuildingData
            {
                Mid = el.Element(XmlNs + "MID")?.Value ?? "",
                Oid = el.Element(XmlNs + "OID")?.Value ?? "",
                BuildingNo = el.Element(XmlNs + "建號母號")?.Value ?? "",
                Floor = el.Element(XmlNs + "層次")?.Value ?? "",
                BoundedByRaw = el.Element(XmlNs + "boundedBy")?.Value ?? ""
            })).ToList();
        }

        /// <summary>
        /// 解析建物資料（JSON）
        /// </summary>
        public List<BuildingData> ProcessJson(string jsonContent)
        {
            var records = JsonSerializer.Deserialize<List<BuildingJsonRecord>>(jsonContent)
                ?? new List<BuildingJsonRecord>();

            return records.Select(r => ValidateAndFix(new BuildingData
            {
                Mid = r.MID.ToString(),
                Oid = r.OID.ToString(),
                BuildingNo = r.建號母號 ?? "",
                Floor = r.層次 ?? "",
                BoundedByRaw = r.boundedBy ?? ""
            })).ToList();
        }

        private static BuildingData ValidateAndFix(BuildingData dto)
        {
            if (string.IsNullOrWhiteSpace(dto.BuildingNo))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("建號缺漏");
                dto.BuildingNo = "UNKNOWN_NO";
                dto.IsFixed = true;
                dto.FixMessages.Add("已自動預設建號為 UNKNOWN_NO");
            }

            if (string.IsNullOrWhiteSpace(dto.Floor))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("層次缺漏");
                dto.Floor = "001";
                dto.IsFixed = true;
                dto.FixMessages.Add("已自動預設層次為 001 樓");
            }

            if (string.IsNullOrWhiteSpace(dto.BoundedByRaw))
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("座標完全缺漏，無法進行 3D 繪製");
                return dto;
            }

            try
            {
                var rawCoords = JsonSerializer.Deserialize<List<List<List<double>>>>(dto.BoundedByRaw);
                if (rawCoords != null && rawCoords.Count > 0)
                {
                    dto.Coordinates = rawCoords;

                    foreach (var polygon in dto.Coordinates)
                    {
                        if (polygon.Count == 0) continue;

                        var firstPoint = polygon[0];
                        var lastPoint = polygon[^1];

                        if (Math.Abs(firstPoint[0] - lastPoint[0]) > 0.000001 ||
                            Math.Abs(firstPoint[1] - lastPoint[1]) > 0.000001)
                        {
                            dto.IsValid = false;
                            dto.ErrorMessages.Add("座標未閉合（幾何邊界破面異常）");
                            polygon.Add(new List<double> { firstPoint[0], firstPoint[1], firstPoint[2] });
                            dto.IsFixed = true;
                            dto.FixMessages.Add("幾何修復：已自動追加閉合端點");
                        }
                    }
                }
            }
            catch
            {
                dto.IsValid = false;
                dto.ErrorMessages.Add("座標 JSON 格式解析失敗");
            }

            return dto;
        }

    }//class end
}//namespace end
