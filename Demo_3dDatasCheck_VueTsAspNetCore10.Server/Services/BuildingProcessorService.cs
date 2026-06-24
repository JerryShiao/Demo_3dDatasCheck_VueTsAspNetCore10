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
        public List<BuildingData> ProcessXml(string xmlContent)
        {
            var resultList = new List<BuildingData>();
            var doc = XDocument.Parse(xmlContent);

            // 根據您 XML 的 Namespace 進行解析
            XNamespace ns = "http://schemas.datacontract.org/2004/07/ModelOfBuilding_WebAPI.Models";
            var elements = doc.Descendants(ns + "ConsistsOfBuildingPart");

            foreach (var el in elements)
            {
                var dto = new BuildingData
                {
                    Mid = el.Element(ns + "MID")?.Value ?? "",
                    Oid = el.Element(ns + "OID")?.Value ?? "",
                    BuildingNo = el.Element(ns + "建號母號")?.Value ?? "",
                    Floor = el.Element(ns + "層次")?.Value ?? "",
                    BoundedByRaw = el.Element(ns + "boundedBy")?.Value ?? ""
                };

                // 1. 檢核：建號與層次缺漏
                if (string.IsNullOrWhiteSpace(dto.BuildingNo))
                {
                    dto.IsValid = false;
                    dto.ErrorMessages.Add("建號缺漏");
                    dto.BuildingNo = "UNKNOWN_NO"; // 自動修復
                    dto.IsFixed = true;
                    dto.FixMessages.Add("已自動預設建號為 UNKNOWN_NO");
                }
                if (string.IsNullOrWhiteSpace(dto.Floor))
                {
                    dto.IsValid = false;
                    dto.ErrorMessages.Add("層次缺漏");
                    dto.Floor = "001"; // 自動修復
                    dto.IsFixed = true;
                    dto.FixMessages.Add("已自動預設層次為 001 樓");
                }

                // 2. 檢核：座標缺漏
                if (string.IsNullOrWhiteSpace(dto.BoundedByRaw))
                {
                    dto.IsValid = false;
                    dto.ErrorMessages.Add("座標完全缺漏，無法進行 3D 繪製");
                    resultList.Add(dto);
                    continue;
                }

                try
                {
                    // 解析坐標 JSON (結構為三維陣列: [[[lon, lat, z], [lon, lat, z], ...]])
                    var rawCoords = JsonSerializer.Deserialize<List<List<List<double>>>>(dto.BoundedByRaw);
                    if (rawCoords != null && rawCoords.Count > 0)
                    {
                        dto.Coordinates = rawCoords;

                        // 3. 檢核與修復：多邊形是否閉合 (座標偏移/首尾不一致偵測)
                        foreach (var polygon in dto.Coordinates)
                        {
                            if (polygon.Count > 0)
                            {
                                var firstPoint = polygon[0];
                                var lastPoint = polygon[^1];

                                // 檢查經緯度高度是否完全相同
                                if (Math.Abs(firstPoint[0] - lastPoint[0]) > 0.000001 ||
                                    Math.Abs(firstPoint[1] - lastPoint[1]) > 0.000001)
                                {
                                    dto.IsValid = false;
                                    dto.ErrorMessages.Add("座標未閉合（幾何邊界破面異常）");

                                    // 自動修復：將首點複製到末點閉合它
                                    polygon.Add(new List<double> { firstPoint[0], firstPoint[1], firstPoint[2] });
                                    dto.IsFixed = true;
                                    dto.FixMessages.Add("幾何修復：已自動追加閉合端點");
                                }
                            }
                        }
                    }
                }
                catch
                {
                    dto.IsValid = false;
                    dto.ErrorMessages.Add("座標 JSON 格式解析失敗");
                }

                resultList.Add(dto);
            }

            return resultList;
        }

    }//class end
}//namespace end
