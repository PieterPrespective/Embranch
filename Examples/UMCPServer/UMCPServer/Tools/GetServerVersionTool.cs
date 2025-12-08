using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;
using UMCPServer.Models;

namespace UMCPServer.Tools;

[McpServerToolType]
public class GetServerVersionTool
{
    private readonly ILogger<GetServerVersionTool> _logger;
    
    public GetServerVersionTool(ILogger<GetServerVersionTool> logger)
    {
        _logger = logger;
    }
    
    [McpServerTool]
    [Description("Retrieves the current version of the UMCP MCP Server.")]
    public virtual Task<object> GetServerVersion()
    {
        try
        {
            _logger.LogInformation("Getting server version information");
            
            // Get version information from assembly
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var assemblyVersion = assembly.GetName().Version?.ToString();
            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            var loggingEnabled = Environment.GetEnvironmentVariable("ENABLE_LOGGING") ?? "false";

            ServerConfiguration config = new ServerConfiguration();
            BuilderUtility.GetServerConfiguration(config);

            return Task.FromResult<object>(new
            {
                success = true,
                message = "Server version retrieved successfully",
                version = new
                {
                    informationalVersion,
                    assemblyVersion,
                    fileVersion,
                    loggingEnabled,
                    config.IsRunningInContainer,
                    config.UnityPort,
                    config.UnityStatePort,
                    config.UnityHost,
                    config.McpPort,
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting server version");
            return Task.FromResult<object>(new
            {
                success = false,
                error = $"Failed to get server version: {ex.Message}"
            });
        }
    }
}