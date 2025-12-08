using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using UMCPServer.Services;
using static UMCPServer.Tools.ToolBaseUtility;

namespace UMCPServer.Tools;

/// <summary>
/// MCP tool for running tests via Unity Test Runner
/// </summary>
[McpServerToolType]
public class RunTestsTool : Toolbase<RunTestsTool>
{
    ///// <summary>
    ///// Logger used for unit- and integration testing 
    ///// </summary>
    //private readonly ILogger<RunTestsTool> _logger;

    ///// <summary>
    ///// Connection service to Unity Editor
    ///// </summary>
    //private readonly IUnityConnectionService _unityConnection;

    /// <summary>
    /// Create a new instance of the RunTestsTool
    /// </summary>
    /// <param name="logger">linked logger</param>
    /// <param name="unityConnection">linked unity connection</param>
    public RunTestsTool(ILogger<RunTestsTool> logger,
        IUnityConnectionService unityConnection,
        IUnityStateConnectionService stateConnection) : base(logger, unityConnection, stateConnection)
    {
        //_logger = logger;
        //_unityConnection = unityConnection;
    }
    
    /// <summary>
    /// Runs specified tests in the Unity Test Runner
    /// </summary>
    [McpServerTool]
    [Description("Run the given tests in the Unity3D Testrunner. Automatically marks a new step for logging. ALWAYS run the 'ForceUpdateEditor' tool before using this tool and only use it after successful result. You don't need to run GetTests before running, the filter is resolved as part of the tool use - if no test are found the tool returns false")]
    public async Task<object> RunTests(
        [Description("The mode of the tests to run; either 'EditMode', 'PlayMode' or 'All'")]
        string TestMode = "All",
        
        [Description("If empty, run all tests matching testmode. If not empty, only run the tests matching the filter provided (example format options: (1) (Partial) Namespace: 'foo.bar', (2) Class/Fixture name: 'fixtureclass', (3) Testname: 'nameOfTest', (4) Any combination of the preceding: 'fixtureclass.nameOfTest', 'foo.bar.fixtureclass', 'foo.bar.fixtureclass.nameOfTest')")]
        string[]? Filter = null,
        
        [Description("Whether to output TestResults")]
        bool OutputTestResults = true,
        
        [Description("Whether to output LogData")]
        bool OutputLogData = true,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Running Unity tests with mode: {TestMode}, filter count: {FilterCount}", 
                TestMode, Filter?.Length ?? 0);
            
            //if (!_unityConnection.IsConnected && !await _unityConnection.ConnectAsync())
            //{
            //    return new
            //    {
            //        success = false,
            //        error = "Unity Editor is not running or MCP Bridge is not available. Please ensure Unity Editor is open and the UMCP Unity3D Client is active."
            //    };
            //}
            
            // Validate TestMode
            if (!IsValidTestMode(TestMode))
            {
                return new
                {
                    success = false,
                    error = $"Invalid TestMode: '{TestMode}'. Valid values are 'EditMode', 'PlayMode', or 'All'."
                };
            }
            
