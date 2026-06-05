using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

/// <summary>
/// Extension methods that make reading MCP tool arguments clean and consistent.
/// Handles required vs optional, type coercion, and clear error messages.
/// </summary>
public static class McpToolArgumentExtensions
{
    // ── Required ──────────────────────────────────────────────────────────────

    public static string GetRequiredString(this Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            throw new ArgumentException($"Required argument '{key}' is missing or empty.");
        var str = value.ToString();
        if (string.IsNullOrWhiteSpace(str))
            throw new ArgumentException($"Required argument '{key}' is missing or empty.");
        return str!;
    }

    public static JsonObject GetRequiredObject(this Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            throw new ArgumentException($"Required argument '{key}' is missing or not a JSON object.");
        if (value is JsonObject obj)
            return obj;
        var json = JsonSerializer.Serialize(value);
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new ArgumentException($"Required argument '{key}' is not a JSON object.");
    }

    // ── Optional ──────────────────────────────────────────────────────────────

    public static string? GetOptionalString(this Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return null;
        return value.ToString();
    }

    public static int? GetOptionalInt(this Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return null;
        return Convert.ToInt32(value);
    }

    public static JsonArray? GetOptionalArray(this Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return null;
        if (value is JsonArray arr)
            return arr;
        var json = JsonSerializer.Serialize(value);
        return JsonNode.Parse(json) as JsonArray;
    }

    // ── ToolInvocationContext overloads ───────────────────────────────────────

    public static string GetRequiredString(this ToolInvocationContext toolCall, string key)
        => toolCall.Arguments.GetRequiredString(key);

    public static JsonObject GetRequiredObject(this ToolInvocationContext toolCall, string key)
        => toolCall.Arguments.GetRequiredObject(key);

    public static string? GetOptionalString(this ToolInvocationContext toolCall, string key)
        => toolCall.Arguments.GetOptionalString(key);

    public static int? GetOptionalInt(this ToolInvocationContext toolCall, string key)
        => toolCall.Arguments.GetOptionalInt(key);

    public static JsonArray? GetOptionalArray(this ToolInvocationContext toolCall, string key)
        => toolCall.Arguments.GetOptionalArray(key);
}
