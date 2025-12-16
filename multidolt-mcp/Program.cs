using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using DMMS.Logging;
using DMMS.Models;
using DMMS.Services;
using DMMS.Tools;

var builder = Host.CreateApplicationBuilder(args);

// Add global exception handling
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    try
    {
        var logFileName = Environment.GetEnvironmentVariable("LOG_FILE_NAME") ?? "DMMS_crash.log";
        var crashLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED EXCEPTION: {e.ExceptionObject}\n";
        File.AppendAllText(logFileName, crashLog);
    }
    catch
    {
        // Ignore logging errors during crash
    }
};

builder.Logging.ClearProviders();
bool enableLogging = LoggingUtility.IsLoggingEnabled;

if (enableLogging)
{
    var logFileName = Environment.GetEnvironmentVariable("LOG_FILE_NAME");
    var logLevel = Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("LOG_LEVEL"), out var level) 
        ? level 
        : LogLevel.Debug; // Default to Debug level for better troubleshooting
    
    builder.Logging.AddFileLogging(logFileName, logLevel);
    builder.Logging.SetMinimumLevel(logLevel);
}

builder.Services.Configure<ServerConfiguration>(options => ConfigurationUtility.GetServerConfiguration(options));
builder.Services.Configure<DoltConfiguration>(options => ConfigurationUtility.GetDoltConfiguration(options));

// Register both implementations
builder.Services.AddSingleton<ChromaDbService>();

// Register the appropriate service based on configuration
builder.Services.AddSingleton<IChromaDbService>(serviceProvider =>
    ChromaDbServiceFactory.CreateService(serviceProvider));

// Register Dolt services
builder.Services.AddSingleton<IDoltCli, DoltCli>();
builder.Services.AddSingleton<ISyncManagerV2, SyncManagerV2>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // Server Management Tools
    .WithTools<GetServerVersionTool>()
    
    // ChromaDB Collection Management Tools
    .WithTools<ChromaListCollectionsTool>()
    .WithTools<ChromaCreateCollectionTool>()
    .WithTools<ChromaDeleteCollectionTool>()
    .WithTools<ChromaGetCollectionCountTool>()
    .WithTools<ChromaGetCollectionInfoTool>()
    .WithTools<ChromaModifyCollectionTool>()
    .WithTools<ChromaPeekCollectionTool>()
    
    // ChromaDB Document Operations Tools  
    .WithTools<ChromaAddDocumentsTool>()
    .WithTools<ChromaQueryDocumentsTool>()
    .WithTools<ChromaGetDocumentsTool>()
    .WithTools<ChromaUpdateDocumentsTool>()
    .WithTools<ChromaDeleteDocumentsTool>()
    
    // Dolt Version Control Tools - Status and Information
    .WithTools<DoltStatusTool>()
    .WithTools<DoltBranchesTool>()
    .WithTools<DoltCommitsTool>()
    .WithTools<DoltShowTool>()
    .WithTools<DoltFindTool>()
    
    // Dolt Version Control Tools - Repository Setup
    .WithTools<DoltInitTool>()
    .WithTools<DoltCloneTool>()
    
    // Dolt Version Control Tools - Remote Synchronization
    .WithTools<DoltFetchTool>()
    .WithTools<DoltPullTool>()
    .WithTools<DoltPushTool>()
    
    // Dolt Version Control Tools - Local Operations
    .WithTools<DoltCommitTool>()
    .WithTools<DoltCheckoutTool>()
    .WithTools<DoltResetTool>();

var host = builder.Build();

if (enableLogging)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    
    var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    
    logger.LogInformation("DMMS (Dolt Multi-Database MCP Server) v{Version} starting up", version);
    logger.LogInformation("This server provides MCP access to multiple Dolt databases via terminal commands");

    // Initialize PythonContext on main thread to prevent GIL deadlocks
    try
    {
        logger.LogInformation("Initializing Python context...");
        var pythonDll = PythonContextUtility.FindPythonDll(logger);
        PythonContext.Initialize(logger, pythonDll);
        logger.LogInformation("Python context initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize Python context. ChromaDB functionality will be unavailable.");
        throw; // Don't continue if Python fails to initialize
    }
}
else
{
    // Initialize PythonContext even without logging
    try
    {
        var pythonDll = PythonContextUtility.FindPythonDll();
        PythonContext.Initialize(pythonDllPath: pythonDll);
    }
    catch (Exception)
    {
        throw; // Don't continue if Python fails to initialize
    }
}

