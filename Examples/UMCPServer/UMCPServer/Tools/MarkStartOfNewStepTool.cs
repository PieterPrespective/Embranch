using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class MarkStartOfNewStepTool
{
    private readonly ILogger<MarkStartOfNewStepTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public MarkStartOfNewStepTool(ILogger<MarkStartOfNewStepTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Marks the start of a new development step by creating a log entry in the Unity Debug Console. This allows for tracking which log messages are relevant to specific development steps.")]
    public async Task<object> MarkStartOfNewStep(
        [Description("The name of the development step to mark. This will be used to identify logs belonging to this step.")]
        string stepName,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Marking start of new step: {StepName}", stepName);
            
            if (string.IsNullOrWhiteSpace(stepName))
            {
                return new
                {
                    success = false,
                    error = "Step name cannot be empty. Please provide a valid step name."
                };
            }
            
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                };
            }
            
            // Build parameters for the Unity command
            var parameters = new JObject
            {
                ["stepName"] = stepName
            };
            
            // Send command to Unity
            var response = await _unityConnection.SendCommandAsync("mark_start_of_new_step", parameters, cancellationToken);
            
            if (response == null)
            {
                return new
                {
                    success = false,
                    error = "Failed to get response from Unity"
                };
            }
            
            // Extract the response data
            var status = response.Value<string>("status");
            if (status == "error")
            {
                return new
                {
                    success = false,
                    error = response.Value<string>("error") ?? "Unknown error from Unity"
                };
            }
            
            var message = response.Value<string>("message");
            var data = response["data"];
            
            return new
            {
                success = true,
                message = message ?? $"Successfully marked start of step '{stepName}'",
                stepName = data?["stepName"]?.ToString() ?? stepName,
                timestamp = data?["timestamp"]?.ToString(),
                markerMessage = data?["markerMessage"]?.ToString()
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MarkStartOfNewStep operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("MarkStartOfNewStep operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking start of new step");
            return new
            {
                success = false,
                error = $"Failed to mark start of new step: {ex.Message}"
            };
        }
    }
}
