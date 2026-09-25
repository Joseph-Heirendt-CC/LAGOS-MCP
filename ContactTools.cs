using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using static ToolHelpers;

public class ContactTools(INetSuiteBusinessAppClient client, ILogger<ContactTools> logger)
{
    private const string ContactTypeList = "customlist_cca_contact_type_list";

    // TODO: confirm script ID of the "Area of Responsibility List" custom list in NetSuite.
    private const string AreaOfRespList = "customlist_cca_area_of_resp";

    private const string AreaOfRespDescription =
        "Comma-separated Area of Responsibility names (from get_area_of_responsibility_options), replacing any existing " +
        "selection (empty string clears). An unrecognized name returns the list of valid values.";

    private async Task<JsonObject> ContactTypeRef(string contactType, CancellationToken ct) =>
        new() { ["id"] = (await ResolveListValueIds(client, ContactTypeList, [contactType], "contactType", ct))[0] };

    // ── get_area_of_responsibility_options ─────────────────────────────────────

    [Function(nameof(GetAreaOfResponsibilityOptions))]
    public async Task<string> GetAreaOfResponsibilityOptions(
        [McpToolTrigger("get_area_of_responsibility_options",
            "Returns the valid Area of Responsibility values for setting on a Contact, as id/name pairs. " +
            "Call before create_contact or update_contact to present the options; pass the chosen names as areaOfResponsibility.")]
        ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        logger.LogInformation("get_area_of_responsibility_options");
        var result = await client.ExecuteSuiteQLAsync(
            $"SELECT id, name FROM {AreaOfRespList} WHERE isinactive = 'F' ORDER BY id", ct);
        return result.ToJsonString();
    }

    // ── create_contact ─────────────────────────────────────────────────────────

