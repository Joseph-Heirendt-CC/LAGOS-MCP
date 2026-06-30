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
            "Returns up to 100 matches with: id, entityId, companyName, brandAmbassador, wbm, planner, subsidiary. " +
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

        if (baId  != null) clauses.Add($"c.custentity_cca_brand_ambassador = {baId}");
        if (name  != null) clauses.Add($"LOWER(c.companyname) LIKE LOWER('%{EscapeSuiteQL(name)}%')");
        if (city  != null) clauses.Add($"LOWER(aba.city) LIKE LOWER('%{EscapeSuiteQL(city)}%')");
        if (state != null) clauses.Add($"LOWER(aba.state) = LOWER('{EscapeSuiteQL(state)}')");

        var where = string.Join("\n  AND ", clauses);

        // Address join only needed when city/state filtering is requested
        var addressJoin = (city != null || state != null)
            ? "LEFT JOIN addressbookaddress aba ON aba.entity = c.id AND aba.defaultbilling = 'T'"
            : string.Empty;

        var query = $"""
            SELECT TOP 100
                c.id,
                c.entityid,
                c.companyname,
                BUILTIN.DF(c.custentity_cca_brand_ambassador) AS brandAmbassador,
                BUILTIN.DF(c.salesrep)           AS wholesaleBrandManager,
                BUILTIN.DF(c.custentity_cca_planner)       AS planner,
                BUILTIN.DF(c.subsidiary)                   AS subsidiary,
                c.custentity_cca_visit_hours,
                c.custentity_cca_door_number,
                c.custentity_cca_sharepoint_url,
                dt.name AS door_type,
                t.name  AS territory
            FROM customer c
            {addressJoin}
            LEFT OUTER JOIN customlist_cca_door_type dt
                ON dt.id = c.custentity_cca_door_type
            LEFT OUTER JOIN customlist_cca_ba_territory t
                ON t.id = c.custentity_cca_territory
            WHERE {where}
            ORDER BY c.companyname
            """;

        logger.LogInformation("lookup_door: baId={BaId} name={Name} city={City} state={State}", baId, name, city, state);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    // ── lookup_door_for_ui_selection ──────────────────────────────────────────

    [Function(nameof(LookupDoorForUiSelection))]
    public async Task<string> LookupDoorForUiSelection(
        [McpToolTrigger("lookup_door_for_ui_selection",
            "Search for active Doors (retail accounts) and return results formatted for a UI choice picker. " +
            "Supply at least one of: baId, name, city, or state. " +
            "Returns { \"choices\": [{\"title\": \"<company name>\", \"value\": \"<id>\"}] } — " +
            "suitable for Copilot Studio adaptive card choice sets. " +
            "Use the value (id) as doorId in create_store_visit and other door tools.")]
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

        if (baId  != null) clauses.Add($"c.custentity_cca_brand_ambassador = {baId}");
        if (name  != null) clauses.Add($"LOWER(c.companyname) LIKE LOWER('%{EscapeSuiteQL(name)}%')");
        if (city  != null) clauses.Add($"LOWER(aba.city) LIKE LOWER('%{EscapeSuiteQL(city)}%')");
        if (state != null) clauses.Add($"LOWER(aba.state) = LOWER('{EscapeSuiteQL(state)}')");

        var where = string.Join("\n  AND ", clauses);

        var addressJoin = (city != null || state != null)
            ? "LEFT JOIN addressbookaddress aba ON aba.entity = c.id AND aba.defaultbilling = 'T'"
            : string.Empty;

        var query = $"""
            SELECT TOP 100
                c.id,
                c.companyname
            FROM customer c
            {addressJoin}
            WHERE {where}
            ORDER BY c.companyname
            """;

        logger.LogInformation("lookup_door_for_ui_selection: baId={BaId} name={Name} city={City} state={State}", baId, name, city, state);

        var result  = await client.ExecuteSuiteQLAsync(query, ct);
        var items   = result["items"] as JsonArray ?? new JsonArray();
        var choices = new JsonArray();

        foreach (var item in items)
        {
            choices.Add(new JsonObject
            {
                ["title"] = item?["companyname"]?.GetValue<string>(),
                ["value"] = item?["id"]?.ToString()
            });
        }

        return new JsonObject { ["choices"] = choices }.ToJsonString();
    }

    // ── lookup_brand_ambassador ───────────────────────────────────────────────

    [Function(nameof(LookupBrandAmbassador))]
    public async Task<string> LookupBrandAmbassador(
        [McpToolTrigger("lookup_brand_ambassador",
            "Resolve a Brand Ambassador (NetSuite employee) by name or email to obtain their internal ID. " +
            "Supply at least one of: name (partial first or last name match) or email (partial email match). " +
            "Filters to active employees only. " +
            "Returns up to 25 matches with: id, entityId, firstName, lastName, email. " +
            "Use the returned id as brandAmbassadorId in create_store_visit.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("name",  "Partial first or last name match")] string? name,
        [McpToolProperty("email", "Partial email address match")] string? email,
        FunctionContext context,
        CancellationToken ct)
    {
        if (name == null && email == null)
            throw new ArgumentException("At least one of name or email is required.");

        var clauses = new List<string> { "e.isinactive = 'F'", "e.custentity_cca_is_brand_ambassador = 'T'" };

        if (name  != null) clauses.Add($"(LOWER(e.firstname) LIKE LOWER('%{EscapeSuiteQL(name)}%') OR LOWER(e.lastname) LIKE LOWER('%{EscapeSuiteQL(name)}%'))");
        if (email != null) clauses.Add($"LOWER(e.email) LIKE LOWER('%{EscapeSuiteQL(email)}%')");

        var where = string.Join("\n  AND ", clauses);

        var query = $"""
            SELECT TOP 25
                e.id,
                e.entityid,
                e.firstname,
                e.lastname,
                e.email,
                el.name AS ba_territory
            FROM employee e
            LEFT OUTER JOIN customlist_cca_ba_territory el
                ON el.id = e.custentity_cca_ba_territory
            WHERE {where}
            ORDER BY e.lastname, e.firstname
            """;

        logger.LogInformation("lookup_brand_ambassador: name={Name} email={Email}", name, email);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    // ── lookup_project ────────────────────────────────────────────────────────

    [Function(nameof(LookupProject))]
    public async Task<string> LookupProject(
        [McpToolTrigger("lookup_project",
            "Find NetSuite Project (Job) records linked to a Brand Ambassador or a Door. " +
            "Supply at least one of: baId (Brand Ambassador employee internal ID) or doorId (Customer internal ID from lookup_door). " +
            "Returns up to 50 matches with: id, entityId, status, projectName, customerName.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("baId",   "Brand Ambassador employee internal ID (numeric NetSuite ID)")] string? baId,
        [McpToolProperty("doorId", "Customer (Door) internal ID from lookup_door (numeric NetSuite ID)")] string? doorId,
        FunctionContext context,
        CancellationToken ct)
    {
        if (baId == null && doorId == null)
            throw new ArgumentException("At least one of baId or doorId is required.");

        if (baId   != null && !long.TryParse(baId,   out _))
            throw new ArgumentException($"baId must be a numeric NetSuite internal ID, got: '{baId}'");
        if (doorId != null && !long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");

        var clauses = new List<string>();
        if (baId   != null) clauses.Add($"j.custentity_cca_brand_ambassador = {baId}");
        if (doorId != null) clauses.Add($"j.custentity_cca_project_door = {doorId}");

        var where = string.Join("\n  AND ", clauses);

        var query = $"""
            SELECT TOP 50
                j.id,
                j.entityid,
                BUILTIN.DF(j.entitystatus) AS status,
                j.jobname                  AS projectName,
                BUILTIN.DF(j.parent)       AS customerName,
                pm.entityid                AS project_manager,
                ba.entityid                AS brand_ambassador,
                j.custentity_cca_special_requests
            FROM job j
            LEFT OUTER JOIN employee pm
                ON pm.id = j.projectmanager
            LEFT OUTER JOIN employee ba
                ON ba.id = j.custentity_cca_brand_ambassador
            WHERE {where}
            ORDER BY j.jobname
            """;

        logger.LogInformation("lookup_project: baId={BaId} doorId={DoorId}", baId, doorId);
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
                con.id,
                con.firstname,
                con.lastname,
                con.email,
                con.phone,
                con.title,
                ct.name AS contact_type
            FROM contact con
            LEFT OUTER JOIN customlist_cca_contact_type_list ct
                ON ct.id = con.custentity_cca_contact_type
            WHERE con.company = {doorId}
              AND con.isinactive = 'F'
            ORDER BY con.lastname, con.firstname
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

    // ── get_tasks_for_store_visits ─────────────────────────────────────────────

    [Function(nameof(GetTasksForStoreVisits))]
    public async Task<string> GetTasksForStoreVisits(
        [McpToolTrigger("get_tasks_for_store_visits",
            "Returns Tasks linked to one or more recent Store Visits, so the BA can review unresolved escalations " +
            "and action items from prior visits before or during a new visit. " +
            "Pass 1 to 3 Store Visit internal IDs (visitId1 required; visitId2/visitId3 optional) from get_recent_store_visits — " +
            "the BA selects how many recent visits' worth of tasks to surface. " +
            "Returns task id, title, assigned employee, priority, status (resolved to a readable label), start date, " +
            "completed date, related store visit name, escalation type, and door.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("visitId1", "Store Visit internal ID — most recent visit, from get_recent_store_visits", true)] string visitId1,
        [McpToolProperty("visitId2", "Store Visit internal ID — 2nd most recent visit (optional)")] string? visitId2,
        [McpToolProperty("visitId3", "Store Visit internal ID — 3rd most recent visit (optional)")] string? visitId3,
        FunctionContext context,
        CancellationToken ct)
    {
        var visitIds = new List<string> { visitId1 };
        if (visitId2 != null) visitIds.Add(visitId2);
        if (visitId3 != null) visitIds.Add(visitId3);

        foreach (var id in visitIds)
            if (!long.TryParse(id, out _))
                throw new ArgumentException($"Store Visit IDs must be numeric NetSuite internal IDs, got: '{id}'");

        var inClause = string.Join(", ", visitIds);

        var query = $"""
            SELECT
                t.id,
                t.title,
                e.entityid                 AS assigned_to,
                t.priority,
                CASE t.status
                    WHEN 'COMPLETE' THEN 'Completed'
                    WHEN 'PROGRESS' THEN 'In Progress'
                    WHEN 'NOTSTART' THEN 'Not Started'
                END                         AS status,
                t.startdate,
                t.completeddate,
                sv.name                     AS related_store_visit,
                et.name                     AS escalation_type,
                c.companyname               AS door
            FROM task t
            LEFT OUTER JOIN employee e
                ON e.id = t.assigned
            LEFT OUTER JOIN customrecord_cca_store_visit sv
                ON sv.id = t.custevent_cca_related_store_visit
            LEFT OUTER JOIN customlist_cca_sv_escal_type et
                ON et.id = t.custevent_cca_sv_escal_type
            LEFT OUTER JOIN customer c
                ON c.id = t.custevent_cca_door
            WHERE t.custevent_cca_related_store_visit IN ({inClause})
            """;

        logger.LogInformation("get_tasks_for_store_visits: visitIds={VisitIds}", inClause);
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
