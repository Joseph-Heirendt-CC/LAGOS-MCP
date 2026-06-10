using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

public class DoorTools(INetSuiteBusinessAppClient client, ILogger<DoorTools> logger)
{
    private static string EscapeSuiteQL(string v) => v.Replace("'", "''");

    // ── lookup_door ────────────────────────────────────────────────────────────

    [Function(nameof(LookupDoor))]
    public async Task<string> LookupDoor(
        [McpToolTrigger("lookup_door",
            "Resolve a Door (retail account / company-type Customer) in NetSuite by one or more criteria. " +
            "Supply at least one of: baId (Brand Ambassador employee internal ID, numeric), name (partial company name match), " +
            "city (partial city match), or state (exact two-letter state abbreviation). " +
            "Filters to active Doors only (custentity_cca_door = T). " +
            "Returns up to 50 matches with: id, entityId, companyName, brandAmbassador, wbm, planner, subsidiary. " +
            "Use the returned id as doorId in all other door and store visit tools.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("name",  "Partial company name match")] string? name,
        [McpToolProperty("baId",  "Brand Ambassador employee internal ID (numeric NetSuite ID)")] string? baId,
        [McpToolProperty("city",  "Partial city match")] string? city,
        [McpToolProperty("state", "Exact two-letter state abbreviation")] string? state,
        FunctionContext context,
        CancellationToken ct)
    {

        if (baId == null && name == null && city == null && state == null)
            throw new ArgumentException("At least one of baId, name, city, or state is required.");

        if (baId != null && !long.TryParse(baId, out _))
            throw new ArgumentException($"baId must be a numeric NetSuite internal ID, got: '{baId}'");

        var clauses = new List<string>
        {
            "c.custentity_cca_door = 'T'",
            "c.isinactive = 'F'"
        };

        if (baId  != null) clauses.Add($"c.custentity_cca_ba_assignment = {baId}");
        if (name  != null) clauses.Add($"LOWER(c.companyname) LIKE LOWER('%{EscapeSuiteQL(name)}%')");
        if (city  != null) clauses.Add($"LOWER(aba.city) LIKE LOWER('%{EscapeSuiteQL(city)}%')");
        if (state != null) clauses.Add($"LOWER(aba.state) = LOWER('{EscapeSuiteQL(state)}')");

        var where = string.Join("\n  AND ", clauses);

        // Address join only needed when city/state filtering is requested
        var addressJoin = (city != null || state != null)
            ? "LEFT JOIN addressbookaddress aba ON aba.entity = c.id AND aba.defaultbilling = 'T'"
            : string.Empty;

        var query = $"""
            SELECT TOP 50
                c.id,
                c.entityid,
                c.companyname,
                BUILTIN.DF(c.custentity_cca_ba_assignment) AS brandAmbassador,
                BUILTIN.DF(c.custentity_cca_wbm)           AS wbm,
                BUILTIN.DF(c.custentity_cca_planner)       AS planner,
                BUILTIN.DF(c.subsidiary)                   AS subsidiary
            FROM customer c
            {addressJoin}
            WHERE {where}
            ORDER BY c.companyname
            """;

        logger.LogInformation("lookup_door: baId={BaId} name={Name} city={City} state={State}", baId, name, city, state);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    // ── get_door_contacts ──────────────────────────────────────────────────────

    [Function(nameof(GetDoorContacts))]
    public async Task<string> GetDoorContacts(
        [McpToolTrigger("get_door_contacts",
            "Returns all active Contacts linked to a Door (retail account). " +
            "Surfaces each contact's name, email, phone, title, and role (e.g. Sales Associate, Store Manager, Department Manager). " +
            "Requires doorId — the Customer internal ID returned by lookup_door.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId", "Customer internal ID from lookup_door", true)] string doorId,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");

        var query = $"""
            SELECT TOP 100
                c.id,
                c.firstname,
                c.lastname,
                c.email,
                c.phone,
                c.title,
                BUILTIN.DF(cc.contactrole) AS role
            FROM contact c
            JOIN customercontact cc ON cc.contact = c.id
            WHERE cc.entity = {doorId}
              AND c.isinactive = 'F'
            ORDER BY c.lastname, c.firstname
            """;

        logger.LogInformation("get_door_contacts: doorId={DoorId}", doorId);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    // ── get_open_tasks_for_door ────────────────────────────────────────────────

    [Function(nameof(GetOpenTasksForDoor))]
    public async Task<string> GetOpenTasksForDoor(
        [McpToolTrigger("get_open_tasks_for_door",
            "Returns open escalation Tasks associated with a Door. " +
            "Filters to not-started and in-progress tasks only (excludes COMPLETE). " +
            "Returns task id, title, status, priority, start date, due date, assigned employee, and message body. " +
            "Requires doorId from lookup_door. Optional limit (default 25, max 100).")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId", "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("limit",  "Max records to return (default 25, max 100)")] int? limit,
        FunctionContext context,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Min(limit ?? 25, 100);

        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");

        var query = $"""
            SELECT TOP {effectiveLimit}
                t.id,
                t.title,
                t.status,
                t.priority,
                t.startdate,
                t.duedate,
                BUILTIN.DF(t.assigned) AS assignedTo,
                t.message
            FROM task t
            WHERE t.company = {doorId}
              AND t.status != 'COMPLETE'
            ORDER BY t.duedate ASC
            """;

        logger.LogInformation("get_open_tasks_for_door: doorId={DoorId} limit={Limit}", doorId, effectiveLimit);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    // ── get_event_and_training_history ─────────────────────────────────────────

    [Function(nameof(GetEventAndTrainingHistory))]
    public async Task<string> GetEventAndTrainingHistory(
        [McpToolTrigger("get_event_and_training_history",
            "Retrieves Event (Project/Job) records and Training Recap records associated with a Door to support pre-visit preparation. " +
            "Returns two separate arrays: events and trainingRecaps. " +
            "Optional filters: startDate and endDate (MM/DD/YYYY format). Optional limit (default 20 each, max 50). " +
            "Requires doorId from lookup_door.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId",    "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("startDate", "Start date filter in MM/DD/YYYY format")] string? startDate,
        [McpToolProperty("endDate",   "End date filter in MM/DD/YYYY format")] string? endDate,
        [McpToolProperty("limit",     "Max records per type (default 20, max 50)")] int? limit,
        FunctionContext context,
        CancellationToken ct)
    {
        var effectiveLimit = Math.Min(limit ?? 20, 50);

        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");

        var dateClauses = new List<string>();
        if (startDate != null) dateClauses.Add($"j.startdate >= '{EscapeSuiteQL(startDate)}'");
        if (endDate   != null) dateClauses.Add($"j.enddate <= '{EscapeSuiteQL(endDate)}'");
        var dateWhere = dateClauses.Count > 0 ? " AND " + string.Join(" AND ", dateClauses) : string.Empty;

        var eventQuery = $"""
            SELECT TOP {effectiveLimit}
                j.id,
                j.jobname AS title,
                j.startdate,
                j.enddate,
                BUILTIN.DF(j.status) AS status,
                j.memo
            FROM job j
            WHERE j.customer = {doorId}
            {dateWhere}
            ORDER BY j.startdate DESC
            """;

        var trainingDateClauses = new List<string>();
        if (startDate != null) trainingDateClauses.Add($"tr.custrecord_cca_tr_date >= '{EscapeSuiteQL(startDate)}'");
        if (endDate   != null) trainingDateClauses.Add($"tr.custrecord_cca_tr_date <= '{EscapeSuiteQL(endDate)}'");
        var trainingDateWhere = trainingDateClauses.Count > 0 ? " AND " + string.Join(" AND ", trainingDateClauses) : string.Empty;

        var trainingQuery = $"""
            SELECT TOP {effectiveLimit}
                tr.id,
                tr.name AS title,
                tr.custrecord_cca_tr_date       AS trainingDate,
                BUILTIN.DF(tr.custrecord_cca_tr_ba) AS brandAmbassador,
                tr.custrecord_cca_tr_notes      AS notes
            FROM customrecord_cca_training_recap tr
            WHERE tr.custrecord_cca_tr_door = {doorId}
            {trainingDateWhere}
            ORDER BY tr.custrecord_cca_tr_date DESC
            """;

        logger.LogInformation("get_event_and_training_history: doorId={DoorId} startDate={StartDate} endDate={EndDate}", doorId, startDate, endDate);

        // Run both queries; training recap record type is pending schema confirmation — fail gracefully
        var eventResult = await client.ExecuteSuiteQLAsync(eventQuery, ct);
        var events = eventResult["items"] as JsonArray ?? new JsonArray();

        JsonArray trainingRecaps;
        string? trainingWarning = null;
        try
        {
            var trainingResult = await client.ExecuteSuiteQLAsync(trainingQuery, ct);
            trainingRecaps = trainingResult["items"] as JsonArray ?? new JsonArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_event_and_training_history: training recap query failed — record type may need verification");
            trainingRecaps = new JsonArray();
            trainingWarning = "Training Recap query failed — customrecord_cca_training_recap record type needs verification.";
        }

        var response = new JsonObject
        {
            ["events"]            = events.DeepClone(),
            ["trainingRecaps"]    = trainingRecaps.DeepClone(),
            ["totalEvents"]       = events.Count,
            ["totalTrainingRecaps"] = trainingRecaps.Count
        };
        if (trainingWarning != null)
            response["warning"] = trainingWarning;

        return response.ToJsonString();
    }
}
