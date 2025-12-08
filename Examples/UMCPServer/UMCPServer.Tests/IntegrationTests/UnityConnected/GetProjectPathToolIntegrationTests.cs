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
/// Real integration tests for GetProjectPath tool with actual Unity connection.
/// Tests the GetProjectPath functionality through the complete integration infrastructure.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class GetProjectPathToolIntegrationTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings for GetProjectPath testing
    /// </summary>
    private class GetProjectPathTestSettings : UnityConnectedTestUtility.TestConfiguration
    {
        public GetProjectPathTool? GetProjectPathTool;
        public ReadConsoleTool? ReadConsoleTool;
    }

    /// <summary>
    /// Reference to the test settings shared in setup and run
    /// </summary>
    private GetProjectPathTestSettings? getProjectPathTestSettings;

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

        services.AddSingleton<GetProjectPathTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        ServiceProvider _serviceProvider = services.BuildServiceProvider();

        getProjectPathTestSettings = new GetProjectPathTestSettings()
        {
            Name = "TestGetProjectPathIntegrationFlow",
            SoughtHarnessName = "GetProjectPathIntegrationTest",
            TestAfterSetup = VerifyGetProjectPathFunctionality,
            ServiceProvider = _serviceProvider!,
            UnityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>()!,
            StateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>()!,
            ForceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>()!,
            GetProjectPathTool = _serviceProvider.GetRequiredService<GetProjectPathTool>(),
            ReadConsoleTool = _serviceProvider.GetRequiredService<ReadConsoleTool>()
        };
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();

        if (getProjectPathTestSettings != null)
        {
            getProjectPathTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests the complete GetProjectPath integration flow with harness setup")]
    public void TestGetProjectPathIntegrationFlow()
    {
        ExecuteTestSteps(RunGetProjectPathIntegrationFlow());
    }

    /// <summary>
    /// Use the UnityConnectedTestUtility to run the complete integration test flow
    /// </summary>
    /// <returns></returns>
    private IEnumerator RunGetProjectPathIntegrationFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(getProjectPathTestSettings!);
    }

    /// <summary>
    /// Verification of the GetProjectPath integration test setup and functionality
    /// </summary>
    /// <param name="_config">reference to the test configuration</param>
    /// <returns></returns>
    private IEnumerator VerifyGetProjectPathFunctionality(TestConfiguration _config)
    {
        if (!(_config is GetProjectPathTestSettings castConfig))
        {
            Assert.Fail($"Test configuration is not of correct type {typeof(GetProjectPathTestSettings).Name}");
            yield break;
        }

        Assert.That(castConfig.GetProjectPathTool, Is.Not.Null, "GetProjectPath tool should be available");
        Assert.That(castConfig.ReadConsoleTool, Is.Not.Null, "Read console tool should be available");

        // First, verify that the integration harness setup log message is present
        var readConsoleCommand = castConfig.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: "GetProjectPathIntegrationTest setup completed successfully",
            count: 10
        );

        if (readConsoleCommand == null)
        {
            Assert.Fail("Failed to initiate read console command");
            yield break;
        }

        var waitForRead = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(readConsoleCommand!, waitForRead).Result == waitForRead)
        {
            Assert.Fail("Read console command timed out");
        }
        var readResult = readConsoleCommand!.Result;

        Assert.That(readResult, Is.Not.Null, "ReadConsole should return a result");

        var readResultObj = JObject.FromObject(readResult);
        var readSuccess = readResultObj["success"]?.Value<bool>() ?? false;
        Assert.That(readSuccess, Is.True, $"ReadConsole should succeed. Error: {readResultObj["error"]}");

        var logEntries = readResultObj["entries"] as JArray;
        Assert.That(logEntries, Is.Not.Null, "Should return log entries array");
        Assert.That(logEntries!.Count, Is.GreaterThan(0), "Should find the expected log message");

        // Verify we found the expected setup message
        bool foundExpectedMessage = false;
        foreach (JObject entry in logEntries.Cast<JObject>())
        {
            var message = entry["message"]?.ToString();
            if (message != null && message.Contains("GetProjectPathIntegrationTest setup completed successfully"))
            {
                foundExpectedMessage = true;
                Console.WriteLine($"Found expected harness setup log message: {message}");
                break;
            }
        }

        Assert.That(foundExpectedMessage, Is.True, "Should find the expected harness setup completion message");

        // Now test the actual GetProjectPath functionality
        var getProjectPathCommand = castConfig.GetProjectPathTool!.GetProjectPath();
        if (getProjectPathCommand == null)
        {
            Assert.Fail("Failed to initiate GetProjectPath command");
            yield break;
        }

        var waitForProjectPath = Task.Delay(TimeSpan.FromSeconds(10));
        if (Task.WhenAny(getProjectPathCommand!, waitForProjectPath).Result == waitForProjectPath)
        {
            Assert.Fail("GetProjectPath command timed out");
        }


        var projectPathResult = getProjectPathCommand!.Result;

        Console.WriteLine($"GetProjectPath raw result: {JObject.FromObject(projectPathResult)}");

        Assert.That(projectPathResult, Is.Not.Null, "GetProjectPath should return a result");

        var projectPathResultObj = JObject.FromObject(projectPathResult);
        var projectPathSuccess = projectPathResultObj["success"]?.Value<bool>() ?? false;
        Assert.That(projectPathSuccess, Is.True, $"GetProjectPath should succeed. Error: {projectPathResultObj["error"]}");

        // Validate the returned data structure
        var data = projectPathResultObj;// projectPathResult as dynamic;
        Assert.That(data, Is.Not.Null, "Should return project path data");

        // Verify all expected fields are present and not empty
        var requiredFields = new[] { "projectPath", "dataPath", "persistentDataPath", "streamingAssetsPath", "temporaryCachePath" };
        
        foreach (var field in requiredFields)
        {
            Assert.That(data![field], Is.Not.Null, $"Should return {field}");
            Assert.That(data[field]?.ToString(), Is.Not.Null.And.Not.Empty, $"{field} should not be empty");
            Console.WriteLine($"Verified {field}: {data[field]}");
        }

        Console.WriteLine("GetProjectPath integration test verification completed successfully");
        yield return null;
    }
}