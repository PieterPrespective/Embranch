using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UMCPServer.Services;

namespace UMCPServer.Tools
{
    public abstract class Toolbase<T>
    {
        /// <summary>
        /// Logger instance for logging messages and errors
        /// </summary>
        protected readonly ILogger<T> _logger;

        /// <summary>
        /// Service for sending tool commands to unity
        /// </summary>
        protected readonly IUnityConnectionService _unityConnection;

        /// <summary>
        /// Service for receiving Unity State updates
        /// </summary>
        protected readonly IUnityStateConnectionService _stateConnection;

        public Toolbase(ILogger<T> logger, IUnityConnectionService unityConnection, IUnityStateConnectionService stateConnection)
        {
            _logger = logger;
            _unityConnection = unityConnection;
            _stateConnection = stateConnection;
        }
    }

    public static class ToolBaseUtility
    {
        public enum Runmode
        {
            Unknown,
            EditMode_Scene,
            EditMode_Prefab,
            PlayMode
        }

        public enum Context
        {
            Disconnected,
            Running,
            Switching,
            Compiling,
            UpdatingAssets,
            Testing
        }

        public struct UnityConnectionState : IEquatable<UnityConnectionState>
        { 
            /// <summary>
            /// Current Runmode of the Unity Application
            /// </summary>
            public Runmode CurrentRunMode;

            /// <summary>
            /// Current Context of the Unity Application
            /// </summary>
            public Context CurrentContext;

            /// <summary>
            /// Optional error message if the state retrieval failed
            /// </summary>
            public string ErrorMessage;

            public bool Equals(UnityConnectionState other)
            {
                return CurrentRunMode == other.CurrentRunMode && CurrentContext == other.CurrentContext;
            }

            override public string ToString()
            {
                return $"Runmode: {CurrentRunMode}, Context: {CurrentContext}, Error:{(string.IsNullOrEmpty(ErrorMessage) ? "None" : ErrorMessage)}";
            }
        }

        public struct AsyncToolCommandResult
        {
            public bool Success;
            public string ErrorMessage;
            public JObject? Response;
            public UnityConnectionState LastKnownState;

            public AsyncToolCommandResult(bool success, JObject? response, string errorMessage = "", UnityConnectionState? lastKnownState = null)
            {
                Success = success;
                Response = response;
                ErrorMessage = errorMessage;
                LastKnownState = lastKnownState ?? new UnityConnectionState() { CurrentRunMode = Runmode.Unknown, CurrentContext = Context.Disconnected, ErrorMessage = "No state retrieved" };
            }
        }

        /// <summary>
        /// Attempt to send a command to Unity when it is in the desired state(s)
        /// </summary>
        /// <typeparam name="T">Type of the sending tool</typeparam>
        /// <param name="commandType">command string send as tool marker</param>
        /// <param name="parameters">parameters send with the command</param>
        /// <param name="desiredStateOptions">connection states deemed valid for unity to receive this command</param>
        /// <param name="unityConnection">unity connection object</param>
        /// <param name="stateConnection">state connection object</param>
        /// <param name="logger">optional logger</param>
        /// <param name="checkIntervalMs">interval between state checks before sending the command</param>
        /// <param name="MaxNoOfIntervals">maximum no of intervals before failing</param>
        /// <param name="cancellationToken">cancellationtoken for the command async</param>
        /// <returns></returns>
        public static async Task<AsyncToolCommandResult> SendCommandAsyncWhenInRightState<T>(
            string commandType, 
            JObject? parameters, 
            UnityConnectionState[] desiredStateOptions, 
            IUnityConnectionService unityConnection, 
            IUnityStateConnectionService stateConnection, 
            ILogger<T> logger, 
            int checkIntervalMs = 3000, 
            int MaxNoOfIntervals = 10,
            CancellationToken cancellationToken = default)
        {
            var currentState = await RetryAndWaitUntilUnityInState(desiredStateOptions, unityConnection, stateConnection, logger, checkIntervalMs, MaxNoOfIntervals);

            if(!currentState.Item1)
            {
                return new AsyncToolCommandResult(
                    false, null, 
                    $"Command {commandType} failed. Unity did not reach the desired state. Last received Unity Client state {currentState.Item2}", 
                    currentState.Item2);
            }

            var response = await unityConnection.SendCommandAsync(commandType, parameters, cancellationToken);
            if (response == null)
            {
                return new AsyncToolCommandResult(
                    false, null,
                    $"Command {commandType} failed. No response was returned within timeout period. Last received Unity Client state {currentState.Item2}",
                    currentState.Item2);
            }

            // Check if the command was successful
            bool commandSuccess = response.Value<bool?>("success") ?? false;
            if (!commandSuccess)
            {
                return new AsyncToolCommandResult(
                    false, null,
                    $"Command {commandType} failed with error {response.Value<string?>("error") ?? "No Error"}. Last received Unity Client state {currentState.Item2}",
                    currentState.Item2);
            }

            return new AsyncToolCommandResult(true, response,"",currentState.Item2);
        }








