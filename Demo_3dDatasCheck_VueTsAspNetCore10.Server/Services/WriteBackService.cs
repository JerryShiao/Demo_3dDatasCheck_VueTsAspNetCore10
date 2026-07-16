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

            // 2. 更新項先 GET 備份
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

            // 3. 依序寫入
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

            payload.MID = mid;
            payload.gmlid = item.Gmlid;
            payload.建號母號 = item.BuildingNo;
            payload.建號子號 = string.IsNullOrWhiteSpace(item.BuildingSubNo) ? "000" : item.BuildingSubNo;
            payload.是否為主要建物 = string.IsNullOrWhiteSpace(item.IsMainBuilding) ? "true" : item.IsMainBuilding;
            payload.附屬建物類型 = string.IsNullOrWhiteSpace(item.AnnexType) ? "No" : item.AnnexType;
            payload.高度 = height;
            payload.面積 = item.Area;
            payload.層次 = item.Floor;
            payload.boundedBy = JsonSerializer.Serialize(item.Coordinates);

            return true;
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
