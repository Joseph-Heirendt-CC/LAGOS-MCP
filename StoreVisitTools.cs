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
            "name (title of this visit record — Door name concatenated with '-Visit ' and the visit date, e.g. 'Charlotte-5-Visit 6/30/2026'; the store is identified by doorId). " +
            "Returns the new record's id — pass this as recordId to update_store_visit.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId",            "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("brandAmbassadorId", "Employee internal ID of the visiting BA", true)] string brandAmbassadorId,
        [McpToolProperty("visitDate",         "Visit date in any recognizable format (e.g. 'June 2nd 2026', '6/2/26', '2026-06-02')", true)] string visitDate,
        [McpToolProperty("name",              "Title of this visit record — Door name concatenated with '-Visit ' and the visit date (e.g. 'Charlotte-5-Visit 6/30/2026'). The store is identified by doorId.", true)] string name,
        FunctionContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required and cannot be empty. Provide a visit record title such as 'Charlotte-5-Visit 6/30/2026'.");
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

    // Parses a boolean string and writes a strict JSON boolean (true/false) to the update body.
    // Accepts "true"/"false" (canonical) and "T"/"F" (legacy), case-insensitive.
    private static void AddBool(JsonObject o, string key, string? v)
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

    [Function(nameof(UpdateStoreVisit))]
    public async Task<string> UpdateStoreVisit(
        [McpToolTrigger("update_store_visit",
            "Updates an existing Store Visit record with checklist responses, issue flags, notes, corrective actions, and summary fields. " +
            "Pass recordId and only the fields you want to update. All non-recordId parameters are optional. " +
            "Boolean fields must be strict boolean: true or false (no quotes, no 'T'/'F').")]
        ToolInvocationContext toolCall,
        [McpToolProperty("recordId",                          "Internal ID of the store visit record (from create_store_visit or get_recent_store_visits)", true)] string recordId,
        // ── Audit booleans ─────────────────────────────────────────────────────
        [McpToolProperty("backstockInvAudited",               "Backstock inventory audit completed (true/false)")] string? backstockInvAudited,
        [McpToolProperty("priceAudited",                      "Price audit completed (true/false)")] string? priceAudited,
        [McpToolProperty("padProductAudit",                   "Pad product audit completed (true/false)")] string? padProductAudit,
        [McpToolProperty("presElemAudit",                     "Presentation elements audit completed (true/false)")] string? presElemAudit,
        [McpToolProperty("fixtureLayoutAudit",                "Fixture layout audit completed (true/false)")] string? fixtureLayoutAudit,
        [McpToolProperty("marketMaterialAudit",               "Marketing material audit completed (true/false)")] string? marketMaterialAudit,
        [McpToolProperty("caselineFlowReviewed",              "Caseline flow reviewed (true/false)")] string? caselineFlowReviewed,
        [McpToolProperty("tarnishingCheck",                   "Tarnishing check completed (true/false)")] string? tarnishingCheck,
        [McpToolProperty("vitrineAudited",                    "Vitrine audited (true/false)")] string? vitrineAudited,
        [McpToolProperty("salesFloorRepVerified",             "Sales floor rep verified (true/false)")] string? salesFloorRepVerified,
        // ── Issue flags ────────────────────────────────────────────────────────
        [McpToolProperty("backstockInvIssue",                 "Backstock inventory issue identified (true/false)")] string? backstockInvIssue,
        [McpToolProperty("priceIssue",                        "Price issue identified (true/false)")] string? priceIssue,
        [McpToolProperty("padProdIssue",                      "Pad product issue identified (true/false)")] string? padProdIssue,
        [McpToolProperty("presElemIssue",                     "Presentation elements issue identified (true/false)")] string? presElemIssue,
        [McpToolProperty("fixtureLayoutIssue",                "Fixture layout issue identified (true/false)")] string? fixtureLayoutIssue,
        [McpToolProperty("markMaterialIssue",                 "Marketing material issue identified (true/false)")] string? markMaterialIssue,
        [McpToolProperty("caselineFlowIssue",                 "Caseline flow issue identified (true/false)")] string? caselineFlowIssue,
        [McpToolProperty("tarnishIssue",                      "Tarnishing issue identified (true/false)")] string? tarnishIssue,
        [McpToolProperty("vitrineIssue",                      "Vitrine issue identified (true/false)")] string? vitrineIssue,
        [McpToolProperty("dsaIssue",                          "DSA issue identified (true/false)")] string? dsaIssue,
        [McpToolProperty("markOppIdentified",                 "Marketing opportunity identified (true/false)")] string? markOppIdentified,
        [McpToolProperty("qualIssue",                         "Quality issue identified (true/false)")] string? qualIssue,
        [McpToolProperty("prodTagsIssue",                     "Product tags issue identified (true/false)")] string? prodTagsIssue,
        [McpToolProperty("prodTagTucked",                     "Product tag tucked issue (true/false)")] string? prodTagTucked,
        [McpToolProperty("trainingNeedsIdentified",           "Training needs identified (true/false)")] string? trainingNeedsIdentified,
        [McpToolProperty("spaceLocationMoved",                "Space/location moved issue (true/false)")] string? spaceLocationMoved,
        [McpToolProperty("incentiveRunning",                  "Incentive currently running (true/false)")] string? incentiveRunning,
        [McpToolProperty("storeAwareIncentive",               "Store aware of incentive (true/false)")] string? storeAwareIncentive,
        [McpToolProperty("competitorIncentives",              "Competitor incentives present (true/false)")] string? competitorIncentives,
        // ── Text fields ────────────────────────────────────────────────────────
        [McpToolProperty("immediateActions",                  "Immediate actions taken during the visit (free text)")] string? immediateActions,
        [McpToolProperty("nextVisitFocus",                    "Focus areas for the next visit (free text)")] string? nextVisitFocus,
        [McpToolProperty("visitSummary",                      "Overall visit summary (free text)")] string? visitSummary,
        [McpToolProperty("sharepointUrl",                     "SharePoint folder URL for this visit's documents (populated by the LLM when the SharePoint folder is created in M365)")] string? sharepointUrl,
        [McpToolProperty("presElemNotes",                     "Presentation elements notes (free text)")] string? presElemNotes,
        [McpToolProperty("additionalContacts",                "Additional store contacts (free text)")] string? additionalContacts,
        [McpToolProperty("backstockInvCorrectiveActions",     "Backstock inventory corrective actions (free text)")] string? backstockInvCorrectiveActions,
        [McpToolProperty("caselineFlowCorrectiveActions",     "Caseline flow corrective actions (free text)")] string? caselineFlowCorrectiveActions,
        [McpToolProperty("caselineFlowNotes",                 "Caseline flow notes (free text)")] string? caselineFlowNotes,
        [McpToolProperty("compVisMerchNotes",                 "Competitor visual merchandising notes (free text)")] string? compVisMerchNotes,
        [McpToolProperty("fixtureLayoutCorrectiveActions",    "Fixture layout corrective actions (free text)")] string? fixtureLayoutCorrectiveActions,
        [McpToolProperty("fixtureLayoutNotes",                "Fixture layout notes (free text)")] string? fixtureLayoutNotes,
        [McpToolProperty("invProdRepNotes",                   "Inventory and product representation notes (free text)")] string? invProdRepNotes,
        [McpToolProperty("marketMaterialCorrectiveActions",   "Marketing material corrective actions (free text)")] string? marketMaterialCorrectiveActions,
        [McpToolProperty("marketMaterialNotes",                "Marketing material notes (free text)")] string? marketMaterialNotes,
        [McpToolProperty("marketingOpportunityNotes",         "Marketing opportunity notes (free text)")] string? marketingOpportunityNotes,
        [McpToolProperty("padProductCorrectiveActions",       "Pad product corrective actions (free text)")] string? padProductCorrectiveActions,
        [McpToolProperty("padProductNotes",                   "Pad product notes (free text)")] string? padProductNotes,
        [McpToolProperty("presElemCorrectiveActions",         "Presentation elements corrective actions (free text)")] string? presElemCorrectiveActions,
        [McpToolProperty("priceIssueCorrectiveActions",       "Price issue corrective actions (free text)")] string? priceIssueCorrectiveActions,
        [McpToolProperty("priceNotes",                        "Price notes (free text)")] string? priceNotes,
        [McpToolProperty("productTagCorrectiveActions",       "Product tag corrective actions (free text)")] string? productTagCorrectiveActions,
        [McpToolProperty("productTagNotes",                   "Product tag notes (free text)")] string? productTagNotes,
        [McpToolProperty("qualityCorrectiveActions",          "Quality corrective actions (free text)")] string? qualityCorrectiveActions,
        [McpToolProperty("qualityNotes",                      "Quality notes (free text)")] string? qualityNotes,
        [McpToolProperty("spaceLocationNotes",                "Space/location notes (free text)")] string? spaceLocationNotes,
        [McpToolProperty("tarnishCorrectiveActions",          "Tarnishing corrective actions (free text)")] string? tarnishCorrectiveActions,
        [McpToolProperty("tarnishingNotes",                   "Tarnishing notes (free text)")] string? tarnishingNotes,
        [McpToolProperty("trainingNeedsNotes",                "Training needs notes (free text)")] string? trainingNeedsNotes,
        [McpToolProperty("vitrineCorrectiveActions",          "Vitrine corrective actions (free text)")] string? vitrineCorrectiveActions,
        [McpToolProperty("vitrineNotes",                      "Vitrine notes (free text)")] string? vitrineNotes,
        [McpToolProperty("dsaCorrectiveActions",              "DSA corrective actions (free text)")] string? dsaCorrectiveActions,
        [McpToolProperty("dsaNotes",                          "DSA notes (free text)")] string? dsaNotes,
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
        AddBool(body, "custrecord_cca_sv_backstock_inv_audited",  backstockInvAudited);
        AddBool(body, "custrecord_cca_sv_price_audited",          priceAudited);
        AddBool(body, "custrecord_cca_sv_pad_product_audit",      padProductAudit);
        AddBool(body, "custrecord_cca_sv_pres_elem_audit",        presElemAudit);
        AddBool(body, "custrecord_cca_sv_fixture_layout_audit",   fixtureLayoutAudit);
        AddBool(body, "custrecord_cca_sv_market_material_audit",  marketMaterialAudit);
        AddBool(body, "custrecord_cca_sv_caseline_flow_reviewed", caselineFlowReviewed);
        AddBool(body, "custrecord_cca_sv_tarnishing_check",       tarnishingCheck);
        AddBool(body, "custrecord_cca_sv_vitrine_audited",        vitrineAudited);
        AddBool(body, "custrecord_cca_sv_sales_floor_rep_verif",  salesFloorRepVerified);

        // Issue flags
        AddBool(body, "custrecord_cca_sv_backstock_inv_issue",    backstockInvIssue);
        AddBool(body, "custrecord_cca_sv_price_issue_id",         priceIssue);
        AddBool(body, "custrecord_cca_sv_pad_prod_issue",         padProdIssue);
        AddBool(body, "custrecord_cca_sv_pres_elem_issue",        presElemIssue);
        AddBool(body, "custrecord_cca_sv_fixture_layout_issue",   fixtureLayoutIssue);
        AddBool(body, "custrecord_cca_sv_mark_material_issue",    markMaterialIssue);
        AddBool(body, "custrecord_cca_sv_caseline_flow_issue",    caselineFlowIssue);
        AddBool(body, "custrecord_cca_sv_tarnish_issue",          tarnishIssue);
        AddBool(body, "custrecord_cca_sv_vitrine_issue_identi",   vitrineIssue);
        AddBool(body, "custrecord_cca_sv_dsa_issue_idenified",    dsaIssue);
        AddBool(body, "custrecord_cca_sv_mark_opp_identified",    markOppIdentified);
        AddBool(body, "custrecord_cca_sv_qual_iss_id",            qualIssue);
        AddBool(body, "custrecord_cca_sv_prod_tags_issue",        prodTagsIssue);
        AddBool(body, "custrecord_cca_sv_prod_tag_tucked",        prodTagTucked);
        AddBool(body, "custrecord_cca_sv_training_needs_id",      trainingNeedsIdentified);
        AddBool(body, "custrecord_cca_sv_space_location_moved",   spaceLocationMoved);
        AddBool(body, "custrecord_cca_sv_incentive_running",      incentiveRunning);
        AddBool(body, "custrecord_cca_sv_store_aware_incentive",  storeAwareIncentive);
        AddBool(body, "custrecord_cca_sv_competitor_incentives",  competitorIncentives);

        // Text fields
        if (immediateActions != null) body["custrecord_cca_sv_immediate_actions"] = immediateActions;
        if (nextVisitFocus   != null) body["custrecord_cca_sv_next_visit_focus"]   = nextVisitFocus;
        if (visitSummary     != null) body["custrecord_cca_sv_visit_summary"]      = visitSummary;
        if (sharepointUrl    != null) body["custrecord_cca_sv_sharepoint_url"]     = sharepointUrl;
        if (presElemNotes                   != null) body["custrecord_cca_pres_elem_notes"]            = presElemNotes;
        if (additionalContacts              != null) body["custrecord_cca_sv_additional_contacts"]     = additionalContacts;
        if (backstockInvCorrectiveActions   != null) body["custrecord_cca_sv_backstock_inv_corr_act"]  = backstockInvCorrectiveActions;
        if (caselineFlowCorrectiveActions   != null) body["custrecord_cca_sv_case_flow_correct_act"]   = caselineFlowCorrectiveActions;
        if (caselineFlowNotes               != null) body["custrecord_cca_sv_caseline_flow_notes"]     = caselineFlowNotes;
        if (compVisMerchNotes               != null) body["custrecord_cca_sv_comp_vis_merch_notes"]    = compVisMerchNotes;
        if (fixtureLayoutCorrectiveActions  != null) body["custrecord_cca_sv_fix_layout_correct_act"]  = fixtureLayoutCorrectiveActions;
        if (fixtureLayoutNotes              != null) body["custrecord_cca_sv_fixture_layout_notes"]    = fixtureLayoutNotes;
        if (invProdRepNotes                 != null) body["custrecord_cca_sv_inv_prod_rep_notes"]      = invProdRepNotes;
        if (marketMaterialCorrectiveActions != null) body["custrecord_cca_sv_mark_mater_correct_act"]  = marketMaterialCorrectiveActions;
        if (marketMaterialNotes             != null) body["custrecord_cca_sv_mark_material_notes"]     = marketMaterialNotes;
        if (marketingOpportunityNotes       != null) body["custrecord_cca_sv_mark_opp_notes"]          = marketingOpportunityNotes;
        if (padProductCorrectiveActions     != null) body["custrecord_cca_sv_pad_prod_correct_act"]    = padProductCorrectiveActions;
        if (padProductNotes                 != null) body["custrecord_cca_sv_pad_product_notes"]       = padProductNotes;
        if (presElemCorrectiveActions       != null) body["custrecord_cca_sv_pres_elem_correct_act"]   = presElemCorrectiveActions;
        if (priceIssueCorrectiveActions     != null) body["custrecord_cca_sv_price_issue_corr_act"]    = priceIssueCorrectiveActions;
        if (priceNotes                      != null) body["custrecord_cca_sv_price_notes"]             = priceNotes;
        if (productTagCorrectiveActions     != null) body["custrecord_cca_sv_prod_tag_correct_act"]    = productTagCorrectiveActions;
        if (productTagNotes                 != null) body["custrecord_cca_sv_prod_tag_notes"]          = productTagNotes;
        if (qualityCorrectiveActions        != null) body["custrecord_cca_sv_quality_correct_act"]     = qualityCorrectiveActions;
        if (qualityNotes                    != null) body["custrecord_cca_sv_quality_notes"]           = qualityNotes;
        if (spaceLocationNotes              != null) body["custrecord_cca_sv_space_location_notes"]    = spaceLocationNotes;
        if (tarnishCorrectiveActions        != null) body["custrecord_cca_sv_tarnish_correct_act"]     = tarnishCorrectiveActions;
        if (tarnishingNotes                 != null) body["custrecord_cca_sv_tarnishing_notes"]        = tarnishingNotes;
        if (trainingNeedsNotes              != null) body["custrecord_cca_sv_training_needs_notes"]    = trainingNeedsNotes;
        if (vitrineCorrectiveActions        != null) body["custrecord_cca_sv_vitrine_correct_act"]     = vitrineCorrectiveActions;
        if (vitrineNotes                    != null) body["custrecord_cca_sv_vitrine_notes"]           = vitrineNotes;
        if (dsaCorrectiveActions            != null) body["custrecord_cca_sv_dsa_correct_act"]         = dsaCorrectiveActions;
        if (dsaNotes                        != null) body["custrecord_cca_sv_dsa_notes"]               = dsaNotes;

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
            "Returns visit date, type, brand ambassador, project, visit summary, immediate actions, next visit focus, " +
            "sharepoint URL, total pads, total gold pads, and corrective-action notes across all issue categories per visit. " +
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
                sv.lastmodified,
                sv.created,
                sv.custrecord_cca_sv_visit_date,
                BUILTIN.DF(sv.custrecord_cca_sv_visit_type)          AS visitType,
                BUILTIN.DF(sv.custrecord_cca_sv_brand_ambassador)    AS brandAmbassador,
                sv.custrecord_cca_sv_project,
                sv.custrecord_cca_sv_visit_summary,
                sv.custrecord_cca_sv_immediate_actions,
                sv.custrecord_cca_sv_next_visit_focus,
                sv.custrecord_cca_sv_sharepoint_url,
                sv.custrecord_cca_sv_total_pads,
                sv.custrecord_cca_sv_total_gold_pads,
                sv.custrecord_cca_sv_backstock_inv_corr_act,
                sv.custrecord_cca_sv_pad_prod_correct_act,
                sv.custrecord_cca_sv_fix_layout_correct_act,
                sv.custrecord_cca_sv_case_flow_correct_act,
                sv.custrecord_cca_sv_pres_elem_correct_act,
                sv.custrecord_cca_sv_tarnish_correct_act,
                sv.custrecord_cca_sv_prod_tag_correct_act,
                sv.custrecord_cca_sv_quality_correct_act,
                sv.custrecord_cca_sv_mark_mater_correct_act,
                sv.custrecord_cca_sv_vitrine_correct_act,
                sv.custrecord_cca_sv_dsa_correct_act,
                sv.custrecord_cca_pres_elem_notes,
                sv.custrecord_cca_sv_additional_contacts,
                sv.custrecord_cca_sv_caseline_flow_notes,
                sv.custrecord_cca_sv_comp_vis_merch_notes,
                sv.custrecord_cca_sv_fixture_layout_notes,
                sv.custrecord_cca_sv_inv_prod_rep_notes,
                sv.custrecord_cca_sv_mark_material_notes,
                sv.custrecord_cca_sv_mark_opp_notes,
                sv.custrecord_cca_sv_pad_product_notes,
                sv.custrecord_cca_sv_price_notes,
                sv.custrecord_cca_sv_price_issue_corr_act,
                sv.custrecord_cca_sv_prod_tag_notes,
                sv.custrecord_cca_sv_quality_notes,
                sv.custrecord_cca_sv_space_location_notes,
                sv.custrecord_cca_sv_tarnishing_notes,
                sv.custrecord_cca_sv_training_needs_notes,
                sv.custrecord_cca_sv_vitrine_notes
            FROM customrecord_cca_store_visit sv
            WHERE sv.custrecord_cca_sv_door = {doorId}
            ORDER BY sv.custrecord_cca_sv_visit_date DESC
            """;

        logger.LogInformation("get_recent_store_visits: doorId={DoorId} limit={Limit}", doorId, effectiveLimit);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }
}