            // If a filter is provided, validate that tests exist with that filter
            if (Filter != null && Filter.Length > 0)
            {
                _logger.LogInformation("Validating filter by running GetTests first with filter: {Filter}", string.Join(", ", Filter));
                
                // For each filter, run GetTests to validate it exists
                var validatedFilters = new List<string>();
                var notFoundFilters = new List<string>();
                
                foreach (var filterItem in Filter)
                {
                    // Build parameters for GetTests
                    var getTestsParams = new JObject
                    {
                        ["TestMode"] = TestMode,
                        ["Filter"] = filterItem
                    };

                    AsyncToolCommandResult toolResult = await ToolBaseUtility.SendCommandAsyncWhenInRightState(
                       commandType: "get_tests",
                       parameters: getTestsParams,
                       desiredStateOptions: new ToolBaseUtility.UnityConnectionState[] {
                            new ToolBaseUtility.UnityConnectionState() {
                                CurrentContext = ToolBaseUtility.Context.Running,
                                CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Scene },
                            new ToolBaseUtility.UnityConnectionState() {
                                CurrentContext = ToolBaseUtility.Context.Running,
                                CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Prefab },
                       },
                       unityConnection: _unityConnection,
                       stateConnection: _stateConnection,
                       logger: _logger,
                       cancellationToken: cancellationToken);

                    if (!toolResult.Success)
                    {
                        continue;
                        _logger.LogWarning("Failed to validate filter: {Filter}", filterItem);
                    }

                    var getTestsResponse = toolResult.Response!;




                    //// Send GetTests command to Unity
                    //var getTestsResponse = await _unityConnection.SendCommandAsync("get_tests", getTestsParams, cancellationToken);
                    
                    //if (getTestsResponse != null && getTestsResponse.Value<string>("status") != "error")
                    //{
                        // Check if any tests were found
                        var data = getTestsResponse.Value<JArray>("data");
                        if (data != null && data.Count > 0)
                        {
                            // Tests found, this filter is valid
                            // Check if we need to use the full test name format
                            var foundTests = data.Select(test => 
                            {
                                //The 'Testname' property will also contain the namespaces and class names, so we can use it directly
                                return test.Value<string>("TestName");
                            }).ToList();
                            
                            //_logger.LogInformation("Found Tests matching filter {f}: {Filters}", filterItem, string.Join(", ", foundTests));


                            // Check if the filter matches exactly or if we need to use the full name
                            var exactMatch = foundTests.FirstOrDefault(t => t.EndsWith($".{filterItem}") || t == filterItem);
                            if (exactMatch != null)
                            {
                                // Use the exact match for the filter
                                validatedFilters.Add(exactMatch);
                                _logger.LogInformation("Filter '{Filter}' resolved to full test name: '{FullName}'", filterItem, exactMatch);
                            }
                            else
                            {
                                string filterWNamespace = (foundTests[0]?.Split(filterItem)?[0] ?? "") + filterItem;

                            // Use the original filter if it found tests
                            validatedFilters.Add(filterWNamespace);
                            _logger.LogInformation("Filter '{Filter}' found {Count} tests -> {namespace}", filterItem, data.Count, filterWNamespace);
                            }
                        }
                        else
                        {
                            notFoundFilters.Add(filterItem);
                            _logger.LogWarning("No tests found for filter: {Filter}", filterItem);
                        }
                    //}
                    //else
                    //{
                    //    notFoundFilters.Add(filterItem);
                    //    _logger.LogWarning("Failed to validate filter: {Filter}", filterItem);
                    //}
                }
                
                // If no valid filters found, return error
                if (validatedFilters.Count == 0)
                {
                    return new
                    {
                        success = false,
                        error = $"No tests found matching the provided filter(s): {string.Join(", ", Filter)}. Please run the GetTests tool first to find the correct filter format. Note that test names should typically include the class name (e.g., 'TestClassName.TestMethodName')."
                    };
                }
                
                // If some filters were not found, log a warning but continue with valid ones
                if (notFoundFilters.Count > 0)
                {
                    _logger.LogWarning("Some filters did not match any tests: {Filters}. Continuing with valid filters.", string.Join(", ", notFoundFilters));
                }
                
                // Update Filter to use validated filters
                Filter = validatedFilters.ToArray();
            }
            
            // Build parameters for the Unity command
            var parameters = new JObject
            {
                ["TestMode"] = TestMode,
                ["OutputTestResults"] = OutputTestResults,
                ["OutputLogData"] = OutputLogData
            };
            
            if (Filter != null && Filter.Length > 0)
            {
                parameters["Filter"] = new JArray(Filter);
            }
            else
            {
                parameters["Filter"] = new JArray();
            }

            DateTime testStartTime = DateTime.Now;


            _logger.LogInformation($"Actually running the tests with filter size {((Filter == null) ? "NULL" : Filter.Length.ToString())}...");

