using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;

namespace UMCPServer.Tools;

[McpServerToolType]
public class GetUnityClientStateTool
{
    private readonly ILogger<GetUnityClientStateTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    private readonly UnityStateConnectionService _stateConnection;
    
    public GetUnityClientStateTool(ILogger<GetUnityClientStateTool> logger, 
        UnityConnectionService unityConnection,
        UnityStateConnectionService stateConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
        _stateConnection = stateConnection;
    }
    
    [McpServerTool]
    [Description("Returns the current state of the connected Unity3D Client including runmode (EditMode_Scene, EditMode_Prefab, PlayMode) and context (Running, Switching, Testing, Compiling, UpdatingAssets).")]
    public async Task<object> GetUnityClientState(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting Unity client state");
            
            // Check if state connection is available
            if (!_stateConnection.IsConnected && !await _stateConnection.ConnectAsync())
            {
                // Fall back to command connection if state connection fails
                _logger.LogWarning("State connection not available, falling back to command connection");
                
                if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
                {
                    return new
                    {
                        success = false,
                        error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                    };
                }
                
                // Get state via command
                var response = await _unityConnection.SendCommandAsync("get_unity_state", null, cancellationToken);
                if (response != null)
                {
                    return new
                    {
                        success = true,
                        message = "Unity client state retrieved via command",
                        runmode = response.Value<string>("runmode"),
                        context = response.Value<string>("context"),
                        canModifyProjectFiles = response.Value<bool?>("canModifyProjectFiles"),
                        isEditorResponsive = response.Value<bool?>("isEditorResponsive"),
                        timestamp = response.Value<string>("timestamp")
                    };
                }
            }
            
            // Try to get current state from state connection
            var cachedState = _stateConnection.CurrentUnityState;
            
            if (cachedState != null)
            {
                return new
                {
                    success = true,
                    message = "Unity client state retrieved from state connection",
                    runmode = cachedState.Value<string>("runmode"),
                    context = cachedState.Value<string>("context"),
                    canModifyProjectFiles = cachedState.Value<bool?>("canModifyProjectFiles"),
                    isEditorResponsive = cachedState.Value<bool?>("isEditorResponsive"),
                    timestamp = cachedState.Value<string>("timestamp"),
                    lastChange = SerializationUtility.ConvertJTokenToObjectSmart(cachedState["lastChange"]!)
                };
            }
            
            // If no state available from state connection, try command connection
            _logger.LogInformation("No cached state available, requesting via command");
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Failed to connect to Unity for state retrieval"
                };
            }
            
            var stateResponse = await _unityConnection.SendCommandAsync("get_unity_state", null, cancellationToken);
            if (stateResponse != null)
            {
                return new
                {
                    success = true,
                    message = "Unity client state retrieved via command",
                    runmode = stateResponse.Value<string>("runmode"),
                    context = stateResponse.Value<string>("context"),
                    canModifyProjectFiles = stateResponse.Value<bool?>("canModifyProjectFiles"),
                    isEditorResponsive = stateResponse.Value<bool?>("isEditorResponsive"),
                    timestamp = stateResponse.Value<string>("timestamp")
                };
            }
            
            return new
            {
                success = false,
                error = "Failed to retrieve Unity client state"
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetUnityClientState operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("GetUnityClientState operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Unity client state");
            return new
            {
                success = false,
                error = $"Failed to get Unity client state: {ex.Message}"
            };
        }
    }
}
