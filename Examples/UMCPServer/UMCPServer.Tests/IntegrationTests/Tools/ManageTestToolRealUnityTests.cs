using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools;

/// <summary>
/// Real integration tests for GetTests and RunTests tools with actual Unity connection
/// This test requires Unity to be running with UMCP Client
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageTestToolRealUnityTests : IntegrationTestBase
{
    private ServiceProvider? _serviceProvider;
    private UnityConnectionService? _unityConnection;
    private UnityStateConnectionService? _stateConnection;
    private GetTestsTool? _getTestsTool;
    private RunTestsTool? _runTestsTool;
    private ForceUpdateEditorTool? _forceUpdateTool;
    
    private const int UnityPort = 6400;
    private const int StatePort = 6401;
    
    // Test namespace for our integration tests
    private const string TestNamespace = "UMCP.editor.integrationtests";
    private const string TestScriptName = "SimpleEditModeTests";
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        
        // Set up dependency injection
        var services = new ServiceCollection();
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        // Configure server settings
        services.Configure<ServerConfiguration>(options =>
        {
            options.UnityHost = "localhost";
            options.UnityPort = UnityPort;
            options.UnityStatePort = StatePort;
            options.ConnectionTimeoutSeconds = 30;
            options.MaxRetries = 5;
            options.RetryDelaySeconds = 2;
            options.BufferSize = 16 * 1024 * 1024; // 16MB for large test results
            options.IsRunningInContainer = false;
        });
        
        // Register services
        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<UnityStateConnectionService>();
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
    
    [Test]
    public void TestGetTestsWithRealUnity_ShouldNotLockEditor()
    {
        ExecuteTestSteps(TestGetTestsRealConnection());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void TestRunTestsWithRealUnity_ShouldNotLockEditor()
    {
        ExecuteTestSteps(TestRunTestsRealConnection());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    private IEnumerator TestGetTestsRealConnection()
    {
        // Step 1: Check if Unity is running
        Console.WriteLine($"Step {CurrentStep + 1}: Checking if Unity is running with UMCP Client...");
        bool isUnityAvailable = false;
        bool portCheckDone = false;
        
        CheckUnityPort((_isOpen) => 
        {
            isUnityAvailable = _isOpen;
            portCheckDone = true;
        });
        
        yield return new WaitUntil(() => portCheckDone);
        
        if (!isUnityAvailable)
        {
            Assert.Ignore("Unity with UMCP Client is not running. This test requires Unity to be running.");
        }
        
        // Step 2: Connect to Unity
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting to Unity...");
        Task<bool> connectTask = _unityConnection!.ConnectAsync();
        yield return connectTask;
        
        Assert.That(connectTask.Result, Is.True, "Failed to connect to Unity");
        Console.WriteLine("Successfully connected to Unity!");
        
        // Step 3: Connect state service
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting state service...");
        Task<bool> stateConnectTask = _stateConnection!.ConnectAsync();
        yield return stateConnectTask;
        
        // Step 4: Test GetTests with All mode
        Console.WriteLine($"Step {CurrentStep + 1}: Testing GetTests with TestMode='All'...");
        Task<object> getAllTestsTask = _getTestsTool!.GetTests("All");
        yield return getAllTestsTask;
        
        dynamic allTestsResult = getAllTestsTask.Result;
        Assert.That(allTestsResult.success, Is.True, "GetTests should succeed");
        Console.WriteLine($"Found {allTestsResult.count} tests total");
        
        // Step 5: Test GetTests with EditMode
        Console.WriteLine($"Step {CurrentStep + 1}: Testing GetTests with TestMode='EditMode'...");
        Task<object> getEditModeTestsTask = _getTestsTool.GetTests("EditMode");
        yield return getEditModeTestsTask;
        
        dynamic editModeResult = getEditModeTestsTask.Result;
        Assert.That(editModeResult.success, Is.True, "GetTests EditMode should succeed");
        Console.WriteLine($"Found {editModeResult.count} EditMode tests");
        
        // Step 6: Test GetTests with filter
        Console.WriteLine($"Step {CurrentStep + 1}: Testing GetTests with filter...");
        Task<object> getFilteredTestsTask = _getTestsTool.GetTests("All", "UMCP");
        yield return getFilteredTestsTask;
        
        dynamic filteredResult = getFilteredTestsTask.Result;
        Assert.That(filteredResult.success, Is.True, "GetTests with filter should succeed");
        Console.WriteLine($"Found {filteredResult.count} tests matching 'UMCP' filter");
        
        Console.WriteLine("GetTests real connection test completed successfully!");
    }
    
    private IEnumerator TestRunTestsRealConnection()
    {
        // Step 1: Check if Unity is running
        Console.WriteLine($"Step {CurrentStep + 1}: Checking if Unity is running with UMCP Client...");
        bool isUnityAvailable = false;
        bool portCheckDone = false;
        
        CheckUnityPort((_isOpen) => 
        {
            isUnityAvailable = _isOpen;
            portCheckDone = true;
        });
        
        yield return new WaitUntil(() => portCheckDone);
        
        if (!isUnityAvailable)
        {
            Assert.Ignore("Unity with UMCP Client is not running. This test requires Unity to be running.");
        }
        
        // Step 2: Connect to Unity
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting to Unity...");
        Task<bool> connectTask = _unityConnection!.ConnectAsync();
        yield return connectTask;
        
        Assert.That(connectTask.Result, Is.True, "Failed to connect to Unity");
        
        // Step 3: Connect state service
        Console.WriteLine($"Step {CurrentStep + 1}: Connecting state service...");
        Task<bool> stateConnectTask = _stateConnection!.ConnectAsync();
        yield return stateConnectTask;
        
        // Step 4: First get available tests
        Console.WriteLine($"Step {CurrentStep + 1}: Getting available tests...");
        Task<object> getTestsTask = _getTestsTool!.GetTests("EditMode", "UMCP");
        yield return getTestsTask;
        
        dynamic getTestsResult = getTestsTask.Result;
        Assert.That(getTestsResult.success, Is.True, "GetTests should succeed");
        
        if (getTestsResult.count == 0)
        {
            Console.WriteLine("No UMCP tests found. Skipping RunTests test.");
            yield break;
        }
        
        // Step 5: Run the first available test
        Console.WriteLine($"Step {CurrentStep + 1}: Running a single test...");
        
        // Get first test name from results
        var tests = getTestsResult.tests as JArray;
        string? firstTestName = tests?[0]?["TestName"]?.ToString();
        
        if (string.IsNullOrEmpty(firstTestName))
        {
            Console.WriteLine("Could not extract test name. Skipping RunTests test.");
            yield break;
        }
        
        Console.WriteLine($"Running test: {firstTestName}");
        
        Task<object> runTestTask = _runTestsTool!.RunTests("EditMode", new[] { firstTestName }, true, true);
        yield return runTestTask;
        
        var runResult = runTestTask.Result as JObject;
        Assert.That(runResult?["success"]?.Value<bool>(), Is.True, "RunTests should succeed");
        
        // Check if we got test results
        bool allSuccess = runResult?["AllSuccess"]?.Value<bool>() ?? false;
        var testResults = runResult?["TestResults"] as JArray;
        
        Console.WriteLine($"Test execution completed. All tests passed: {allSuccess}");
        if (testResults != null)
        {
            Console.WriteLine($"Executed {testResults.Count} test(s)");
        }
        
        Console.WriteLine("RunTests real connection test completed successfully!");
    }
    
    private async void CheckUnityPort(Action<bool> callback)
    {
        try
        {
            using (var client = new System.Net.Sockets.TcpClient())
            {
                var connectTask = client.ConnectAsync("localhost", UnityPort);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                
                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    throw new TimeoutException("Connection timeout");
                }
                
                callback(true);
            }
        }
        catch
        {
            callback(false);
        }
    }
}