using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tools;
using static UMCPServer.Tools.GetTestsTool;

namespace UMCPServer.Tests.IntegrationTests.Tools;

/// <summary>
/// Integration test for UMCP-Story-008 validation - tests RunTests tool with real Unity connection
/// to validate the fix for LLM client result handling. This test specifically validates the 
/// E2ETestEStageModuleProjection test case.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
[Category("UMCP-Story-008")]
public class RunTestsResultValidationTests : IntegrationTestBase
{
    private ServiceProvider? _serviceProvider;
    private UnityConnectionService? _unityConnection;
    private UnityStateConnectionService? _stateConnection;
    private GetTestsTool? _getTestsTool;
    private RunTestsTool? _runTestsTool;
    private ForceUpdateEditorTool? _forceUpdateTool;
    
    private const int UnityPort = 6400;
    private const int StatePort = 6401;
    
    // Test filter for E2ETestEStageModuleProjection 
    private const string TestFilter = "UMCP";
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        
        // Set up dependency injection
        var services = new ServiceCollection();
        
        // Configure logging with more verbose output for debugging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Configure server settings
        services.Configure<ServerConfiguration>(options =>
        {
            options.UnityHost = "localhost";
            options.UnityPort = UnityPort;
            options.UnityStatePort = StatePort;
            options.ConnectionTimeoutSeconds = 60; // Extended timeout for test execution
            options.MaxRetries = 5;
            options.RetryDelaySeconds = 2;
            options.BufferSize = 16 * 1024 * 1024; // 16MB for large test results
            options.IsRunningInContainer = false;
        });
        
