using System.Text.Json.Nodes;

/// <summary>
/// Abstraction over the downstream Azure Function business app that proxies to NetSuite.
/// All six business app functions are represented here.
/// </summary>
public interface INetSuiteBusinessAppClient
{
    // ── SuiteQL ──────────────────────────────────────────────────────────────
    Task<JsonNode> ExecuteSuiteQLAsync(string query, CancellationToken ct = default);

    // ── Record CRUD ──────────────────────────────────────────────────────────
    Task<JsonNode> GetRecordAsync(string recordType, string recordId, string? extensions = null, CancellationToken ct = default);
    Task<JsonNode> CreateRecordAsync(string recordType, JsonObject body, CancellationToken ct = default);
    Task<JsonNode> UpdateRecordAsync(string recordType, string recordId, JsonObject body, CancellationToken ct = default);
    Task<JsonNode> DeleteRecordAsync(string recordType, string recordId, CancellationToken ct = default);

    // ── Transform ────────────────────────────────────────────────────────────
    Task<JsonNode> TransformRecordAsync(string fromRecordType, string fromId, string toRecordType, JsonObject? body = null, CancellationToken ct = default);
}
