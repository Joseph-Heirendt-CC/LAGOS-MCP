using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// MCP tools for Sales Orders and Invoices.
/// </summary>
public class SalesTransactionTools(INetSuiteBusinessAppClient client, ILogger<SalesTransactionTools> logger)
{
    // ── Sales Orders ──────────────────────────────────────────────────────────

    [Function(nameof(GetSalesOrder))]
    public async Task<string> GetSalesOrder(
        [McpToolTrigger("get_sales_order",
            "Retrieve a single NetSuite sales order by internal ID. " +
            "Optionally supply 'fields' (comma-separated) to limit the response payload.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var fields = toolCall.Arguments.GetOptionalString("fields");
        var extensions = fields is not null ? $"?fields={Uri.EscapeDataString(fields)}" : null;

        logger.LogInformation("get_sales_order: recordId={RecordId}", recordId);
        var result = await client.GetRecordAsync("salesOrder", recordId, extensions, ct);
        return result.ToJsonString();
    }

    [Function(nameof(ListSalesOrders))]
    public async Task<string> ListSalesOrders(
        [McpToolTrigger("list_sales_orders",
            "List NetSuite sales orders with optional SuiteQL filters. " +
            "Returns order ID, transaction number, customer, date, status, and total. " +
            "Status codes: A=Pending Approval, B=Pending Fulfillment, D=Partially Fulfilled, G=Billed, H=Closed. " +
            "Example filter: \"status = 'B' AND trandate >= '01/01/2025'\"")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "voided = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT TOP({Math.Min(limit, 1000)})
                t.id, t.tranid, t.trandate, t.duedate,
                BUILTIN.DF(t.entity) AS customer,
                BUILTIN.DF(t.status) AS status,
                t.foreigntotal AS total,
                t.memo, t.quantitypicked, BUILTIN.DF(t.billingstatus) AS billingstatus
            FROM transaction t
            WHERE t.type = 'SalesOrd' AND {filter}
            ORDER BY t.trandate DESC
            """;

        logger.LogInformation("list_sales_orders: filter={Filter}", filter);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    [Function(nameof(CreateSalesOrder))]
    public async Task<string> CreateSalesOrder(
        [McpToolTrigger("create_sales_order",
            "Create a new NetSuite sales order. " +
            "Required fields: entity (customer internal ID), item (sublist with items array). " +
            "Each line item needs: item (object with id), quantity, rate. " +
            "Optional: tranDate, memo, shippingAddress, terms, salesRep.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var body = toolCall.Arguments.GetRequiredObject("fields");

        logger.LogInformation("create_sales_order: entity={Entity}", body["entity"]?.ToJsonString());
        var result = await client.CreateRecordAsync("salesOrder", body, ct);
        return result.ToJsonString();
    }

    [Function(nameof(UpdateSalesOrder))]
    public async Task<string> UpdateSalesOrder(
        [McpToolTrigger("update_sales_order",
            "Update an existing NetSuite sales order by internal ID. " +
            "Supply only the fields to change. To replace line items include the full item sublist.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var body = toolCall.Arguments.GetRequiredObject("fields");

        logger.LogInformation("update_sales_order: recordId={RecordId}", recordId);
        var result = await client.UpdateRecordAsync("salesOrder", recordId, body, ct);
        return result.ToJsonString();
    }

    [Function(nameof(FulfillSalesOrder))]
    public async Task<string> FulfillSalesOrder(
        [McpToolTrigger("fulfill_sales_order",
            "Transform a NetSuite sales order into an item fulfillment (ship/pick the order). " +
            "Required: salesOrderId. Optional: lines (array of {orderLine, quantity, itemReceive}), memo. " +
            "If lines are omitted all eligible lines are fulfilled.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var salesOrderId = toolCall.Arguments.GetRequiredString("salesOrderId");
        var memo = toolCall.Arguments.GetOptionalString("memo");
        var lines = toolCall.Arguments.GetOptionalArray("lines");

        var requestBody = new JsonObject();
        if (memo is not null) requestBody["memo"] = memo;
        if (lines is not null) requestBody["item"] = new JsonObject { ["items"] = lines };

        logger.LogInformation("fulfill_sales_order: salesOrderId={Id}", salesOrderId);
        var result = await client.TransformRecordAsync("salesOrder", salesOrderId, "itemFulfillment", requestBody, ct);
        return result.ToJsonString();
    }

    [Function(nameof(InvoiceSalesOrder))]
    public async Task<string> InvoiceSalesOrder(
        [McpToolTrigger("invoice_sales_order",
            "Transform a NetSuite sales order into an invoice (bill the customer). " +
            "Required: salesOrderId. Optional: memo, tranDate.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var salesOrderId = toolCall.Arguments.GetRequiredString("salesOrderId");
        var memo = toolCall.Arguments.GetOptionalString("memo");
        var tranDate = toolCall.Arguments.GetOptionalString("tranDate");

        var requestBody = new JsonObject();
        if (memo is not null) requestBody["memo"] = memo;
        if (tranDate is not null) requestBody["tranDate"] = tranDate;

        logger.LogInformation("invoice_sales_order: salesOrderId={Id}", salesOrderId);
        var result = await client.TransformRecordAsync("salesOrder", salesOrderId, "invoice", requestBody, ct);
        return result.ToJsonString();
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    [Function(nameof(GetInvoice))]
    public async Task<string> GetInvoice(
        [McpToolTrigger("get_invoice",
            "Retrieve a single NetSuite invoice by internal ID. " +
            "Optionally supply 'fields' (comma-separated) to filter the response.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var fields = toolCall.Arguments.GetOptionalString("fields");
        var extensions = fields is not null ? $"?fields={Uri.EscapeDataString(fields)}" : null;

        logger.LogInformation("get_invoice: recordId={RecordId}", recordId);
        var result = await client.GetRecordAsync("invoice", recordId, extensions, ct);
        return result.ToJsonString();
    }

    [Function(nameof(ListInvoices))]
    public async Task<string> ListInvoices(
        [McpToolTrigger("list_invoices",
            "List NetSuite invoices with optional SuiteQL filters. " +
            "Returns invoice number, customer, date, due date, total, amount remaining, and status. " +
            "Example filter: \"amountremaining > 0 AND duedate < SYSDATE\"")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "voided = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT TOP({Math.Min(limit, 1000)})
                t.id, t.tranid AS invoiceNumber, t.trandate, t.duedate,
                BUILTIN.DF(t.entity) AS customer,
                t.foreigntotal AS total,
                t.foreignamountunpaid AS balanceDue,
                REPLACE(BUILTIN.DF(t.status), 'Invoice : ', '') AS status,
                t.memo
            FROM transaction t
            WHERE t.type = 'CustInvc' AND {filter}
            ORDER BY t.trandate DESC
            """;

        logger.LogInformation("list_invoices: filter={Filter}", filter);
        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    [Function(nameof(UpdateInvoice))]
    public async Task<string> UpdateInvoice(
        [McpToolTrigger("update_invoice",
            "Update an existing NetSuite invoice by internal ID. " +
            "Common editable fields: memo, dueDate, terms.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var recordId = toolCall.Arguments.GetRequiredString("recordId");
        var body = toolCall.Arguments.GetRequiredObject("fields");

        logger.LogInformation("update_invoice: recordId={RecordId}", recordId);
        var result = await client.UpdateRecordAsync("invoice", recordId, body, ct);
        return result.ToJsonString();
    }
}
