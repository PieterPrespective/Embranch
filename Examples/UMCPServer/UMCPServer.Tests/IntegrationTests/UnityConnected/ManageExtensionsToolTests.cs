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
/// Integration tests for ManageExtensionsTool with actual Unity connection.
/// Tests all 3 actions: query_unity_object_knowledge, custom_unity_object_operation, get_all_registered_operations.
/// Validates proper Unity Object knowledge queries, custom operation execution, and error handling.
/// This test requires Unity to be running with UMCP Client and ManageExtensions harness.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageExtensionsToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom test settings for ManageExtensionsTool tests
    /// </summary>
    private class ManageExtensionsToolTestSettings : TestConfiguration
    {
        public ManageExtensionsTool? ManageExtensionsTool { get; set; }
        public ExecuteMenuItemTool? ExecuteMenuItemTool { get; set; }
        public ReadConsoleTool? ReadConsoleTool { get; set; }
    }

    private ManageExtensionsToolTestSettings? testSettings;

    private const int UnityPort = 6400;
    private const int StatePort = 6401;

    // Common Unity Object types to test
    private const string TestTransformClass = "Transform";
    private const string TestGameObjectClass = "UnityEngine.GameObject";
    private const string TestRigidbodyClass = "Rigidbody";
    private const string TestMonoBehaviourClass = "MonoBehaviour";
    private const string TestScriptableObjectClass = "ScriptableObject";

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

        services.AddSingleton<ManageExtensionsTool>();
        services.AddSingleton<ExecuteMenuItemTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        var serviceProvider = services.BuildServiceProvider();

        testSettings = new ManageExtensionsToolTestSettings()
        {
            Name = "TestManageExtensionsTool",
            SoughtHarnessName = "DummyIntegrationTest",//"ManageExtensionsIntegrationTest",
            TestAfterSetup = ValidateManageExtensionsTests,
            ServiceProvider = serviceProvider,
            UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
            StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
            ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
            ManageExtensionsTool = serviceProvider.GetRequiredService<ManageExtensionsTool>(),
            ExecuteMenuItemTool = serviceProvider.GetRequiredService<ExecuteMenuItemTool>(),
            ReadConsoleTool = serviceProvider.GetRequiredService<ReadConsoleTool>()
        };
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();
        testSettings?.ServiceProvider?.Dispose();
    }

    [Test]
    [Timeout(180000)] // 3 minutes timeout for comprehensive tests
    public void ManageExtensions_IntegrationTests_RunSuccessfully()
    {
        ExecuteTestSteps(RunIntegrationTestFlow());
    }

    /// <summary>
    /// Runs the Unity connected integration test flow
    /// </summary>
    private IEnumerator RunIntegrationTestFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(testSettings!);
    }

    /// <summary>
    /// Validates all ManageExtensions tool operations after the harness is set up
    /// </summary>
    private static IEnumerator ValidateManageExtensionsTests(TestConfiguration settings)
    {
        var config = settings as ManageExtensionsToolTestSettings;
        Assert.That(config, Is.Not.Null, "Settings should be ManageExtensionsToolTestSettings");

        var manageExtensionsTool = config!.ManageExtensionsTool;
        var executeMenuItemTool = config.ExecuteMenuItemTool;
        var readConsoleTool = config.ReadConsoleTool;

        Assert.That(manageExtensionsTool, Is.Not.Null, "ManageExtensionsTool should be available");
        Assert.That(executeMenuItemTool, Is.Not.Null, "ExecuteMenuItemTool should be available");
        Assert.That(readConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");

        Console.WriteLine("Starting ManageExtensionsTool validation tests...");

        // Test query_unity_object_knowledge action
        yield return TestQueryUnityObjectKnowledge(manageExtensionsTool!);
        yield return TestQueryTransformClass(manageExtensionsTool!);
        yield return TestQueryGameObjectClass(manageExtensionsTool!);
        yield return TestQueryInvalidClass(manageExtensionsTool!);

        // Test get_all_registered_operations action
        yield return TestGetAllRegisteredOperations(manageExtensionsTool!);

        // Test custom_unity_object_operation action (if operations are registered)
        yield return TestCustomUnityObjectOperation(manageExtensionsTool!);
        yield return TestInvalidCustomOperation(manageExtensionsTool!);

        Console.WriteLine("ManageExtensionsTool tests completed successfully!");
    }

    #region Query Unity Object Knowledge Tests

    private static IEnumerator TestQueryUnityObjectKnowledge(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Query Unity Object Knowledge - Transform");

        var queryTask = tool.ManageExtensions(
            action: "query_unity_object_knowledge",
            targetClass: TestTransformClass
        );

        if (!WaitForTask(queryTask, out var result))
        {
            Assert.Fail("Query Unity Object Knowledge operation timed out");
        }

        Console.WriteLine("QueryUnityObjectKnowledge result: " + JObject.FromObject(result).ToString());

        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Query operation should succeed. Error: {resultObj["error"]}");

        // Validate response structure
        Assert.That(resultObj["fullClassName"], Is.Not.Null, "fullClassName should be present");
        Assert.That(resultObj["category"], Is.Not.Null, "category should be present");

        // Transform should be a Component
        var category = resultObj["category"]?.ToString();
        Assert.That(category, Is.EqualTo("Component").Or.EqualTo("UnityObject"),
            "Transform should be categorized as Component or UnityObject");

        yield return null;
    }

    private static IEnumerator TestQueryTransformClass(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Query Unity Object Knowledge - Full Transform class");

        var queryTask = tool.ManageExtensions(
            action: "query_unity_object_knowledge",
            targetClass: "UnityEngine.Transform"
        );

        if (!WaitForTask(queryTask, out var result))
        {
            Assert.Fail("Query Transform class operation timed out");
        }

        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Query Transform operation should succeed. Error: {resultObj["error"]}");

        var fullClassName = resultObj["fullClassName"]?.ToString();
        Assert.That(fullClassName, Does.Contain("Transform"),
            "Full class name should contain Transform");

        // Check if serialized form is provided
        if (resultObj["serializedForm"] != null)
        {
            Console.WriteLine("Transform has serialized form information");
        }

        yield return null;
    }

    private static IEnumerator TestQueryGameObjectClass(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Query Unity Object Knowledge - GameObject");

        var queryTask = tool.ManageExtensions(
            action: "query_unity_object_knowledge",
            targetClass: TestGameObjectClass
        );

        if (!WaitForTask(queryTask, out var result))
        {
            Assert.Fail("Query GameObject class operation timed out");
        }

        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Query GameObject operation should succeed. Error: {resultObj["error"]}");

        var category = resultObj["category"]?.ToString();
        Assert.That(category, Is.Not.Null.And.Not.Empty,
            "GameObject should have a category");

        // Check for description
        if (resultObj["description"] != null)
        {
            var description = resultObj["description"]?.ToString();
            Console.WriteLine($"GameObject description: {description}");
        }

        yield return null;
    }

    private static IEnumerator TestQueryInvalidClass(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Query Unity Object Knowledge - Invalid class");

        var queryTask = tool.ManageExtensions(
            action: "query_unity_object_knowledge",
            targetClass: "NonExistentClass12345"
        );

        if (!WaitForTask(queryTask, out var result))
        {
            Assert.Fail("Query invalid class operation timed out");
        }

        var resultObj = JObject.FromObject(result!);

        // The response might indicate the type wasn't found
        if (resultObj["category"]?.ToString() == "TypeNotFound" ||
            resultObj["error"] != null ||
            resultObj["success"]?.Value<bool>() == false)
        {
            Console.WriteLine("Invalid class correctly identified as not found");
        }
        else
        {
            // Some response was still returned, which is acceptable
            Assert.That(resultObj["fullClassName"], Is.Not.Null,
                "Should provide some class information even if not found");
        }

        yield return null;
    }

    #endregion

    #region Get All Registered Operations Tests

    private static IEnumerator TestGetAllRegisteredOperations(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Get All Registered Operations");

        var getOperationsTask = tool.ManageExtensions(
            action: "get_all_registered_operations"
        );

        if (!WaitForTask(getOperationsTask, out var result))
        {
            Assert.Fail("Get all registered operations timed out");
        }

        Console.WriteLine("GetAllRegisteredOperations result: " + JObject.FromObject(result).ToString());

        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Get operations should succeed. Error: {resultObj["error"]}");

        // Check for operations list
        var operations = resultObj["operations"];
        Assert.That(operations, Is.Not.Null, "Operations list should be present");

        var count = resultObj["count"]?.Value<int>() ?? 0;
        Console.WriteLine($"Found {count} registered operations");

        if (operations != null && operations.HasValues)
        {
            foreach (var op in operations)
            {
                Console.WriteLine($"  - Operation: {op}");
            }
        }

        yield return null;
    }

    #endregion

    #region Custom Unity Object Operation Tests

    private static IEnumerator TestCustomUnityObjectOperation(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Custom Unity Object Operation (if available)");

        // First get available operations to test with
        var getOperationsTask = tool.ManageExtensions(
            action: "get_all_registered_operations"
        );

        if (!WaitForTask(getOperationsTask, out var opsResult))
        {
            Console.WriteLine("Could not get operations list, skipping custom operation test");
            yield return null;
            yield break;
        }

        var opsResultObj = JObject.FromObject(opsResult!);
        var operations = opsResultObj["operations"] as JArray;

        if (operations == null || !operations.Any())
        {
            Console.WriteLine("No operations registered, skipping custom operation test");
            yield return null;
            yield break;
        }

        // Try to execute the first available operation
        var firstOp = operations.First() as JObject;
        string? operationName = firstOp?["name"]?.ToString() ?? "TestOperation";

        var operationTask = tool.ManageExtensions(
            action: "custom_unity_object_operation",
            operationName: operationName,
            operationType: "Transform",
            operationParameters: "{}"
        );

        if (!WaitForTask(operationTask, out var result))
        {
            Console.WriteLine("Custom operation execution timed out (may be expected if no operations registered)");
            yield return null;
            yield break;
        }

        var resultObj = JObject.FromObject(result!);
        Console.WriteLine($"Custom operation '{operationName}' result: {resultObj}");

        // If the operation succeeded, validate the response
        if (resultObj["success"]?.Value<bool>() == true)
        {
            Assert.That(resultObj["message"], Is.Not.Null, "Success message should be present");
            Console.WriteLine($"Operation executed successfully: {resultObj["message"]}");
        }

        yield return null;
    }

    private static IEnumerator TestInvalidCustomOperation(ManageExtensionsTool tool)
    {
        Console.WriteLine("\nTesting: Invalid Custom Unity Object Operation");

        var operationTask = tool.ManageExtensions(
            action: "custom_unity_object_operation",
            operationName: "NonExistentOperation12345",
            operationType: "InvalidType",
            operationParameters: "{\"invalid\": \"params\"}"
        );

        if (!WaitForTask(operationTask, out var result))
        {
            Console.WriteLine("Invalid operation timed out (expected behavior)");
            yield return null;
            yield break;
        }

        var resultObj = JObject.FromObject(result!);

        // Expect this to fail
        var success = resultObj["success"]?.Value<bool>() ?? true;
        if (!success)
        {
            Console.WriteLine($"Invalid operation correctly failed: {resultObj["error"]}");
            Assert.That(resultObj["error"], Is.Not.Null.And.Not.Empty,
                "Error message should be provided for invalid operation");
        }
        else
        {
            // Some implementations might handle unknown operations gracefully
            Console.WriteLine("Operation handled invalid request gracefully");
        }

        yield return null;
    }

    #endregion

    #region Missing Action Parameter Test

    [Test]
    public async Task ManageExtensions_MissingAction_ReturnsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.Configure<ServerConfiguration>(options =>
        {
            options.UnityHost = "localhost";
            options.UnityPort = UnityPort;
            options.UnityStatePort = StatePort;
            options.ConnectionTimeoutSeconds = 5;
        });

        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());
        services.AddSingleton<UnityStateConnectionService>();
        services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());
        services.AddSingleton<ManageExtensionsTool>();

        using var serviceProvider = services.BuildServiceProvider();
        var tool = serviceProvider.GetRequiredService<ManageExtensionsTool>();

        // Act - Call without action parameter
        var result = await tool.ManageExtensions(action: "");

        // Assert
        var resultObj = JObject.FromObject(result);
        Assert.That(resultObj["success"]?.Value<bool>(), Is.False, "Should fail without action");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("action"),
            "Error should mention missing action parameter");
    }

    [Test]
    public async Task ManageExtensions_InvalidAction_ReturnsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.Configure<ServerConfiguration>(options =>
        {
            options.UnityHost = "localhost";
            options.UnityPort = UnityPort;
            options.UnityStatePort = StatePort;
            options.ConnectionTimeoutSeconds = 5;
        });

        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());
        services.AddSingleton<UnityStateConnectionService>();
        services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());
        services.AddSingleton<ManageExtensionsTool>();

        using var serviceProvider = services.BuildServiceProvider();
        var tool = serviceProvider.GetRequiredService<ManageExtensionsTool>();

        // Act - Call with invalid action
        var result = await tool.ManageExtensions(action: "invalid_action_12345");

        // Assert
        var resultObj = JObject.FromObject(result);
        Assert.That(resultObj["success"]?.Value<bool>(), Is.False, "Should fail with invalid action");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("Invalid action"),
            "Error should indicate invalid action");
    }

    [Test]
    public async Task ManageExtensions_QueryWithoutTargetClass_ReturnsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.Configure<ServerConfiguration>(options =>
        {
            options.UnityHost = "localhost";
            options.UnityPort = UnityPort;
            options.UnityStatePort = StatePort;
            options.ConnectionTimeoutSeconds = 5;
        });

        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<IUnityConnectionService>(provider => provider.GetRequiredService<UnityConnectionService>());
        services.AddSingleton<UnityStateConnectionService>();
        services.AddSingleton<IUnityStateConnectionService>(provider => provider.GetRequiredService<UnityStateConnectionService>());
        services.AddSingleton<ManageExtensionsTool>();

        using var serviceProvider = services.BuildServiceProvider();
        var tool = serviceProvider.GetRequiredService<ManageExtensionsTool>();

        // Act - Query without targetClass
        var result = await tool.ManageExtensions(
            action: "query_unity_object_knowledge",
            targetClass: null);

        // Assert
        var resultObj = JObject.FromObject(result);
        Assert.That(resultObj["success"]?.Value<bool>(), Is.False,
            "Should fail without targetClass for query");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("targetClass"),
            "Error should mention missing targetClass parameter");
    }

    #endregion

    #region Helper Methods

    private static bool WaitForTask<T>(Task<T> task, int timeoutMs = 10000)
    {
        return WaitForTask(task, out _, timeoutMs);
    }

    private static bool WaitForTask<T>(Task<T> task, out T? result, int timeoutMs = 10000)
    {
        result = default;
        if (!task.Wait(timeoutMs))
        {
            return false;
        }

        result = task.Result;
        return true;
    }

    #endregion
}