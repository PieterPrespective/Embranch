using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using UMCPServer.Services;
using static UMCPServer.Tools.ToolBaseUtility;

namespace UMCPServer.Tools;

[McpServerToolType]
public class ForceUpdateEditorTool : Toolbase<ForceUpdateEditorTool>
{
    public ForceUpdateEditorTool(
        ILogger<ForceUpdateEditorTool> logger, 
        IUnityConnectionService unityConnection,
        IUnityStateConnectionService stateConnection) : base(logger, unityConnection, stateConnection)
    {
        
    }
    
    [McpServerTool]
    [Description("Forces the Unity Editor to update regardless of whether the application has focus. If in PlayMode, reverts to EditMode first. Waits for Unity to reach EditMode_Running state or times out after 30 seconds.")]
    public async Task<object> ForceUpdateEditor(
        [Description("Timeout in milliseconds to wait for Unity to reach EditMode_Running state. Default is 30000 (30 seconds).")]
        int timeoutMilliseconds = 30000,
        CancellationToken cancellationToken = default)
    {
        try
        {

            AsyncToolCommandResult toolResult =  await ToolBaseUtility.SendCommandAsyncWhenInRightState(
                commandType: "force_update_editor",
                parameters: null,
                desiredStateOptions: new ToolBaseUtility.UnityConnectionState[] {
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Scene },
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Prefab },
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.PlayMode },
                },
                unityConnection: _unityConnection,
                stateConnection: _stateConnection,
                logger: _logger,
                cancellationToken: cancellationToken);

            if(!toolResult.Success)
            {
                return new
                {
                    success = false,
                    error = toolResult.ErrorMessage ?? "Unknown error sending command to Unity"
                };
            }

            var result = toolResult.Response;

            // Get the initial action that was taken
            var commandResult = result["data"];
            string? action = commandResult?.Value<string>("action");
            
            _logger.LogInformation("Force update command executed successfully. Action: {Action}, current state: {state}", action ?? "unknown", _stateConnection.CurrentUnityState?.ToString() ?? "NO_STATE");
            
            // Now wait for Unity to reach EditMode_Running state
            var startTime = DateTime.UtcNow;
            
            // Create timeout cancellation token
            using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            var tcs = new TaskCompletionSource<JObject>();
            


            // Subscribe to state changes
            void OnStateChanged(JObject newState)
            {
                string? runmode = newState.Value<string>("runmode");
                string? context = newState.Value<string>("context");
                
                _logger.LogDebug("State change received: runmode={Runmode}, context={Context}", runmode, context);
                
                // Check if we've reached the desired state (EditMode with Running context)
                if (IsEditModeRunning(runmode, context))
                {
                    _logger.LogInformation("Unity reached EditMode_Running state");
                    tcs.TrySetResult(newState);
                }
            }
            
            _stateConnection.UnityStateChanged += OnStateChanged;
            
            try
            {
                // Add 1 second wait to allow asset database updates to start
                _logger.LogInformation("Waiting 1 second for asset database updates to start...");
                await Task.Delay(1000, linkedCts.Token);
                
                // Implement retry loop for checking EditMode_Running state
                const int maxRetries = 5;
                int retryCount = 0;
                
                while (retryCount < maxRetries)
                {
                    _logger.LogInformation("rety active state check {RetryCount}/{MaxRetries} = {state}", retryCount + 1, maxRetries, _stateConnection.CurrentUnityState?.ToString() ?? "NO_STATE");
                    var currentState = _stateConnection.CurrentUnityState;
                    if (currentState != null && IsEditModeRunning(
                        currentState.Value<string>("runmode"), 
                        currentState.Value<string>("context")))
                    {
                        var waitTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        _logger.LogInformation("Unity reached EditMode_Running state after {RetryCount} retries", retryCount);
                        
                        return new
                        {
                            success = true,
                            message = $"Unity Editor force update completed successfully after {retryCount} retries",
                            initialState = new
                            {
                                runmode = toolResult.LastKnownState.CurrentRunMode.ToString(),//initialRunmode,
                                context = toolResult.LastKnownState.CurrentContext.ToString()//initialContext
                            },
                            finalState = new
                            {
                                runmode = currentState.Value<string>("runmode"),
                                context = currentState.Value<string>("context"),
                                timestamp = currentState.Value<string>("timestamp")
                            },
                            action = action,
                            waitTimeMs = (int)waitTime,
                            retries = retryCount
                        };
                    }
                    
                    if (retryCount < maxRetries - 1)
                    {
                        _logger.LogDebug("Unity not in EditMode_Running state yet (retry {RetryCount}/{MaxRetries}). Current state: runmode={Runmode}, context={Context}", 
                            retryCount + 1, maxRetries, 
                            currentState?.Value<string>("runmode") ?? "unknown", 
                            currentState?.Value<string>("context") ?? "unknown");
                        
                        // Wait before next retry (exponential backoff with max 2 seconds)
                        var retryDelay = Math.Min(500 * Math.Pow(2, retryCount), 2000);
                        await Task.Delay((int)retryDelay, linkedCts.Token);
                    }
                    
                    retryCount++;
                }
                
                // Wait for the desired state or timeout
                var completedTask = await Task.WhenAny(
                    tcs.Task, 
                    Task.Delay(Timeout.Infinite, linkedCts.Token));
                
                if (completedTask == tcs.Task)
                {
                    var finalState = await tcs.Task;
                    var waitTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    _logger.LogInformation("Unity reached EditMode_Running state after {WaitTime}ms", waitTime);
                    
                    return new
                    {
                        success = true,
                        message = $"Unity Editor force update completed after {waitTime:F0}ms",
                        initialState = new
                        {
                            runmode = toolResult.LastKnownState.CurrentRunMode.ToString(),//initialRunmode,
                            context = toolResult.LastKnownState.CurrentContext.ToString()//initialContext
                        },
                        finalState = new
                        {
                            runmode = finalState.Value<string>("runmode"),
                            context = finalState.Value<string>("context"),
                            timestamp = finalState.Value<string>("timestamp")
                        },
                        action = action,
                        waitTimeMs = (int)waitTime
                    };
                }
                
                // Timeout occurred
                var timeoutState = _stateConnection.CurrentUnityState;
                return new
                {
                    success = false,
                    error = $"Timeout waiting for Unity to reach EditMode_Running state after {timeoutMilliseconds}ms",
                    initialState = new
                    {
                        runmode = toolResult.LastKnownState.CurrentRunMode.ToString(),//initialRunmode,
                        context = toolResult.LastKnownState.CurrentContext.ToString()//initialContext
                    },
                    currentState = new
                    {
                        runmode = timeoutState?.Value<string>("runmode"),
                        context = timeoutState?.Value<string>("context"),
                        timestamp = timeoutState?.Value<string>("timestamp")
                    },
                    action = action,
                    timeoutMs = timeoutMilliseconds
                };
            }
            finally
            {
                _stateConnection.UnityStateChanged -= OnStateChanged;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ForceUpdateEditor operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ForceUpdateEditor operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ForceUpdateEditor");
            return new
            {
                success = false,
                error = $"Failed to force update Unity Editor: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Checks if Unity is in EditMode with Running context
    /// </summary>
    private static bool IsEditModeRunning(string? runmode, string? context)
    {
        return (runmode == "EditMode_Scene" || runmode == "EditMode_Prefab") && 
               context == "Running";
    }
}
