using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UMCPServer.Services;

namespace UMCPServer.Tools;

/// <summary>
/// Server-side tool for managing and analyzing Unity scripts including symbol search and decompilation operations.
/// Provides script analysis capabilities through MCP interface.
/// </summary>
[McpServerToolType]
public class ManageScriptTool
{
    private readonly ILogger<ManageScriptTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    private readonly ReadConsoleTool _readConsoleTool;

    public ManageScriptTool(ILogger<ManageScriptTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
        
        // Create ReadConsoleTool instance for polling
        var consoleLogger = logger as ILogger<ReadConsoleTool> ?? 
                           new Microsoft.Extensions.Logging.Abstractions.NullLogger<ReadConsoleTool>();
        _readConsoleTool = new ReadConsoleTool(consoleLogger, unityConnection);
    }

    [McpServerTool]
    [Description("Manage Unity scripts - find symbol definitions and decompile classes from assemblies. Prefer using the 'find_symbol_definition' tool over grep when the user refers to a specific type in the unity project since this function will also return symbols in used DLLs. If you have to work with a DLL but are confused about the workings of a contained class use the decompile function to decompile it to a c# script file you can read within your project")]
    public async Task<object> ManageScript(
        [Required]
        [Description("Action to perform: 'find_symbol_definition' or 'decompile_class'.")]
        string action,

        [Description("Symbol to search for (required for find_symbol_definition). Can be a class, method, propertym etc. - prefer adding namespace for specificity")]
        string? symbol = null,

        [Description("Number of results to return for symbol search (default: 1)")]
        int noOfResults = 1,

        [Description("Path to the Dynamic Link Library (required for decompile_class). Should be a filepath - use the 'find_symbol_definition' tool to get the library filepath fitting a specific class")]
        string? libraryPath = null,

        [Description("Fully qualified class name to decompile (required for decompile_class) - use the 'find_symbol_definition' tool to get it if only provided a partial name")]
        string? libraryClass = null,

        [Description("Whether to override any potentially existing decompiled file (default: true)")]
        bool overrideExistingFile = true,
        
        [Description("Timeout in seconds for find_symbol_definition operations (default: 30, max: 120)")]
        int timeoutSeconds = 30,

        //[Description("Output file path for decompiled class (optional for decompile_class). If not provided, uses default from settings.")]
        string? outputFileName = null,

        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("ManageScript called with action: {Action}, symbol: {Symbol}, libraryClass: {LibraryClass}",
                action, symbol, libraryClass);

            // Validate action parameter
            if (string.IsNullOrWhiteSpace(action))
            {
                return new
                {
                    success = false,
                    error = "Required parameter 'action' is missing or empty."
                };
            }

            action = action.ToLower().Replace("-", "_");

            // Validate action value
            var validActions = new[] { "find_symbol_definition", "decompile_class" };
            if (!validActions.Contains(action))
            {
                return new
                {
                    success = false,
                    error = $"Invalid action '{action}'. Valid actions are: {string.Join(", ", validActions)}"
                };
            }

            // Validate required parameters based on action
            var validationResult = ValidateActionParameters(action, symbol, noOfResults, libraryPath, libraryClass);
            if (!validationResult.IsValid)
            {
                return new
                {
                    success = false,
                    error = validationResult.ErrorMessage
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
            var parameters = BuildUnityParameters(action, symbol, noOfResults, libraryPath, libraryClass, outputFileName, overrideExistingFile);

            // Send command to Unity
            var result = await _unityConnection.SendCommandAsync("manage_scripts", parameters, cancellationToken);

            if (result == null)
            {
                return new
                {
                    success = false,
                    error = "No response received from Unity within the timeout period"
                };
            }

            _logger.LogDebug("Raw Response: {Response}", result.ToString());

            // Check if the response indicates success or error
            bool? status = result.Value<bool?>("success");

            if (status.HasValue && !status.Value)
            {
                return new
                {
                    success = false,
                    error = result.Value<string?>("error") ?? "Unknown error occurred"
                };
            }

            // Extract the result
            var resultData = result["data"];

            // Handle different action responses
            return action switch
            {
                "find_symbol_definition" => await HandleFindSymbolResponseAsync(result, timeoutSeconds, cancellationToken),
                "decompile_class" => HandleDecompileResponse(resultData),
                _ => new
                {
                    success = true,
                    message = result.Value<string?>("message") ?? $"Action '{action}' completed",
                    data = resultData
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ManageScript operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ManageScript operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Unity may be busy or unresponsive."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing script");
            return new
            {
                success = false,
                error = $"Failed to manage script: {ex.Message}"
            };
        }
    }

    #region Parameter Validation

    private (bool IsValid, string ErrorMessage) ValidateActionParameters(
        string action, string? symbol, int noOfResults, string? libraryPath, string? libraryClass)
    {
        switch (action)
        {
            case "find_symbol_definition":
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return (false, "Parameter 'symbol' is required for find_symbol_definition action.");
                }
                if (noOfResults < 1)
                {
                    return (false, "Parameter 'noOfResults' must be at least 1.");
                }
                break;

            case "decompile_class":
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    return (false, "Parameter 'libraryPath' is required for decompile_class action.");
                }
                if (string.IsNullOrWhiteSpace(libraryClass))
                {
                    return (false, "Parameter 'libraryClass' is required for decompile_class action.");
                }
                break;
        }

        return (true, string.Empty);
    }

