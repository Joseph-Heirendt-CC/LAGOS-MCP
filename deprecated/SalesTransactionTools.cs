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
            "Retrieve a full NetSuite sales order record by internal ID. " +
            "Use the optional 'fields' parameter (comma-separated) to limit the response payload and improve performance. " +
            "Key header fields: tranId, tranDate, dueDate, entity, status, memo, foreignTotal, currency, " +
            "terms, salesRep, shippingAddress, billingAddress, billingStatus, quantityFulfilled, quantityBilled. " +
            "Include 'item' in fields to retrieve the line-item sublist. " +
            "Status values: A=Pending Approval, B=Pending Fulfillment, D=Partially Fulfilled, E=Cancelled, G=Billed, H=Closed. " +
            "Example: recordId='55555' fields='tranId,tranDate,status,entity,foreignTotal,memo,item'")] ToolInvocationContext toolCall,
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
            "Returns id, tranid, trandate, duedate, customer, status, total, memo, quantitypicked, and billingstatus per row. " +
            "The 'filter' parameter is a raw SuiteQL WHERE clause applied after 't.type = ''SalesOrd'''. " +
            "Column names must be prefixed with 't.' for the transaction table. " +
            "Status codes (raw column value): A=Pending Approval, B=Pending Fulfillment, " +
            "D=Partially Fulfilled, E=Cancelled, G=Billed, H=Closed. " +
            "Filter examples: " +
            "Pending fulfillment: status = 'B' | " +
            "Since a date: trandate >= '01/01/2025' | " +
            "By customer ID: entity = 12345 | " +
            "Overdue open orders: status IN ('A','B','D') AND duedate < SYSDATE | " +
            "Partially fulfilled: status = 'D' | " +
            "Combined: status = 'B' AND trandate >= '01/01/2025'. " +
            "Default filter: voided = 'F'. Default limit: 50, max: 1000.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "voided = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT TOP {Math.Min(limit, 1000)}
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
            "The 'fields' parameter must be a JSON object. " +
            "Required: entity (customer ID object, e.g. { \"id\": \"12345\" }), " +
            "item sublist with an items array where each entry contains: " +
            "item { \"id\": \"itemId\" }, quantity (number), rate (number). " +
            "Optional header fields: tranDate (MM/DD/YYYY string), memo (string), " +
            "terms { \"id\": \"n\" }, salesRep { \"id\": \"n\" }, department { \"id\": \"n\" }, " +
            "shippingCost (number), shippingAddress (object). " +
            "Full example: { " +
            "\"entity\": { \"id\": \"12345\" }, " +
            "\"tranDate\": \"01/15/2025\", " +
            "\"memo\": \"Rush order\", " +
            "\"item\": { \"items\": [ " +
            "{ \"item\": { \"id\": \"100\" }, \"quantity\": 2, \"rate\": 50.00 }, " +
            "{ \"item\": { \"id\": \"200\" }, \"quantity\": 1, \"rate\": 120.00 } " +
            "] } }. " +
            "Returns the new sales order internal ID.")] ToolInvocationContext toolCall,
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
            "Patch an existing NetSuite sales order by internal ID. " +
            "The 'fields' parameter must contain only the fields to change — all omitted fields remain unchanged. " +
            "Editable header fields: memo (string), tranDate (MM/DD/YYYY), dueDate (MM/DD/YYYY), " +
            "terms { \"id\": \"n\" }, salesRep { \"id\": \"n\" }, department { \"id\": \"n\" }, shippingCost (number). " +
            "To replace line items include the full item sublist — partial line updates replace the entire sublist. " +
            "Example — update memo only: { \"memo\": \"Rush — ship by Friday\" }. " +
            "Example — replace all line items: { \"item\": { \"items\": [ { \"item\": { \"id\": \"100\" }, \"quantity\": 5, \"rate\": 45.00 } ] } }. " +
            "Note: Orders in status G (Billed) or H (Closed) cannot be edited.")] ToolInvocationContext toolCall,
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
            "Transform a NetSuite sales order into an item fulfillment record (marks items as shipped or picked). " +
            "Required: salesOrderId (the sales order internal ID as a string). " +
            "Optional: memo (string added to the fulfillment record). " +
            "Optional: lines array to fulfill specific quantities. " +
            "Each line entry: { \"orderLine\": lineNumber (integer, 1-based), \"quantity\": qty (number), \"itemReceive\": true }. " +
            "If lines are omitted all eligible unfulfilled lines are fully fulfilled. " +
            "The sales order must be in status B (Pending Fulfillment) or D (Partially Fulfilled). " +
            "Example — fulfill all lines: salesOrderId='55555'. " +
            "Example — partial fulfill line 1 only: salesOrderId='55555' memo='Partial ship' lines=[{ \"orderLine\": 1, \"quantity\": 3, \"itemReceive\": true }]. " +
            "Returns the newly created item fulfillment internal ID.")] ToolInvocationContext toolCall,
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
            "Transform a NetSuite sales order into an invoice (creates a billable invoice for the customer). " +
            "Required: salesOrderId (the sales order internal ID as a string). " +
            "Optional: memo (string added to the invoice). " +
            "Optional: tranDate (invoice date in MM/DD/YYYY format; defaults to today if omitted). " +
            "The sales order must be fulfilled or contain non-fulfillment items (e.g. services) before invoicing. " +
            "Orders still in status B (Pending Fulfillment) will be rejected unless all items are non-inventory. " +
            "Returns the newly created invoice record with its internal ID and invoice number. " +
            "Example: salesOrderId='55555' memo='Monthly billing' tranDate='01/31/2025'.")] ToolInvocationContext toolCall,
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
            "Retrieve a full NetSuite invoice record by internal ID. " +
            "Use the optional 'fields' parameter (comma-separated) to limit the response payload and improve performance. " +
            "Key fields: tranId (invoice number), tranDate, dueDate, entity (customer), " +
            "foreignTotal (invoice total), foreignAmountUnpaid (remaining balance), status, memo, " +
            "terms, currency, createdFrom (link to originating sales order). " +
            "Include 'item' in fields to retrieve line items. " +
            "Status values: Open, Paid In Full, Partially Paid. " +
            "Example: recordId='77777' fields='tranId,tranDate,dueDate,entity,foreignTotal,foreignAmountUnpaid,status'")] ToolInvocationContext toolCall,
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
            "Returns id, invoiceNumber, trandate, duedate, customer, total, status, and memo per row. " +
            "The 'filter' parameter is a raw SuiteQL WHERE clause applied after 't.type = ''CustInvc'''. " +
            "Column names must be prefixed with 't.' for the transaction table. " +
            "Filter examples: " +
            "All open (unpaid): amountremaining > 0 | " +
            "Overdue: amountremaining > 0 AND duedate < SYSDATE | " +
            "By customer ID: entity = 12345 | " +
            "This month: trandate >= '02/01/2025' AND trandate <= '02/28/2025' | " +
            "Large balances: amountremaining > 5000 | " +
            "Customer overdue: entity = 12345 AND amountremaining > 0 AND duedate < SYSDATE. " +
            "Default filter: voided = 'F'. Default limit: 50, max: 1000.")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var filter = toolCall.Arguments.GetOptionalString("filter") ?? "voided = 'F'";
        var limit = toolCall.Arguments.GetOptionalInt("limit") ?? 50;

        var query = $"""
            SELECT TOP {Math.Min(limit, 1000)}
                t.id, t.tranid AS invoiceNumber, t.trandate, t.duedate,
                BUILTIN.DF(t.entity) AS customer,
                t.foreigntotal AS total,
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
            "Patch an existing NetSuite invoice record by internal ID. " +
            "The 'fields' parameter must contain only the fields to change — all omitted fields remain unchanged. " +
            "Commonly editable fields: memo (string), dueDate (MM/DD/YYYY), terms { \"id\": \"n\" }. " +
            "Note: Financial fields (total, line items) cannot be edited on a posted invoice. " +
            "To correct financial details, void the invoice and re-generate it from the sales order. " +
            "Example — extend due date: { \"dueDate\": \"03/31/2025\" }. " +
            "Example — flag for review: { \"memo\": \"Payment disputed — on hold\" }.")] ToolInvocationContext toolCall,
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