// Register shutdown hook to clean up Python context
var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
applicationLifetime.ApplicationStopping.Register(() =>
{
    var logger = enableLogging ? host.Services.GetService<ILogger<Program>>() : null;
    logger?.LogInformation("Shutting down Python context...");
    PythonContext.Shutdown();
    logger?.LogInformation("Python context shutdown complete");
});

await host.RunAsync();

/// <summary>
/// Utility class for managing server configuration settings
/// </summary>
public static class ConfigurationUtility
{
    /// <summary>
    /// Populates the server configuration from environment variables
    /// </summary>
    /// <param name="options">The server configuration to populate</param>
    /// <returns>The populated server configuration</returns>
    public static ServerConfiguration GetServerConfiguration(ServerConfiguration options)
    {
        options.McpPort = int.TryParse(Environment.GetEnvironmentVariable("MCP_PORT"), out var mcpPort) ? mcpPort : 6500;
        options.ConnectionTimeoutSeconds = double.TryParse(Environment.GetEnvironmentVariable("CONNECTION_TIMEOUT"), out var timeout) ? timeout : 86400.0;
        options.BufferSize = int.TryParse(Environment.GetEnvironmentVariable("BUFFER_SIZE"), out var bufferSize) ? bufferSize : 16 * 1024 * 1024;
        options.MaxRetries = int.TryParse(Environment.GetEnvironmentVariable("MAX_RETRIES"), out var retries) ? retries : 3;
        options.RetryDelaySeconds = double.TryParse(Environment.GetEnvironmentVariable("RETRY_DELAY"), out var delay) ? delay : 1.0;
        options.ChromaHost = Environment.GetEnvironmentVariable("CHROMA_HOST") ?? "localhost";
        options.ChromaPort = int.TryParse(Environment.GetEnvironmentVariable("CHROMA_PORT"), out var chromaPort) ? chromaPort : 8000;
        options.ChromaMode = Environment.GetEnvironmentVariable("CHROMA_MODE") ?? "persistent";
        options.ChromaDataPath = Environment.GetEnvironmentVariable("CHROMA_DATA_PATH") ?? "./chroma_data";
        
        return options;
    }

    /// <summary>
    /// Populates the Dolt configuration from environment variables
    /// </summary>
    /// <param name="options">The Dolt configuration to populate</param>
    /// <returns>The populated Dolt configuration</returns>
    public static DoltConfiguration GetDoltConfiguration(DoltConfiguration options)
    {
        // Check for Dolt executable path, defaulting to "C:\Program Files\Dolt\bin\dolt.exe" on Windows
        var defaultPath = Environment.OSVersion.Platform == PlatformID.Win32NT 
            ? @"C:\Program Files\Dolt\bin\dolt.exe" 
            : "dolt";
        options.DoltExecutablePath = Environment.GetEnvironmentVariable("DOLT_EXECUTABLE_PATH") ?? defaultPath;
        options.RepositoryPath = Environment.GetEnvironmentVariable("DOLT_REPOSITORY_PATH") ?? "./data/dolt-repo";
        options.RemoteName = Environment.GetEnvironmentVariable("DOLT_REMOTE_NAME") ?? "origin";
        options.RemoteUrl = Environment.GetEnvironmentVariable("DOLT_REMOTE_URL");
        options.CommandTimeoutMs = int.TryParse(Environment.GetEnvironmentVariable("DOLT_COMMAND_TIMEOUT"), out var timeout) ? timeout : 30000;
        options.EnableDebugLogging = bool.TryParse(Environment.GetEnvironmentVariable("DOLT_DEBUG_LOGGING"), out var debug) && debug;
        
        return options;
    }
}

/// <summary>
/// Utility class for managing logging configuration
/// </summary>
public static class LoggingUtility
{
    /// <summary>
    /// Default logging setting - can be overridden by ENABLE_LOGGING environment variable
    /// </summary>
    public const bool EnableLogging = false;
    
    /// <summary>
    /// Determines if logging is enabled based on environment variable or default setting
    /// </summary>
    public static bool IsLoggingEnabled => bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_LOGGING"), out var envLogging) 
        ? envLogging 
        : EnableLogging;
}