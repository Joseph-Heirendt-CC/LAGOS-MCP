using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

public class StoreVisitTools(INetSuiteBusinessAppClient client, ILogger<StoreVisitTools> logger)
{
    // Accepts any human-readable date and normalizes to yyyy-MM-dd for NetSuite.
    // Strips ordinal suffixes (1st, 2nd, 3rd, 4th) before parsing so inputs like
    // "June 2nd 2026" work alongside "6/2/26", "6/2", and ISO dates.
    private static string NormalizeDate(string input)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            input.Trim(), @"(\d+)(st|nd|rd|th)\b", "$1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (DateTime.TryParse(stripped, out var dt))
            return dt.ToString("yyyy-MM-dd");

        throw new ArgumentException(
            $"Could not parse '{input}' as a date. Try formats like '2026-06-02', '6/2/2026', or 'June 2 2026'.");
    }

    // ── create_store_visit ─────────────────────────────────────────────────────

    [Function(nameof(CreateStoreVisit))]
    public async Task<string> CreateStoreVisit(
        [McpToolTrigger("create_store_visit",
            "Creates a new Store Visit record in NetSuite as a skeleton for the visit.  " +
            "Call this at the start of a store visit before capturing checklist data. " +
            "Required parameters: " +
            "doorId (Customer internal ID from lookup_door), " +
            "brandAmbassadorId (employee internal ID of the visiting BA), " +
            "visitDate (any recognizable date format, e.g. 'June 2nd 2026', '6/2/26', '2026-06-02'), " +
            "name (title of this visit record — NOT the store or company name; the store is identified by doorId; e.g. 'Smith - Nordstrom Visit 6/11/2026'). " +
            "Returns the new record's id — pass this as recordId to update_store_visit.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId",            "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("brandAmbassadorId", "Employee internal ID of the visiting BA", true)] string brandAmbassadorId,
        [McpToolProperty("visitDate",         "Visit date in any recognizable format (e.g. 'June 2nd 2026', '6/2/26', '2026-06-02')", true)] string visitDate,
        [McpToolProperty("name",              "Title of this visit record — NOT the store or company name (e.g. 'Smith - Nordstrom Visit 6/11/2026'). The store is identified by doorId.", true)] string name,
        FunctionContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required and cannot be empty. Provide a visit record title such as 'Smith - Nordstrom Visit 6/11/2026', not the store or company name.");
        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");
        if (!long.TryParse(brandAmbassadorId, out _))
            throw new ArgumentException($"brandAmbassadorId must be a numeric NetSuite internal ID, got: '{brandAmbassadorId}'");

        var body = new JsonObject
        {
            ["name"]                              = name,
            ["custrecord_cca_sv_door"]            = new JsonObject { ["id"] = doorId },
            ["custrecord_cca_sv_brand_ambassador"] = new JsonObject { ["id"] = brandAmbassadorId },
            ["custrecord_cca_sv_visit_date"]       = NormalizeDate(visitDate)
        };

        logger.LogInformation("create_store_visit: name={Name} doorId={DoorId} baId={BaId} visitDate={VisitDate}",
            name, doorId, brandAmbassadorId, visitDate);

        var result = await client.CreateRecordAsync("customrecord_cca_store_visit", body, ct);
        return result.ToJsonString();
    }

    // ── update_store_visit ─────────────────────────────────────────────────────

    [Function(nameof(UpdateStoreVisit))]
    public async Task<string> UpdateStoreVisit(
        [McpToolTrigger("update_store_visit",
            "Updates an existing Store Visit record with checklist responses, issue flags, and summary fields. " +
            "Pass recordId (from create_store_visit or get_recent_store_visits) and a fields JSON object containing only the fields to update. " +
            "Boolean values must be 'T' (true) or 'F' (false). " +
            "\n\nAUDIT BOOLEANS ('T'/'F'):" +
            "\ncustrecord_cca_sv_backstock_inv_audited — Backstock inventory audit completed" +
            "\ncustrecord_cca_sv_price_audited — Price audit completed" +
            "\ncustrecord_cca_sv_pad_product_audit — Pad product audit completed" +
            "\ncustrecord_cca_sv_pres_elem_audit — Presentation elements audit completed" +
            "\ncustrecord_cca_sv_fixture_layout_audit — Fixture layout audit completed" +
            "\ncustrecord_cca_sv_market_material_audit — Marketing material audit completed" +
            "\ncustrecord_cca_sv_caseline_flow_reviewed — Caseline flow reviewed" +
            "\ncustrecord_cca_sv_tarnishing_check — Tarnishing check completed" +
            "\ncustrecord_cca_sv_vitrine_audited — Vitrine audited" +
            "\ncustrecord_cca_sv_sales_floor_rep_verif — Sales floor rep verified" +
            "\n\nISSUE FLAGS ('T'/'F'):" +
            "\ncustrecord_cca_sv_backstock_inv_issue — Backstock inventory issue identified" +
            "\ncustrecord_cca_sv_price_issue_id — Price issue identified" +
            "\ncustrecord_cca_sv_pad_prod_issue — Pad product issue identified" +
            "\ncustrecord_cca_sv_pres_elem_issue — Presentation elements issue identified" +
            "\ncustrecord_cca_sv_fixture_layout_issue — Fixture layout issue identified" +
            "\ncustrecord_cca_sv_mark_material_issue — Marketing material issue identified" +
            "\ncustrecord_cca_sv_caseline_flow_issue — Caseline flow issue identified" +
            "\ncustrecord_cca_sv_tarnish_issue — Tarnishing issue identified" +
            "\ncustrecord_cca_sv_vitrine_issue_identi — Vitrine issue identified" +
            "\ncustrecord_cca_sv_dsa_issue_idenified — DSA issue identified" +
            "\ncustrecord_cca_sv_mark_opp_identified — Marketing opportunity identified" +
            "\ncustrecord_cca_sv_qual_iss_id — Quality issue identified" +
            "\ncustrecord_cca_sv_prod_tags_issue — Product tags issue identified" +
            "\ncustrecord_cca_sv_prod_tag_tucked — Product tag tucked issue" +
            "\ncustrecord_cca_sv_training_needs_id — Training needs identified" +
            "\ncustrecord_cca_sv_space_location_moved — Space/location moved issue" +
            "\ncustrecord_cca_sv_incentive_running — Incentive currently running" +
            "\ncustrecord_cca_sv_store_aware_incentive — Store aware of incentive" +
            "\ncustrecord_cca_sv_competitor_incentives — Competitor incentives present" +
            "\n\nTEXT / NUMERIC FIELDS:" +
            "\ncustrecord_cca_sv_immediate_actions (string) — Immediate actions taken during visit" +
            "\ncustrecord_cca_sv_next_visit_focus (string) — Focus areas for next visit" +
            "\ncustrecord_cca_sv_caseline_space (number) — Caseline space count" +
            "\ncustrecord_cca_sv_gold_pads (number) — Gold pad count" +
            "\ncustrecord_cca_sv_numb_pads_mens (number) — Number of men's pads" +
            "\ncustrecord_cca_sv_numb_pads_women (number) — Number of women's pads" +
            "\ncustrecord_cca_sv_total_gold_pads (number) — Total gold pads" +
            "\ncustrecord_cca_sv_total_pads (number) — Total pads" +
            "\n\nExample: { \"custrecord_cca_sv_backstock_inv_audited\": \"T\", \"custrecord_cca_sv_backstock_inv_issue\": \"F\", \"custrecord_cca_sv_immediate_actions\": \"Replenish pad tray\" }")]
        ToolInvocationContext toolCall,
        [McpToolProperty("recordId", "Internal ID of the store visit record", true)] string recordId,
        [McpToolProperty("fields",   "Key-value pairs of fields to update", true)] string? fieldsJson,
        FunctionContext context,
        CancellationToken ct)
    {
        var fields = toolCall.GetRequiredObject("fields");

        logger.LogInformation("update_store_visit: recordId={RecordId}", recordId);
        var result = await client.UpdateRecordAsync("customrecord_cca_store_visit", recordId, fields, ct);
        return result.ToJsonString();
    }

    // ── get_recent_store_visits ────────────────────────────────────────────────

    [Function(nameof(GetRecentStoreVisits))]
    public async Task<string> GetRecentStoreVisits(
        [McpToolTrigger("get_recent_store_visits",
            "Retrieves the most recent Store Visit records for a Door, used to generate the Pre-Visit Summary. " +
            "Returns visit date, type, brand ambassador, immediate actions, next visit focus, total pads, and total gold pads per visit. " +
            "Use the returned id as recordId when calling update_store_visit on an existing record. " +
            "Requires doorId from lookup_door. Optional limit (default 5, max 50).")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId", "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("limit",  "Max records to return (default 5, max 50)")] int? limit,
        FunctionContext context,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Min(limit ?? 5, 50);

        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");

        var query = $"""
            SELECT TOP {effectiveLimit}
                sv.id,
                sv.name,
                sv.custrecord_cca_sv_visit_date,
                BUILTIN.DF(sv.custrecord_cca_sv_visit_type)          AS visitType,
                BUILTIN.DF(sv.custrecord_cca_sv_brand_ambassador)    AS brandAmbassador,
                sv.custrecord_cca_sv_immediate_actions,
                sv.custrecord_cca_sv_next_visit_focus,
                sv.custrecord_cca_sv_total_pads,
                sv.custrecord_cca_sv_total_gold_pads
            FROM customrecord_cca_store_visit sv
            WHERE sv.custrecord_cca_sv_door = {doorId}
            ORDER BY sv.custrecord_cca_sv_visit_date DESC
            """;

        logger.LogInformation("get_recent_store_visits: doorId={DoorId} limit={Limit}", doorId, effectiveLimit);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }
}