    [Function(nameof(CreateContact))]
    public async Task<string> CreateContact(
        [McpToolTrigger("create_contact",
            "Creates a new Contact linked to a Door (retail account). " +
            "Required: doorId (Customer internal ID from lookup_door), firstName, lastName. " +
            "Optional: email, phone, title (job title), contactType (e.g. Sales Associate, Store Manager, Department Manager), " +
            "areaOfResponsibility (comma-separated names). " +
            "The contact's subsidiary is set automatically from the Door. " +
            "If an active contact with the same email already exists on the Door, no record is created — use update_contact instead. " +
            "Returns the new contact's id.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("doorId",      "Customer internal ID from lookup_door", true)] string doorId,
        [McpToolProperty("firstName",   "Contact first name", true)] string firstName,
        [McpToolProperty("lastName",    "Contact last name", true)] string lastName,
        [McpToolProperty("email",       "Contact email address")] string? email,
        [McpToolProperty("phone",       "Main phone number")] string? phone,
        [McpToolProperty("title",       "Job title")] string? title,
        [McpToolProperty("contactType", "Contact role, e.g. Sales Associate, Store Manager, Department Manager")] string? contactType,
        [McpToolProperty("areaOfResponsibility", AreaOfRespDescription)] string? areaOfResponsibility,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!long.TryParse(doorId, out _))
            throw new ArgumentException($"doorId must be a numeric NetSuite internal ID, got: '{doorId}'");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("firstName and lastName are required and cannot be empty.");

        // Source the subsidiary from the Door — NetSuite rejects a contact whose subsidiary
        // doesn't match its company's.
        var doorResult = await client.ExecuteSuiteQLAsync(
            $"SELECT c.id, c.subsidiary FROM customer c WHERE c.id = {doorId} AND c.custentity_cca_door = 'T'", ct);
        var door = (doorResult["items"] as JsonArray)?.FirstOrDefault()
            ?? throw new ArgumentException($"No Door found with id '{doorId}'. Use lookup_door to find a valid doorId.");
        var subsidiaryId = door["subsidiary"]?.ToString();

        if (!string.IsNullOrWhiteSpace(email))
        {
            var dupResult = await client.ExecuteSuiteQLAsync($"""
                SELECT con.id, con.firstname, con.lastname
                FROM contact con
                WHERE con.company = {doorId}
                  AND con.isinactive = 'F'
                  AND LOWER(con.email) = LOWER('{EscapeSuiteQL(email.Trim())}')
                """, ct);
            var existing = (dupResult["items"] as JsonArray)?.FirstOrDefault();
            if (existing != null)
                throw new ArgumentException(
                    $"An active contact with email '{email}' already exists on this Door " +
                    $"(id {existing["id"]}, {existing["firstname"]} {existing["lastname"]}). Use update_contact instead.");
        }

        var body = new JsonObject
        {
            ["firstName"] = firstName.Trim(),
            ["lastName"]  = lastName.Trim(),
            ["company"]   = new JsonObject { ["id"] = doorId }
        };
        if (!string.IsNullOrWhiteSpace(subsidiaryId)) body["subsidiary"] = new JsonObject { ["id"] = subsidiaryId };
        if (email  != null) body["email"] = email.Trim();
        if (phone  != null) body["phone"] = phone;
        if (title  != null) body["title"] = title;
        if (contactType != null)
            body["custentity_cca_contact_type"] = await ContactTypeRef(contactType, ct);
        if (areaOfResponsibility != null)
            body["custentity_cca_area_of_resp"] = await BuildMultiSelect(client, AreaOfRespList, areaOfResponsibility, "areaOfResponsibility", ct);

        logger.LogInformation("create_contact: doorId={DoorId} name={First} {Last}", doorId, firstName, lastName);
        var result = await client.CreateRecordAsync("contact", body, ct);
        return result.ToJsonString();
    }

    // ── update_contact ─────────────────────────────────────────────────────────

    [Function(nameof(UpdateContact))]
    public async Task<string> UpdateContact(
        [McpToolTrigger("update_contact",
            "Updates an existing Contact. Pass contactId (from get_door_contacts or create_contact) and only the fields you want to change. " +
            "Set isInactive to true to deactivate a contact who has left the store. " +
            "areaOfResponsibility replaces the contact's full selection — include existing values (from get_door_contacts) to keep them. " +
            "Boolean fields must be strict boolean: true or false.")]
        ToolInvocationContext toolCall,
        [McpToolProperty("contactId",   "Contact internal ID from get_door_contacts or create_contact", true)] string contactId,
        [McpToolProperty("firstName",   "Contact first name")] string? firstName,
        [McpToolProperty("lastName",    "Contact last name")] string? lastName,
        [McpToolProperty("email",       "Contact email address")] string? email,
        [McpToolProperty("phone",       "Main phone number")] string? phone,
        [McpToolProperty("title",       "Job title")] string? title,
        [McpToolProperty("contactType", "Contact role, e.g. Sales Associate, Store Manager, Department Manager")] string? contactType,
        [McpToolProperty("areaOfResponsibility", AreaOfRespDescription)] string? areaOfResponsibility,
        [McpToolProperty("isInactive",  "Deactivate (true) or reactivate (false) the contact")] string? isInactive,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!long.TryParse(contactId, out _))
            throw new ArgumentException($"contactId must be a numeric NetSuite internal ID, got: '{contactId}'");

        var body = new JsonObject();
        if (firstName != null) body["firstName"] = firstName.Trim();
        if (lastName  != null) body["lastName"]  = lastName.Trim();
        if (email     != null) body["email"]     = email.Trim();
        if (phone     != null) body["phone"]     = phone;
        if (title     != null) body["title"]     = title;
        if (contactType != null)
            body["custentity_cca_contact_type"] = await ContactTypeRef(contactType, ct);
        if (areaOfResponsibility != null)
            body["custentity_cca_area_of_resp"] = await BuildMultiSelect(client, AreaOfRespList, areaOfResponsibility, "areaOfResponsibility", ct);
        AddBool(body, "isInactive", isInactive);

        if (body.Count == 0)
            throw new ArgumentException("At least one field must be provided to update.");

        logger.LogInformation("update_contact: contactId={ContactId} fieldCount={Count}", contactId, body.Count);
        var result = await client.UpdateRecordAsync("contact", contactId, body, ct);
        return result.ToJsonString();
    }
}
