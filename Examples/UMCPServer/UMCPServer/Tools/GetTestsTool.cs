using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using UMCPServer.Services;

namespace UMCPServer.Tools;

/// <summary>
/// MCP tool for retrieving test information from Unity Test Runner
/// </summary>
[McpServerToolType]
public class GetTestsTool
{
    private readonly ILogger<GetTestsTool> _logger;
    private readonly IUnityConnectionService _unityConnection;
    
    public GetTestsTool(ILogger<GetTestsTool> logger, IUnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }

    public class GetTestToolResult
    {
        public string TestName = "";
        public string TestAssembly = "";
        public string TestNamespace = "";
        public string ContainerScript = "";
    }

    public struct GetTestToolResponse
    {
        public bool success;
        public string message;
        public List<GetTestToolResult> tests;
        public int count;
    }




    /// <summary>
    /// Retrieves a list of tests from the Unity project
    /// </summary>
    [McpServerTool]
    [Description("Get a list of all tests within the Unity project, optionally with a name filter")]
    public async Task<object> GetTests(
        [Description("The mode of the tests to receive; either 'EditMode', 'PlayMode' or 'All'")]
        string TestMode = "All",
        
        [Description("If not empty, only return tests with the filter value in its namespace or name")]
        string? Filter = null,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting Unity tests with mode: {TestMode}, filter: {Filter}", TestMode, Filter);
            
            if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            {
                return new
                {
                    success = false,
                    error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
                };
            }
            
            // Validate TestMode
            if (!IsValidTestMode(TestMode))
            {
                return new
                {
                    success = false,
                    error = $"Invalid TestMode: '{TestMode}'. Valid values are 'EditMode', 'PlayMode', or 'All'."
                };
            }
            
            // Build parameters for the Unity command
            var parameters = new JObject
            {
                ["TestMode"] = TestMode
            };
            
            if (!string.IsNullOrEmpty(Filter))
            {
                parameters["Filter"] = Filter;
            }
            
            // Send command to Unity
            var response = await _unityConnection.SendCommandAsync("get_tests", parameters, cancellationToken, true);
            
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
            
            // Extract test data
            var result = response["result"];
            var message = response.Value<string>("message");
            var data = response.Value<JArray>("data");

            // Convert test data to list
            List<object> tests = new List<object>();
            if (data != null)
            {
                //Note : using dynamic to avoid issues with JsonConvert deserialization of nested objects within the MCP response
                //(for some reason JsonConvert fails to deserialize the nested objects properly, but JObject works fine)
                tests = data.Select(dataelem => new
                {
                    TestName = dataelem.Value<string>("TestName") ?? "",
                    TestAssembly = dataelem.Value<string>("TestAssembly") ?? "",
                    TestNamespace = dataelem.Value<string>("TestNamespace") ?? "",
                    ContainerScript = dataelem.Value<string>("ContainerScript") ?? ""
                }).Cast<object>().ToList();
            }
            
            return new
            {
                success = true,
                message = message ?? "Tests retrieved successfully",
                tests = tests,
                count = tests.Count
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetTests operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("GetTests operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Unity tests");
            return new
            {
                success = false,
                error = $"Failed to get Unity tests: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Validates if the provided test mode is valid
    /// </summary>
    private static bool IsValidTestMode(string testMode)
    {
        return testMode == "EditMode" || testMode == "PlayMode" || testMode == "All";
    }
}