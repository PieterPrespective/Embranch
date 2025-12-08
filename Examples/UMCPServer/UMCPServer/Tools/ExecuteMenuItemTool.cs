using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class ExecuteMenuItemTool
{
    private readonly ILogger<ExecuteMenuItemTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public ExecuteMenuItemTool(ILogger<ExecuteMenuItemTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Execute a Unity Editor menu item by its path. Can also retrieve available menu items.")]
    public async Task<object> ExecuteMenuItem(
        [Description("The action to perform. Options: 'execute' (default), 'get_available_menus'")]
        string? action = "execute",
        
        [Description("The menu item path to execute (e.g., 'GameObject/Create Empty', 'Window/General/Console'). Required for 'execute' action. Used as filter for the 'get_available_menus' action (only menuitems containing filter string will be returned)")]
        string? menuPath = null,
        
        [Description("Optional alias for common menu items (not implemented yet)")]
        string? alias = null,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ExecuteMenuItem called with action: {Action}, menuPath: {MenuPath}", action, menuPath);
            
            // Validate parameters
            action = string.IsNullOrWhiteSpace(action) ? "execute" : action.ToLower();
            
            if (action == "execute" && string.IsNullOrWhiteSpace(menuPath))
            {
                return new
                {
                    success = false,
                    error = "Required parameter 'menuPath' is missing or empty for 'execute' action."
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
            
            if (!string.IsNullOrWhiteSpace(menuPath))
            {
                parameters["menu_path"] = menuPath;
            }
            
            if (!string.IsNullOrWhiteSpace(alias))
            {
                parameters["alias"] = alias;
            }
            
            // Send command to Unity
            var result = await _unityConnection.SendCommandAsync("execute_menu_item", parameters, cancellationToken);
            
            //Console.WriteLine("ExecuteMenuItem result: " + result.ToString());

            if (result == null)
            {
                return new
                {
                    success = false,
                    error = "No response received from Unity within the timeout period"
                };
            }
            
            // Check if the response indicates success or error
            string? status = result.Value<string?>("status");
            
            if (status == "error")
            {
                return new
                {
                    success = false,
                    error = result.Value<string?>("error") ?? "Unknown error occurred"
                };
            }
            
            // Extract the result
            var resultData = result["data"];
            
            if (action == "get_available_menus")
            {
                // For get_available_menus, return the list of menu items
                var menuItems = SerializationUtility.ConvertJTokenToObjectSmart(resultData!);
                //var menuItems = resultData?.ToObject<List<string>>() ?? new List<string>();
                return new
                {
                    success = true,
                    message = result?.Value<string?>("message") ?? "Available menu items retrieved",
                    menuItems = menuItems
                };
            }
            else
            {
                // For execute action, return the success message
                return new
                {
                    success = true,
                    message = result?.Value<string?>("message") ?? $"Menu item '{menuPath}' execution attempted"
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ExecuteMenuItem operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ExecuteMenuItem operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing menu item");
            return new
            {
                success = false,
                error = $"Failed to execute menu item: {ex.Message}"
            };
        }
    }
}
