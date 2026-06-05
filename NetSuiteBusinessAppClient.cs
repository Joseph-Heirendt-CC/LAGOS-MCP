using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// HTTP client that forwards MCP tool invocations to the downstream
/// Azure Function business app (nsrestinterface-{company}.azurewebsites.net).
/// Authentication (x-functions-key) is injected via HttpClient factory in Program.cs.
/// </summary>
public class NetSuiteBusinessAppClient : INetSuiteBusinessAppClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public NetSuiteBusinessAppClient(HttpClient http) => _http = http;

    // ── SuiteQL ──────────────────────────────────────────────────────────────

    public async Task<JsonNode> ExecuteSuiteQLAsync(string query, CancellationToken ct = default)
    {
        var allItems = new JsonArray();
        string nextPageLink = "";
        int totalResults = 0;
        const int maxPages = 50;

        for (int page = 0; page < maxPages; page++)
        {
            var payload = new JsonObject
            {
                ["nextPageLink"] = nextPageLink,
                ["query"] = query
            };

            var result = await PostAsync("/api/netsuiteSuiteqlQuery", payload, ct);
            var obj = result as JsonObject;

            if (obj?["items"] is JsonArray items)
                foreach (var item in items)
                    allItems.Add(item?.DeepClone());

            totalResults = obj?["totalResults"]?.GetValue<int>() ?? allItems.Count;

            var hasMore = obj?["hasMore"]?.GetValue<bool>() ?? false;
            if (!hasMore) break;

            var nextLink = (obj?["links"] as JsonArray)?
                .OfType<JsonObject>()
                .FirstOrDefault(l => l["rel"]?.GetValue<string>() == "next")?
                ["href"]?.GetValue<string>();

            if (string.IsNullOrEmpty(nextLink)) break;
            nextPageLink = nextLink;
        }

        return new JsonObject
        {
            ["items"] = allItems,
            ["count"] = allItems.Count,
            ["totalResults"] = totalResults,
            ["hasMore"] = false
        };
    }

    // ── Record CRUD ──────────────────────────────────────────────────────────

    public async Task<JsonNode> GetRecordAsync(
        string recordType, string recordId, string? extensions = null, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["recordType"] = recordType,
            ["recordId"] = recordId,
            ["extensions"] = extensions ?? string.Empty
        };
        return await PostAsync("/api/netsuiteGetRequest", payload, ct);
    }

    public async Task<JsonNode> CreateRecordAsync(
        string recordType, JsonObject body, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["recordType"] = recordType,
            ["requestBody"] = body
        };
        return await PostAsync("/api/netsuitePostRequest", payload, ct);
    }

    public async Task<JsonNode> UpdateRecordAsync(
        string recordType, string recordId, JsonObject body, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["recordType"] = recordType,
            ["recordId"] = recordId,
            ["requestBody"] = body
        };
        return await PatchAsync("/api/netsuitePatchRequest", payload, ct);
    }

    public async Task<JsonNode> DeleteRecordAsync(
        string recordType, string recordId, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["recordType"] = recordType,
            ["recordId"] = recordId
        };
        return await DeleteAsync("/api/netsuiteDeleteRequest", payload, ct);
    }

    // ── Transform ────────────────────────────────────────────────────────────

    public async Task<JsonNode> TransformRecordAsync(
        string fromRecordType, string fromId, string toRecordType,
        JsonObject? body = null, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["fromRecordType"] = fromRecordType,
            ["fromId"] = fromId,
            ["toRecordType"] = toRecordType,
            ["requestBody"] = body ?? new JsonObject()
        };
        return await PostAsync("/api/netsuiteTransformRequest", payload, ct);
    }

    // ── Private HTTP helpers ──────────────────────────────────────────────────

    private async Task<JsonNode> PostAsync(string path, JsonObject payload, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(path, payload, _jsonOpts, ct);
        return await ReadResponseAsync(response, ct);
    }

    private async Task<JsonNode> PatchAsync(string path, JsonObject payload, CancellationToken ct)
    {
        var content = JsonContent.Create(payload, options: _jsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = content };
        var response = await _http.SendAsync(request, ct);
        return await ReadResponseAsync(response, ct);
    }

    private async Task<JsonNode> DeleteAsync(string path, JsonObject payload, CancellationToken ct)
    {
        // Business app DELETE endpoint accepts a JSON body
        var content = JsonContent.Create(payload, options: _jsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = content };
        var response = await _http.SendAsync(request, ct);
        return await ReadResponseAsync(response, ct);
    }

    private static async Task<JsonNode> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);

        // Always parse JSON regardless of status — NetSuite surfaces detailed
        // error info as JSON on 4xx/5xx responses, which the MCP tool should
        // return to the model verbatim rather than throw.
        var node = JsonNode.Parse(json) ?? new JsonObject { ["raw"] = json };
        return node;
    }
}
