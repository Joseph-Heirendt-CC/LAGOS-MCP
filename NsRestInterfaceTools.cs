using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace NS_MCP.Function;

public class NsRestInterfaceTools
{
    private readonly ILogger _logger;
    private readonly HttpClient _http;

    private static readonly string AZURE_FUNCTIONS_KEY = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_KEY") ?? "taGSZsPQqaJdc3EXuOiYO1AwGezCbRWk63jd6cD39NdEAzFuacvr2w==";
    private static readonly string BASE_URL = Environment.GetEnvironmentVariable("AZURE_FUNCTION_APP_URL") ?? "https://nsrestinterface-dev.azurewebsites.net";

    public NsRestInterfaceTools(ILogger<executeSuiteQL> logger)
    {
        _logger = loggerFactory.CreateLogger<NsRestInterfaceTools>();
        _http = new HttpClient();
    }

    [Function(nameof(ExecuteSuiteQL))]
	public async Task<string> ExecuteSuiteQL(
    	[McpToolTrigger(
        	"executeSuiteQL",
        	"Execute a NetSuite SuiteQL query via nsrestinterface-dev using the netsuiteSuiteqlQuery endpoint.")]
    	ToolInvocationContext context)
	{
    	// -----------------------------
    	// 1) Parse MCP tool arguments
    	// -----------------------------
    	var argsJson = context?.ArgumentsJson ?? "{}";
    	SuiteQlArgs args;

    	try
    	{
        	args = JsonSerializer.Deserialize<SuiteQlArgs>(argsJson, JsonOptions.Default)
               	?? throw new InvalidOperationException("Tool arguments were empty or invalid.");
    	}
    	catch (Exception ex)
    	{
        	_logger.LogWarning(ex, "Invalid MCP tool arguments JSON.");
        	return McpError("InvalidArguments",
            	"Arguments must be JSON: { \"query\": \"...\", \"limit\": <int?>, \"offset\": <int?> }");
    	}

    	if (string.IsNullOrWhiteSpace(args.Query))
        	return McpError("InvalidArguments", "Missing required field: query");

    	// Lightweight guardrails (tune to your governance)
    	if (args.Query.Length > 20_000)
        	return McpError("InvalidArguments", "Query is too long.");

    	// Optional: enforce SELECT-only to reduce risk
    	// (Remove if your use case requires other operations)
    	if (!args.Query.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase))
        	return McpError("PolicyDenied", "Only SELECT queries are allowed by this MCP tool.");

    	// -----------------------------
    	// 2) Build request to SuiteQL API
    	// -----------------------------
    	// Per docs: endpoint /api/netsuiteSuiteqlQuery with x-functions-key header,
    	// and SuiteQL supplied as querystring param "query", called via POST. [1](https://citrincooperman.sharepoint.com/sites/MicrosoftPlatformTeam/Shared%20Documents/Integration%20and%20Development/Azure%20-%20NetSuite%20REST%20API%20Interface%20Project/Version%201/Azure_nsrestinterface_API_V1.html?web=1)[2](https://loop.cloud.microsoft/p/eyJ1IjoiaHR0cHM6Ly9jaXRyaW5jb29wZXJtYW4uc2hhcmVwb2ludC5jb20vY29udGVudHN0b3JhZ2UveDhGTk8teHRza3VDUlgyX2ZNVEhMVUFyc2V3UlZiaEd2S01kTTdtYzNpRT9uYXY9Y3owbE1rWmpiMjUwWlc1MGMzUnZjbUZuWlNVeVJuZzRSazVQSlRKRWVIUnphM1ZEVWxneUpUVkdaazFVU0V4VlFYSnpaWGRTVm1Kb1IzWkxUV1JOTjIxak0ybEZKbVE5WWlVeU1XcDVXR051SlRWR2RGWmlWVU5XZUVOMFJsUmxKVFZHYjJreU5DVTFSbXhhZUVkVVZWSk1ibHBPYUhCdGFFVmFaV2RMUjBneFdrZ3dlVEZTTkZBeFZ6Um9XbEoxY1hRbVpqMHdNVkpXVDA5Qk5sVlZOVmRQU2tSSlJGZEpVa1pLVnpSU1JFVklUbFl5V0RZMUptTTlKVEpHIn0%3D)
    	var functionKey = AZURE_FUNCTIONS_KEY;
    	if (string.IsNullOrWhiteSpace(functionKey))
        	return McpError("ConfigurationError", "Missing environment variable NSREST_X_FUNCTIONS_KEY.");

    	var encodedQuery = Uri.EscapeDataString(args.Query);
    	var url = $"{BASE_URL.TrimEnd('/')}/api/netsuiteSuiteqlQuery?query={encodedQuery}";

    	// Include paging params only if your endpoint supports them (safe to omit)
    	if (args.Limit is not null)  url += $"&limit={args.Limit.Value}";
    	if (args.Offset is not null) url += $"&offset={args.Offset.Value}";

    	var http = _httpClientFactory.CreateClient("NsRestInterface");

