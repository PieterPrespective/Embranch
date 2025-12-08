using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using UMCPServer.Services;

namespace UMCPServer.Tools;

public class ConsoleLogResult
{
    [JsonProperty("success")]
    public bool Success;

    [JsonProperty("message")]
    public string Message;

    [JsonProperty("entries")]
    public List<ConsoleLogEntry> Entries;

    [JsonProperty("count")]
    public int Count;
}



public class ConsoleLogEntry
{
    [JsonProperty("type")]
    public string Type;

    [JsonProperty("message")]
    public string Message;

    [JsonProperty("file")]
    public string File;

    [JsonProperty("line")]
    public int Line;

    [JsonProperty("stackTrace")]
    public string StackTrace;
}


[McpServerToolType]
public class ReadConsoleTool
{
    private readonly ILogger<ReadConsoleTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    


    public ReadConsoleTool(ILogger<ReadConsoleTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Read Unity Editor console log entries. Can retrieve specific types of logs, filter by text, and optionally clear the console.")]
    public async Task<object> ReadConsole(
        [Description("Action to perform: 'get' (default) to retrieve logs, or 'clear' to clear the console")]
        string action = "get",
        
        [Description("Types of logs to retrieve (when action is 'get'). Can be: 'error', 'warning', 'log', or 'all'. Default is ['error', 'warning', 'log']")]
        string[]? types = null,
        
        [Description("Maximum number of log entries to return")]
        int? count = null,
        
        [Description("Filter logs by text content (case-insensitive)")]
        string? filterText = null,
        
        [Description("Return format: 'detailed' (default) returns structured data, 'plain' returns just messages")]
        string format = "detailed",
        
        [Description("Include stack traces in the output (default: true)")]
        bool includeStacktrace = true,
        CancellationToken cancellationToken = default,
        bool returnAsCastObject = false)
    {
        try
        {
            _logger.LogInformation("Reading Unity console with action: {Action}", action);
            
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                };
            }
            
            // Build parameters for the Unity command
            var parameters = new JObject
            {
                ["action"] = action
            };
            
            if (action == "get")
            {
                // Add 'get' specific parameters
                if (types != null && types.Length > 0)
                {
                    parameters["types"] = new JArray(types);
                }
                
                if (count.HasValue)
                {
                    parameters["count"] = count.Value;
                }
                
                if (!string.IsNullOrEmpty(filterText))
                {
                    parameters["filterText"] = filterText;
                }
                
                parameters["format"] = format;
                parameters["includeStacktrace"] = includeStacktrace;
            }
            
            // Send command to Unity
            var response = await _unityConnection.SendCommandAsync("read_console", parameters, cancellationToken);
            
            if (response == null)
            {
                return new
                {
                    success = false,
                    error = "Failed to get response from Unity"
                };
            }
            
            // Extract the response data
            var status = response.Value<string>("status");
            if (status == "error")
            {
                return new
                {
                    success = false,
                    error = response.Value<string>("error") ?? "Unknown error from Unity"
                };
            }
            
            // For 'clear' action, return simple success message
            if (action == "clear")
            {
                // For clear action, message might be at root level or in result
                var clearMessage = response.Value<string>("message") ?? response["result"]?.Value<string>("message");
                return new
                {
                    success = true,
                    message = clearMessage ?? "Console cleared successfully"
                };
            }
            
            // For 'get' action, return the log entries - Fix: data is nested in result object
            var result = response["result"];
            var message = response.Value<string>("message");

            //var dataWLinebreaks = result?.Value<string>("data");
            //dataWLinebreaks = dataWLinebreaks.Replace('\n', ' ').Replace('\r', ' ');

            var data = response.Value<JArray>("data");//JArray.Parse(dataWLinebreaks);
                                                      //response.Value<JArray>("data");
            //Console.WriteLine("Raw Data: " + (data?.ToString() ?? "null"));
            if (returnAsCastObject)
            {
                List<ConsoleLogEntry> dynamicData = (data != null) ? data.Select((jobj)=>jobj.Value<ConsoleLogEntry>() ?? new ConsoleLogEntry()).ToList() : new List<ConsoleLogEntry>();
                return new ConsoleLogResult
                {
                    Success = true,
                    Message = message ?? "Log entries retrieved successfully" /*: '" + response.ToString() + "'"*/,
                    Entries = dynamicData,
                    Count = dynamicData.Count()
                };
            }
            else
            {
                List<object> dynamicData = (data != null) ? data.Select(ConvertJTokenToObjectSmart).ToList() : new List<object>();
                return new
                {
                    success = true,
                    message = message ?? "Log entries retrieved successfully" /*: '" + response.ToString() + "'"*/,
                    entries = dynamicData,
                    count = data?.Count() ?? 0
                };
            }


                

            // Method 1: Simple conversion
            //List<string> dynamicData = (data != null) ? data.Select(ConvertJTokenToObjectSmart).Select((obj)=> obj.ToString() ?? "").ToList() : new List<string>();



            //dynamic[] dynamicData = ((data?.Count() ?? 0) > 0) ? data.Select(d => (dynamic)d).ToArray() : new dynamic[0];
            //result?.Value<string>("message");
            //var data = result?["data"];

            //return new
            //{
            //    success = true,
            //    message = message ?? "Log entries retrieved successfully" /*: '" + response.ToString() + "'"*/,
            //    entries = dynamicData,
            //    count = data?.Count() ?? 0
            //};
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ReadConsole operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ReadConsole operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading Unity console");
            return new
            {
                success = false,
                error = $"Failed to read Unity console: {ex.Message}"
            };
        }
    }

    public static object ConvertJTokenToObjectSmart(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var dict = new Dictionary<string, object>();
                foreach (JProperty prop in token.Children<JProperty>())
                {
                    dict[prop.Name] = ConvertJTokenToObjectSmart(prop.Value);
                }
                return dict;

            case JTokenType.Array:
                return token.Select(ConvertJTokenToObjectSmart).ToList();

            case JTokenType.Integer:
                var intValue = token.Value<long>();
                // Return int if it fits, otherwise long
                return intValue >= int.MinValue && intValue <= int.MaxValue
                    ? (object)(int)intValue
                    : intValue;

            case JTokenType.Float:
                // Try to preserve precision
                var floatStr = token.ToString();
                if (decimal.TryParse(floatStr, out decimal decValue))
                    return decValue;
                return token.Value<double>();

            case JTokenType.String:
                var strValue = token.Value<string>();
                // Optionally try to parse known formats
                if (DateTime.TryParse(strValue, out DateTime dateValue))
                    return dateValue;
                if (Guid.TryParse(strValue, out Guid guidValue))
                    return guidValue;
                return strValue;

            case JTokenType.Boolean:
                return token.Value<bool>();

            case JTokenType.Date:
                return token.Value<DateTime>();

            case JTokenType.Null:
                return null;

            default:
                return token.Value<string>();
        }
    }

}
