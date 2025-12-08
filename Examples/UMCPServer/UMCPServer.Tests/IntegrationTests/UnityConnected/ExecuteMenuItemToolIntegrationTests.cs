using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using System.Threading.Tasks;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tests.IntegrationTests.Tools;
using UMCPServer.Tools;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected;

/// <summary>
/// Integration tests for ExecuteMenuItem tool with actual Unity connection.
/// Tests the complete ExecuteMenuItem functionality including menu item execution and log verification.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ExecuteMenuItemToolIntegrationTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings including the ExecuteMenuItemTool and ReadConsoleTool
    /// </summary>
    private class ExecuteMenuItemTestSettings : UnityConnectedTestUtility.TestConfiguration
    {
        public ExecuteMenuItemTool? ExecuteMenuItemTool;
        public ReadConsoleTool? ReadConsoleTool;
    }

    /// <summary>
    /// Reference to the test settings shared in setup and run
    /// </summary>
    private ExecuteMenuItemTestSettings? executeMenuItemTestSettings;
    
    private const int UnityPort = 6400;
    private const int StatePort = 6401;
    
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
            options.BufferSize = 16 * 1024 * 1024; // 16MB for large responses
            options.IsRunningInContainer = false;
        });
        
        // Register services
        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());

        services.AddSingleton<UnityStateConnectionService>();
        services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());

        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();
        services.AddSingleton<ExecuteMenuItemTool>();

        ServiceProvider _serviceProvider = services.BuildServiceProvider();

        executeMenuItemTestSettings = new ExecuteMenuItemTestSettings()
        {
            Name = "TestExecuteMenuItemIntegration",
            SoughtHarnessName = "ExecuteMenuItemIntegrationTest",
            TestAfterSetup = VerifyExecuteMenuItemFunctionality,
            ServiceProvider = _serviceProvider!,
            UnityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>()!,
            StateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>()!,
            ForceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>()!,
            ExecuteMenuItemTool = _serviceProvider.GetRequiredService<ExecuteMenuItemTool>(),
            ReadConsoleTool = _serviceProvider.GetRequiredService<ReadConsoleTool>(),
            SetupTimeoutS = 90, //The setup timeout can be significant if it needs to wait for the compile flag to be active
            MaxStateRetries = 100,
            CleanupTimeoutS = 90 //The setup timeout can be significant if it needs to wait for the compile flag to be removed
        };
    }
    
    [TearDown]
    public override void TearDown()
    {
        base.TearDown();

        if(executeMenuItemTestSettings != null)
        {
            executeMenuItemTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests the complete ExecuteMenuItem integration flow with Unity connection")]
    public void TestExecuteMenuItemIntegrationFlow()
    {
        ExecuteTestSteps(RunExecuteMenuItemIntegrationFlow());
    }

    /// <summary>
    /// Use the UnityConnectedTestUtility to run the actual test flow
    /// </summary>
    /// <returns></returns>
    private IEnumerator RunExecuteMenuItemIntegrationFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(executeMenuItemTestSettings!);
    }

    /// <summary>
    /// Verification of the ExecuteMenuItem functionality after harness setup
    /// </summary>
    /// <param name="_config">reference to the test configuration</param>
    /// <returns></returns>
    private IEnumerator VerifyExecuteMenuItemFunctionality(TestConfiguration _config)
    {
        if (!(_config is ExecuteMenuItemTestSettings castConfig))
        {
            Assert.Fail($"Test configuration is not of correct type {typeof(ExecuteMenuItemTestSettings).Name}");
            yield break;
        }
        
        Assert.That(castConfig.ExecuteMenuItemTool, Is.Not.Null, "ExecuteMenuItem tool should be available");
        Assert.That(castConfig.ReadConsoleTool, Is.Not.Null, "ReadConsole tool should be available");

        // Test 1: Execute the main test menu item
        Console.WriteLine("Testing menu item: UMCP Integration Tests/Execute Menu Item Test");
        var executeMenuResult = ExecuteMenuItemAndWaitForResult(
            castConfig.ExecuteMenuItemTool!, 
            "UMCP Integration Tests/Execute Menu Item Test"
        );
        
        if (executeMenuResult == null)
        {
            Assert.Fail("Failed to execute menu item");
        }

        var executeTask = Task.Delay(TimeSpan.FromSeconds(15));
        if (Task.WhenAny(executeMenuResult!, executeTask).Result == executeTask)
        {
            Assert.Fail("ExecuteMenuItem command timed out");
        }
        
        var result = executeMenuResult!.Result;
        Assert.That(result, Is.Not.Null, "ExecuteMenuItem should return a result");
        
        var resultObj = JObject.FromObject(result);
        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"ExecuteMenuItem should succeed. Error: {resultObj["error"]}");
        
        Console.WriteLine($"ExecuteMenuItem result: {resultObj}");


        //yield return new WaitForSeconds(20f);

        // Test 2: Poll for the log message with retries
        Console.WriteLine("Polling for log message from executed menu item (max 5 attempts)");
        bool foundExpectedMessage = false;
        int maxPollingAttempts = 20;
        
        for (int attempt = 1; attempt <= maxPollingAttempts && !foundExpectedMessage; attempt++)
        {
            Console.WriteLine($"NEW Polling attempt {attempt}/{maxPollingAttempts}");
            
            // Wait 0.5 seconds between polling attempts
            if (attempt > 1)
            {
                yield return new WaitForSeconds(2f);
            }
            
            var readConsoleCommand = castConfig.ReadConsoleTool!.ReadConsole(
                action: "get",
                filterText: "[UMCP_INTEGRATION_TEST] ExecuteMenuItem test menu item was executed successfully!",
                count: 40
            );

            if (readConsoleCommand == null)
            {
                Console.WriteLine($"Attempt {attempt}: Failed to initiate read console command");
                continue;
            }

            var waitForRead = Task.Delay(TimeSpan.FromSeconds(10));
            if (Task.WhenAny(readConsoleCommand!, waitForRead).Result == waitForRead)
            {
                Console.WriteLine($"Attempt {attempt}: Read console command timed out");
                continue;
            }
            
            var readResult = readConsoleCommand!.Result;
            if (readResult == null)
            {
                Console.WriteLine($"Attempt {attempt}: ReadConsole returned null");
                continue;
            }

            var readResultObj = JObject.FromObject(readResult);
            var readSuccess = readResultObj["success"]?.Value<bool>() ?? false;
            if (!readSuccess)
            {
                Console.WriteLine($"Attempt {attempt}: ReadConsole failed - {readResultObj["error"]}");
                continue;
            }

            var logEntries = readResultObj["entries"] as JArray;
            if (logEntries == null || logEntries.Count == 0)
            {
                Console.WriteLine($"Attempt {attempt}: No log entries found");
                continue;
            }

            // Check if we found the expected message
            foreach (JObject entry in logEntries.Cast<JObject>())
            {
                var message = entry["message"]?.ToString();
                if (message != null && message.Contains("[UMCP_INTEGRATION_TEST] ExecuteMenuItem test menu item was executed successfully!"))
                {
                    foundExpectedMessage = true;
                    Console.WriteLine($"Attempt {attempt}: Found expected log message: {message}");
                    break;
                }
            }
            
            if (!foundExpectedMessage)
            {
                Console.WriteLine($"Attempt {attempt}: Expected message not found in {logEntries.Count} entries");
            }
        }

        Assert.That(foundExpectedMessage, Is.True, $"Should find the expected menu execution log message after {maxPollingAttempts} polling attempts");

        // Test 3: Execute second menu item
        Console.WriteLine("Testing menu item: UMCP Integration Tests/Execute Menu Item Test 2");
        var executeMenu2Result = ExecuteMenuItemAndWaitForResult(
            castConfig.ExecuteMenuItemTool!, 
            "UMCP Integration Tests/Execute Menu Item Test 2"
        );
        
        if (executeMenu2Result != null)
        {
            var executeTask2 = Task.Delay(TimeSpan.FromSeconds(15));
            if (Task.WhenAny(executeMenu2Result, executeTask2).Result != executeTask2)
            {
                var result2 = executeMenu2Result.Result;
                var resultObj2 = JObject.FromObject(result2);
                var success2 = resultObj2["success"]?.Value<bool>() ?? false;
                Assert.That(success2, Is.True, $"Second ExecuteMenuItem should succeed. Error: {resultObj2["error"]}");
                Console.WriteLine("Second menu item executed successfully");
            }
        }

        // Test 4: Execute nested menu item
        Console.WriteLine("Testing nested menu item: UMCP Integration Tests/Submenu/Execute Menu Item Nested Test");
        var executeNestedResult = ExecuteMenuItemAndWaitForResult(
            castConfig.ExecuteMenuItemTool!, 
            "UMCP Integration Tests/Submenu/Execute Menu Item Nested Test"
        );
        
        if (executeNestedResult != null)
        {
            var executeTaskNested = Task.Delay(TimeSpan.FromSeconds(15));
            if (Task.WhenAny(executeNestedResult, executeTaskNested).Result != executeTaskNested)
            {
                var resultNested = executeNestedResult.Result;
                var resultObjNested = JObject.FromObject(resultNested);
                var successNested = resultObjNested["success"]?.Value<bool>() ?? false;
                Assert.That(successNested, Is.True, $"Nested ExecuteMenuItem should succeed. Error: {resultObjNested["error"]}");
                Console.WriteLine("Nested menu item executed successfully");
            }
        }

        // Test 5: Test invalid menu item (should fail gracefully)
        Console.WriteLine("Testing invalid menu item: Invalid/Menu/Path");
        var executeInvalidResult = ExecuteMenuItemAndWaitForResult(
            castConfig.ExecuteMenuItemTool!, 
            "Invalid/Menu/Path"
        );
        
        if (executeInvalidResult != null)
        {
            var executeTaskInvalid = Task.Delay(TimeSpan.FromSeconds(15));
            if (Task.WhenAny(executeInvalidResult, executeTaskInvalid).Result != executeTaskInvalid)
            {
                var resultInvalid = executeInvalidResult.Result;
                var resultObjInvalid = JObject.FromObject(resultInvalid);
                // For invalid menu items, we expect success=true but Unity may log an error
                // The tool reports success if the command was sent, even if Unity can't find the menu
                Console.WriteLine($"Invalid menu item result: {resultObjInvalid}");
            }
        }

        // Test 6: Test get_available_menus action (though not fully implemented)
        Console.WriteLine("Testing get_available_menus action");
        var getMenusResult = castConfig.ExecuteMenuItemTool!.ExecuteMenuItem(
            action: "get_available_menus",
            menuPath: "UMCP"
        );
        
        if (getMenusResult != null)
        {
            var getMenusTask = Task.Delay(TimeSpan.FromSeconds(10));
            if (Task.WhenAny(getMenusResult, getMenusTask).Result != getMenusTask)
            {
                var menusResult = getMenusResult.Result;

                //Console.WriteLine($"Raw get_available_menus result: {menusResult.ToString()}");

                var menusResultObj = JObject.FromObject(menusResult);
                var menusSuccess = menusResultObj["success"]?.Value<bool>() ?? false;
                Assert.That(menusSuccess, Is.True, "get_available_menus should return success (even if not fully implemented)");
                Console.WriteLine($"get_available_menus result: {menusResultObj}");
            }
        }

        yield return null;
    }

    /// <summary>
    /// Helper method to execute a menu item and return the task
    /// </summary>
    /// <param name="tool">ExecuteMenuItem tool instance</param>
    /// <param name="menuPath">Menu path to execute</param>
    /// <returns>Task with the result</returns>
    private Task<object>? ExecuteMenuItemAndWaitForResult(ExecuteMenuItemTool tool, string menuPath)
    {
        try
        {
            return tool.ExecuteMenuItem(
                action: "execute",
                menuPath: menuPath
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception executing menu item '{menuPath}': {ex.Message}");
            return null;
        }
    }

    
}