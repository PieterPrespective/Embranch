using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class RequestStepLogsTool
{
    private readonly ILogger<RequestStepLogsTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public RequestStepLogsTool(ILogger<RequestStepLogsTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Retrieves log messages for a specific development step, going back to the mark_start_of_new_step invocation. This allows you to see all logs that occurred after marking the start of a particular development step.")]
    public async Task<object> RequestStepLogs(
        [Description("The name of the development step to retrieve logs for. Must match a previously marked step name.")]
        string stepName,
        
        [Description("Include stack traces in the output (default: true)")]
        bool includeStacktrace = true,
        
        [Description("Return format: 'detailed' (default) returns structured data with type, message, file, line, and stack trace. 'plain' returns just the message text.")]
        string format = "detailed",
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Requesting logs for step: {StepName}", stepName);
            
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
                ["stepName"] = stepName,
                ["includeStacktrace"] = includeStacktrace,
                ["format"] = format
            };
            
            // Send command to Unity
            var response = await _unityConnection.SendCommandAsync("request_step_logs", parameters, cancellationToken);
            
            if (response == null)
            {
                return new
                {
                    success = false,
                    error = "Failed to get response from Unity"
                };
            }
            
            // Extract the response data - Fix: data is nested in result object
            var status = response.Value<string>("status");
            if (status == "error")
            {
                return new
                {
                    success = false,
                    error = response.Value<string>("error") ?? "Unknown error from Unity"
                };
            }
            
            // Get the result object which contains the actual data
            var result = response["result"];
            var message = response.Value<string>("message");
            var data = response.Value<JArray>("data");
            List<object> dynamicData = (data != null) ? data.Select(ReadConsoleTool.ConvertJTokenToObjectSmart).ToList() : new List<object>();

            return new
            {
                success = true,
                message = message ?? $"Retrieved logs for step '{stepName}'",
                stepName = stepName,
                entries = dynamicData,
                count = data?.Count() ?? 0
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("RequestStepLogs operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("RequestStepLogs operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting step logs");
            return new
            {
                success = false,
                error = $"Failed to request step logs: {ex.Message}"
            };
        }
    }
}
