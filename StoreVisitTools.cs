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

    // Validates and adds a 'T'/'F' boolean field to the update body.
    private static void AddTF(JsonObject o, string key, string? v)
    {
        if (v == null) return;
        if (v != "T" && v != "F")
            throw new ArgumentException($"'{key}' must be 'T' or 'F', got: '{v}'");
        o[key] = v;
    }

    [Function(nameof(UpdateStoreVisit))]
    public async Task<string> UpdateStoreVisit(
        [McpToolTrigger("update_store_visit",
            "Updates an existing Store Visit record with checklist responses, issue flags, and summary fields. " +
            "Pass recordId and only the fields you want to update. All non-recordId parameters are optional. " +
            "Boolean fields accept 'T' (true) or 'F' (false) only.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("recordId",                          "Internal ID of the store visit record (from create_store_visit or get_recent_store_visits)", true)] string recordId,
        // ── Audit booleans ─────────────────────────────────────────────────────
        [McpToolProperty("backstockInvAudited",               "Backstock inventory audit completed ('T'/'F')")] string? backstockInvAudited,
        [McpToolProperty("priceAudited",                      "Price audit completed ('T'/'F')")] string? priceAudited,
        [McpToolProperty("padProductAudit",                   "Pad product audit completed ('T'/'F')")] string? padProductAudit,
        [McpToolProperty("presElemAudit",                     "Presentation elements audit completed ('T'/'F')")] string? presElemAudit,
        [McpToolProperty("fixtureLayoutAudit",                "Fixture layout audit completed ('T'/'F')")] string? fixtureLayoutAudit,
        [McpToolProperty("marketMaterialAudit",               "Marketing material audit completed ('T'/'F')")] string? marketMaterialAudit,
        [McpToolProperty("caselineFlowReviewed",              "Caseline flow reviewed ('T'/'F')")] string? caselineFlowReviewed,
        [McpToolProperty("tarnishingCheck",                   "Tarnishing check completed ('T'/'F')")] string? tarnishingCheck,
        [McpToolProperty("vitrineAudited",                    "Vitrine audited ('T'/'F')")] string? vitrineAudited,
        [McpToolProperty("salesFloorRepVerified",             "Sales floor rep verified ('T'/'F')")] string? salesFloorRepVerified,
        // ── Issue flags ────────────────────────────────────────────────────────
        [McpToolProperty("backstockInvIssue",                 "Backstock inventory issue identified ('T'/'F')")] string? backstockInvIssue,
        [McpToolProperty("priceIssue",                        "Price issue identified ('T'/'F')")] string? priceIssue,
        [McpToolProperty("padProdIssue",                      "Pad product issue identified ('T'/'F')")] string? padProdIssue,
        [McpToolProperty("presElemIssue",                     "Presentation elements issue identified ('T'/'F')")] string? presElemIssue,
        [McpToolProperty("fixtureLayoutIssue",                "Fixture layout issue identified ('T'/'F')")] string? fixtureLayoutIssue,
        [McpToolProperty("markMaterialIssue",                 "Marketing material issue identified ('T'/'F')")] string? markMaterialIssue,
        [McpToolProperty("caselineFlowIssue",                 "Caseline flow issue identified ('T'/'F')")] string? caselineFlowIssue,
        [McpToolProperty("tarnishIssue",                      "Tarnishing issue identified ('T'/'F')")] string? tarnishIssue,
        [McpToolProperty("vitrineIssue",                      "Vitrine issue identified ('T'/'F')")] string? vitrineIssue,
        [McpToolProperty("dsaIssue",                          "DSA issue identified ('T'/'F')")] string? dsaIssue,
        [McpToolProperty("markOppIdentified",                 "Marketing opportunity identified ('T'/'F')")] string? markOppIdentified,
        [McpToolProperty("qualIssue",                         "Quality issue identified ('T'/'F')")] string? qualIssue,
        [McpToolProperty("prodTagsIssue",                     "Product tags issue identified ('T'/'F')")] string? prodTagsIssue,
        [McpToolProperty("prodTagTucked",                     "Product tag tucked issue ('T'/'F')")] string? prodTagTucked,
        [McpToolProperty("trainingNeedsIdentified",           "Training needs identified ('T'/'F')")] string? trainingNeedsIdentified,
        [McpToolProperty("spaceLocationMoved",                "Space/location moved issue ('T'/'F')")] string? spaceLocationMoved,
        [McpToolProperty("incentiveRunning",                  "Incentive currently running ('T'/'F')")] string? incentiveRunning,
        [McpToolProperty("storeAwareIncentive",               "Store aware of incentive ('T'/'F')")] string? storeAwareIncentive,
        [McpToolProperty("competitorIncentives",              "Competitor incentives present ('T'/'F')")] string? competitorIncentives,
        // ── Text fields ────────────────────────────────────────────────────────
        [McpToolProperty("immediateActions",                  "Immediate actions taken during the visit (free text)")] string? immediateActions,
        [McpToolProperty("nextVisitFocus",                    "Focus areas for the next visit (free text)")] string? nextVisitFocus,
        // ── Numeric fields ─────────────────────────────────────────────────────
        [McpToolProperty("caselineSpace",                     "Caseline space count")] int? caselineSpace,
        [McpToolProperty("goldPads",                          "Gold pad count")] int? goldPads,
        [McpToolProperty("numbPadsMens",                      "Number of men's pads")] int? numbPadsMens,
        [McpToolProperty("numbPadsWomen",                     "Number of women's pads")] int? numbPadsWomen,
        [McpToolProperty("totalGoldPads",                     "Total gold pads")] int? totalGoldPads,
        [McpToolProperty("totalPads",                         "Total pads")] int? totalPads,
        FunctionContext context,
        CancellationToken ct)
    {
        var body = new JsonObject();

        // Audit booleans
        AddTF(body, "custrecord_cca_sv_backstock_inv_audited",  backstockInvAudited);
        AddTF(body, "custrecord_cca_sv_price_audited",          priceAudited);
        AddTF(body, "custrecord_cca_sv_pad_product_audit",      padProductAudit);
        AddTF(body, "custrecord_cca_sv_pres_elem_audit",        presElemAudit);
        AddTF(body, "custrecord_cca_sv_fixture_layout_audit",   fixtureLayoutAudit);
        AddTF(body, "custrecord_cca_sv_market_material_audit",  marketMaterialAudit);
        AddTF(body, "custrecord_cca_sv_caseline_flow_reviewed", caselineFlowReviewed);
        AddTF(body, "custrecord_cca_sv_tarnishing_check",       tarnishingCheck);
        AddTF(body, "custrecord_cca_sv_vitrine_audited",        vitrineAudited);
        AddTF(body, "custrecord_cca_sv_sales_floor_rep_verif",  salesFloorRepVerified);

        // Issue flags
        AddTF(body, "custrecord_cca_sv_backstock_inv_issue",    backstockInvIssue);
        AddTF(body, "custrecord_cca_sv_price_issue_id",         priceIssue);
        AddTF(body, "custrecord_cca_sv_pad_prod_issue",         padProdIssue);
        AddTF(body, "custrecord_cca_sv_pres_elem_issue",        presElemIssue);
        AddTF(body, "custrecord_cca_sv_fixture_layout_issue",   fixtureLayoutIssue);
        AddTF(body, "custrecord_cca_sv_mark_material_issue",    markMaterialIssue);
        AddTF(body, "custrecord_cca_sv_caseline_flow_issue",    caselineFlowIssue);
        AddTF(body, "custrecord_cca_sv_tarnish_issue",          tarnishIssue);
        AddTF(body, "custrecord_cca_sv_vitrine_issue_identi",   vitrineIssue);
        AddTF(body, "custrecord_cca_sv_dsa_issue_idenified",    dsaIssue);
        AddTF(body, "custrecord_cca_sv_mark_opp_identified",    markOppIdentified);
        AddTF(body, "custrecord_cca_sv_qual_iss_id",            qualIssue);
        AddTF(body, "custrecord_cca_sv_prod_tags_issue",        prodTagsIssue);
        AddTF(body, "custrecord_cca_sv_prod_tag_tucked",        prodTagTucked);
        AddTF(body, "custrecord_cca_sv_training_needs_id",      trainingNeedsIdentified);
        AddTF(body, "custrecord_cca_sv_space_location_moved",   spaceLocationMoved);
        AddTF(body, "custrecord_cca_sv_incentive_running",      incentiveRunning);
        AddTF(body, "custrecord_cca_sv_store_aware_incentive",  storeAwareIncentive);
        AddTF(body, "custrecord_cca_sv_competitor_incentives",  competitorIncentives);

        // Text fields
        if (immediateActions != null) body["custrecord_cca_sv_immediate_actions"] = immediateActions;
        if (nextVisitFocus   != null) body["custrecord_cca_sv_next_visit_focus"]   = nextVisitFocus;

        // Numeric fields
        if (caselineSpace  != null) body["custrecord_cca_sv_caseline_space"]  = caselineSpace;
        if (goldPads       != null) body["custrecord_cca_sv_gold_pads"]       = goldPads;
        if (numbPadsMens   != null) body["custrecord_cca_sv_numb_pads_mens"]  = numbPadsMens;
        if (numbPadsWomen  != null) body["custrecord_cca_sv_numb_pads_women"] = numbPadsWomen;
        if (totalGoldPads  != null) body["custrecord_cca_sv_total_gold_pads"] = totalGoldPads;
        if (totalPads      != null) body["custrecord_cca_sv_total_pads"]      = totalPads;

        if (body.Count == 0)
            throw new ArgumentException("At least one field must be provided to update.");

        logger.LogInformation("update_store_visit: recordId={RecordId} fieldCount={Count}", recordId, body.Count);
        var result = await client.UpdateRecordAsync("customrecord_cca_store_visit", recordId, body, ct);
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
