using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// MCP tool: execute_suiteql
/// Passes a raw SuiteQL query to the business app's netsuiteSuiteqlQuery endpoint.
/// This is the most flexible tool — the LLM can use it for any ad-hoc read,
/// reporting, filtering, or analytics that the structured CRUD tools don't cover.
/// </summary>
public class SuiteQLTools(INetSuiteBusinessAppClient client, ILogger<SuiteQLTools> logger)
{
    [Function(nameof(ExecuteSuiteQL))]
    public async Task<string> ExecuteSuiteQL(
        [McpToolTrigger("execute_suiteql",
            "Execute a SuiteQL SELECT query against NetSuite. " +
            "Use this for complex searches, reporting, cross-record joins, and analytics. " +
            "Returns a JSON object with 'items' (array of rows), 'totalResults', and 'hasMore'. " +
            "Example: SELECT id, companyname, email FROM customer WHERE isinactive = 'F'")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var query = toolCall.Arguments.GetRequiredString("query");

        logger.LogInformation("execute_suiteql: {Query}", query);

        var result = await client.ExecuteSuiteQLAsync(query, ct);
        return result.ToJsonString();
    }

    [Function(nameof(GetCustomerAging))]
    public async Task<string> GetCustomerAging(
        [McpToolTrigger("get_customer_aging",
            "Returns accounts receivable aging summary for all customers (or a specific customer) " +
            "with buckets: Current, 1-30, 31-60, 61-90, and 90+ days past due. " +
            "Optionally filter by customerId (NetSuite internal ID) or customerName (partial match).")] ToolInvocationContext toolCall,
        FunctionContext context,
        CancellationToken ct)
    {
        var customerId = toolCall.Arguments.GetOptionalString("customerId");
        var customerName = toolCall.Arguments.GetOptionalString("customerName");

        var whereClause = BuildAgingWhereClause(customerId, customerName);

        var agingQuery = $"""
            SELECT
                BUILTIN.DF(Transaction.Entity) AS Customer,
                SUM(CASE WHEN (TRUNC(SYSDATE) - Transaction.DueDate) < 1
                    THEN COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)
                    ELSE 0 END) AS Current_Amt,
                SUM(CASE WHEN (TRUNC(SYSDATE) - Transaction.DueDate) BETWEEN 1 AND 30
                    THEN COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)
                    ELSE 0 END) AS Days_1_30,
                SUM(CASE WHEN (TRUNC(SYSDATE) - Transaction.DueDate) BETWEEN 31 AND 60
                    THEN COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)
                    ELSE 0 END) AS Days_31_60,
                SUM(CASE WHEN (TRUNC(SYSDATE) - Transaction.DueDate) BETWEEN 61 AND 90
                    THEN COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)
                    ELSE 0 END) AS Days_61_90,
                SUM(CASE WHEN (TRUNC(SYSDATE) - Transaction.DueDate) > 90
                    THEN COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)
                    ELSE 0 END) AS Days_90_Plus,
                SUM(COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)) AS Total_Due
            FROM Transaction
            INNER JOIN TransactionAccountingLine TAL ON TAL.Transaction = Transaction.ID
            INNER JOIN Customer ON Customer.ID = Transaction.Entity
            WHERE Transaction.Posting = 'T'
              AND Transaction.Voided = 'F'
              AND (TAL.AmountUnpaid <> 0 OR TAL.PaymentAmountUnused <> 0)
              {whereClause}
            GROUP BY BUILTIN.DF(Transaction.Entity)
            HAVING SUM(COALESCE(TAL.AmountUnpaid, 0) - COALESCE(TAL.PaymentAmountUnused, 0)) <> 0
            ORDER BY BUILTIN.DF(Transaction.Entity)
            """;

        logger.LogInformation("get_customer_aging: customerId={CustomerId}, customerName={CustomerName}", customerId, customerName);

        var result = await client.ExecuteSuiteQLAsync(agingQuery, ct);
        return result.ToJsonString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildAgingWhereClause(string? customerId, string? customerName)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
            return $"AND Transaction.Entity = {customerId}";

        if (!string.IsNullOrWhiteSpace(customerName))
            return $"AND LOWER(BUILTIN.DF(Transaction.Entity)) LIKE LOWER('%{EscapeSuiteQL(customerName)}%')";

        return string.Empty;
    }

    /// <summary>Minimal escaping for SuiteQL string literals — prevents SQL injection via name filter.</summary>
    private static string EscapeSuiteQL(string value) => value.Replace("'", "''");
}