            AsyncToolCommandResult runTestsResult = await ToolBaseUtility.SendCommandAsyncWhenInRightState(
               commandType: "run_tests",
               parameters: parameters,
               desiredStateOptions: new ToolBaseUtility.UnityConnectionState[] {
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Scene },
                    new ToolBaseUtility.UnityConnectionState() {
                        CurrentContext = ToolBaseUtility.Context.Running,
                        CurrentRunMode = ToolBaseUtility.Runmode.EditMode_Prefab },
               },
               unityConnection: _unityConnection,
               stateConnection: _stateConnection,
               logger: _logger,
               cancellationToken: cancellationToken);

            if (!runTestsResult.Success)
            {
                return new
                {
                    success = false,
                    error = "Failed to get response from Unity, " + runTestsResult.ErrorMessage ?? "Unknown error sending command to Unity"
                };
            }
            var response = runTestsResult.Response!;


            //// Send command to Unity - this will start the tests
            //var response = await _unityConnection.SendCommandAsync("run_tests", parameters, cancellationToken);
            


            //if (response == null)
            //{
            //    return new
            //    {
            //        success = false,
            //        error = "Failed to get response from Unity"
            //    };
            //}
            
            // Unity can return the response directly or wrapped in a result object
            // Check if this is a direct response from Unity (has success/error at root)
            var directSuccess = response.Value<bool?>("success");
            //var directError = response.Value<string>("error");
            
            //// If we have a direct error response
            //if (directSuccess == false && !string.IsNullOrEmpty(directError))
            //{
            //    return new
            //    {
            //        success = false,
            //        error = directError
            //    };
            //}

            // If we have a direct success response with running status
            if (directSuccess == true && response.Value<string>("status") == "running")
            {
                _logger.LogInformation($"Raw response: {response.ToString()}");

                JObject dataObj = response.Value<JObject>("data")!;
                if(dataObj == null)
                    {
                    return new
                    {
                        success = false,
                        error = "Malformed response from Unity: missing 'guid' data"
                    };
                }

                var guid = dataObj.Value<string>("guid");
                _logger.LogInformation($"Tests with guid:{guid} started running in Unity, waiting for completion...");

                // Extract the step GUID from Unity's response (it's embedded in the logs)
                // We'll need to wait and then request the step logs
                await Task.Delay(2000, cancellationToken); // Initial delay to let tests start

                // Poll for test completion with exponential backoff
                var maxAttempts = 30; // Base maximum attempts
                var delayMs = 1000; // Start with 1 second
                var maxDelayMs = 5000; // Max 5 seconds between attempts
                var completed = false;
                var completionDetected = false;
                var completionDetectedAttempt = 0;
                var maxPostCompletionAttempts = 5; // Continue polling for 5 more attempts after completion
                JObject completionData = null;
                var consecutiveFailures = 0; // Track consecutive polling failures
                var domainReloadDetected = false; // Track if domain reload is suspected
                const int maxConsecutiveFailures = 10; // Allow up to 10 consecutive failures

                for (int attempt = 0; attempt < maxAttempts && !completed; attempt++)
                {
                    // Check if tests are still running by querying the console
                    var consoleParams = new JObject
                    {
                        ["action"] = "get",
                        ["types"] = new JArray("log"),
                        ["filterText"] = "[RunTests]",
                        ["count"] = 50,
                        ["format"] = "detailed"
                    };

                    JObject consoleResponse = null;
                    
                    try
                    {
                        consoleResponse = await _unityConnection.SendCommandAsync("read_console", consoleParams, cancellationToken);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
                    {
                        consecutiveFailures++;
                        _logger.LogInformation("Unity connection lost during polling (attempt {Attempt}), likely due to domain reload. Will retry... (consecutive failures: {ConsecutiveFailures})", 
                            attempt + 1, consecutiveFailures);
                        
                        // Check if we should extend timeout for domain reload
                        if (consecutiveFailures >= 3 && !domainReloadDetected)
                        {
                            domainReloadDetected = true;
                            maxAttempts += 20; // Extend timeout for domain reload
                            _logger.LogInformation("Multiple consecutive failures detected, extending timeout by 20 attempts for potential domain reload (new max: {MaxAttempts})", maxAttempts);
                        }
                        
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError("Too many consecutive communication failures ({Count}), giving up", consecutiveFailures);
                            return new
                            {
                                success = false,
                                error = "Persistent communication failure with Unity after domain reload attempts. Unity may be unresponsive."
                            };
                        }
                        
                        // Continue to next attempt
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, maxDelayMs);
                        continue;
                    }
                    catch (Exception ex) when (ex.Message.Contains("Failed to communicate with Unity"))
                    {
                        consecutiveFailures++;
                        _logger.LogWarning("Communication error during polling (attempt {Attempt}): {Error}. Continuing... (consecutive failures: {ConsecutiveFailures})", 
                            attempt + 1, ex.Message, consecutiveFailures);
                        
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError("Too many consecutive communication failures ({Count}), giving up", consecutiveFailures);
                            return new
                            {
                                success = false,
                                error = $"Persistent communication failure with Unity: {ex.Message}"
                            };
                        }
                        
                        // Continue to next attempt
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, maxDelayMs);
                        continue;
                    }

                    // Handle successful response

                    //_logger.LogInformation("[RUNTESTSTOOL] Console response: {Response}", consoleResponse["success"].ToString());




                    if (consoleResponse != null && consoleResponse["success"]?.Value<bool>() == true)
                    {
                        // Reset consecutive failures on successful response
                        if (consecutiveFailures > 0)
                        {
                            _logger.LogInformation("Connection restored after {FailureCount} consecutive failures", consecutiveFailures);
                            consecutiveFailures = 0;
                        }
                        
                        var entries = consoleResponse["data"] as JArray;

                        //_logger.LogInformation($"[RUNTESTSTOOL] # of entries:{((entries == null) ? "NULL" : entries.Count)}");


                        if (entries != null)
                        {
                            // Look for completion message
                            // Note: ReadConsole now returns newest entries first, so no need to reverse
                            int cnt = 0;
                            foreach (var entry in entries) // Already newest first from ReadConsole
                            {
                                string message;

                                

                                // Handle both plain format (string) and detailed format (object with message property)
                                if (entry.Type == JTokenType.String)
                                {
                                    // Plain format: entry is a direct string
                                    message = entry.Value<string>() ?? "";
                                }
                                else
                                {
                                    // Detailed format: entry is an object with message property
                                    message = entry["message"]?.Value<string>() ?? "";
                                }


                                //_logger.LogInformation($"[RUNTESTSTOOL] Message {cnt}/{entries.Count} = {message}");

                                if(TryExtractTestExecutionResult(message, guid, out TestExecutionResult _testExecutionResult))
                                {
                                    //Console.WriteLine("got test Execution results: " + _testExecutionResult.ToString());

                                    completionData = new JObject
                                    {
                                        ["success"] = true,
                                        ["startTime"] = _testExecutionResult.TestStart,
                                        ["endTime"] = _testExecutionResult.TestEnd,
                                        ["testResultFiles"] = JArray.FromObject(_testExecutionResult.TestResultPaths),
                                        ["message"] = $"Tests completed. #{_testExecutionResult.TestResultPaths.Count} testmode results saved to: {string.Join(',', _testExecutionResult.TestResultPaths)}. Use InterpretTestResults tool to analyze these results.",
                                        //["testCount"] = testCount,
                                        //["allTestsPassed"] = allSuccess
                                    };
                                    _logger.LogInformation("File paths found, marking as completed");
                                    completed = true;
                                    break; // Exit the foreach loop
                                }

                                //if (message.Contains("[RunTests] TEST_EXECUTION_COMPLETED"))
                                //{



                                //    _logger.LogInformation("Test execution completed, parsing results...");

                                //    // Mark completion detected but continue polling to capture file paths
                                //    if (!completionDetected)
                                //    {
                                //        completionDetected = true;
                                //        completionDetectedAttempt = attempt;
                                //        _logger.LogInformation("Completion detected at attempt {Attempt}, continuing to poll for file paths...", attempt);
                                //    }

                                //    // Parse the results from the console logs (including from all previous attempts)
                                //    var testResultFilePaths = new List<string>();
                                //    var allSuccess = true;
                                //    var testCount = 0;

                                //    foreach (var logEntry in entries)
                                //    {
                                //        string logMessage;

                                //        // Handle both plain format (string) and detailed format (object with message property)
                                //        if (logEntry.Type == JTokenType.String)
                                //        {
                                //            // Plain format: entry is a direct string
                                //            logMessage = logEntry.Value<string>() ?? "";
                                //        }
                                //        else
                                //        {
                                //            // Detailed format: entry is an object with message property
                                //            logMessage = logEntry["message"]?.Value<string>() ?? "";
                                //        }








                                //        // Parse success/failure messages to determine overall success
                                //        if (logMessage.Contains("[RunTests] ✗ FAIL:"))
                                //        {
                                //            allSuccess = false;
                                //        }

                                //        // Check the completion summary
                                //        if (logMessage.Contains("[RunTests] Test execution completed."))
                                //        {
                                //            // Parse format: "[RunTests] Test execution completed. AllSuccess: True, Tests: 0"
                                //            allSuccess = logMessage.Contains("AllSuccess: True");

                                //            // Extract test count
                                //            var testCountMatch = System.Text.RegularExpressions.Regex.Match(logMessage, @"Tests: (\d+)");
                                //            if (testCountMatch.Success && int.TryParse(testCountMatch.Groups[1].Value, out int parsedTestCount))
                                //            {
                                //                testCount = parsedTestCount;
                                //            }
                                //        }

                                //        if(TryExtractTestResults(logMessage, out List<string> resultPaths))
                                //        {
                                //            testResultFilePaths.AddRange(resultPaths);
                                //        }






                                //        // Check for test result file paths
                                //        //if (logMessage.Contains("[RunTests] TEST_RESULTS_FILE_PATH:"))
                                //        //{



                                //        //    var pathStart = logMessage.IndexOf("TEST_RESULTS_FILE_PATH:") + "TEST_RESULTS_FILE_PATH:".Length;
                                //        //    var path = logMessage.Substring(pathStart).Trim();

                                //        //    //Validate the found result file actually belongs to the current test run
                                //        //    string sampleDate = "20250819_134941.xml";
                                //        //    string testResultTime = path.Substring(path.Length - sampleDate.Length, sampleDate.Length - 4);

                                //        //    DateTime testResultTimeParsed = default;
                                //        //    try
                                //        //    {
                                //        //        testResultTimeParsed = DateTime.ParseExact(testResultTime, "yyyyMMdd_HHmmss", null);
                                //        //    }
                                //        //    catch
                                //        //    {
                                //        //        _logger.LogWarning("Failed to parse test result time from path '{Path}'. Skipping this file.", path);
                                //        //        continue; // Skip this file if parsing fails
                                //        //    }

                                //        //    if(testStartTime > testResultTimeParsed)
                                //        //        {
                                //        //        _logger.LogWarning("Test result file path '{Path}' has a timestamp earlier than the test start time. Skipping this file.", path);
                                //        //        continue; // Skip this file if it doesn't match the test run
                                //        //        }



                                //        //    _logger.LogInformation(testStartTime.ToString("yyyyMMdd_HHmmss") + " vs " + testResultTimeParsed.ToString("yyyyMMdd_HHmmss") + " = " + (testStartTime < testResultTimeParsed), attempt);




                                //        //    if (!string.IsNullOrEmpty(path))
                                //        //    {
                                //        //        testResultFilePaths.Add(path);
                                //        //    }
                                //        //}

                                //        //// Check for all test result file paths (when running All mode)
                                //        //if (logMessage.Contains("[RunTests] TEST_RESULTS_FILE_PATHS_ALL:"))
                                //        //{
                                //        //    var pathsStart = logMessage.IndexOf("TEST_RESULTS_FILE_PATHS_ALL:") + "TEST_RESULTS_FILE_PATHS_ALL:".Length;
                                //        //    var pathsString = logMessage.Substring(pathsStart).Trim();
                                //        //    if (!string.IsNullOrEmpty(pathsString))
                                //        //    {
                                //        //        // Clear existing paths and use the combined list
                                //        //        testResultFilePaths.Clear();
                                //        //        testResultFilePaths.AddRange(pathsString.Split(';').Where(p => !string.IsNullOrEmpty(p)));

                                //        //        for(int i = testResultFilePaths.Count - 1; i >= 0; i--)
                                //        //            {
                                //        //            // Validate the found result file actually belongs to the current test run
                                //        //            string sampleDate = "20250819_134941.xml";
                                //        //            string testResultTimeAll = testResultFilePaths[i].Substring(testResultFilePaths[i].Length - sampleDate.Length, sampleDate.Length - 4);

                                //        //            DateTime testResultTimeParsedAll = default;
                                //        //            try
                                //        //            {
                                //        //                testResultTimeParsedAll = DateTime.ParseExact(testResultTimeAll, "yyyyMMdd_HHmmss", null);
                                //        //            }
                                //        //            catch
                                //        //            {
                                //        //                _logger.LogWarning("Failed to parse test result time from path '{Path}'. Skipping this file.", testResultFilePaths[i]);
                                //        //                continue; // Skip this file if parsing fails
                                //        //            }
                                //        //            if(testStartTime > testResultTimeParsedAll)
                                //        //            {
                                //        //                _logger.LogWarning("Test result file path '{Path}' has a timestamp earlier than the test start time. Skipping this file.", testResultFilePaths[i]);
                                //        //                testResultFilePaths.RemoveAt(i--); // Remove and adjust index
                                //        //            }
                                //        //        }
                                //        //    }
                                //        //}
                                //    }

                                //    // Test execution completed successfully (tool ran without errors)
                                //    // Return test result files regardless of whether individual tests passed or failed
                                //    if (testResultFilePaths.Count > 0)
                                //    {
                                //        completionData = new JObject
                                //        {
                                //            ["success"] = true,
                                //            ["testResultFiles"] = JArray.FromObject(testResultFilePaths),
                                //            ["message"] =  $"Tests completed. #{testResultFilePaths.Count} Results saved to: {string.Join(',', testResultFilePaths)}. Use InterpretTestResults tool to analyze the results.",
                                //            //["testCount"] = testCount,
                                //            //["allTestsPassed"] = allSuccess
                                //        };
                                //        _logger.LogInformation("File paths found, marking as completed");
                                //        completed = true;
                                //    }
                                //    else if (attempt - completionDetectedAttempt >= maxPostCompletionAttempts)
                                //    {
                                //        // We've polled long enough after completion detection without finding file paths
                                //        completionData = new JObject
                                //        {
                                //            ["success"] = true,
                                //            ["message"] = "Test execution completed but no test result files were found after extended polling.",
                                //            //["testCount"] = testCount,
                                //            //["allTestsPassed"] = allSuccess
                                //        };
                                //        _logger.LogWarning("No file paths found after {PostCompletionAttempts} additional polling attempts", maxPostCompletionAttempts);
                                //        completed = true;
                                //    }
                                //    else
                                //    {
                                //        // Continue polling for file paths
                                //        _logger.LogInformation("No file paths found yet, continuing to poll... (attempt {CurrentAttempt} of {MaxPostCompletion} post-completion attempts)", 
                                //            attempt - completionDetectedAttempt, maxPostCompletionAttempts);
                                //        completionData = new JObject
                                //        {
                                //            ["success"] = true,
                                //            ["message"] = "Test execution completed but no test result files were generated.",
                                //            //["testCount"] = testCount,
                                //            //["allTestsPassed"] = allSuccess
                                //        };
                                //    }

                                //    break; // Exit the foreach loop
                                //}
                                cnt++;
                            }
                        }
                    }
                    else if (consoleResponse == null)
                    {
                        consecutiveFailures++;
                        _logger.LogWarning("Received null response from Unity console (attempt {Attempt}), may be reconnecting after domain reload (consecutive failures: {ConsecutiveFailures})", 
                            attempt + 1, consecutiveFailures);
                        
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError("Too many consecutive null responses ({Count}), giving up", consecutiveFailures);
                            return new
                            {
                                success = false,
                                error = "Persistent null responses from Unity. Unity may be unresponsive."
                            };
                        }
                    }
                    else
                    {
                        consecutiveFailures++;
                        _logger.LogWarning("Received unsuccessful response from Unity console (attempt {Attempt}) (consecutive failures: {ConsecutiveFailures})", 
                            attempt + 1, consecutiveFailures);
                        
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError("Too many consecutive unsuccessful responses ({Count}), giving up", consecutiveFailures);
                            return new
                            {
                                success = false,
                                error = "Persistent unsuccessful responses from Unity. Unity may be in an error state."
                            };
                        }
                    }


                    // If completed, break out of the main polling loop
                    if (completed)
                    {
                        break;
                    }

                    // Wait before next attempt
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs); // Exponential backoff
                }

                // If we completed successfully, return the completion data
                if (completed && completionData != null)
                {
                    // Convert the JObject to a properly formatted response using the smart converter
                    var result = new Dictionary<string, object>();
                    foreach (var property in completionData.Properties())
                    {
                        result[property.Name] = ReadConsoleTool.ConvertJTokenToObjectSmart(property.Value);
                    }
                    return result;
                }

                // Timeout - check if we have any completion data to return
                if (completionData != null)
                {
                    _logger.LogWarning("Polling timed out but test execution had completed. Returning available completion data.");
                    var timeoutResult = new Dictionary<string, object>();
                    foreach (var property in completionData.Properties())
                    {
                        timeoutResult[property.Name] = ReadConsoleTool.ConvertJTokenToObjectSmart(property.Value);
                    }
                    // Add timeout warning to message
                    if (timeoutResult.ContainsKey("message"))
                    {
                        timeoutResult["message"] = timeoutResult["message"] + " (Note: Polling timed out but test execution had completed)";
                    }
                    return timeoutResult;
                }
                
                // True timeout - no completion detected
                return new
                {
                    success = false,
                    error = "Test runner timed out after waiting for completion. Tests may still be running in Unity. This is a tool execution failure, not a test failure.",
                    consecutiveFailures = consecutiveFailures,
                    domainReloadDetected = domainReloadDetected,
                    maxAttemptsUsed = maxAttempts
                };
            }
            
            // If we get here, Unity didn't return the expected "running" status
            // This might happen if tests complete immediately or if there's an unexpected response format
            _logger.LogWarning("Unexpected response format from Unity. Response: {Response}", response);
            
            // Try to extract any success/failure indication from the response
            if (directSuccess == true)
            {
                return new
                {
                    success = true,
                    message = response.Value<string>("message") ?? "Test execution completed. No test results file was generated.",
                    testCount = 0
                };
            }
            
            return new
            {
                success = false,
                error = $"Unexpected response from Unity. Expected test execution to start. Response: {response}"
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("RunTests operation was cancelled");
            return new
            {
                success = false,
                error = "Operation was cancelled"
            };
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("RunTests operation timed out");
            return new
            {
                success = false,
                error = "Request timed out. Test execution may take a long time. Consider increasing the timeout."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Unity tests");
            return new
            {
                success = false,
                error = $"Failed to run Unity tests: {ex.Message}",
                stackTrace = ex.StackTrace
            };
        }
    }

    public class TestExecutionResult
    {
        public string Guid { get; set; } = "";
        public string TestStart { get; set; } = "";
        public string TestEnd { get; set; } = "";
        public List<string> TestResultPaths { get; set; } = new List<string>();

        public override string ToString()
        {
            return $"GUID: {Guid}, TestStart: {TestStart}, TestEnd: {TestEnd}, TestResultPaths: [{((TestResultPaths == null) ? "NULL" : string.Join(", ", TestResultPaths))}]";
        }
    }



    public static bool TryExtractTestExecutionResult(string input, string guid, out TestExecutionResult result)
        {
        result = null!;
        if (string.IsNullOrEmpty(input) || !input.StartsWith("[RunTests] TEST_EXECUTION_COMPLETED") || !input.Contains(guid))
        {
            return false;
        }

        result = new TestExecutionResult();
        try
        {
            // Extract GUID
            var guidMatch = Regex.Match(input, @"GUID:\s*([^,}]+)");
            if (guidMatch.Success)
            {
                result.Guid = guidMatch.Groups[1].Value.Trim();
            }

            // Extract TestStart
            var testStartMatch = Regex.Match(input, @"TestStart:\s*([^,}]+)");
            if (testStartMatch.Success)
            {
                result.TestStart = testStartMatch.Groups[1].Value.Trim();
            }

            // Extract TestEnd
            var testEndMatch = Regex.Match(input, @"TestEnd:\s*([^,}]+)");
            if (testEndMatch.Success)
            {
                result.TestEnd = testEndMatch.Groups[1].Value.Trim();
            }

            // Extract TestResultPaths
            var pathsMatch = Regex.Match(input, @"TestResultPaths:\s*\[(.*?)\]}", RegexOptions.Singleline);
            if (pathsMatch.Success)
            {
                var pathsString = pathsMatch.Groups[1].Value;

                // Split by comma but be careful with paths that contain commas
                // Use regex to find all quoted paths
                var pathPattern = @"'([^']+)'";
                var pathMatches = Regex.Matches(pathsString, pathPattern);

                foreach (Match match in pathMatches)
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var path = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            result.TestResultPaths.Add(path);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log the exception if you have a logging framework
            Console.WriteLine($"Error parsing test execution string: {ex.Message}");
            // Return what we've parsed so far
        }

        return true;
    }









        private static bool TryExtractTestResults(string _inputMessage, out List<string> resultPaths)
    {
        resultPaths = new List<string>();

        if(!_inputMessage.StartsWith("[RunTests] TEST_RESULTS_FILE_PATHS_ALL:"))
            {
            return false;
            }


        // Remove the prefix text
        string cleaned = _inputMessage.Substring(_inputMessage.IndexOf("TEST_RESULTS_FILE_PATHS_ALL:") + "TEST_RESULTS_FILE_PATHS_ALL:".Length);
        

        // Find all strings between single quotes
        var matches = Regex.Matches(cleaned, @"'([^']*)'");

        foreach (Match match in matches)
        {
            string value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(value) && value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                resultPaths.Add(value);
            }
        }

        return (resultPaths.Count > 0);
    }



    //private static bool TryExtractDateTime(string _inputMessage, out DateTime result)
    //{

    //}






        /// <summary>
        /// Validates if the provided test mode is valid
        /// </summary>
    private static bool IsValidTestMode(string testMode)
    {
        return testMode == "EditMode" || testMode == "PlayMode" || testMode == "All";
    }
    
    /// <summary>
    /// Extracts the test name from a log message
    /// </summary>
    private static string ExtractTestName(string logMessage)
    {
        // Format: "[RunTests] ✓ PASS: TestName (0.123s)" or "[RunTests] ✗ FAIL: TestName (0.123s)"
        var startIndex = logMessage.IndexOf("PASS:") + 5;
        if (startIndex < 5)
        {
            startIndex = logMessage.IndexOf("FAIL:") + 5;
        }
        
        if (startIndex > 5)
        {
            var endIndex = logMessage.IndexOf(" (", startIndex);
            if (endIndex > startIndex)
            {
                return logMessage.Substring(startIndex, endIndex - startIndex).Trim();
            }
        }
        
        return "";
    }
    
    /// <summary>
    /// Extracts the test duration from a log message
    /// </summary>
    private static double ExtractDuration(string logMessage)
    {
        // Format: "(0.123s)"
        var match = System.Text.RegularExpressions.Regex.Match(logMessage, @"\((\d+\.?\d*)s\)");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var duration))
        {
            return duration;
        }
        return 0.0;
    }
    
    /// <summary>
    /// Extracts the assembly name from a fully qualified test name
    /// </summary>
    private static string ExtractAssembly(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length > 0 ? parts[0] : "Unknown";
    }
    
    /// <summary>
    /// Extracts the namespace from a fully qualified test name
    /// </summary>
    private static string ExtractNamespace(string fullName)
    {
        var lastDotIndex = fullName.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            var nameWithoutMethod = fullName.Substring(0, lastDotIndex);
            var secondLastDotIndex = nameWithoutMethod.LastIndexOf('.');
            if (secondLastDotIndex > 0)
            {
                return nameWithoutMethod.Substring(0, secondLastDotIndex);
            }
        }
        return string.Empty;
    }
    
    /// <summary>
    /// Extracts the container script name from a fully qualified test name
    /// </summary>
    private static string ExtractContainerScript(string fullName)
    {
        var parts = fullName.Split('.');
        if (parts.Length >= 2)
        {
            return parts[parts.Length - 2];
        }
        return string.Empty;
    }
}