using Newtonsoft.Json.Linq;

namespace UMCPServer.Services;

/// <summary>
/// Interface for Unity state connection service
/// </summary>
public interface IUnityStateConnectionService : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the service is connected to Unity state port
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Gets the current Unity state
    /// </summary>
    JObject? CurrentUnityState { get; }
    
    /// <summary>
    /// Event fired when Unity state changes
    /// </summary>
    event Action<JObject> UnityStateChanged;
    
    /// <summary>
    /// Connects to Unity state port asynchronously
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    Task<bool> ConnectAsync();
    
    /// <summary>
    /// Disconnects from Unity state port
    /// </summary>
    void Disconnect();
}