        /// <summary>
        /// Retries requests util Unity is in the desired state or max attempts reached
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="targetState">the state were looking for</param>
        /// <param name="unityConnection">unity connection</param>
        /// <param name="stateConnection">state connection</param>
        /// <param name="logger">optional logger for debugging</param>
        /// <param name="checkIntervalMs">ms between state checks</param>
        /// <param name="MaxNoOfIntervals">maximum no of intervals before we return a fail</param>
        /// <returns></returns>
        public static async Task<(bool, UnityConnectionState)> RetryAndWaitUntilUnityInState<T>(
            UnityConnectionState[] targetStateOptions,
            IUnityConnectionService unityConnection, 
            IUnityStateConnectionService stateConnection, 
            ILogger<T> logger,
            int checkIntervalMs = 3000,
            int MaxNoOfIntervals = 10)
        {
            int attempts = 0;
            UnityConnectionState lastState = new UnityConnectionState() { CurrentRunMode = Runmode.Unknown, CurrentContext = Context.Disconnected };
            while (attempts < MaxNoOfIntervals)
            {
                lastState = await GetCurrentUnityState<T>(unityConnection, stateConnection, logger);
                for(int i = 0; i < targetStateOptions.Length; i++)
                {
                    var targetState = targetStateOptions[i];
                    if (lastState.Equals(targetState))
                    {
                        return (true, lastState);
                    }
                }

                logger?.LogInformation($"Unity not in desired state. Current State: {lastState.ToString()}. Retrying {attempts}/{MaxNoOfIntervals}  in {checkIntervalMs} ms...");
                await Task.Delay(checkIntervalMs);
                attempts++;
            }
            logger?.LogWarning("Max attempts reached. Unity did not reach the desired state.");
            return (false, new UnityConnectionState
            {
                CurrentRunMode = lastState.CurrentRunMode,
                CurrentContext = lastState.CurrentContext,
                ErrorMessage = "Max attempts reached. Unity did not reach the desired state."
            });
        }


        /// <summary>
        /// Returns the current state of the Unity application
        /// </summary>
        /// <typeparam name="T">type of tool used</typeparam>
        /// <param name="unityConnection">unity connection</param>
        /// <param name="stateConnection">unity state connection</param>
        /// <param name="logger">logger used for debugging</param>
        /// <returns>current unity connection state</returns>
        public static async Task<UnityConnectionState> GetCurrentUnityState<T>(IUnityConnectionService unityConnection, IUnityStateConnectionService stateConnection, ILogger<T>? logger)
        {
            try
            {
                logger?.LogInformation("Retrieving Unity Connection State");

                if (!unityConnection.IsConnected)
                {
                    logger?.LogInformation("Not connected to Unity. Attempting to connect...");
                    var connected = await unityConnection.ConnectAsync();
                    if (!connected)
                    {
                        logger?.LogWarning("Failed to connect to Unity.");
                        return new UnityConnectionState
                        {
                            CurrentRunMode = Runmode.Unknown,
                            CurrentContext = Context.Disconnected,
                            ErrorMessage = "Failed to connect to Unity."
                        };
                    }
                    logger?.LogInformation("Successfully connected to Unity.");
                }

                if (!stateConnection.IsConnected)
                {
                    logger?.LogInformation("Not connected to Unity State port. Attempting to connect...");
                    var stateConnected = await stateConnection.ConnectAsync();
                    if (!stateConnected)
                    {
                        logger?.LogWarning("Failed to connect to Unity State port.");
                        return new UnityConnectionState
                        {
                            CurrentRunMode = Runmode.Unknown,
                            CurrentContext = Context.Disconnected,
                            ErrorMessage = "Failed to connect to Unity State port."
                        };
                    }
                    logger?.LogInformation("Successfully connected to Unity State port.");
                }

                var initialState = stateConnection.CurrentUnityState;
                string initialRunmode = initialState?.Value<string>("runmode") ?? "Unknown";
                string initialContext = initialState?.Value<string>("context") ?? "Unknown";

                Runmode runmode = initialRunmode.ToLower() switch
                {
                    "editmode_scene" => Runmode.EditMode_Scene,
                    "editmode_prefab" => Runmode.EditMode_Prefab,
                    "playmode" => Runmode.PlayMode,
                    _ => Runmode.Unknown
                };
                
                Context context = initialContext.ToLower() switch
                {
                    "running" => Context.Running,
                    "switching" => Context.Switching,
                    "compiling" => Context.Compiling,
                    "updating_assets" => Context.UpdatingAssets,
                    "testing" => Context.Testing,
                    _ => Context.Disconnected
                };

                UnityConnectionState result = new UnityConnectionState
                {
                    CurrentRunMode = runmode,
                    CurrentContext = context,
                    ErrorMessage = string.Empty
                };

                logger?.LogInformation("Retrieved Unity state: {State}", result.ToString());

                return result;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error retrieving Unity state");
                return new UnityConnectionState
                {
                    CurrentRunMode = Runmode.Unknown,
                    CurrentContext = Context.Disconnected,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
