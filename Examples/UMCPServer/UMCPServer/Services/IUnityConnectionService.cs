using Newtonsoft.Json.Linq;

namespace UMCPServer.Services;

/// <summary>
/// Interface for Unity connection service
/// </summary>
public interface IUnityConnectionService : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the service is connected to Unity
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Connects to Unity asynchronously
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    Task<bool> ConnectAsync();
    
    /// <summary>
    /// Disconnects from Unity
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Sends a command to Unity asynchronously
    /// </summary>
    /// <param name="commandType">The type of command to send</param>
    /// <param name="parameters">Optional parameters for the command</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="_logResult">optional - whether to log the result upon reception</param>
    /// <returns>Response from Unity</returns>
    Task<JObject?> SendCommandAsync(string commandType, JObject? parameters, CancellationToken cancellationToken = default, bool _logResult = false);
}