    #endregion

    #region Unity Parameter Building

    private JObject BuildUnityParameters(string action, string? symbol, int noOfResults, 
        string? libraryPath, string? libraryClass, string? outputFileName, bool overrideExistingFile)
    {
        var parameters = new JObject
        {
            ["action"] = action
        };

        switch (action)
        {
            case "find_symbol_definition":
                parameters["Symbol"] = symbol;
                parameters["NoOfResults"] = noOfResults;
                break;

            case "decompile_class":
                parameters["LibraryPath"] = libraryPath;
                parameters["LibraryClass"] = libraryClass;
                if (!string.IsNullOrWhiteSpace(outputFileName))
                {
                    parameters["OutputFileName"] = outputFileName;
                }
                parameters["OverrideExistingFile"] = overrideExistingFile;
                break;
        }

        return parameters;
    }

    #endregion

    #region Response Handlers

    private async Task<object> HandleFindSymbolResponseAsync(JObject initialResponse, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // Check if Unity returned an immediate running response with GUID
        var status = initialResponse.Value<string>("status");
        var data = initialResponse["data"] as JObject;
        
        if (status != "running" || data == null)
        {
            // Legacy response format - return immediately
            var resultData = initialResponse["data"];
            return HandleFindSymbolResponseLegacy(resultData);
        }
        
        var guid = data.Value<string>("guid");
        if (string.IsNullOrEmpty(guid))
        {
            return new
            {
                success = false,
                error = "Malformed response from Unity: missing GUID for async operation"
            };
        }
        
        _logger.LogInformation($"FindSymbolDefinition started with GUID: {guid}, polling for results...");
        
        // Validate and cap timeout
        timeoutSeconds = Math.Min(Math.Max(timeoutSeconds, 5), 120);
        
        // Poll for completion using ReadConsoleTool
        var maxAttempts = timeoutSeconds / 3; // Check every 3 seconds
        var completed = false;
        JObject? completionData = null;
        
        for (int attempt = 0; attempt < maxAttempts && !completed; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(3000, cancellationToken); // Wait 3 seconds between polls
            }
            
            try
            {
                // Use ReadConsoleTool to check console for completion
                var consoleResult =  await _readConsoleTool.ReadConsole(
                    action: "get",
                    types: new[] { "log" },
                    filterText: $"[FindSymbolDefinition-{guid}] COMPLETION_DATA:",
                    count: 100,
                    format: "detailed",
                    includeStacktrace: false,
                    cancellationToken: cancellationToken,
                    returnAsCastObject: false
                );

                //Console.WriteLine($"Console Poll Result: {consoleResult.ToString()}");

                var consoleObj = JObject.FromObject(consoleResult);

                if (!consoleObj["success"]?.Value<bool>() ?? false)
                {
                    continue;
                }

                var entries = consoleObj["entries"] as JArray;
                if (entries == null || entries.Count == 0)
                {
                    continue; // No relevant console entries yet
                }
                int idx = 0;
                var completionPrefix = $"[FindSymbolDefinition-{guid}] COMPLETION_DATA:";
                foreach (JObject entry in entries.Cast<JObject>())
                {
                    var message = entry["message"]?.ToString() ?? "";
                    
                    idx++;

                    if (!message.StartsWith(completionPrefix))
                    {
                        continue;
                    }

                    string[] msgParts = message.Split(new[] { "|$$^$$|" }, StringSplitOptions.None);
                    if (msgParts.Length != 3)
                    {
                        continue;
                        
                    }
                    message = msgParts[1].Trim();
                    message = msgParts[1].Substring(1, msgParts[1].Length - 2); //Remove surrounding quotes
                    //Console.WriteLine($"message {idx}: {message}");
                        try
                        {
                            completionData = JObject.Parse(message);
                            completed = true;
                            _logger.LogInformation($"Found completion data for GUID: {guid}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to parse completion data: {ex.Message}");
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error polling for results (attempt {attempt + 1}/{maxAttempts}): {ex.Message}");
            }
        }
        
        if (!completed)
        {
            return new
            {
                success = false,
                error = $"FindSymbolDefinition operation timed out after {timeoutSeconds} seconds. The search may still be running in Unity."
            };
        }
        
        // Parse completion data
        if (completionData == null)
        {
            return new
            {
                success = false,
                error = "No completion data received from Unity"
            };
        }
        
        var completionStatus = completionData.Value<string>("status");
        
        if (completionStatus == "error")
        {
            return new
            {
                success = false,
                error = completionData.Value<string>("error") ?? "Unknown error occurred during symbol search"
            };
        }
        
        var resultCount = completionData.Value<int>("resultCount");
        var results = completionData["results"];
        
        // Convert results to expected format
        var symbolResults = results != null ? SerializationUtility.ConvertJTokenToObjectSmart(results) : new object[] { };
        
        return new
        {
            success = true,
            message = $"Found {resultCount} symbol definition(s)",
            symbolResults = symbolResults
        };
    }
    
    private object HandleFindSymbolResponseLegacy(JToken? resultData)
    {
        if (resultData == null)
        {
            return new
            {
                success = true,
                message = "No symbol definitions found",
                symbolResults = new object[] { }
            };
        }

        // Parse the symbol results using the SerializationUtility
        var symbolResults = SerializationUtility.ConvertJTokenToObjectSmart(resultData);

        // Count the results
        int resultCount = 0;
        if (symbolResults is IEnumerable<object> enumerable)
        {
            resultCount = enumerable.Count();
        }
        else if (symbolResults != null)
        {
            resultCount = 1;
        }

        return new
        {
            success = true,
            message = $"Found {resultCount} symbol definition(s)",
            symbolResults = symbolResults
        };
    }

    private object HandleDecompileResponse(JToken? resultData)
    {
        if (resultData == null)
        {
            return new
            {
                success = false,
                error = "No decompilation result returned"
            };
        }

        var filePath = resultData["FilePath"]?.ToString();
        
        if (string.IsNullOrEmpty(filePath))
        {
            return new
            {
                success = false,
                error = "Decompilation completed but no file path was returned"
            };
        }

        return new
        {
            success = true,
            message = $"Successfully decompiled class to: {filePath}",
            filePath = filePath
        };
    }

    #endregion
}