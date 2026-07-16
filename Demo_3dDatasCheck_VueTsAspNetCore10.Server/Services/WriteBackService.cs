using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Models;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services
{
    /// <summary>
    /// 將修復後樓層寫回 ModelOfBuilding ConsistsOfBuildingParts API（含補償式復原）
    /// </summary>
    public class WriteBackService
    {
        /// <summary>
        /// 外部 API 使用原始屬性名（OID / MID / 中文欄位），不可套用 camelCase
        /// </summary>
        private static readonly JsonSerializerOptions ExternalJsonOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _httpClient;
        private readonly ModelOfBuildingApiOptions _options;
        private readonly ILogger<WriteBackService> _logger;

        public WriteBackService(
            HttpClient httpClient,
            IOptions<ModelOfBuildingApiOptions> options,
            ILogger<WriteBackService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// 批次寫回；任一失敗則補償已成功操作
        /// </summary>
        public async Task<(WriteBackResponse? Success, WriteBackErrorResponse? Error, int StatusCode)> WriteBackAsync(
            WriteBackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request?.Items == null || request.Items.Count == 0)
            {
                return (null, new WriteBackErrorResponse { Message = "寫回項目不可為空" }, StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(_options.ConsistsOfBuildingPartsUrl))
            {
                return (null, new WriteBackErrorResponse { Message = "未設定 ModelOfBuildingApi:ConsistsOfBuildingPartsUrl" }, StatusCodes.Status500InternalServerError);
            }

            var baseUrl = _options.ConsistsOfBuildingPartsUrl.TrimEnd('/');
            var prepared = new List<PreparedWriteItem>();

            // 1. 驗證並映射
            foreach (var item in request.Items)
            {
                if (!TryMapPayload(item, out var payload, out var isInsert, out var oid, out var mapError))
                {
                    return (null, new WriteBackErrorResponse
                    {
                        Message = mapError,
                        FailedOid = item.Oid,
                        FailedRowId = item.RowId,
                    }, StatusCodes.Status400BadRequest);
                }

                prepared.Add(new PreparedWriteItem
                {
                    Source = item,
                    Payload = payload,
                    IsInsert = isInsert,
                    Oid = oid,
                });
            }

            // 2. 新增項先依 MID 查詢，確認五欄位無重複
            foreach (var item in prepared.Where(p => p.IsInsert))
            {
                var dupResult = await CheckDuplicateInsertAsync(baseUrl, item, cancellationToken);
                if (dupResult != null)
                {
                    return dupResult.Value;
                }
            }

            // 3. 更新項先 GET 備份
            var backups = new Dictionary<int, string>();
            foreach (var item in prepared.Where(p => !p.IsInsert))
            {
                var oid = item.Oid!.Value;
                try
                {
                    var getUrl = $"{baseUrl}/{oid}";
                    using var getResponse = await _httpClient.GetAsync(getUrl, cancellationToken);
                    if (!getResponse.IsSuccessStatusCode)
                    {
                        var body = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                        return (null, new WriteBackErrorResponse
                        {
                            Message = $"寫入前備份失敗（GET OID={oid}）：HTTP {(int)getResponse.StatusCode} {body}",
                            FailedOid = oid.ToString(),
                            FailedRowId = item.Source.RowId,
                        }, StatusCodes.Status502BadGateway);
                    }

                    backups[oid] = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GET backup failed for OID {Oid}", oid);
                    return (null, new WriteBackErrorResponse
                    {
                        Message = $"寫入前備份失敗（GET OID={oid}）：{ex.Message}",
                        FailedOid = oid.ToString(),
                        FailedRowId = item.Source.RowId,
                    }, StatusCodes.Status502BadGateway);
                }
            }

            // 4. 依序寫入
            var succeeded = new List<SucceededOp>();
            PreparedWriteItem? current = null;
            try
            {
                foreach (var item in prepared)
                {
                    current = item;
                    if (item.IsInsert)
                    {
                        var newOid = await PostInsertAsync(baseUrl, item.Payload, cancellationToken);
                        succeeded.Add(new SucceededOp
                        {
                            IsInsert = true,
                            Oid = newOid,
                            RowId = item.Source.RowId,
                            OriginalOid = item.Source.Oid,
                        });
                    }
                    else
                    {
                        var oid = item.Oid!.Value;
                        await PutUpdateAsync(baseUrl, oid, item.Payload, cancellationToken);
                        succeeded.Add(new SucceededOp
                        {
                            IsInsert = false,
                            Oid = oid,
                            RowId = item.Source.RowId,
                            OriginalOid = item.Source.Oid,
                            BackupJson = backups[oid],
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Write-back failed after {Count} succeeded ops", succeeded.Count);
                var (compOk, compMsg) = await CompensateAsync(baseUrl, succeeded, cancellationToken);
                return (null, new WriteBackErrorResponse
                {
                    Message = $"寫回失敗：{ex.Message}",
                    FailedOid = current?.Source.Oid,
                    FailedRowId = current?.Source.RowId,
                    CompensationSucceeded = compOk,
                    CompensationMessage = compMsg,
                }, StatusCodes.Status502BadGateway);
            }

            var results = succeeded.Select(s => new WriteBackItemResult
            {
                RowId = s.RowId,
                OriginalOid = s.OriginalOid,
                NewOid = s.IsInsert ? s.Oid : null,
                IsInsert = s.IsInsert,
            }).ToList();

            return (new WriteBackResponse
            {
                Success = true,
                Message = $"成功寫回 {results.Count} 筆",
                Results = results,
            }, null, StatusCodes.Status200OK);
        }

        private static bool TryMapPayload(
            WriteBackBuildingItem item,
            out ConsistsOfBuildingPartPayload payload,
            out bool isInsert,
            out int? oid,
            out string error)
        {
            payload = new ConsistsOfBuildingPartPayload();
            isInsert = false;
            oid = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(item.Mid) || !int.TryParse(item.Mid.Trim(), out var mid))
            {
                error = $"MID 無效：'{item.Mid}'";
                return false;
            }

            if (item.Coordinates == null || item.Coordinates.Count == 0)
            {
                error = $"OID '{item.Oid}' 缺少座標資料";
                return false;
            }

            isInsert = IsInsertOid(item.Oid);
            if (!isInsert)
            {
                if (!int.TryParse(item.Oid.Trim(), out var parsedOid) || parsedOid <= 0)
                {
                    error = $"更新用 OID 無效：'{item.Oid}'";
                    return false;
                }

                oid = parsedOid;
                payload.OID = parsedOid;
            }

            decimal? height = item.Height;
            if (height == null && item.MinHeight.HasValue && item.MaxHeight.HasValue)
            {
                height = (decimal)(item.MaxHeight.Value - item.MinHeight.Value);
            }

            decimal? area = item.Area;
            if (area == null)
            {
                if (!TryComputeFootprintAreaSqm(item.Coordinates, out var computedArea))
                {
                    error = $"OID '{item.Oid}' 無法由座標計算面積";
                    return false;
                }

                area = computedArea;
            }

            var floor = item.Floor ?? string.Empty;

            payload.MID = mid;
            payload.gmlid = string.IsNullOrWhiteSpace(item.Gmlid) ? "id" + floor : item.Gmlid;
            payload.建號母號 = item.BuildingNo;
            payload.建號子號 = string.IsNullOrWhiteSpace(item.BuildingSubNo) ? "000" : item.BuildingSubNo;
            payload.是否為主要建物 = string.IsNullOrWhiteSpace(item.IsMainBuilding) ? "true" : item.IsMainBuilding;
            payload.附屬建物類型 = string.IsNullOrWhiteSpace(item.AnnexType) ? "No" : item.AnnexType;
            payload.高度 = height;
            payload.面積 = area;
            payload.層次 = floor;
            payload.boundedBy = JsonSerializer.Serialize(item.Coordinates);

            return true;
        }

        /// <summary>
        /// 新增前依 MID 查詢外部 API，若五欄位皆相同則視為重複
        /// </summary>
        private async Task<(WriteBackResponse? Success, WriteBackErrorResponse? Error, int StatusCode)?> CheckDuplicateInsertAsync(
            string baseUrl,
            PreparedWriteItem item,
            CancellationToken cancellationToken)
        {
            var mid = item.Payload.MID;
            if (mid == null)
            {
                return (null, new WriteBackErrorResponse
                {
                    Message = "新增項目缺少 MID，無法做重複檢查",
                    FailedOid = item.Source.Oid,
                    FailedRowId = item.Source.RowId,
                }, StatusCodes.Status400BadRequest);
            }

            try
            {
                var getUrl = $"{baseUrl}/?MID={mid}";
                using var response = await _httpClient.GetAsync(getUrl, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, new WriteBackErrorResponse
                    {
                        Message = $"新增前重複檢查失敗（GET MID={mid}）：HTTP {(int)response.StatusCode} {body}",
                        FailedOid = item.Source.Oid,
                        FailedRowId = item.Source.RowId,
                    }, StatusCodes.Status502BadGateway);
                }

                if (HasDuplicatePart(body, item.Payload))
                {
                    return (null, new WriteBackErrorResponse
                    {
                        Message = $"資料重複，無法新增：MID={mid}、建號母號={item.Payload.建號母號}、層次={item.Payload.層次}"
                            + "（MID／建號母號／是否為主要建物／附屬建物類型／層次皆相同）",
                        FailedOid = item.Source.Oid,
                        FailedRowId = item.Source.RowId,
                    }, StatusCodes.Status400BadRequest);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Duplicate check failed for MID {Mid}", mid);
                return (null, new WriteBackErrorResponse
                {
                    Message = $"新增前重複檢查失敗（GET MID={mid}）：{ex.Message}",
                    FailedOid = item.Source.Oid,
                    FailedRowId = item.Source.RowId,
                }, StatusCodes.Status502BadGateway);
            }
        }

        /// <summary>
        /// 解析 GET ?MID= 回應，比對五欄位是否與待新增 payload 重複
        /// </summary>
        private static bool HasDuplicatePart(string responseBody, ConsistsOfBuildingPartPayload candidate)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                foreach (var element in EnumerateRecordElements(doc.RootElement))
                {
                    if (MatchesDuplicateKey(element, candidate))
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        private static IEnumerable<JsonElement> EnumerateRecordElements(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        yield return el;
                    }
                }

                yield break;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var wrapName in new[] { "data", "items", "results", "value" })
                {
                    if (root.TryGetProperty(wrapName, out var wrapped)
                        && wrapped.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in wrapped.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.Object)
                            {
                                yield return el;
                            }
                        }

                        yield break;
                    }
                }

                yield return root;
            }
        }

        private static bool MatchesDuplicateKey(JsonElement element, ConsistsOfBuildingPartPayload candidate)
        {
            var mid = TryGetJsonInt(element, "MID", "mid");
            var buildingNo = TryGetJsonString(element, "建號母號", "buildingNo", "BuildingNo");
            var isMain = TryGetJsonString(element, "是否為主要建物", "isMainBuilding", "IsMainBuilding");
            var annex = TryGetJsonString(element, "附屬建物類型", "annexType", "AnnexType");
            var floor = TryGetJsonString(element, "層次", "floor", "Floor");

            if (mid == null || candidate.MID == null)
            {
                return false;
            }

            return mid == candidate.MID
                && string.Equals(NormalizeField(buildingNo), NormalizeField(candidate.建號母號), StringComparison.Ordinal)
                && string.Equals(NormalizeField(isMain), NormalizeField(candidate.是否為主要建物), StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeField(annex), NormalizeField(candidate.附屬建物類型), StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeField(floor), NormalizeField(candidate.層次), StringComparison.Ordinal);
        }

        private static string NormalizeField(string? value)
            => (value ?? string.Empty).Trim();

        private static int? TryGetJsonInt(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyIgnoreCase(element, name, out var prop))
                {
                    continue;
                }

                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
                {
                    return n;
                }

                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var sn))
                {
                    return sn;
                }
            }

            return null;
        }

        private static string? TryGetJsonString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetPropertyIgnoreCase(element, name, out var prop))
                {
                    continue;
                }

                if (prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }

                if (prop.ValueKind == JsonValueKind.Number || prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                {
                    return prop.ToString();
                }
            }

            return null;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement property)
        {
            if (element.TryGetProperty(name, out property))
            {
                return true;
            }

            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = p.Value;
                    return true;
                }
            }

            property = default;
            return false;
        }

        /// <summary>
        /// 由建物座標選 footprint 環，equirectangular 轉公尺後以 Shoelace 計算面積（m²）
        /// </summary>
        private static bool TryComputeFootprintAreaSqm(
            List<List<List<double>>> coordinates,
            out decimal areaSqm)
        {
            areaSqm = 0;
            var ring = SelectFootprintRing(coordinates);
            if (ring.Count < 3)
            {
                return false;
            }

            var meanLat = ring.Average(p => p.Lat);
            var metersPerDegLat = 110540.0;
            var metersPerDegLon = 111320.0 * Math.Cos(meanLat * Math.PI / 180.0);

            var meters = new List<(double X, double Y)>(ring.Count);
            foreach (var p in ring)
            {
                meters.Add((p.Lon * metersPerDegLon, p.Lat * metersPerDegLat));
            }

            var area = Math.Abs(ShoelaceArea(meters));
            if (area <= 0 || !double.IsFinite(area))
            {
                return false;
            }

            areaSqm = Math.Round((decimal)area, 3, MidpointRounding.AwayFromZero);
            return areaSqm > 0;
        }

        private static List<(double Lon, double Lat)> SelectFootprintRing(List<List<List<double>>> coordinates)
        {
            List<(double Lon, double Lat)> bestRing = [];
            var bestScore = double.NegativeInfinity;

            foreach (var polygon in coordinates)
            {
                var ring = RingTo2D(polygon);
                if (ring.Count < 3)
                {
                    continue;
                }

                var planarArea = Math.Abs(ShoelaceArea(ring.Select(p => (p.Lon, p.Lat)).ToList()));
                if (planarArea <= 0)
                {
                    continue;
                }

                var zs = polygon
                    .Where(pt => pt != null && pt.Count >= 3 && double.IsFinite(pt[2]))
                    .Select(pt => pt[2])
                    .ToList();
                var zSpan = zs.Count > 0 ? zs.Max() - zs.Min() : double.PositiveInfinity;
                var score = planarArea - zSpan * 1_000_000;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRing = ring;
                }
            }

            if (bestRing.Count > 0)
            {
                return bestRing;
            }

            return coordinates.Count > 0 ? RingTo2D(coordinates[0]) : [];
        }

        private static List<(double Lon, double Lat)> RingTo2D(List<List<double>> polygon)
        {
            var ring = new List<(double Lon, double Lat)>();
            foreach (var pt in polygon)
            {
                if (pt == null || pt.Count < 2)
                {
                    continue;
                }

                if (!double.IsFinite(pt[0]) || !double.IsFinite(pt[1]))
                {
                    continue;
                }

                ring.Add((pt[0], pt[1]));
            }

            return ring;
        }

        private static double ShoelaceArea(IReadOnlyList<(double X, double Y)> ring)
        {
            if (ring.Count < 3)
            {
                return 0;
            }

            double area = 0;
            for (var i = 0; i < ring.Count; i++)
            {
                var j = (i + 1) % ring.Count;
                area += ring[i].X * ring[j].Y;
                area -= ring[j].X * ring[i].Y;
            }

            return area / 2.0;
        }

        private static bool IsInsertOid(string? oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
            {
                return true;
            }

            if (oid.StartsWith("PATCH_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !int.TryParse(oid.Trim(), out var n) || n <= 0;
        }

        private async Task<int> PostInsertAsync(
            string baseUrl,
            ConsistsOfBuildingPartPayload payload,
            CancellationToken cancellationToken)
        {
            using var response = await _httpClient.PostAsJsonAsync(baseUrl, payload, ExternalJsonOptions, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"POST 新增失敗：HTTP {(int)response.StatusCode} {body}");
            }

            var newOid = TryParseOidFromResponse(body);
            if (newOid == null)
            {
                throw new InvalidOperationException($"POST 成功但無法解析新 OID：{Truncate(body, 200)}");
            }

            return newOid.Value;
        }

        private async Task PutUpdateAsync(
            string baseUrl,
            int oid,
            ConsistsOfBuildingPartPayload payload,
            CancellationToken cancellationToken)
        {
            var url = $"{baseUrl}/{oid}";
            using var response = await _httpClient.PutAsJsonAsync(url, payload, ExternalJsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"PUT 更新失敗（OID={oid}）：HTTP {(int)response.StatusCode} {body}");
            }
        }

        private async Task<(bool Ok, string Message)> CompensateAsync(
            string baseUrl,
            List<SucceededOp> succeeded,
            CancellationToken cancellationToken)
        {
            if (succeeded.Count == 0)
            {
                return (true, "無需補償（尚無成功寫入）");
            }

            var errors = new List<string>();
            for (var i = succeeded.Count - 1; i >= 0; i--)
            {
                var op = succeeded[i];
                try
                {
                    if (op.IsInsert)
                    {
                        var deleteUrl = $"{baseUrl}/{op.Oid}";
                        using var deleteResponse = await _httpClient.DeleteAsync(deleteUrl, cancellationToken);
                        if (!deleteResponse.IsSuccessStatusCode)
                        {
                            var body = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                            errors.Add($"DELETE OID={op.Oid} 失敗：HTTP {(int)deleteResponse.StatusCode} {body}");
                        }
                    }
                    else
                    {
                        var putUrl = $"{baseUrl}/{op.Oid}";
                        using var content = new StringContent(op.BackupJson ?? "{}", Encoding.UTF8, "application/json");
                        using var putResponse = await _httpClient.PutAsync(putUrl, content, cancellationToken);
                        if (!putResponse.IsSuccessStatusCode)
                        {
                            var body = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                            errors.Add($"還原 PUT OID={op.Oid} 失敗：HTTP {(int)putResponse.StatusCode} {body}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"補償 OID={op.Oid} 例外：{ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                return (true, $"已補償還原 {succeeded.Count} 筆");
            }

            return (false, string.Join("；", errors));
        }

        private static int? TryParseOidFromResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var trimmed = body.Trim();
            if (int.TryParse(trimmed, out var plainOid))
            {
                return plainOid;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out var numOid))
                {
                    return numOid;
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "OID", "oid", "Oid", "id", "Id" })
                    {
                        if (root.TryGetProperty(name, out var prop))
                        {
                            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var id))
                            {
                                return id;
                            }

                            if (prop.ValueKind == JsonValueKind.String
                                && int.TryParse(prop.GetString(), out var sid))
                            {
                                return sid;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // ignore
            }

            return null;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value[..max] + "…";

        private sealed class PreparedWriteItem
        {
            public required WriteBackBuildingItem Source { get; init; }
            public required ConsistsOfBuildingPartPayload Payload { get; init; }
            public bool IsInsert { get; init; }
            public int? Oid { get; init; }
        }

        private sealed class SucceededOp
        {
            public bool IsInsert { get; init; }
            public int Oid { get; init; }
            public string? RowId { get; init; }
            public string OriginalOid { get; init; } = string.Empty;
            public string? BackupJson { get; init; }
        }
    }
}
