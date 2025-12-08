using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Reflection;
using UMCPServer.Models;
using UMCPServer.Prompts;
using UMCPServer.Services;
using UMCPServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging - expanded to enable more logging in Docker containers for troubleshooting
builder.Logging.ClearProviders();
// Check for explicit logging configuration from environment
bool enableLogging = LoggingConfig.IsEnabled;

if (enableLogging)
{
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

// Detect if running in Docker
bool isRunningInDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

// Configure server settings
//builder.Services.Configure<ServerConfiguration>(options =>
//{
//    // Unity host with Docker awareness
//    string defaultHost = isRunningInDocker ? "host.docker.internal" : "localhost";
//    options.UnityHost = Environment.GetEnvironmentVariable("UNITY_HOST") ?? defaultHost;
//    options.UnityPort = int.TryParse(Environment.GetEnvironmentVariable("UNITY_PORT"), out var port) ? port : 6400;
//    options.UnityStatePort = int.TryParse(Environment.GetEnvironmentVariable("UNITY_STATE_PORT"), out var statePort) ? statePort : 6401; // Separate port for state updates, should be added to docker config
//    options.McpPort = int.TryParse(Environment.GetEnvironmentVariable("MCP_PORT"), out var mcpPort) ? mcpPort : 6500;
//    options.ConnectionTimeoutSeconds = double.TryParse(Environment.GetEnvironmentVariable("CONNECTION_TIMEOUT"), out var timeout) ? timeout : 86400.0;
//    options.BufferSize = int.TryParse(Environment.GetEnvironmentVariable("BUFFER_SIZE"), out var bufferSize) ? bufferSize : 16 * 1024 * 1024;
//    options.MaxRetries = int.TryParse(Environment.GetEnvironmentVariable("MAX_RETRIES"), out var retries) ? retries : 3;
//    options.RetryDelaySeconds = double.TryParse(Environment.GetEnvironmentVariable("RETRY_DELAY"), out var delay) ? delay : 1.0;
//    options.IsRunningInContainer = isRunningInDocker;
//});

builder.Services.Configure<ServerConfiguration>(options => BuilderUtility.GetServerConfiguration(options));

// Register services
builder.Services.AddSingleton<UnityConnectionService>();
builder.Services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());
builder.Services.AddSingleton<UnityStateConnectionService>();
builder.Services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());

// Register MCP server with tools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithPrompts<ServerPrompts>()
    .WithTools<ExecuteMenuItemTool>()
    .WithTools<GetProjectPathTool>()
    .WithTools<GetServerVersionTool>()
    .WithTools<GetUnityClientStateTool>()
    .WithTools<ManageAssetTool>()
    .WithTools<ManageEditorTool>()
    .WithTools<ManageGameObjectTool>()
    .WithTools<ManageSceneTool>()
    .WithTools<ManageScriptTool>()
    .WithTools<MarkStartOfNewStepTool>()
    .WithTools<ReadConsoleTool>()
    .WithTools<RequestStepLogsTool>()
    .WithTools<ForceUpdateEditorTool>()
    .WithTools<GetTestsTool>()
    .WithTools<RunTestsTool>()
    .WithTools<InterpretTestResultsTool>()
    .WithTools<CleanupTestResultsTool>();


// Add hosted service for Unity connection lifecycle
builder.Services.AddHostedService<UnityConnectionLifecycleService>();

var host = builder.Build();

// Log startup information
if (enableLogging)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    // Get version from assembly
    var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    logger.LogInformation("UMCP MCP Server v{Version} starting up", version);
    logger.LogInformation("This server bridges MCP requests to the Unity Editor via TCP");
    logger.LogInformation("Ensure Unity Editor is running with the UMCP Unity3D Client active");
    
    // Log Docker-specific information
    if (isRunningInDocker)
    {
        var config = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerConfiguration>>().Value;
        logger.LogInformation("Running in Docker container");
        logger.LogInformation("Unity host: {UnityHost}:{UnityPort}", config.UnityHost, config.UnityPort);
        logger.LogInformation("MCP port: {McpPort}", config.McpPort);
    }
}

await host.RunAsync();

