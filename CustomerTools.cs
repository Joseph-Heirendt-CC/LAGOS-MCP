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
            "Retrieve a single NetSuite customer record by internal ID or external ID. " +
            "Use 'eid:EXTERNAL_ID' prefix for external IDs. " +
            "Optionally supply 'fields' (comma-separated) to limit the response, e.g. 'entityId,companyName,email,aging'.")] ToolInvocationContext toolCall,
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
            "Returns id, entityid, companyname, email, phone, terms, and isinactive. " +
            "Examples: filter='isinactive = ''F''' or filter='companyname LIKE ''Acme%'''.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "isinactive = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT id, entityid, companyname, email, phone, BUILTIN.DF(terms) AS terms, isinactive
            FROM customer
            WHERE {filter}
            ORDER BY entityid
            LIMIT {Math.Min(limit, 1000)}
            """;

        logger.LogInformation("list_customers: filter={Filter}", filter);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    [Function(nameof(CreateCustomer))]
    public async Task<string> CreateCustomer(
        [McpToolTrigger("create_customer",
            "Create a new NetSuite customer record. " +
            "Required: companyName (or firstName+lastName for individuals), subsidiary (internal ID). " +
            "Optional: email, phone, entityId, terms (internal ID), custentity_* custom fields.")] ToolInvocationContext toolCall,
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
            "Update an existing NetSuite customer record by internal ID. " +
            "Supply only the fields to change. Set a field to null to clear it. " +
            "Common fields: email, phone, comments, salesRep, terms, companyName.")] ToolInvocationContext toolCall,
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
            "Delete a NetSuite customer record by internal ID. " +
            "WARNING: This is irreversible. Confirm with the user before calling.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");

        logger.LogInformation("delete_customer: recordId={RecordId}", recordId);
        var result = await client.DeleteRecordAsync("customer", recordId, ct);
        return result.ToJsonString();
    }
}
