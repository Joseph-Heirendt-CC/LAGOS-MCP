using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// MCP tools for Customer records.
/// Record type: "customer"
/// </summary>
public class CustomerTools(INetSuiteBusinessAppClient client, ILogger<CustomerTools> logger)
{
    [Function(nameof(GetCustomer))]
    public async Task<string> GetCustomer(
        [McpToolTrigger("get_customer",
            "Retrieve a full NetSuite customer record by internal ID or external ID. " +
            "For internal IDs supply the numeric string (e.g. '12345'). " +
            "For external IDs use the 'eid:' prefix (e.g. 'eid:CUST-001'). " +
            "Use the optional 'fields' parameter (comma-separated) to limit the response payload and improve performance. " +
            "Available fields include: entityId, companyName, firstName, lastName, email, phone, fax, " +
            "defaultAddress, terms, salesRep, subsidiary, currency, creditLimit, " +
            "aging, comments, isinactive, dateCreated, lastModifiedDate. " +
            "Example: recordId='12345' fields='entityId,companyName,email,phone,terms,creditLimit'")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var fields = toolCall.Arguments.GetOptionalString("fields");
        var extensions = fields is not null ? $"?fields={Uri.EscapeDataString(fields)}" : null;

        logger.LogInformation("get_customer: recordId={RecordId}", recordId);
        var result = await client.GetRecordAsync("customer", recordId, extensions, ct);
        return result.ToJsonString();
    }

    [Function(nameof(ListCustomers))]
    public async Task<string> ListCustomers(
        [McpToolTrigger("list_customers",
            "Search and list NetSuite customers using a SuiteQL WHERE clause filter. " +
            "Returns id, entityid, companyname, email, phone, terms, and isinactive per row. " +
            "The 'filter' parameter is a raw SuiteQL WHERE clause — do not include the keyword WHERE. " +
            "All SuiteQL column names are lowercase. String values require single quotes. " +
            "Supported operators: =, !=, LIKE, NOT LIKE, IN, IS NULL, IS NOT NULL, AND, OR. " +
            "Filter examples: " +
            "Active only: isinactive = 'F' | " +
            "By name: companyname LIKE 'Acme%' | " +
            "By email domain: email LIKE '%@example.com' | " +
            "By terms display value: BUILTIN.DF(terms) = 'Net 30' | " +
            "Combined: isinactive = 'F' AND companyname LIKE 'Smith%'. " +
            "Default filter: isinactive = 'F'. Default limit: 50, max: 1000.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "isinactive = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT TOP {Math.Min(limit, 1000)} id, entityid, companyname, email, phone, BUILTIN.DF(terms) AS terms, isinactive
            FROM customer
            WHERE {filter}
            ORDER BY entityid
            """;

        logger.LogInformation("list_customers: filter={Filter}", filter);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    [Function(nameof(CreateCustomer))]
    public async Task<string> CreateCustomer(
        [McpToolTrigger("create_customer",
            "Create a new NetSuite customer record. " +
            "The 'fields' parameter must be a JSON object. " +
            "Required for companies: companyName (string). " +
            "Required for individuals: isPerson=true, firstName (string), lastName (string). " +
            "Required always: subsidiary as an ID object, e.g. { \"id\": \"1\" }. " +
            "Optional: email (string), phone (string), entityId (customer code string), " +
            "terms { \"id\": \"n\" }, salesRep { \"id\": \"n\" }, comments (string), " +
            "addressbook sublist, custentity_* custom fields. " +
            "Company example: { \"companyName\": \"Acme Corp\", \"subsidiary\": { \"id\": \"1\" }, \"email\": \"info@acme.com\", \"phone\": \"555-1234\", \"terms\": { \"id\": \"2\" } }. " +
            "Individual example: { \"isPerson\": true, \"firstName\": \"Jane\", \"lastName\": \"Doe\", \"subsidiary\": { \"id\": \"1\" }, \"email\": \"jane@example.com\" }.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var body = toolCall.Arguments.GetRequiredObject("fields");

        logger.LogInformation("create_customer: companyName={Name}", body["companyname"]?.GetValue<string>());
        var result = await client.CreateRecordAsync("customer", body, ct);
        return result.ToJsonString();
    }

    [Function(nameof(UpdateCustomer))]
    public async Task<string> UpdateCustomer(
        [McpToolTrigger("update_customer",
            "Patch an existing NetSuite customer record by internal ID. " +
            "The 'fields' parameter must be a JSON object containing only the fields to change — all omitted fields remain unchanged. " +
            "Set a field value to null to clear it. " +
            "Editable fields: companyName (string), firstName (string), lastName (string), " +
            "email (string), phone (string), fax (string), comments (string), " +
            "terms { \"id\": \"n\" }, salesRep { \"id\": \"n\" }, creditLimit (number), isinactive (boolean). " +
            "Example — update contact info: { \"email\": \"new@example.com\", \"phone\": \"555-9999\" }. " +
            "Example — change payment terms: { \"terms\": { \"id\": \"3\" } }. " +
            "Example — deactivate customer: { \"isinactive\": true }. " +
            "Example — clear sales rep assignment: { \"salesRep\": null }.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var body = toolCall.Arguments.GetRequiredObject("fields");

        logger.LogInformation("update_customer: recordId={RecordId}", recordId);
        var result = await client.UpdateRecordAsync("customer", recordId, body, ct);
        return result.ToJsonString();
    }

    [Function(nameof(DeleteCustomer))]
    public async Task<string> DeleteCustomer(
        [McpToolTrigger("delete_customer",
            "Permanently delete a NetSuite customer record by internal ID. " +
            "WARNING: This action is irreversible and cannot be undone. " +
            "NetSuite will reject the deletion if the customer has associated transactions " +
            "(sales orders, invoices, payments, or deposits). " +
            "In most cases, prefer deactivating the customer via update_customer with isinactive=true instead of deleting. " +
            "Always confirm explicitly with the user before calling this tool.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");

        logger.LogInformation("delete_customer: recordId={RecordId}", recordId);
        var result = await client.DeleteRecordAsync("customer", recordId, ct);
        return result.ToJsonString();
    }
}