    	using var req = new HttpRequestMessage(HttpMethod.Post, url);
    	req.Headers.Add("x-functions-key", functionKey); // required per docs [1](https://citrincooperman.sharepoint.com/sites/MicrosoftPlatformTeam/Shared%20Documents/Integration%20and%20Development/Azure%20-%20NetSuite%20REST%20API%20Interface%20Project/Version%201/Azure_nsrestinterface_API_V1.html?web=1)[2](https://loop.cloud.microsoft/p/eyJ1IjoiaHR0cHM6Ly9jaXRyaW5jb29wZXJtYW4uc2hhcmVwb2ludC5jb20vY29udGVudHN0b3JhZ2UveDhGTk8teHRza3VDUlgyX2ZNVEhMVUFyc2V3UlZiaEd2S01kTTdtYzNpRT9uYXY9Y3owbE1rWmpiMjUwWlc1MGMzUnZjbUZuWlNVeVJuZzRSazVQSlRKRWVIUnphM1ZEVWxneUpUVkdaazFVU0V4VlFYSnpaWGRTVm1Kb1IzWkxUV1JOTjIxak0ybEZKbVE5WWlVeU1XcDVXR051SlRWR2RGWmlWVU5XZUVOMFJsUmxKVFZHYjJreU5DVTFSbXhhZUVkVVZWSk1ibHBPYUhCdGFFVmFaV2RMUjBneFdrZ3dlVEZTTkZBeFZ6Um9XbEoxY1hRbVpqMHdNVkpXVDA5Qk5sVlZOVmRQU2tSSlJGZEpVa1pLVnpSU1JFVklUbFl5V0RZMUptTTlKVEpHIn0%3D)
    	req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    	// POST with empty body is shown in examples [1](https://citrincooperman.sharepoint.com/sites/MicrosoftPlatformTeam/Shared%20Documents/Integration%20and%20Development/Azure%20-%20NetSuite%20REST%20API%20Interface%20Project/Version%201/Azure_nsrestinterface_API_V1.html?web=1)[2](https://loop.cloud.microsoft/p/eyJ1IjoiaHR0cHM6Ly9jaXRyaW5jb29wZXJtYW4uc2hhcmVwb2ludC5jb20vY29udGVudHN0b3JhZ2UveDhGTk8teHRza3VDUlgyX2ZNVEhMVUFyc2V3UlZiaEd2S01kTTdtYzNpRT9uYXY9Y3owbE1rWmpiMjUwWlc1MGMzUnZjbUZuWlNVeVJuZzRSazVQSlRKRWVIUnphM1ZEVWxneUpUVkdaazFVU0V4VlFYSnpaWGRTVm1Kb1IzWkxUV1JOTjIxak0ybEZKbVE5WWlVeU1XcDVXR051SlRWR2RGWmlWVU5XZUVOMFJsUmxKVFZHYjJreU5DVTFSbXhhZUVkVVZWSk1ibHBPYUhCdGFFVmFaV2RMUjBneFdrZ3dlVEZTTkZBeFZ6Um9XbEoxY1hRbVpqMHdNVkpXVDA5Qk5sVlZOVmRQU2tSSlJGZEpVa1pLVnpSU1JFVklUbFl5V0RZMUptTTlKVEpHIn0%3D)
    	req.Content = null;

    	// -----------------------------
    	// 3) Send + handle response
    	// -----------------------------
    	try
    	{
        	_logger.LogInformation("executeSuiteQL calling {Url}", SanitizeUrl(url));

        	using var resp = await http.SendAsync(req);
        	var body = await resp.Content.ReadAsStringAsync();

        	if (resp.IsSuccessStatusCode)
            	return body; // raw JSON from nsrestinterface

        	return McpError("UpstreamError",
            	$"SuiteQL endpoint returned {(int)resp.StatusCode} {resp.ReasonPhrase}",
            	new
            	{
                	status = (int)resp.StatusCode,
                	reason = resp.ReasonPhrase,
                	upstreamBody = Truncate(body, 4000)
            	});
    	}
    	catch (TaskCanceledException ex)
    	{
        	_logger.LogWarning(ex, "Timeout calling SuiteQL endpoint.");
        	return McpError("Timeout", "SuiteQL endpoint request timed out.");
    	}
    	catch (Exception ex)
    	{
        	_logger.LogError(ex, "Unhandled error calling SuiteQL endpoint.");
        	return McpError("Exception", "Unhandled error calling SuiteQL endpoint.", new { message = ex.Message });
    	}
	}

	// -----------------------------
	// Models + helpers
	// -----------------------------
	private sealed record SuiteQlArgs
	{
    	public string Query { get; init; } = "";
    	public int? Limit { get; init; }
    	public int? Offset { get; init; }
	}

	private static class JsonOptions
	{
    	public static readonly JsonSerializerOptions Default = new()
    	{
        	PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        	WriteIndented = false
    	};
	}

	private static string McpError(string code, string message, object? details = null)
    	=> JsonSerializer.Serialize(new
    	{
        	isError = true,
        	error = new { code, message, details }
    	}, JsonOptions.Default);

	private static string Truncate(string? s, int max)
    	=> string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

	private static string SanitizeUrl(string url)
	{
    	var idx = url.IndexOf("?query=", StringComparison.OrdinalIgnoreCase);
    	return idx < 0 ? url : url.Substring(0, idx) + "?query=<redacted>";
	}

}


