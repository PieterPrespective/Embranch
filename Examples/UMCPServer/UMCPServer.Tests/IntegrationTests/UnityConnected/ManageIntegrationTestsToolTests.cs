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
/// Real integration tests for ManageIntegrationTests tool with actual Unity connection.
/// Tests the complete integration test harness lifecycle with polling architecture.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageIntegrationTestsToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings including the ReadconsoleTool
    /// </summary>
    private class ManageIntegrationTestsToolTestSettings : UnityConnectedTestUtility.TestConfiguration
    {
        public ReadConsoleTool? ReadConsoleTool;
    }

    /// <summary>
    /// Reference to the test settings shared in setup and run
    /// </summary>
    private ManageIntegrationTestsToolTestSettings? manageIntegrationTestsToolTestSettings;

    
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

        manageIntegrationTestsToolTestSettings = new ManageIntegrationTestsToolTestSettings()
        {
            Name = "TestDummyIntegrationTestFlow",
            SoughtHarnessName = "DummyIntegrationTest",
            TestAfterSetup = VerifyIntegrationTestLogsNew,
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

        if(manageIntegrationTestsToolTestSettings!=null)
        {
            manageIntegrationTestsToolTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests the complete DummyIntegrationTest flow with polling architecture")]
    public void TestDummyIntegrationTestFlow()
    {
        ExecuteTestSteps(RunDummyIntegrationTestFlowNew());
    }

    /// <summary>
    /// Use the UnityConnectedTestUtility to run the actual test flow (test unity connection, test unity state, test harnas exists, optionally clean, setup harnas, run, clean)
    /// </summary>
    /// <returns></returns>
    private IEnumerator RunDummyIntegrationTestFlowNew()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(manageIntegrationTestsToolTestSettings!);
    }

    /// <summary>
    /// Actual verification of the integration test setup at the unity client side - for now we just check the logs
    /// </summary>
    /// <param name="_config">reference to the test configuration</param>
    /// <returns></returns>
    private IEnumerator VerifyIntegrationTestLogsNew(TestConfiguration _config)
    {

        if (!(_config is ManageIntegrationTestsToolTestSettings castConfig))
        {
            Assert.Fail($"Testconfiguration is not of correct type {typeof(ManageIntegrationTestsToolTestSettings).Name}");
            yield break;
        }
        
            Assert.That(castConfig.ReadConsoleTool, Is.Not.Null, "Read console tool should be available");

            // Look for the expected log message from DummyIntegrationTest
            var readConsoleCommand = castConfig.ReadConsoleTool!.ReadConsole(
                action: "get",
                filterText: "DummyIntegrationTest setup completed successfully",
                count: 10
            );

            if (readConsoleCommand == null)
            {
                Assert.Fail("Failed to initiate read console command");
            }

            var waitForRead = Task.Delay(TimeSpan.FromSeconds(10));
            if (Task.WhenAny(readConsoleCommand!, waitForRead).Result == waitForRead)
            {
                Assert.Fail("Read console command timed out");
            }
            var result = readConsoleCommand!.Result;

            Assert.That(result, Is.Not.Null, "ReadConsole should return a result");

            var resultObj = JObject.FromObject(result);

            var success = resultObj["success"]?.Value<bool>() ?? false;
            Assert.That(success, Is.True, $"ReadConsole should succeed. Error: {resultObj["error"]}");

            //Console.WriteLine("ReadConsole result: " + resultObj.ToString());

            var logEntries = resultObj["entries"] as JArray;
            Assert.That(logEntries, Is.Not.Null, "Should return log entries array");
            Assert.That(logEntries!.Count, Is.GreaterThan(0), "Should find the expected log message");

            // Verify we found the expected message
            bool foundExpectedMessage = false;
            foreach (JObject entry in logEntries.Cast<JObject>())
            {
                var message = entry["message"]?.ToString();
                if (message != null && message.Contains("DummyIntegrationTest setup completed successfully"))
                {
                    foundExpectedMessage = true;
                    Console.WriteLine($"Found expected log message: {message}");
                    break;
                }
            }

            Assert.That(foundExpectedMessage, Is.True, "Should find the expected setup completion message");
            yield return null;
    }
}