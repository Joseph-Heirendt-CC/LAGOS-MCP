using System.Text.Json.Nodes;

/// <summary>
/// Shared helpers for MCP tool classes. Import with `using static ToolHelpers;`.
/// </summary>
public static class ToolHelpers
{
    // Escapes single quotes for string literals embedded in SuiteQL.
    public static string EscapeSuiteQL(string v) => v.Replace("'", "''");

    // Parses a boolean string and writes a strict JSON boolean (true/false) to the request body.
    // Accepts "true"/"false" (canonical) and "T"/"F" (legacy), case-insensitive.
    public static void AddBool(JsonObject o, string key, string? v)
    {
        if (v == null) return;
        bool parsed = v.Trim().ToUpperInvariant() switch
        {
            "T" or "TRUE"  => true,
            "F" or "FALSE" => false,
            _ => throw new ArgumentException($"'{key}' must be true or false, got: '{v}'")
        };
        o[key] = JsonValue.Create(parsed);
    }

    // Resolves display names (e.g. "Store Manager") to internal IDs in a NetSuite custom list.
    // Matching is case-insensitive; throws with the valid names if any value doesn't match.
    public static async Task<List<string>> ResolveListValueIds(
        INetSuiteBusinessAppClient client, string listScriptId, IEnumerable<string> names, string paramName, CancellationToken ct)
    {
        var result = await client.ExecuteSuiteQLAsync(
            $"SELECT id, name FROM {listScriptId} WHERE isinactive = 'F'", ct);
        var items = (result["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();

        var ids = new List<string>();
        foreach (var name in names)
        {
            var match = items.FirstOrDefault(i =>
                string.Equals(i["name"]?.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                var valid = string.Join(", ", items.Select(i => i["name"]?.ToString()));
                throw new ArgumentException($"Unknown {paramName} value '{name.Trim()}'. Valid values: {valid}");
            }
            ids.Add(match["id"]!.ToString());
        }
        return ids;
    }

    // Builds a multi-select field value ({ items: [{ id }, …] }) from a comma-separated list of
    // display names, resolved against the custom list at call time. NetSuite replaces the whole
    // selection, so this is the complete set; an empty string clears it.
    public static async Task<JsonObject> BuildMultiSelect(
        INetSuiteBusinessAppClient client, string listScriptId, string commaSeparatedNames, string paramName, CancellationToken ct)
    {
        var names = commaSeparatedNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = names.Length == 0
            ? new List<string>()
            : await ResolveListValueIds(client, listScriptId, names, paramName, ct);
        return new JsonObject
        {
            ["items"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray())
        };
    }

    // Converts a comma-separated multi-select column (as returned by BUILTIN.DF(), e.g.
    // "Buying, Sales") into a JSON array on every row of a SuiteQL result. Null/empty → [].
    public static void SplitMultiSelectColumn(JsonNode result, string column)
    {
        foreach (var item in (result["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var raw = item[column]?.ToString();
            item[column] = new JsonArray((raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => (JsonNode)JsonValue.Create(v)!)
                .ToArray());
        }
    }

    // Builds an EXISTS clause matching city (partial) and/or state (exact) against the entity's
    // default shipping address. An EXISTS subquery (rather than a join) keeps entities that have
    // no default shipping address from affecting other filters.
    // City and state must match on the same address. Returns null when neither is supplied.
    public static string? AddressFilterClause(string entityAlias, string? city, string? state)
    {
        if (city == null && state == null) return null;

        var conditions = new List<string> { $"aba.entity = {entityAlias}.id", "aba.defaultshipping = 'T'" };
        if (city  != null) conditions.Add($"LOWER(aba.city) LIKE LOWER('%{EscapeSuiteQL(city)}%')");
        if (state != null) conditions.Add($"LOWER(aba.state) = LOWER('{EscapeSuiteQL(state)}')");

        return $"EXISTS (SELECT 1 FROM addressbookaddress aba WHERE {string.Join(" AND ", conditions)})";
    }
}
