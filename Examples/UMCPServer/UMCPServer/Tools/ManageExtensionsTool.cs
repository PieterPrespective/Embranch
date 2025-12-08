using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using UMCPServer.Services;
using static UMCPServer.Tools.ToolBaseUtility;

namespace UMCPServer.Tools;

/// <summary>
/// Server-side tool for managing Unity extensions including custom serialization and operations.
/// Provides capabilities to query Unity Object knowledge and execute custom operations through MCP interface.
/// </summary>
[McpServerToolType]
public class ManageExtensionsTool : Toolbase<ManageExtensionsTool>
{
    public ManageExtensionsTool(
        ILogger<ManageExtensionsTool> logger,
        IUnityConnectionService unityConnection,
        IUnityStateConnectionService stateConnection) : base(logger, unityConnection, stateConnection)
    {
    }

    [McpServerTool]
    [Description("Manage Unity extensions - query Unity Object knowledge and execute custom operations")]
    public async Task<object> ManageExtensions(
        [Required]
        [Description("Action to perform: 'query_unity_object_knowledge', 'custom_unity_object_operation', or 'get_all_registered_operations'")]
        string action,

        [Description("Target Unity Object class name (required for query_unity_object_knowledge)")]
        string? targetClass = null,

        [Description("Operation name (required for custom_unity_object_operation)")]
        string? operationName = null,

        [Description("Operation type/target class (optional for custom_unity_object_operation)")]
        string? operationType = null,

        [Description("Operation parameters as JSON string (optional for custom_unity_object_operation)")]
        string? operationParameters = null,

        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ManageExtensions called with action: {Action}", action);

            // Validate action parameter
            if (string.IsNullOrWhiteSpace(action))
            {
                return new
                {
                    success = false,
                    error = "Required parameter 'action' is missing or empty."
                };
            }

            action = action.ToLower().Replace("_", "");

            // Validate action value
            var validActions = new[] { "queryunityobjectknowledge", "customunityobjectoperation", "getallregisteredoperations" };
            if (!validActions.Contains(action))
            {
                return new
                {
                    success = false,
                    error = $"Invalid action '{action}'. Valid actions are: 'query_unity_object_knowledge', 'custom_unity_object_operation', 'get_all_registered_operations'"
                };
            }

            // Validate required parameters based on action
            var validationResult = ValidateActionParameters(action, targetClass, operationName);
            if (!validationResult.IsValid)
            {
                return new
                {
                    success = false,
                    error = validationResult.ErrorMessage
                };
            }

            // Prepare parameters for Unity
            var parameters = BuildUnityParameters(action, targetClass, operationName, operationType, operationParameters);

            // Send command to Unity
            AsyncToolCommandResult toolResult = await SendCommandAsyncWhenInRightState(
                commandType: "manage_extensions",
                parameters: parameters,
                desiredStateOptions: new UnityConnectionState[] {
                    new UnityConnectionState() {
                        CurrentContext = Context.Running,
                        CurrentRunMode = Runmode.EditMode_Scene
                    },
                    new UnityConnectionState() {
                        CurrentContext = Context.Running,
                        CurrentRunMode = Runmode.EditMode_Prefab
                    }
                },
                unityConnection: _unityConnection,
                stateConnection: _stateConnection,
                logger: _logger,
                cancellationToken: cancellationToken);

            if (!toolResult.Success)
            {
                return new
                {
                    success = false,
                    error = toolResult.ErrorMessage ?? "Unknown error sending command to Unity"
                };
            }

            var result = toolResult.Response!;

            // Extract and return the result
            var resultData = result["data"];

            // Handle different action responses
            return action switch
            {
                "queryunityobjectknowledge" => HandleQueryResponse(resultData),
                "customunityobjectoperation" => HandleOperationResponse(resultData),
                "getallregisteredoperations" => HandleListOperationsResponse(resultData),
                _ => new
                {
                    success = true,
                    message = resultData?.ToString() ?? $"Action '{action}' completed",
                    data = resultData
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ManageExtensions operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ManageExtensions operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ManageExtensions");
            return new
            {
                success = false,
                error = $"Failed to execute ManageExtensions: {ex.Message}"
            };
        }
    }

    #region Parameter Validation

    /// <summary>
    /// Validates required parameters based on the action being performed.
    /// </summary>
    private static (bool IsValid, string? ErrorMessage) ValidateActionParameters(
        string action, string? targetClass, string? operationName)
    {
        return action switch
        {
            "queryunityobjectknowledge" => ValidateQueryParameters(targetClass),
            "customunityobjectoperation" => ValidateOperationParameters(operationName),
            "getallregisteredoperations" => (true, null), // No required parameters
            _ => (true, null)
        };
    }

    private static (bool IsValid, string? ErrorMessage) ValidateQueryParameters(string? targetClass)
    {
        if (string.IsNullOrWhiteSpace(targetClass))
            return (false, "'targetClass' parameter is required for 'query_unity_object_knowledge' action.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateOperationParameters(string? operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
            return (false, "'operationName' parameter is required for 'custom_unity_object_operation' action.");

        return (true, null);
    }

    #endregion

    #region Unity Command Building

    /// <summary>
    /// Builds parameters object for Unity command based on action and inputs.
    /// </summary>
    private static JObject BuildUnityParameters(
        string action, string? targetClass, string? operationName,
        string? operationType, string? operationParameters)
    {
        var parameters = new JObject();

        // Map action back to Unity format
        string unityAction = action switch
        {
            "queryunityobjectknowledge" => "query_unity_object_knowledge",
            "customunityobjectoperation" => "custom_unity_object_operation",
            "getallregisteredoperations" => "get_all_registered_operations",
            _ => action
        };

        parameters["action"] = unityAction;

        // Add parameters based on action
        switch (action)
        {
            case "queryunityobjectknowledge":
                parameters["TargetClass"] = targetClass;
                break;

            case "customunityobjectoperation":
                parameters["OperationName"] = operationName;
                if (!string.IsNullOrWhiteSpace(operationType))
                    parameters["OperationType"] = operationType;
                if (!string.IsNullOrWhiteSpace(operationParameters))
                    parameters["OperationParameters"] = operationParameters;
                break;

            case "getallregisteredoperations":
                // No additional parameters needed
                break;
        }

        return parameters;
    }

    #endregion

    #region Response Handlers

    /// <summary>
    /// Handles response from query_unity_object_knowledge action.
    /// </summary>
    private static object HandleQueryResponse(JToken? resultData)
    {
        if (resultData == null)
        {
            return new
            {
                success = false,
                error = "No data returned from Unity for query operation"
            };
        }

        // Check if this is an error response
        if (resultData["error"] != null)
        {
            return new
            {
                success = false,
                error = resultData["error"].ToString()
            };
        }

        // Return the full query result
        return new
        {
            success = true,
            fullClassName = resultData["FullClassName"]?.ToString(),
            symbolDefinition = resultData["SymbolDefinition"],
            category = resultData["Category"]?.ToString(),
            classKnowledge = resultData["ClassKnowledge"]?.ToString(),
            serializedForm = resultData["SerializedForm"],
            customOperations = resultData["CustomOperations"],
            description = resultData["Description"]?.ToString()
        };
    }

    /// <summary>
    /// Handles response from custom_unity_object_operation action.
    /// </summary>
    private static object HandleOperationResponse(JToken? resultData)
    {
        if (resultData == null)
        {
            return new
            {
                success = false,
                error = "No data returned from Unity for operation execution"
            };
        }

        // Check if this is an error response
        if (resultData["error"] != null)
        {
            return new
            {
                success = false,
                error = resultData["error"].ToString()
            };
        }

        // Check for success field
        bool? success = resultData["success"]?.ToObject<bool>();
        if (success.HasValue && !success.Value)
        {
            return new
            {
                success = false,
                error = resultData["message"]?.ToString() ?? "Operation failed"
            };
        }

        // Return the operation result
        return new
        {
            success = true,
            message = resultData["message"]?.ToString() ?? "Operation executed successfully",
            result = resultData["result"]
        };
    }

    /// <summary>
    /// Handles response from get_all_registered_operations action.
    /// </summary>
    private static object HandleListOperationsResponse(JToken? resultData)
    {
        if (resultData == null)
        {
            return new
            {
                success = false,
                error = "No data returned from Unity for list operations"
            };
        }

        // Check if this is an error response
        if (resultData["error"] != null)
        {
            return new
            {
                success = false,
                error = resultData["error"].ToString()
            };
        }

        // Return the operations list
        return new
        {
            success = true,
            operations = resultData["operations"] ?? new JArray(),
            count = resultData["operations"]?.Count() ?? 0
        };
    }

    #endregion
}