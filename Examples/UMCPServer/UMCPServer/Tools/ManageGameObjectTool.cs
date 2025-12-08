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
/// Server-side tool for managing GameObjects in Unity scenes including creation, modification, deletion, and component management.
/// Provides comprehensive GameObject manipulation capabilities through MCP interface.
/// </summary>
[McpServerToolType]
public class ManageGameObjectTool : Toolbase<ManageGameObjectTool>
{
    public ManageGameObjectTool(
        ILogger<ManageGameObjectTool> logger, 
        IUnityConnectionService unityConnection, 
        IUnityStateConnectionService _stateConnection) : base(logger, unityConnection, _stateConnection)
    {
        
    }

    [McpServerTool]
    [Description("Manage Unity GameObjects - create, modify, delete, find, and manipulate GameObjects and their components in the current scene")]
    public async Task<object> ManageGameObject(
        [Required]
        [Description("Action to perform: 'create', 'modify', 'delete', 'find', 'get_components', 'add_component', 'remove_component', 'set_component_property'")]
        string action,

        [Description("Target GameObject identifier - can be name (string), path (string), or instanceID (integer). Used for find, modify, delete, and component operations.")]
        string? target = null,

        [Description("Search method for finding GameObjects: 'by_id', 'by_name', 'by_path', 'by_tag','by_layer','by_component' or 'by_id_or_name_or_path' (default)")]
        string searchMethod = "by_id_or_name_or_path",

        [Description("Required for search function : Whether to return all matches, or just one - default is return just one (i.e. false)")]
        bool? findAll = false,

        [Description("Name for the GameObject (required for create, optional for modify)")]
        string? name = null,

        [Description("Prefab path for instantiation (optional for create action)")]
        string? prefabPath = null,

        [Description("Primitive type to create: 'Cube', 'Sphere', 'Capsule', 'Cylinder', 'Plane', 'Quad' (optional for create)")]
        string? primitiveType = null,

        [Description("Parent GameObject identifier (optional for create/modify)")]
        string? parent = null,

        [Description("Tag to assign to the GameObject (optional for create/modify/find)")]
        string? tag = null,

        [Description("Layer name to assign to the GameObject (optional for create/modify/find)")]
        string? layer = null,

        [Description("Active state of the GameObject (optional for modify)")]
        bool? isActive = null,

        [Description("Transform properties as JSON object with position, rotation, and/or scale (optional for create/modify)")]
        string? transform = null,

        [Description("Component type name for add/remove/set operations (e.g., 'Rigidbody', 'BoxCollider')")]
        string? componentName = null,

        [Description("Component properties to set as JSON object (for set_component_property action)")]
        string? componentProperties = null,

        [Description("Component type to search for (for find action with type search method)")]
        string? componentType = null,

        [Description("Maximum number of results to return for find action (default: 100)")]
        int maxResults = 100,

        [Description("Whether to save created GameObject as prefab (optional for create)")]
        bool saveAsPrefab = false,

        [Description("Path to save prefab at (required if saveAsPrefab is true)")]
        string? savePrefabPath = null,

        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ManageGameObject called with action: {Action}, target: {Target}",
                action, target);

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
            var validActions = new[] { "create", "modify", "delete", "find", "get_components", 
                                       "add_component", "remove_component", "set_component_property" };
            if (!validActions.Contains(action))
            {
                return new
                {
                    success = false,
                    error = $"Invalid action '{action}'. Valid actions are: {string.Join(", ", validActions)}"
                };
            }

            // Validate required parameters based on action
            var validationResult = ValidateActionParameters(action, target, name, componentName);
            if (!validationResult.IsValid)
            {
                return new
                {
                    success = false,
                    error = validationResult.ErrorMessage
                };
            }

            // Check for prefab redirection
            if (ShouldRedirectToManageAsset(action, target))
            {
                return await RedirectToManageAsset(action, target, componentName, componentProperties, cancellationToken);
            }

            // Prepare parameters for Unity
            var parameters = BuildUnityParameters(action, target, searchMethod, name, prefabPath, primitiveType,
                parent, tag, layer, isActive, transform, componentName, componentProperties, componentType,
                maxResults, saveAsPrefab, savePrefabPath, findAll);


