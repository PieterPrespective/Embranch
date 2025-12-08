using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using UMCPServer.Services;

namespace UMCPServer.Tools;

/// <summary>
/// Server-side tool for managing Unity assets including creation, modification, deletion, and search operations.
/// Provides comprehensive asset management capabilities through MCP interface.
/// </summary>
[McpServerToolType]
public class ManageAssetTool
{
    private readonly ILogger<ManageAssetTool> _logger;
    private readonly UnityConnectionService _unityConnection;

    public ManageAssetTool(ILogger<ManageAssetTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }

    [McpServerTool]
    [Description("Manage Unity assets - create, modify, delete, search, and manipulate assets within the Unity project")]
    public async Task<object> ManageAsset(
        [Required]
        [Description("Action to perform: 'import', 'create', 'modify', 'delete', 'duplicate', 'move', 'rename', 'search', 'get_info', 'create_folder', 'get_components'")]
        string action,

        [Description("Asset path relative to Assets/ directory. Required for most actions except search.")]
        string? path = null,

        [Description("Type of asset to create (e.g., 'material', 'scriptableobject', 'folder'). Required for 'create' action.")]
        string? assetType = null,

        [Description("Destination path for move/rename/duplicate operations")]
        string? destination = null,

        [Description("Properties to apply when creating or modifying assets. JSON object containing property-value pairs.")]
        string? properties = null,

        [Description("Search pattern for asset search (supports Unity search syntax)")]
        string? searchPattern = null,

        [Description("Filter by asset type for search (e.g., 'Material', 'Prefab', 'Texture2D')")]
        string? filterType = null,

        [Description("Filter search results to assets modified after this date (ISO 8601 format)")]
        string? filterDateAfter = null,

        [Description("Number of search results per page (default: 50)")]
        int pageSize = 50,

        [Description("Page number for paginated search results (default: 1)")]
        int pageNumber = 1,

        [Description("Whether to generate Base64-encoded preview images for assets (default: false)")]
        bool generatePreview = false,

        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ManageAsset called with action: {Action}, path: {Path}, assetType: {AssetType}",
                action, path, assetType);

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
            var validActions = new[] { "import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components" };
            if (!validActions.Contains(action))
            {
                return new
                {
                    success = false,
                    error = $"Invalid action '{action}'. Valid actions are: {string.Join(", ", validActions)}"
                };
            }

            // Validate required parameters based on action
            var validationResult = ValidateActionParameters(action, path, assetType, destination);
            if (!validationResult.IsValid)
            {
                return new
                {
                    success = false,
                    error = validationResult.ErrorMessage
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
            var parameters = BuildUnityParameters(action, path, assetType, destination, properties, 
                searchPattern, filterType, filterDateAfter, pageSize, pageNumber, generatePreview);

            // Send command to Unity
            var result = await _unityConnection.SendCommandAsync("manage_asset", parameters, cancellationToken);

            if (result == null)
            {
                return new
                {
                    success = false,
                    error = "No response received from Unity within the timeout period"
                };
            }

            Console.WriteLine("Raw Response: " + result.ToString());

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
            return action switch
            {
                "import" => HandleImportResponse(resultData),
                "create" => HandleCreateResponse(resultData),
                "modify" => HandleModifyResponse(resultData),
                "delete" => HandleDeleteResponse(resultData),
                "duplicate" => HandleDuplicateResponse(resultData),
                "move" or "rename" => HandleMoveRenameResponse(resultData),
                "search" => HandleSearchResponse(resultData),
                "get_info" => HandleGetInfoResponse(resultData),
                "create_folder" => HandleCreateFolderResponse(resultData),
                "get_components" => HandleGetComponentsResponse(resultData),
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
            _logger.LogWarning("ManageAsset operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ManageAsset operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing asset");
            return new
            {
                success = false,
                error = $"Failed to manage asset: {ex.Message}"
            };
        }
    }

    #region Parameter Validation

    /// <summary>
    /// Validates required parameters based on the action being performed.
    /// </summary>
    private static (bool IsValid, string? ErrorMessage) ValidateActionParameters(
        string action, string? path, string? assetType, string? destination)
    {
        return action switch
        {
            "create" => ValidateCreateParameters(path, assetType),
            "move" or "rename" => ValidateMoveRenameParameters(path, destination),
            "search" => (true, null), // Search doesn't require path
            var a when new[] { "modify", "delete", "duplicate", "get_info", "create_folder", "get_components", "import" }.Contains(a)
                => ValidatePathRequired(path, a),
            _ => (true, null)
        };
    }

    private static (bool IsValid, string? ErrorMessage) ValidateCreateParameters(string? path, string? assetType)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "'path' parameter is required for 'create' action.");
        
