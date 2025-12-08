namespace UMCPServer.Models;

public class ServerConfiguration
{
    // Network settings
    public string UnityHost { get; set; } = "localhost";
    public int UnityPort { get; set; } = 6400;
    public int UnityStatePort { get; set; } = 6401; // Separate port for state updates
    public int McpPort { get; set; } = 6500;
    
    // Connection settings
    public double ConnectionTimeoutSeconds { get; set; } = 86400.0; // 24 hours
    public int BufferSize { get; set; } = 16 * 1024 * 1024; // 16MB
    
    // Server settings
    public int MaxRetries { get; set; } = 3;
    public double RetryDelaySeconds { get; set; } = 1.0;
    
    // Environment settings
    /// <summary>
    /// Indicates if the server is running inside a Docker container.
    /// This affects network connectivity behavior and logging.
    /// </summary>
    public bool IsRunningInContainer { get; set; } = false;
}