public static class BuilderUtility
{ 
    public static ServerConfiguration GetServerConfiguration(ServerConfiguration options)
    {
        bool isRunningInDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        string defaultHost = isRunningInDocker ? "host.docker.internal" : "localhost";
        options.UnityHost = Environment.GetEnvironmentVariable("UNITY_HOST") ?? defaultHost;
        options.UnityPort = int.TryParse(Environment.GetEnvironmentVariable("UNITY_PORT"), out var port) ? port : 6400;
        options.UnityStatePort = int.TryParse(Environment.GetEnvironmentVariable("UNITY_STATE_PORT"), out var statePort) ? statePort : 6401; // Separate port for state updates, should be added to docker config
        options.McpPort = int.TryParse(Environment.GetEnvironmentVariable("MCP_PORT"), out var mcpPort) ? mcpPort : 6500;
        options.ConnectionTimeoutSeconds = double.TryParse(Environment.GetEnvironmentVariable("CONNECTION_TIMEOUT"), out var timeout) ? timeout : 86400.0;
        options.BufferSize = int.TryParse(Environment.GetEnvironmentVariable("BUFFER_SIZE"), out var bufferSize) ? bufferSize : 16 * 1024 * 1024;
        options.MaxRetries = int.TryParse(Environment.GetEnvironmentVariable("MAX_RETRIES"), out var retries) ? retries : 3;
        options.RetryDelaySeconds = double.TryParse(Environment.GetEnvironmentVariable("RETRY_DELAY"), out var delay) ? delay : 1.0;
        options.IsRunningInContainer = isRunningInDocker;
        return options;
    }

}




// Global configuration that determines if logging is enabled
public static class LoggingConfig
{
    // Default logging setting - can be overridden by ENABLE_LOGGING environment variable
    public const bool EnableLogging = false;
    
    public static bool IsEnabled => bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_LOGGING"), out var envLogging) 
        ? envLogging 
        : EnableLogging;
}

// Hosted service to manage Unity connection lifecycle
public class UnityConnectionLifecycleService : BackgroundService
{
    private readonly ILogger<UnityConnectionLifecycleService> _logger;
    private readonly UnityConnectionService _unityConnection;
    private readonly UnityStateConnectionService _unityStateConnection;
    private readonly ServerConfiguration _config;
    
    public UnityConnectionLifecycleService(
        ILogger<UnityConnectionLifecycleService> logger,
        UnityConnectionService unityConnection,
        UnityStateConnectionService unityStateConnection,
        Microsoft.Extensions.Options.IOptions<ServerConfiguration> config)
    {
        _logger = logger;
        _unityConnection = unityConnection;
        _unityStateConnection = unityStateConnection;
        _config = config.Value;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Try to connect to Unity on startup
        _logger.LogInformation("Attempting to connect to Unity Editor at {UnityHost}:{UnityPort}...", 
            _config.UnityHost, _config.UnityPort);
        
        // For Docker, provide more detailed connection information
        if (_config.IsRunningInContainer)
        {
            _logger.LogInformation("Running in container environment. Make sure Unity is accessible at {UnityHost}:{UnityPort}", 
                _config.UnityHost, _config.UnityPort);
            _logger.LogInformation("If connection fails, verify that:");
            _logger.LogInformation("1. Unity Editor is running on the host machine with UMCP Unity3D Client active");
            _logger.LogInformation("2. Unity TCP server is listening on all interfaces (not just localhost)");
            _logger.LogInformation("3. Host firewall allows incoming connections on port {UnityPort}", _config.UnityPort);
            _logger.LogInformation("4. Container has proper network configuration (host.docker.internal maps to host)");
        }
        
        // Connect to main command port
        bool commandConnected = await _unityConnection.ConnectAsync();
        if (commandConnected)
        {
            _logger.LogInformation("Successfully connected to Unity Editor command port on startup");
        }
        else
        {
            _logger.LogWarning("Could not connect to Unity Editor command port on startup. Connection will be attempted when needed.");
            
            if (_config.IsRunningInContainer)
            {
                _logger.LogWarning("For Docker environments, ensure UNITY_HOST is set to 'host.docker.internal' or the actual host IP address");
            }
        }
        
        // Connect to state port
        _logger.LogInformation("Attempting to connect to Unity state port at {UnityHost}:{UnityStatePort}...", 
            _config.UnityHost, _config.UnityStatePort);
            
        bool stateConnected = await _unityStateConnection.ConnectAsync();
        if (stateConnected)
        {
            _logger.LogInformation("Successfully connected to Unity state port on startup");
            
            // Subscribe to state changes
            _unityStateConnection.UnityStateChanged += OnUnityStateChanged;
        }
        else
        {
            _logger.LogWarning("Could not connect to Unity state port on startup. State updates will not be available.");
        }
        
        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private void OnUnityStateChanged(JObject state)
    {
        _logger.LogInformation("Unity state update received: runmode={Runmode}, context={Context}",
            state["runmode"], state["context"]);
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("UMCP MCP Server shutting down");
        
        // Unsubscribe from state changes
        _unityStateConnection.UnityStateChanged -= OnUnityStateChanged;
        
        // Dispose connections
        _unityConnection.Dispose();
        _unityStateConnection.Dispose();
        
        await base.StopAsync(cancellationToken);
    }
}
