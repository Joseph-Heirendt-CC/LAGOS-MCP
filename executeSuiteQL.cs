using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace NS_MCP.Function;

public class executeSuiteQL
{
    private ILogger<executeSuiteQL> _logger;
    private static readonly string AZURE_FUNCTIONS_KEY = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_KEY") ?? "Production";



    public executeSuiteQL(ILogger<executeSuiteQL> logger)
    {
        _logger = logger;
    }

    [Function(nameof(executeSuiteQL))]
    public string Run(
        [McpToolTrigger(nameof(executeSuiteQL), "Responds to the user with a hello message.")] ToolInvocationContext context,
        [McpToolProperty(nameof(name), "The name of the person to greet.")] string? name
    )
    {
        _logger.LogInformation("C# MCP tool trigger function processed a request.");
        return $"Hello, {name ?? "world"}! This is an MCP Tool!";
    }
}
