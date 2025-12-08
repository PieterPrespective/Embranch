using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class ManageSceneTool
{
    private readonly ILogger<ManageSceneTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public ManageSceneTool(ILogger<ManageSceneTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Manage Unity scenes - create, load, save, and query hierarchy")]
    public async Task<object> ManageScene(
        [Required]
        [Description("Action to perform: 'create', 'load', 'save', 'get_hierarchy', 'get_active', 'get_build_settings'")]
        string action,
        
        [Description("Name of the scene (without .unity extension). Required for 'create' action, optional for 'load' and 'save'.")]
        string? name = null,
        
        [Description("Path relative to Assets/ directory where the scene should be created/saved. Default is 'Scenes' for create action.")]
        string? path = null,
        
        [Description("Build index of the scene to load. Alternative to name/path for 'load' action.")]
        int? buildIndex = null,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ManageScene called with action: {Action}, name: {Name}, path: {Path}, buildIndex: {BuildIndex}", 
                action, name, path, buildIndex);
            
            // Validate action parameter
            if (string.IsNullOrWhiteSpace(action))
            {
                return new
                {
                    success = false,
                    error = "Required parameter 'action' is missing or empty."
                };
            }
            
            action = action.ToLower();
            
            // Validate action value
            var validActions = new[] { "create", "load", "save", "get_hierarchy", "get_active", "get_build_settings" };
            if (!validActions.Contains(action))
            {
                return new
                {
                    success = false,
                    error = $"Invalid action '{action}'. Valid actions are: {string.Join(", ", validActions)}"
                };
            }
            
            // Validate required parameters based on action
            if (action == "create" && string.IsNullOrWhiteSpace(name))
            {
                return new
                {
                    success = false,
                    error = "'name' parameter is required for 'create' action."
                };
            }
            
            if (action == "load" && string.IsNullOrWhiteSpace(name) && !buildIndex.HasValue)
            {
                return new
                {
                    success = false,
                    error = "Either 'name'/'path' or 'buildIndex' must be provided for 'load' action."
                };
            }
            
            // Check if Unity connection is available
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                };
            }
            
            // Prepare parameters for Unity
            var parameters = new JObject
            {
                ["action"] = action
            };
            
            if (!string.IsNullOrWhiteSpace(name))
            {
                parameters["name"] = name;
            }
            
            if (!string.IsNullOrWhiteSpace(path))
            {
                parameters["path"] = path;
            }
            
            if (buildIndex.HasValue)
            {
                parameters["buildIndex"] = buildIndex.Value;
            }
            
            // Send command to Unity
            var result = await _unityConnection.SendCommandAsync("manage_scene", parameters, cancellationToken);
            
            if (result == null)
            {
                return new
                {
                    success = false,
                    error = "No response received from Unity within the timeout period"
                };
            }

            Console.WriteLine("Raw Result: " + result.ToString());

            // Check if the response indicates success or error
            bool? status = result.Value<bool?>("success");
            
            if (status.HasValue && !status.Value)
            {
                return new
                {
                    success = false,
                    error = result.Value<string?>("error") ?? "Unknown error occurred"
                };
            }

            

            // Extract the result
            var resultData = result["data"];


            
            // Handle different action responses
            switch (action)
            {
                case "create":
                    return new
                    {
                        success = true,
                        message = result?.Value<string?>("message") ?? $"Scene '{name}' created successfully",
                        path = resultData?.Value<string?>("path")
                    };
                    
                case "load":
                    return new
                    {
                        success = true,
                        message = resultData?.Value<string?>("message") ?? "Scene loaded successfully",
                        path = resultData?.Value<string?>("path"),
                        name = resultData?.Value<string?>("name"),
                        buildIndex = resultData?.Value<int?>("buildIndex") ?? -1
                    };
                    
                case "save":
                    return new
                    {
                        success = true,
                        message = resultData?.Value<string?>("message") ?? "Scene saved successfully",
                        path = resultData?.Value<string?>("path"),
                        name = resultData?.Value<string?>("name")
                    };
                    
                case "get_hierarchy":

                    var hierarchy = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);

                        //resultData?.ToObject<List<object>>() ?? new List<object>();
                    return new
                    {
                        success = true,
                        message = result?.Value<string?>("message") ?? "Retrieved scene hierarchy",
                        hierarchy = hierarchy
                    };
                    
                case "get_active":
                    return new
                    {
                        success = true,
                        message = resultData?.Value<string?>("message") ?? "Retrieved active scene information",
                        name = resultData?.Value<string?>("name"),
                        path = resultData?.Value<string?>("path"),
                        buildIndex = resultData?.Value<int?>("buildIndex"),
                        isDirty = resultData?.Value<bool?>("isDirty"),
                        isLoaded = resultData?.Value<bool?>("isLoaded"),
                        rootCount = resultData?.Value<int?>("rootCount")
                    };
                    
                case "get_build_settings":
                    var scenes = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);
                    //resultData?.ToObject<List<object>>() ?? new List<object>();

                    return new
                    {
                        success = true,
                        message = result?.Value<string?>("message") ?? "Retrieved build settings scenes",
                        scenes = scenes
                    };
                    
                default:
                    return new
                    {
                        success = true,
                        message = resultData?.ToString() ?? $"Action '{action}' completed",
                        data = resultData
                    };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ManageScene operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ManageScene operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing scene");
            return new
            {
                success = false,
                error = $"Failed to manage scene: {ex.Message}"
            };
        }
    }
}
