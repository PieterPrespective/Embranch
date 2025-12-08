using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using System.Threading.Tasks;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tools;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected;

/// <summary>
/// Integration tests for GetTestsTool with actual Unity connection.
/// Tests the retrieval of test information from Unity Test Runner.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class GetTestsToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings for GetTestsTool tests
    /// </summary>
    private class GetTestsToolTestSettings : TestConfiguration
    {
        public GetTestsTool? GetTestsTool;
    }

    /// <summary>
    /// Reference to the test settings
    /// </summary>
    private GetTestsToolTestSettings? getTestsToolTestSettings;

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

        services.AddSingleton<GetTestsTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        ServiceProvider _serviceProvider = services.BuildServiceProvider();

        getTestsToolTestSettings = new GetTestsToolTestSettings()
        {
            Name = "TestGetTestsToolFlow",
            SoughtHarnessName = "DummyIntegrationTest", // Reuse existing harness as per requirements
            TestAfterSetup = TestGetTestsRetrievalScenarios,
            ServiceProvider = _serviceProvider!,
            UnityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>()!,
            StateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>()!,
            ForceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>()!,
            GetTestsTool = _serviceProvider.GetRequiredService<GetTestsTool>()
        };
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();

        if (getTestsToolTestSettings != null)
        {
            getTestsToolTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests GetTestsTool with various filtering scenarios")]
    public void TestGetTestsToolFlow()
    {
        ExecuteTestSteps(RunGetTestsToolFlow());
    }

    /// <summary>
    /// Run the GetTestsTool test flow using UnityConnectedTestUtility
    /// </summary>
    private IEnumerator RunGetTestsToolFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(getTestsToolTestSettings!);
    }

    /// <summary>
    /// Test various GetTests retrieval scenarios
    /// </summary>
    private IEnumerator TestGetTestsRetrievalScenarios(TestConfiguration _config)
    {
        if (!(_config is GetTestsToolTestSettings castConfig))
        {
            Assert.Fail($"TestConfiguration is not of correct type {typeof(GetTestsToolTestSettings).Name}");
            yield break;
        }

        Assert.That(castConfig.GetTestsTool, Is.Not.Null, "GetTestsTool should be available");

        // Test 1: Retrieve by namespace part
        Console.WriteLine("[GetTestToolTests] : Test 1: Retrieve by namespace part..."); 
        yield return TestRetrievalByNamespace(castConfig.GetTestsTool!);

        // Test 2: Retrieve by test function name
        Console.WriteLine("[GetTestToolTests] : Test 2: Retrieve by test function name...");
        yield return TestRetrievalByTestName(castConfig.GetTestsTool!);

        // Test 3: Retrieve by test fixture/class name
        Console.WriteLine("[GetTestToolTests] : Test 3: Retrieve by test fixture/class name...");
        yield return TestRetrievalByTestFixture(castConfig.GetTestsTool!);

        // Test 4: Test EditMode filter
        Console.WriteLine("[GetTestToolTests] : Test 4: Test EditMode filter...");
        yield return TestEditModeFilter(castConfig.GetTestsTool!);

        // Test 5: Test PlayMode filter
        Console.WriteLine("[GetTestToolTests] : Test 5: Test PlayMode filter...");
        yield return TestPlayModeFilter(castConfig.GetTestsTool!);

        // Test 6: Test All mode (both EditMode and PlayMode)
        Console.WriteLine("[GetTestToolTests] : Test 6: Test All mode (both EditMode and PlayMode)...");
        yield return TestAllModeFilter(castConfig.GetTestsTool!);

        // Test 7: Test empty filter (should return all tests)
        Console.WriteLine("[GetTestToolTests] : Test 7: Test empty filter (should return all tests)...");
        yield return TestEmptyFilter(castConfig.GetTestsTool!);

        // Test 8: Test invalid filter (should return empty)
        Console.WriteLine("[GetTestToolTests] : Test 8: Test invalid filter (should return empty)...");
        yield return TestInvalidFilter(castConfig.GetTestsTool!);
    }

    /// <summary>
    /// Test retrieval by namespace part
    /// </summary>
    private IEnumerator TestRetrievalByNamespace(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "EditMode", Filter: "UMCP.Tests.Integration");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests by namespace timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        //Console.WriteLine($"GetTests by namespace result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests by namespace should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;

        //Console.WriteLine($">>>> Found {tests?.Count} tests in UMCP.Tests.Integration namespace:" + resultObj["message"]);

        for (int i = 0; i < tests.Count; i++)
        {
            Console.WriteLine($"Test {i}: {tests?[i]?["TestName"]} in namespace {tests?[i]?["TestNamespace"]}");
        }   


        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Should find tests in UMCP.Tests.Integration namespace");




        // Verify all returned tests are from the specified namespace
        foreach (JObject test in tests.Cast<JObject>())
        {
            var testNamespace = test["TestNamespace"]?.ToString();
            Assert.That(testNamespace, Does.Contain("UMCP.Tests.Integration"), 
                $"Test namespace should contain 'UMCP.Tests.Integration': {testNamespace}");
        }

        yield return null;
    }


    /// <summary>
    /// Test retrieval by test function name
    /// </summary>
    private IEnumerator TestRetrievalByTestName(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "EditMode", Filter: "SimpleEditModeTest");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests by test name timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests by test name result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests by test name should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Should find tests with 'SimpleEditModeTest' in name");

        // Verify at least one test contains the search term in its name
        bool foundMatch = false;
        foreach (JObject test in tests.Cast<JObject>())
        {
            var testName = test["TestName"]?.ToString();
            if (testName != null && testName.Contains("SimpleEditModeTest"))
            {
                foundMatch = true;
                break;
            }
        }
        Assert.That(foundMatch, Is.True, "Should find at least one test with 'SimpleEditModeTest' in name");

        yield return null;
    }

    /// <summary>
    /// Test retrieval by test fixture/class name
    /// </summary>
    private IEnumerator TestRetrievalByTestFixture(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "EditMode", Filter: "SimpleEditModeTests");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests by test fixture timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests by test fixture result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests by test fixture should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Should find tests from 'SimpleEditModeTests' fixture");

        // Verify tests are from the expected fixture
        bool foundMatch = false;
        foreach (JObject test in tests.Cast<JObject>())
        {
            var containerScript = test["ContainerScript"]?.ToString();
            if (containerScript != null && containerScript.Contains("SimpleEditModeTests"))
            {
                foundMatch = true;
                break;
            }
        }
        Assert.That(foundMatch, Is.True, "Should find tests from 'SimpleEditModeTests' fixture");

        yield return null;
    }

    public const string EDITMODE_IT_TEST_NAMESPACE = "UMCP.editor.integrationtests";
    public const string PLAYMODE_IT_TEST_NAMESPACE = "UMCP.Tests.Integration.Player";



    /// <summary>
    /// Test EditMode filter functionality
    /// </summary>
    private IEnumerator TestEditModeFilter(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "EditMode", Filter: null);
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests EditMode timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests EditMode result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests EditMode should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Should find EditMode tests");

        // Verify we have EditMode tests (checking for typical EditMode test namespaces)
        bool foundEditModeTest = false;
        foreach (JObject test in tests.Cast<JObject>())
        {
            var testNamespace = test["TestNamespace"]?.ToString();
            if (testNamespace != null && testNamespace.Contains(EDITMODE_IT_TEST_NAMESPACE))
            {
                foundEditModeTest = true;
                break;
            }
        }
        Assert.That(foundEditModeTest, Is.True, $"Should find EditMode tests (containing '{EDITMODE_IT_TEST_NAMESPACE}' in namespace)");

        yield return null;
    }

    /// <summary>
    /// Test PlayMode filter functionality
    /// </summary>
    private IEnumerator TestPlayModeFilter(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "PlayMode", Filter: null);
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests PlayMode timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests PlayMode result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests PlayMode should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        
        // PlayMode tests might be empty if none exist, that's okay
        if (tests!.Count > 0)
        {
            // Verify we have PlayMode tests (checking for typical PlayMode test namespaces)
            bool foundPlayModeTest = false;
            foreach (JObject test in tests.Cast<JObject>())
            {
                var testNamespace = test["TestNamespace"]?.ToString();
                if (testNamespace != null && testNamespace.Contains(PLAYMODE_IT_TEST_NAMESPACE))
                {
                    foundPlayModeTest = true;
                    break;
                }
            }
            Assert.That(foundPlayModeTest, Is.True, $"Should find PlayMode tests (containing '{PLAYMODE_IT_TEST_NAMESPACE}' in namespace)");
        }

        yield return null;
    }

    /// <summary>
    /// Test All mode (both EditMode and PlayMode)
    /// </summary>
    private IEnumerator TestAllModeFilter(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "All", Filter: null);
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests All mode timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests All mode result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests All mode should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Should find tests in All mode");

        // In All mode, we should have tests from both EditMode and PlayMode (if they exist)
        bool foundEditModeTest = false;
        bool foundPlayModeTest = false;
        
        foreach (JObject test in tests.Cast<JObject>())
        {
            var testNamespace = test["TestNamespace"]?.ToString();
            if (testNamespace != null)
            {
                if (testNamespace.Contains("UMCP.editor.integrationtests"))
                    foundEditModeTest = true;
                if (testNamespace.Contains(PLAYMODE_IT_TEST_NAMESPACE))
                    foundPlayModeTest = true;
            }
        }
        
        // At least EditMode tests should exist
        Assert.That(foundEditModeTest, Is.True, $"Should find '{EDITMODE_IT_TEST_NAMESPACE}' tests in All mode");
        Assert.That(foundPlayModeTest, Is.True, $"Should find '{PLAYMODE_IT_TEST_NAMESPACE}' tests in All mode");
        yield return null;
    }

    /// <summary>
    /// Test empty filter (should return all tests)
    /// </summary>
    private IEnumerator TestEmptyFilter(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "All", Filter: "");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests with empty filter timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests empty filter result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests with empty filter should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array");
        Assert.That(tests!.Count, Is.GreaterThan(0), "Empty filter should return all tests");

        yield return null;
    }

    /// <summary>
    /// Test invalid filter (should return empty or very few results)
    /// </summary>
    private IEnumerator TestInvalidFilter(GetTestsTool tool)
    {
        var getTestsTask = tool.GetTests(TestMode: "All", Filter: "NonExistentTestNameXYZ123");
        
        var waitForTask = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getTestsTask, waitForTask).Result == waitForTask)
        {
            Assert.Fail("GetTests with invalid filter timed out");
        }

        var result = getTestsTask.Result;
        Assert.That(result, Is.Not.Null, "GetTests should return a result");

        var resultObj = JObject.FromObject(result);
        Console.WriteLine($"GetTests invalid filter result: {resultObj.ToString()}");

        var success = resultObj["success"]?.Value<bool>() ?? false;
        Assert.That(success, Is.True, $"GetTests with invalid filter should succeed. Error: {resultObj["error"]}");

        var tests = resultObj["tests"] as JArray;
        Assert.That(tests, Is.Not.Null, "Should return tests array (even if empty)");
        Assert.That(tests!.Count, Is.EqualTo(0), "Invalid filter should return no tests");

        yield return null;
    }
}