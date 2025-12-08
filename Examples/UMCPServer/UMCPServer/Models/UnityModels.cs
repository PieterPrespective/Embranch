using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UMCPServer.Models;

/// <summary>
/// Model representing a command sent to Unity
/// </summary>
public class UnityCommand
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonProperty("params")]
    public JObject? Params { get; set; }
}

/// <summary>
/// Model representing an acknowledgment response from Unity
/// (used for commands that have a delayed response)
/// </summary>
public class UnityAckResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string? Message { get; set; }
}

public class UnityResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonProperty("result")]
    public JObject? Result { get; set; }
    
    [JsonProperty("error")]
    public string? Error { get; set; }
    
    [JsonProperty("message")]
    public string? Message { get; set; }
}
