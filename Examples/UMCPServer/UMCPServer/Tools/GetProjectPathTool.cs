using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class GetProjectPathTool
{
    private readonly ILogger<GetProjectPathTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public GetProjectPathTool(ILogger<GetProjectPathTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Retrieves the current Unity project path. Returns project path, data path, persistent data path, streaming assets path, and temporary cache path.")]
    public async Task<object> GetProjectPath(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting Unity project path");
            // Check if Unity connection is available
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                };
            }
            // Send command to Unity
            var result = await _unityConnection.SendCommandAsync("get_project_path", null, cancellationToken);
            
            if (result == null)
            {
                return new
                {
                    success = false,
                    error = "No response received from Unity within the timeout period"
                };
            }
            // Extract the response data
            bool success = result.Value<bool?>("success") ?? false;
            string? message = result.Value<string?>("message");
            var data = result["data"];
            
            if (!success)
            {
                return new
                {
                    success = false,
                    error = result.Value<string?>("error") ?? "Failed to get project path"
                };
            }
            // Return the project path information
            return new
            {
                success = true,
                message = message ?? "Project path retrieved successfully",
                projectPath = data?.Value<string>("projectPath"),
                dataPath = data?.Value<string>("dataPath"),
                persistentDataPath = data?.Value<string>("persistentDataPath"),
                streamingAssetsPath = data?.Value<string>("streamingAssetsPath"),
                temporaryCachePath = data?.Value<string>("temporaryCachePath")
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetProjectPath operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("GetProjectPath operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project path");
            return new
            {
                success = false,
                error = $"Failed to get project path: {ex.Message}"
            };
        }
    }
}