            AsyncToolCommandResult toolResult = await ToolBaseUtility.SendCommandAsyncWhenInRightState(
               commandType: "manage_gameobject",
               parameters: parameters,
               desiredStateOptions: new ToolBaseUtility.UnityConnectionState[] {
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Scene },
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Prefab },
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



            // Extract the result
            var resultData = result["data"];

            // Handle different action responses
            return action switch
            {
                "create" => HandleCreateResponse(resultData),
                "modify" => HandleModifyResponse(resultData),
                "delete" => HandleDeleteResponse(result, resultData),
                "find" => HandleFindResponse(resultData),
                "get_components" => HandleGetComponentsResponse(resultData),
                "add_component" => HandleAddComponentResponse(result, resultData),
                "remove_component" => HandleRemoveComponentResponse(resultData),
                "set_component_property" => HandleSetComponentPropertyResponse(result, resultData),
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
            _logger.LogWarning("ManageGameObject operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ManageGameObject operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing GameObject");
            return new
            {
                success = false,
                error = $"Failed to manage GameObject: {ex.Message}"
            };
        }
    }

    #region Parameter Validation

    /// <summary>
    /// Validates required parameters based on the action being performed.
    /// </summary>
    private static (bool IsValid, string? ErrorMessage) ValidateActionParameters(
        string action, object? target, string? name, string? componentName)
    {
        return action switch
        {
            "create" => ValidateCreateParameters(name),
            "modify" or "delete" or "get_components" => ValidateTargetRequired(target, action),
            "add_component" or "remove_component" => ValidateComponentOperation(target, componentName, action),
            "set_component_property" => ValidateSetComponentProperty(target, componentName),
            "find" => (true, null), // Find doesn't require specific parameters
            _ => (true, null)
        };
    }

    private static (bool IsValid, string? ErrorMessage) ValidateCreateParameters(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "'name' parameter is required for 'create' action.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateTargetRequired(object? target, string action)
    {
        if (target == null)
            return (false, $"'target' parameter is required for '{action}' action.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateComponentOperation(
        object? target, string? componentName, string action)
    {
        if (target == null)
            return (false, $"'target' parameter is required for '{action}' action.");

        if (string.IsNullOrWhiteSpace(componentName))
            return (false, $"'componentName' parameter is required for '{action}' action.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateSetComponentProperty(
        object? target, string? componentName)
    {
        if (target == null)
            return (false, "'target' parameter is required for 'set_component_property' action.");

        if (string.IsNullOrWhiteSpace(componentName))
            return (false, "'componentName' parameter is required for 'set_component_property' action.");

        return (true, null);
    }

    #endregion

    #region Prefab Redirection

    /// <summary>
    /// Determines if the action should be redirected to ManageAsset for prefab operations.
    /// </summary>
    private static bool ShouldRedirectToManageAsset(string action, object? target)
    {
        if (target is string targetStr && targetStr.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return action == "modify" || action == "set_component_property";
        }
        return false;
    }

    /// <summary>
    /// Redirects prefab modification operations to ManageAsset tool.
    /// </summary>
    private async Task<object> RedirectToManageAsset(
        string action, object? target, string? componentName, object? componentProperties,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Redirecting {Action} for prefab '{Target}' to ManageAsset", action, target);

        var assetParams = new JObject
        {
            ["action"] = "modify",
            ["path"] = target?.ToString()
        };

        // Build properties for asset modification
        var properties = new JObject();
        if (action == "set_component_property" && !string.IsNullOrWhiteSpace(componentName))
        {
            if (componentProperties is string jsonStr)
            {
                try
                {
                    properties[componentName] = JObject.Parse(jsonStr);
                }
                catch
                {
                    properties[componentName] = JToken.FromObject(componentProperties ?? new JObject());
                }
            }
            else
            {
                properties[componentName] = JToken.FromObject(componentProperties ?? new JObject());
            }
        }
        else if (componentProperties != null)
        {
            properties = componentProperties is string str ? JObject.Parse(str) : JObject.FromObject(componentProperties);
        }

        assetParams["properties"] = properties;

        // Send to manage_asset command
        var result = await _unityConnection.SendCommandAsync("manage_asset", assetParams, cancellationToken);

        if (result == null)
        {
            return new
            {
                success = false,
                error = "No response received from Unity for prefab modification"
            };
        }

        return result;
    }

    #endregion

    #region Unity Parameter Building

    /// <summary>
    /// Builds the parameter object to send to Unity based on the action and provided parameters.
    /// </summary>
    private static JObject BuildUnityParameters(
        string action, object? target, string searchMethod, string? name, string? prefabPath, string? primitiveType,
        object? parent, string? tag, string? layer, bool? isActive, object? transform,
        string? componentName, object? componentProperties, string? componentType,
        int maxResults, bool saveAsPrefab, string? savePrefabPath, bool? findAll)
    {
        var parameters = new JObject
        {
            ["action"] = action
        };

        // Add target parameter
        if (target != null)
        {
            if (target is int intTarget)
                parameters["target"] = intTarget;
            else
                parameters["target"] = target.ToString();
        }

        if(findAll.HasValue)
        {
            parameters["findAll"] = findAll.Value;
        }

        parameters["searchMethod"] = searchMethod;

        // Add common parameters
        if (!string.IsNullOrWhiteSpace(name))
            parameters["name"] = name;

        if (!string.IsNullOrWhiteSpace(prefabPath))
            parameters["prefabPath"] = prefabPath;

        if (!string.IsNullOrWhiteSpace(primitiveType))
            parameters["primitiveType"] = primitiveType;

        if (parent != null)
        {
            if (parent is int intParent)
                parameters["parent"] = intParent;
            else
                parameters["parent"] = parent.ToString();
        }

        if (!string.IsNullOrWhiteSpace(tag))
            parameters["tag"] = tag;

        if (layer != null)
            parameters["layer"] = layer;

        if (isActive.HasValue)
            parameters["isActive"] = isActive.Value;

        if (transform != null)
        {
            Console.WriteLine($"Transform object type: {transform.GetType().Name}, value: {transform.ToString()}");

            //CASE : the LLM has given the component value as a JSON string - for some reason this is directly interpreted as a JsonElement by c#?
            if (transform is JsonElement elem)
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
                transform = converted;
            }
            else if (transform is JToken token)
            {
                transform = SerializationUtility.ConvertJTokenToObjectSmart(token);
            }

            
            //parameters["transform"] = transform.GetType().Name + " == " + transform.ToString();

            parameters["transform"] = ConvertToJToken(transform);

            //CASE : the LLM has given the component value as a JSON string
            if(parameters["transform"] is JValue || parameters["transform"].Type == JTokenType.String)
            {
                parameters["transform"] = JObject.Parse((string)parameters["transform"]);
            }

            //Console.WriteLine($"Position after conversion: {parameters["transform"]?["position"]?.Type.ToString() ?? "NULL"}");

            if (parameters["transform"]?["position"] != null && !(parameters["transform"]?["position"] is JArray))
            {
                
                //Console.WriteLine($"position not formatted as JArray, so likely set as object");
                if (parameters["transform"]?["position"] is JObject posObj)
                {
                    float x = posObj.Value<float?>("x") ?? 0f;
                    float y = posObj.Value<float?>("y") ?? 0f;
                    float z = posObj.Value<float?>("z") ?? 0f;
                    parameters["transform"]["position"] = new JArray(x, y, z);
                }
            }

            if(parameters["transform"]?["rotation"] != null && !(parameters["transform"]?["rotation"] is JArray))
            {
                //Console.WriteLine($"rotation not formatted as JArray, so likely set as object");
                if (parameters["transform"]?["rotation"] is JObject rotObj)
                {
                    float x = rotObj.Value<float?>("x") ?? 0f;
                    float y = rotObj.Value<float?>("y") ?? 0f;
                    float z = rotObj.Value<float?>("z") ?? 0f;
                    parameters["transform"]["rotation"] = new JArray(x, y, z);
                }
            }

            if(parameters["transform"]?["scale"] != null && !(parameters["transform"]?["scale"] is JArray))
            {
                //Console.WriteLine($"scale not formatted as JArray, so likely set as object");
                if (parameters["transform"]?["scale"] is JObject rotObj)
                {
                    float x = rotObj.Value<float?>("x") ?? 0f;
                    float y = rotObj.Value<float?>("y") ?? 0f;
                    float z = rotObj.Value<float?>("z") ?? 0f;
                    parameters["transform"]["scale"] = new JArray(x, y, z);
                }
            }
            
        }

        if (!string.IsNullOrWhiteSpace(componentName))
            parameters["componentName"] = componentName;

        if (componentProperties != null)
        {

            //CASE : the LLM has given the component value as a JSON string - for some reason this is directly interpreted as a JsonElement by c#?
            if (componentProperties is JsonElement elem)
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
                componentProperties = converted;
            }
            else if (transform is JToken token)
            {
                componentProperties = SerializationUtility.ConvertJTokenToObjectSmart(token);
            }

            //parameters["transform"] = transform.GetType().Name + " == " + transform.ToString();

            parameters["componentProperties"] = ConvertToJToken(componentProperties);

            //CASE : the LLM has given the component value as a JSON string
            if (parameters["componentProperties"] is JValue)
            {
                parameters["componentProperties"] = JObject.Parse((string)parameters["componentProperties"]);
            }






            //parameters["componentProperties"] = ConvertToJToken(componentProperties);
        }

        if (!string.IsNullOrWhiteSpace(componentType))
            parameters["componentType"] = componentType;

        if (action == "find")
        {
            parameters["maxResults"] = maxResults;
        }

        if (saveAsPrefab)
        {
            parameters["saveAsPrefab"] = true;
            if (!string.IsNullOrWhiteSpace(savePrefabPath))
                parameters["prefabPath"] = savePrefabPath;
        }

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

    private static dynamic HandleCreateResponse(JToken? resultData)
    {
        //Console.WriteLine("Raw Rsultdata: " +  resultData.ToString());

        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "GameObject created successfully",
            gameObjectData = SerializationUtility.ConvertJTokenToObjectSmart(resultData!)
        };
    }

    private static dynamic HandleModifyResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "GameObject modified successfully",
            gameObjectData = SerializationUtility.ConvertJTokenToObjectSmart(resultData!)
        };
    }

    private static dynamic HandleDeleteResponse(JToken? baseData, JToken? resultData)
    {
        return new
        {
            success = true,
            message = baseData?.Value<string?>("message") ?? "GameObject deleted successfully",
            data = SerializationUtility.ConvertJTokenToObjectSmart(resultData!)
        };
    }

    private static dynamic HandleFindResponse(JToken? resultData)
    {
        var gameObjects = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);
        int noOfGO = resultData?.ToObject<List<object>>()?.Count ?? 0;

        return new
        {
            success = true,
            message = $"Found {noOfGO} GameObject(s)",
            gameObjects = gameObjects
        };
    }

    private static dynamic HandleGetComponentsResponse(JToken? resultData)
    {
        var components = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);
        int componentCount = resultData?.ToObject<List<object>>()?.Count ?? 0;

        return new
        {
            success = true,
            message = $"Found {componentCount} component(s)",
            components = components
        };
    }

    private static dynamic HandleAddComponentResponse(JToken? baseData, JToken? resultData)
    {
        var gameObject = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);

        return new
        {
            success = true,
            message = baseData?.Value<string?>("message") ?? "Component added successfully",
            gameObjectData = gameObject
        };
    }

    private static dynamic HandleRemoveComponentResponse(JToken? resultData)
    {
        return new
        {
            success = true,
            message = resultData?.Value<string?>("message") ?? "Component removed successfully"
        };
    }

    private static dynamic HandleSetComponentPropertyResponse(JToken? baseData, JToken? resultData)
    {
        var gameObject = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);

        return new
        {
            success = true,
            message = baseData?.Value<string?>("message") ?? "Component property set successfully",
            gameObjectData = gameObject
        };
    }

    #endregion
}