        if (string.IsNullOrWhiteSpace(assetType))
            return (false, "'assetType' parameter is required for 'create' action.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateMoveRenameParameters(string? path, string? destination)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "'path' parameter is required for move/rename operations.");
        
        if (string.IsNullOrWhiteSpace(destination))
            return (false, "'destination' parameter is required for move/rename operations.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidatePathRequired(string? path, string action)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, $"'path' parameter is required for '{action}' action.");

        return (true, null);
    }

    #endregion

    #region Unity Parameter Building

    /// <summary>
    /// Builds the parameter object to send to Unity based on the action and provided parameters.
    /// </summary>
    private static JObject BuildUnityParameters(
        string action, string? path, string? assetType, string? destination, object? properties,
        string? searchPattern, string? filterType, string? filterDateAfter, int pageSize, int pageNumber, bool generatePreview)
    {
        var parameters = new JObject
        {
            ["action"] = action
        };

        // Add common parameters
        if (!string.IsNullOrWhiteSpace(path))
            parameters["path"] = path;

        if (!string.IsNullOrWhiteSpace(assetType))
            parameters["assetType"] = assetType;

        if (!string.IsNullOrWhiteSpace(destination))
            parameters["destination"] = destination;

        if (properties != null)
        {
            Console.WriteLine($"Transform object type: {properties.GetType().Name}, value: {properties.ToString()}");

            //CASE : the LLM has given the component value as a JSON string - for some reason this is directly interpreted as a JsonElement by c#?
            if (properties is JsonElement elem)
            {
                // Convert JsonElement to appropriate .NET type
                object? converted = elem.ValueKind switch
                {
                    JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText()),
                    JsonValueKind.Array => JsonSerializer.Deserialize<List<object>>(elem.GetRawText()),
                    JsonValueKind.String => elem.GetString(),
                    JsonValueKind.Number => elem.GetInt32(), // or GetDouble() based on expected type
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
                properties = converted;
            }
            else if (properties is JToken token)
            {
                properties = SerializationUtility.ConvertJTokenToObjectSmart(token);
            }

            parameters["properties"] = ConvertToJToken(properties);

            //CASE : the LLM has given the component value as a JSON string
            if (parameters["properties"] is JValue || parameters?["properties"]?.Type == JTokenType.String)
            {
                parameters["transform"] = JObject.Parse((string)parameters?["properties"]);
            }
        }

        // Add search-specific parameters
        if (!string.IsNullOrWhiteSpace(searchPattern))
            parameters["searchPattern"] = searchPattern;

        if (!string.IsNullOrWhiteSpace(filterType))
            parameters["filterType"] = filterType;

        if (!string.IsNullOrWhiteSpace(filterDateAfter))
            parameters["filterDateAfter"] = filterDateAfter;

        if (action == "search")
        {
            parameters["pageSize"] = pageSize;
            parameters["pageNumber"] = pageNumber;
        }

        parameters["generatePreview"] = generatePreview;

        return parameters;
    }

        private static JToken ConvertToJToken(object value)
    {
        if (value is string jsonString)
        {
            try
            {
                return JToken.Parse(jsonString);
            }
            catch
            {
                return JToken.FromObject(value);
            }
        }
        return JToken.FromObject(value);
    }




    #endregion

    #region Response Handlers

    private static dynamic HandleImportResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset imported successfully",
            assetData = resultData
        };
    }

    private static dynamic HandleCreateResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset created successfully",
            path = resultData?.Value<string?>("path"),
            assetData = resultData
        };
    }

    private static dynamic HandleModifyResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset modified successfully",
            assetData = resultData
        };
    }

    private static dynamic HandleDeleteResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset deleted successfully"
        };
    }

    private static dynamic HandleDuplicateResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset duplicated successfully",
            newPath = resultData?.Value<string?>("path"),
            assetData = resultData
        };
    }

    private static dynamic HandleMoveRenameResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Asset moved/renamed successfully",
            newPath = resultData?.Value<string?>("path"),
            assetData = resultData
        };
    }

    private static dynamic HandleSearchResponse(JToken? resultData)
    {
        var assets = resultData?["assets"]?.ToObject<List<object>>() ?? new List<object>();
        var totalAssets = resultData?.Value<int?>("totalAssets") ?? 0;
        var pageSize = resultData?.Value<int?>("pageSize") ?? 0;
        var pageNumber = resultData?.Value<int?>("pageNumber") ?? 0;

        return new
        {
            success = true,
            message = $"Found {totalAssets} asset(s). Returning page {pageNumber} ({assets.Count} assets)",
            totalAssets = totalAssets,
            pageSize = pageSize,
            pageNumber = pageNumber,
            assets = assets
        };
    }

    private static dynamic HandleGetInfoResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = "Asset information retrieved",
            assetData = resultData
        };
    }

    private static dynamic HandleCreateFolderResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Folder created successfully",
            path = resultData?.Value<string?>("path"),
            assetData = resultData
        };
    }

    private static dynamic HandleGetComponentsResponse(JToken? resultData)
    {
        var components = resultData?.ToObject<List<object>>() ?? new List<object>();
        
        return new
        {
            success = true,
            message = $"Found {components.Count} component(s)",
            components = components
        };
    }

    #endregion
}