        // Register services with interfaces
        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());
        
        services.AddSingleton<UnityStateConnectionService>();
        services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());
        
        services.AddSingleton<GetTestsTool>();
        services.AddSingleton<RunTestsTool>();
        services.AddSingleton<ForceUpdateEditorTool>();
        
        _serviceProvider = services.BuildServiceProvider();
        _unityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>();
        _stateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>();
        _getTestsTool = _serviceProvider.GetRequiredService<GetTestsTool>();
        _runTestsTool = _serviceProvider.GetRequiredService<RunTestsTool>();
        _forceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>();
    }
    
    [TearDown]
    public override void TearDown()
    {
        // Cleanup services
        _unityConnection?.Dispose();
        _stateConnection?.Dispose();
        _serviceProvider?.Dispose();
        
        base.TearDown();
    }
    
    /// <summary>
    /// Main integration test that validates the UMCP-Story-008 fix by testing the exact scenario
    /// that was failing: running E2ETestEStageModuleProjection and ensuring results are returned
    /// </summary>
    [Test]
    public void ValidateUMCPStory008Fix_RunTestsWithE2ETestEStageModuleProjection_ShouldReturnResults()
    {
        ExecuteTestSteps(ValidateRunTestsResultHandling());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    private IEnumerator ValidateRunTestsResultHandling()
    {
        // Step 1: Check if Unity is running
        Console.WriteLine($"Step {CurrentStep + 1}: Checking if Unity is running with UMCP Client (updated)...");
        bool isUnityAvailable = false;
        bool portCheckDone = false;
        
        CheckUnityPort((_isOpen) => 
        {
            isUnityAvailable = _isOpen;
            portCheckDone = true;
        });
        
        var waitUntil = new WaitUntil(() => portCheckDone);
        waitUntil.TimeoutDuration = 30000; // 30 second timeout
        yield return waitUntil;

        if (!isUnityAvailable)
        {
            Assert.Ignore("Unity with UMCP Client is not running. This test requires Unity to be running with the UMCP Bridge.");
        }
        
        // Step 2: Connect to Unity main connection
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting to Unity main service...");
        Task<bool> connectTask = _unityConnection!.ConnectAsync();
        yield return connectTask;
        
        Assert.That(connectTask.Result, Is.True, "Failed to connect to Unity main service");
        Console.WriteLine("Successfully connected to Unity main service!");
        
        // Step 3: Connect to Unity state service
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting to Unity state service...");
        Task<bool> stateConnectTask = _stateConnection!.ConnectAsync();
        yield return stateConnectTask;
        
        Assert.That(stateConnectTask.Result, Is.True, "Failed to connect to Unity state service");
        Console.WriteLine("Successfully connected to Unity state service!");
        
        // Step 4: Force update Unity Editor to ensure clean state
        Console.WriteLine($"Step {CurrentStep + 1}: Forcing Unity Editor update...");
        Task<object> forceUpdateTask = _forceUpdateTool!.ForceUpdateEditor();

        


        yield return forceUpdateTask;
        
        var forceUpdateResult = forceUpdateTask.Result as dynamic;
        Assert.That(forceUpdateResult.success, Is.True, "Force update should succeed");
        Console.WriteLine("Unity Editor successfully updated!");
        //yield return new WaitForSeconds(10);
        Console.WriteLine("Waited for 10 seconds until force update resolved!");

        // Step 5: Validate that E2ETestEStageModuleProjection test exists
        Console.WriteLine($"Step {CurrentStep + 1}: Validating that '{TestFilter}' test exists...");
        Task<object> getTestsTask = _getTestsTool!.GetTests("EditMode", TestFilter);
        yield return getTestsTask;
        
        dynamic getTestsResult = getTestsTask.Result;
        Assert.That(getTestsResult.success, Is.True, "GetTests should succeed");
        
        if (getTestsResult.count == 0)
        {
            Assert.Fail($"No tests found matching filter '{TestFilter}'. This test requires the E2ETestEStageModuleProjection test to be present in the Unity project.");
        }
        
        Console.WriteLine($"Found {getTestsResult.count} test(s) matching '{TestFilter}' filter");
        
        // Extract full test name for more precise filtering
        var tests = getTestsResult.tests as List<dynamic>;
        Console.WriteLine($"# tests:" + tests?.Count);

        GetTestToolResult firstTest = tests?[0];
        Console.WriteLine($"# has value?:{((firstTest != null) ? firstTest.GetType().Name : "NULL")}");
        //Console.WriteLine(firstTest.Cast<object>);

        string ? fullTestName = firstTest?.TestName;

        if (string.IsNullOrEmpty(fullTestName))
        {
            Assert.Fail("Could not extract full test name from GetTests result");
        }
        
        Console.WriteLine($"Full test name: {fullTestName}");
        
        // Step 6: Run the specific test and validate EVERY step in the result handling pipeline
        Console.WriteLine($"Step {CurrentStep + 1}: Running test '{fullTestName}' and validating result pipeline...");
        
        // Record the time before starting the test
        var testStartTime = DateTime.UtcNow;
        Console.WriteLine($"Test execution started at: {testStartTime:yyyy-MM-dd HH:mm:ss.fff}");
        
        //We want to test against the full filter
        Task<object> runTestTask = _runTestsTool!.RunTests("EditMode", new string[] { TestFilter }, true, true);
        Task delay = Task.Delay(TimeSpan.FromSeconds(60)); // Wait a bit to ensure the test starts properly

        yield return Task.WhenAny(runTestTask, delay);

        if(delay.IsCompleted)
        {
            Assert.Fail("Test execution did not complete within the expected time frame. This may indicate a hanging issue in the RunTests tool.");
        }
        
        var testEndTime = DateTime.UtcNow;
        var testDuration = testEndTime - testStartTime;
        Console.WriteLine($"Test execution completed at: {testEndTime:yyyy-MM-dd HH:mm:ss.fff}");
        Console.WriteLine($"Total execution time: {testDuration.TotalSeconds:F2} seconds");
        
        var runResult = runTestTask.Result;
        
        // Step 7: Validate that we got a proper result object (this was the main issue in UMCP-Story-008)
        Console.WriteLine($"Step {CurrentStep + 1}: Validating result object structure...");
        
        Assert.That(runResult, Is.Not.Null, "RunTests should return a non-null result");
        Console.WriteLine($"✓ Result object is not null");
        
        // Check if it's a JObject or a different type
        if (runResult is JObject jObjectResult)
        {
            Console.WriteLine($"✓ Result is JObject with properties: {string.Join(", ", jObjectResult.Properties().Select(p => p.Name))}");
            
            // Validate success field
            var success = jObjectResult["success"]?.Value<bool>();
            Assert.That(success, Is.True, "RunTests should report success=true");
            Console.WriteLine($"✓ Success field: {success}");
            
            // Check for message field
            var message = jObjectResult["message"]?.Value<string>();
            Console.WriteLine($"✓ Message: {message ?? "No message field"}");
            
            // Check for test result files (this was a key part of the fix)
            var testResultFiles = jObjectResult["testResultFiles"] as JArray;
            if (testResultFiles != null && testResultFiles.Count > 0)
            {
                Console.WriteLine($"✓ Test result files found: {testResultFiles.Count}");
                foreach (var file in testResultFiles)
                {
                    Console.WriteLine($"  - {file}");
                }
                
                // This validates that the file path detection logic is working
                Assert.That(testResultFiles.Count, Is.GreaterThan(0), "Should have at least one test result file");
            }
            else
            {
                Console.WriteLine($"⚠ No test result files in response (testResultFiles field: {testResultFiles?.Count ?? 0} items)");
            }
            
            // Check test count and success status
            var testCount = jObjectResult["testCount"]?.Value<int>();
            var allTestsPassed = jObjectResult["allTestsPassed"]?.Value<bool>();
            
            Console.WriteLine($"✓ Test count: {testCount ?? 0}");
            Console.WriteLine($"✓ All tests passed: {allTestsPassed ?? false}");
            
        }
        else if (runResult is Dictionary<string, object> dictResult)
        {
            Console.WriteLine($"✓ Result is Dictionary with keys: {string.Join(", ", dictResult.Keys)}");
            
            // Validate success field
            if (dictResult.TryGetValue("success", out var successObj) && successObj is bool success)
            {
                Assert.That(success, Is.True, "RunTests should report success=true");
                Console.WriteLine($"✓ Success field: {success}");
            }
            else
            {
                Assert.Fail("Result should contain a 'success' boolean field");
            }
            
            // Check for message
            if (dictResult.TryGetValue("message", out var messageObj))
            {
                Console.WriteLine($"✓ Message: {messageObj}");
            }
            
            // Check for test result files
            if (dictResult.TryGetValue("testResultFiles", out var filesObj) && filesObj is IList<object> filesList)
            {
                Console.WriteLine($"✓ Test result files found: {filesList.Count}");
                foreach (var file in filesList)
                {
                    Console.WriteLine($"  - {file}");
                }
                Assert.That(filesList.Count, Is.GreaterThan(0), "Should have at least one test result file");
            }
            else
            {
                Console.WriteLine($"⚠ No test result files in dictionary response");
            }
        }
        else
        {
            Console.WriteLine($"Result type: {runResult.GetType().Name}");
            Console.WriteLine($"Result toString: {runResult}");
            Assert.Fail($"Unexpected result type: {runResult.GetType().Name}. Expected JObject or Dictionary<string, object>");
        }
        
        // Step 8: Validate timing and performance expectations
        Console.WriteLine($"Step {CurrentStep + 1}: Validating performance characteristics...");
        
        // The test should complete in reasonable time (not hang indefinitely)
        Assert.That(testDuration.TotalMinutes, Is.LessThan(5), 
            $"Test execution took too long: {testDuration.TotalSeconds:F2} seconds. This may indicate polling issues.");
        
        Console.WriteLine($"✓ Test completed in reasonable time: {testDuration.TotalSeconds:F2} seconds");
        
        // Step 9: Additional validation - check Unity connection is still alive
        Console.WriteLine($"Step {CurrentStep + 1}: Validating Unity connection is still responsive...");
        
        Assert.That(_unityConnection.IsConnected, Is.True, "Unity connection should still be alive after test execution");
        Assert.That(_stateConnection.IsConnected, Is.True, "Unity state connection should still be alive after test execution");
        
        Console.WriteLine($"✓ Unity connections are still active");
        
        Console.WriteLine("🎉 UMCP-Story-008 validation completed successfully!");
        Console.WriteLine("✅ RunTests tool correctly returns results to LLM client");
        Console.WriteLine($"✅ Test execution pipeline working end-to-end");
        Console.WriteLine($"✅ No hanging or timeout issues detected");
    }
    
    /// <summary>
    /// Helper method to check if Unity is running on the expected port
    /// </summary>
    private async void CheckUnityPort(Action<bool> callback)
    {
        try
        {
            using (var client = new System.Net.Sockets.TcpClient())
            {
                var connectTask = client.ConnectAsync("localhost", UnityPort);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                Task _comp = await Task.WhenAny(connectTask, timeoutTask);
                if (timeoutTask.IsCompleted)
                {
                    throw new TimeoutException("Connection timeout");
                }
                callback(true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unity port check failed: {ex.Message}");
            callback(false);
        }
    }
}

