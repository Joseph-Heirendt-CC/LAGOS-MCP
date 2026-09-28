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

    // Returns the rows of a SuiteQL result, or throws with NetSuite's error when the query failed
    // (ExecuteSuiteQLAsync returns the error body, which has no items array).
    public static JsonArray RequireItems(JsonNode result, string what) =>
        result["items"] as JsonArray
            ?? throw new InvalidOperationException($"{what} query failed: {result.ToJsonString()}");

    // Resolves display names (e.g. "Store Manager") to internal IDs in a NetSuite custom list.
    // Matching is case-insensitive; throws with the valid names if any value doesn't match.
    public static async Task<List<string>> ResolveListValueIds(
        INetSuiteBusinessAppClient client, string listScriptId, IEnumerable<string> names, string paramName, CancellationToken ct)
    {
        var result = await client.ExecuteSuiteQLAsync(
            $"SELECT id, name FROM {listScriptId} WHERE isinactive = 'F'", ct);
        var items = RequireItems(result, listScriptId).OfType<JsonObject>().ToList();

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

    // Builds an EXISTS clause matching city (partial) and/or state against the customer's default
    // shipping or default billing address (customeraddressbook → customeraddressbookentityaddress).
    // An EXISTS subquery (rather than a join) avoids duplicate rows when both addresses match.
    // City and state must match on the same address. Returns null when neither is supplied.
    public static string? AddressFilterClause(string customerAlias, string? city, string? state)
    {
        if (city == null && state == null) return null;

        var conditions = new List<string>
        {
            $"cab.entity = {customerAlias}.id",
            "(cab.defaultshipping = 'T' OR cab.defaultbilling = 'T')"
        };
        if (city  != null) conditions.Add($"LOWER(addr.city) LIKE LOWER('%{EscapeSuiteQL(city.Trim())}%')");
        if (state != null) conditions.Add($"UPPER(addr.state) = '{NormalizeState(state)}'");

        return "EXISTS (SELECT 1 FROM customeraddressbook cab " +
               "INNER JOIN customeraddressbookentityaddress addr ON addr.nkey = cab.addressbookaddress " +
               $"WHERE {string.Join(" AND ", conditions)})";
    }

    // Accepts a two-letter US state/territory abbreviation or its full name (case-insensitive)
    // and returns the abbreviation NetSuite stores on addresses.
    public static string NormalizeState(string state)
    {
        var s = state.Trim();
        if (s.Length == 2 && StateNames.ContainsValue(s.ToUpperInvariant())) return s.ToUpperInvariant();
        if (StateNames.TryGetValue(s, out var abbr)) return abbr;
        throw new ArgumentException($"'{state}' is not a recognized US state. Use a two-letter abbreviation (e.g. 'NC') or full name.");
    }

    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL", ["Alaska"] = "AK", ["Arizona"] = "AZ", ["Arkansas"] = "AR", ["California"] = "CA",
        ["Colorado"] = "CO", ["Connecticut"] = "CT", ["Delaware"] = "DE", ["District of Columbia"] = "DC",
        ["Florida"] = "FL", ["Georgia"] = "GA", ["Hawaii"] = "HI", ["Idaho"] = "ID", ["Illinois"] = "IL",
        ["Indiana"] = "IN", ["Iowa"] = "IA", ["Kansas"] = "KS", ["Kentucky"] = "KY", ["Louisiana"] = "LA",
        ["Maine"] = "ME", ["Maryland"] = "MD", ["Massachusetts"] = "MA", ["Michigan"] = "MI", ["Minnesota"] = "MN",
        ["Mississippi"] = "MS", ["Missouri"] = "MO", ["Montana"] = "MT", ["Nebraska"] = "NE", ["Nevada"] = "NV",
        ["New Hampshire"] = "NH", ["New Jersey"] = "NJ", ["New Mexico"] = "NM", ["New York"] = "NY",
        ["North Carolina"] = "NC", ["North Dakota"] = "ND", ["Ohio"] = "OH", ["Oklahoma"] = "OK", ["Oregon"] = "OR",
        ["Pennsylvania"] = "PA", ["Rhode Island"] = "RI", ["South Carolina"] = "SC", ["South Dakota"] = "SD",
        ["Tennessee"] = "TN", ["Texas"] = "TX", ["Utah"] = "UT", ["Vermont"] = "VT", ["Virginia"] = "VA",
        ["Washington"] = "WA", ["West Virginia"] = "WV", ["Wisconsin"] = "WI", ["Wyoming"] = "WY",
        ["Puerto Rico"] = "PR", ["Guam"] = "GU", ["U.S. Virgin Islands"] = "VI"
    };
}
