using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tools;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected;

/// <summary>
/// Integration tests for ReadConsoleTool with actual Unity connection.
/// Tests filtering, ordering, and count limits for console messages.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ReadConsoleToolIntegrationTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings for ReadConsole integration tests
    /// </summary>
    private class ReadConsoleTestSettings : UnityConnectedTestUtility.TestConfiguration
    {
        public ReadConsoleTool? ReadConsoleTool;
    }

    private ReadConsoleTestSettings? readConsoleTestSettings;
    
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

        ServiceProvider _serviceProvider = services.BuildServiceProvider();

        readConsoleTestSettings = new ReadConsoleTestSettings()
        {
            Name = "ReadConsoleIntegrationTest",
            SoughtHarnessName = "ReadConsoleIntegrationTest",
            TestAfterSetup = RunAllReadConsoleTests,
            ServiceProvider = _serviceProvider!,
            UnityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>()!,
            StateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>()!,
            ForceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>()!,
            ReadConsoleTool = _serviceProvider.GetRequiredService<ReadConsoleTool>(),
        };
    }
    
    [TearDown]
    public override void TearDown()
    {
        base.TearDown();

        if(readConsoleTestSettings != null)
        {
            readConsoleTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests ReadConsoleTool filtering, ordering, and count limits")]
    public void TestReadConsoleIntegrationFlow()
    {
        ExecuteTestSteps(RunReadConsoleIntegrationTestFlow());
    }

    /// <summary>
    /// Use the UnityConnectedTestUtility to run the actual test flow
    /// </summary>
    private IEnumerator RunReadConsoleIntegrationTestFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(readConsoleTestSettings!);
    }

    /// <summary>
    /// Run all ReadConsole tests after the integration test harness is set up
    /// </summary>
    private IEnumerator RunAllReadConsoleTests(TestConfiguration _config)
    {
        if (!(_config is ReadConsoleTestSettings castConfig))
        {
            Assert.Fail($"Testconfiguration is not of correct type {typeof(ReadConsoleTestSettings).Name}");
            yield break;
        }
        
        Assert.That(castConfig.ReadConsoleTool, Is.Not.Null, "Read console tool should be available");

        // Test 1: Test individual message type filtering
        yield return TestIndividualMessageTypeFiltering(castConfig);
        
        // Test 2: Test combined message type filtering
        yield return TestCombinedMessageTypeFiltering(castConfig);
        
        // Test 3: Test message ordering (newest first)
        yield return TestMessageOrdering(castConfig);
        
        // Test 4: Test count limit enforcement
        yield return TestCountLimitEnforcement(castConfig);
        
        // Test 5: Test clear console functionality
        yield return TestClearConsoleFunctionality(castConfig);
    }

    /// <summary>
    /// Test filtering for individual message types (Log, Warning, Error)
    /// </summary>
    private IEnumerator TestIndividualMessageTypeFiltering(ReadConsoleTestSettings config)
    {
        Console.WriteLine("Testing individual message type filtering...");
        
        // Test Log messages only
        var logTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "log" },
            filterText: "ReadConsoleIntegrationTest_Log",
            count: 10
        );
        
        var logResult = WaitForTask(logTask, "Read log messages");
        AssertSuccess(logResult, "Reading log messages");
        
        var logEntries = GetEntries(logResult);
        Assert.That(logEntries.Count, Is.GreaterThan(0), "Should find log messages");
        
        // Verify all entries are log type
        foreach (JObject entry in logEntries)
        {
            var message = entry["message"]?.ToString() ?? "";
            int msgLen = int.Min(message.Length, 50);
            Assert.That(message.Substring(0, msgLen).Contains("_Log_"), Is.True, $"Message should be a log type: {message}");
            Assert.That(message.Substring(0, msgLen).Contains("_Warning_"), Is.False, $"Message should not be a warning: {message}");
            Assert.That(message.Substring(0, msgLen).Contains("_Error_"), Is.False, $"Message should not be an error: {message}");
        }
        
        // Test Warning messages only
        var warningTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "warning" },
            filterText: "ReadConsoleIntegrationTest_Warning",
            count: 10
        );
        
        var warningResult = WaitForTask(warningTask, "Read warning messages");
        AssertSuccess(warningResult, "Reading warning messages");
        
        var warningEntries = GetEntries(warningResult);
        Assert.That(warningEntries.Count, Is.GreaterThan(0), "Should find warning messages");
        
        // Verify all entries are warning type
        foreach (JObject entry in warningEntries)
        {
            var message = entry["message"]?.ToString() ?? "";
            Assert.That(message.Contains("_Warning_"), Is.True, $"Message should be a warning type: {message}");
        }
        
        // Test Error messages only
        var errorTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "error" },
            filterText: "ReadConsoleIntegrationTest_Error",
            count: 100
        );
        
        var errorResult = WaitForTask(errorTask, "Read error messages");
        AssertSuccess(errorResult, "Reading error messages");
        
        var errorEntries = GetEntries(errorResult);
        Assert.That(errorEntries.Count, Is.GreaterThan(0), "Should find error messages");
        
        // Verify all entries are error type
        foreach (JObject entry in errorEntries)
        {
            var message = entry["message"]?.ToString() ?? "";
            Assert.That(message.Contains("_Error_"), Is.True, $"Message should be an error type: {message}");
        }
        
        Console.WriteLine("Individual message type filtering test passed");
        yield return null;
    }

    /// <summary>
    /// Test combined message type filtering
    /// </summary>
    private IEnumerator TestCombinedMessageTypeFiltering(ReadConsoleTestSettings config)
    {
        Console.WriteLine("Testing combined message type filtering...");
        
        // Test Log and Warning messages combined
        var combinedTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "log", "warning" },
            filterText: "ReadConsoleIntegrationTest",
            count: 200
        );
        
        var combinedResult = WaitForTask(combinedTask, "Read log and warning messages");
        AssertSuccess(combinedResult, "Reading combined log and warning messages");
        
        var combinedEntries = GetEntries(combinedResult);
        Assert.That(combinedEntries.Count, Is.GreaterThan(0), "Should find combined messages");
        
        bool foundLog = false;
        bool foundWarning = false;
        bool foundError = false;
        

        foreach (JObject entry in combinedEntries)
        {
            var message = entry["message"]?.ToString() ?? "";
            int msgLen = int.Min(message.Length, 50);
            //Only check the first 100 characters to avoid 'finding' wrong types in debugging messages on the unity side (messages containing other messages)
            if (message.Substring(0, msgLen).Contains("_Log_")) foundLog = true;
            if (message.Substring(0, msgLen).Contains("_Warning_")) foundWarning = true;
            if (message.Substring(0, msgLen).Contains("_Error_")) foundError = true;
        }
        
        Assert.That(foundLog, Is.True, "Should find log messages in combined filter");
        Assert.That(foundWarning, Is.True, "Should find warning messages in combined filter");
        Assert.That(foundError, Is.False, "Should NOT find error messages when not included in filter");
        
        // Test all message types
        var allTypesTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "log", "warning", "error" },
            filterText: "ReadConsoleIntegrationTest",
            count: 30
        );
        
        var allTypesResult = WaitForTask(allTypesTask, "Read all message types");
        AssertSuccess(allTypesResult, "Reading all message types");
        
        var allEntries = GetEntries(allTypesResult);
        Assert.That(allEntries.Count, Is.GreaterThan(10), "Should find messages of all types");
        
        Console.WriteLine("Combined message type filtering test passed");
        yield return null;
    }

    /// <summary>
    /// Test that messages are returned in newest-first order
    /// </summary>
    private IEnumerator TestMessageOrdering(ReadConsoleTestSettings config)
    {
        Console.WriteLine("Testing message ordering (newest first)...");
        
        // Get all messages with numbered identifiers
        var orderTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            types: new[] { "log" },
            filterText: "ReadConsoleIntegrationTest_Log",
            count: 30
        );
        
        var orderResult = WaitForTask(orderTask, "Read messages for ordering test");
        AssertSuccess(orderResult, "Reading messages for ordering");
        
        var orderEntries = GetEntries(orderResult);
        Assert.That(orderEntries.Count, Is.LessThanOrEqualTo(30), "Should get equal or less than 30 messages");
        
        // Extract message numbers and verify they are in descending order (newest first)
        var messageNumbers = new List<int>();
        foreach (JObject entry in orderEntries)
        {
            var message = entry["message"]?.ToString() ?? "";
            Console.WriteLine($"Message: {message.Length}");
            if (message.Length > 100)
            {
                continue; // Skip overly long messages that may be debug info
            }

            // Extract number from message like "ReadConsoleIntegrationTest_Log_3: This is log message number 3"
            var parts = message.Split('_');
            if (parts.Length >= 3)
            {
                var numberPart = parts[2].Split(':')[0];
                if (int.TryParse(numberPart, out int number))
                {
                    messageNumbers.Add(number);
                }
            }
        }
        
        Assert.That(messageNumbers.Count, Is.EqualTo(5), "Should extract numbers from all messages");
        
        // Messages should be in descending order (5, 4, 3, 2, 1) since newest first
        for (int i = 0; i < messageNumbers.Count - 1; i++)
        {
            Assert.That(messageNumbers[i], Is.GreaterThan(messageNumbers[i + 1]), 
                $"Messages should be in descending order (newest first). Got: {string.Join(", ", messageNumbers)}");
        }
        
        Console.WriteLine($"Message ordering verified: {string.Join(", ", messageNumbers)} (newest first)");
        yield return null;
    }

    /// <summary>
    /// Test that count limit is properly enforced
    /// </summary>
    private IEnumerator TestCountLimitEnforcement(ReadConsoleTestSettings config)
    {
        Console.WriteLine("Testing count limit enforcement...");
        
        // Test with count = 3
        var limitTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "ReadConsoleIntegrationTest",
            count: 3
        );
        
        var limitResult = WaitForTask(limitTask, "Read messages with count limit 3");
        AssertSuccess(limitResult, "Reading messages with count limit");
        
        var limitEntries = GetEntries(limitResult);
        Assert.That(limitEntries.Count, Is.EqualTo(3), "Should return exactly 3 messages when count=3");
        
        // Test with count = 1
        var singleTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "ReadConsoleIntegrationTest",
            count: 1
        );
        
        var singleResult = WaitForTask(singleTask, "Read single message");
        AssertSuccess(singleResult, "Reading single message");
        
        var singleEntries = GetEntries(singleResult);
        Assert.That(singleEntries.Count, Is.EqualTo(1), "Should return exactly 1 message when count=1");
        
        // Test with large count (should return all available)
        var largeTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "ReadConsoleIntegrationTest",
            count: 100
        );
        
        var largeResult = WaitForTask(largeTask, "Read messages with large count");
        AssertSuccess(largeResult, "Reading messages with large count");
        
        var largeEntries = GetEntries(largeResult);
        Assert.That(largeEntries.Count, Is.GreaterThan(15), "Should find at least 15 messages total");
        Assert.That(largeEntries.Count, Is.LessThanOrEqualTo(100), "Should not exceed requested count");
        
        Console.WriteLine("Count limit enforcement test passed");
        yield return null;
    }

    /// <summary>
    /// Test clearing the console
    /// </summary>
    private IEnumerator TestClearConsoleFunctionality(ReadConsoleTestSettings config)
    {
        Console.WriteLine("Testing clear console functionality...");
        
        // First verify we have messages
        var beforeClearTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "ReadConsoleIntegrationTest",
            count: 10
        );
        
        var beforeClearResult = WaitForTask(beforeClearTask, "Read messages before clear");
        AssertSuccess(beforeClearResult, "Reading messages before clear");
        
        var beforeClearEntries = GetEntries(beforeClearResult);
        Assert.That(beforeClearEntries.Count, Is.GreaterThan(0), "Should have messages before clear");
        
        // Clear the console
        var clearTask = config.ReadConsoleTool!.ReadConsole(
            action: "clear"
        );
        
        var clearResult = WaitForTask(clearTask, "Clear console");
        AssertSuccess(clearResult, "Clearing console");
        
        // Verify console is cleared
        var afterClearTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "ReadConsoleIntegrationTest",
            count: 10
        );
        
        var afterClearResult = WaitForTask(afterClearTask, "Read messages after clear");
        AssertSuccess(afterClearResult, "Reading messages after clear");
        
        var afterClearEntries = GetEntries(afterClearResult);
        Assert.That(afterClearEntries.Count, Is.EqualTo(0), "Should have no messages after clear");
        
        Console.WriteLine("Clear console functionality test passed");
        yield return null;
    }

    /// <summary>
    /// Helper to wait for a task and return its result
    /// </summary>
    private JObject WaitForTask(Task<object?>? task, string description)
    {
        Assert.That(task, Is.Not.Null, $"Task should not be null for: {description}");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(task!, waitForTask).Result == waitForTask)
        {
            Assert.Fail($"Task timed out: {description}");
        }
        
        var result = task!.Result;
        Assert.That(result, Is.Not.Null, $"Result should not be null for: {description}");
        
        return JObject.FromObject(result);
    }

    /// <summary>
    /// Helper to assert task success
    /// </summary>
    private void AssertSuccess(JObject result, string operation)
    {
        var success = result["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"{operation} should succeed. Error: {result["error"]}");
    }

    /// <summary>
    /// Helper to get entries array from result
    /// </summary>
    private JArray GetEntries(JObject result)
    {
        var entries = result["entries"] as JArray;
        Assert.That(entries, Is.Not.Null, "Should return entries array");
        return entries!;
    }
}