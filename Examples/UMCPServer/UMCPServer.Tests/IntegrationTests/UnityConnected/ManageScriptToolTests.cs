using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Collections;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tools;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected;

/// <summary>
/// Integration tests for ManageScriptTool with actual Unity connection.
/// Tests symbol definition finding and class decompilation functionality.
/// This test requires Unity to be running with UMCP Client active.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageScriptToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom test settings for ManageScriptTool tests
    /// </summary>
    private class ManageScriptToolTestSettings : TestConfiguration
    {
        public ManageScriptTool? ManageScriptTool { get; set; }
        public ReadConsoleTool? ReadConsoleTool { get; set; }
    }

    private ManageScriptToolTestSettings? testSettings;
    
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
        
        services.AddSingleton<ManageScriptTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        var serviceProvider = services.BuildServiceProvider();

        testSettings = new ManageScriptToolTestSettings()
        {
            Name = "TestManageScriptTool",
            SoughtHarnessName = "DummyIntegrationTest", // No specific harness needed for script tools
            TestAfterSetup = ValidateManageScriptTests,
            ServiceProvider = serviceProvider,
            UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
            StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
            ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
            ManageScriptTool = serviceProvider.GetRequiredService<ManageScriptTool>(),
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
    [Timeout(120000)] // 2 minutes timeout
    public void ManageScript_IntegrationTests_RunSuccessfully()
    {
        ExecuteTestSteps(RunIntegrationTestFlow());
    }

    /// <summary>
    /// Runs the Unity connected integration test flow
    /// </summary>
    private IEnumerator RunIntegrationTestFlow()
    {
        yield return RunUnityConnectedIntegrationTest(testSettings!);
    }
    
    public const string ValidationPrefix = "[ManageScriptValidation]";
    public const string ValidationSuccess = "VALIDATION_SUCCESS";
    public const string ValidationFailure = "VALIDATION_FAILURE";

    private IEnumerator ValidateManageScriptTests(TestConfiguration configuration)
    {
        var settings = configuration as ManageScriptToolTestSettings;
        Assert.That(settings, Is.Not.Null, "Settings should be ManageScriptToolTestSettings");
        Assert.That(settings!.ManageScriptTool, Is.Not.Null, "ManageScriptTool should be available");
        Assert.That(settings.ReadConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");
        
        yield return new WaitForSeconds(0.5f);
        
        // Test 1: Find Symbol Definition for a Unity class
        yield return TestFindSymbolDefinition_UnityClass(settings);
        
        // Test 2: Find Symbol Definition for a project class (if available)
        yield return TestFindSymbolDefinition_ProjectClass(settings);
        
        // Test 3: Find Symbol Definition with multiple results
        yield return TestFindSymbolDefinition_MultipleResults(settings);
        
        // Test 4: Find Symbol Definition for non-existent symbol
        yield return TestFindSymbolDefinition_NonExistent(settings);
        
        // Test 5: Decompile a Unity class
        yield return TestDecompileClass_UnityClass(settings);
        
        // Test 6: Test invalid action handling
        yield return TestInvalidAction(settings);
    }

    #region FindSymbolDefinition Tests





    private IEnumerator TestFindSymbolDefinition_UnityClass(ManageScriptToolTestSettings settings)
    {
        LogMessage($">>>>> {ValidationPrefix} Testing FindSymbolDefinition for Unity class 'GameObject'");
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "find_symbol_definition",
            symbol: "GameObject",
            noOfResults: 1,
            timeoutSeconds: 40
        );
        
        yield return WaitForTask(resultTask, "FindSymbolDefinition_GameObject", 45);
        
        var result = resultTask.Result;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {ValidationPrefix} Raw result: {result?.ToString() ?? "NULL"}");

        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.True, "FindSymbolDefinition should succeed for GameObject");
        Assert.That(dynamicResult.symbolResults, Is.Not.Null, "Should have symbol results");
        
        LogMessage($"<<<<<{ValidationPrefix} FindSymbolDefinition Unity class test: {ValidationSuccess}");
    }

    private IEnumerator TestFindSymbolDefinition_ProjectClass(ManageScriptToolTestSettings settings)
    {
        LogMessage($">>>>>{ValidationPrefix} Testing FindSymbolDefinition for project class 'ManageIntegrationTests'");
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "find_symbol_definition",
            symbol: "ManageIntegrationTests",
            noOfResults: 1,
            timeoutSeconds: 40
        );
        
        yield return WaitForTask(resultTask, "FindSymbolDefinition_ManageIntegrationTests", 45);
        
        var result = resultTask.Result;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {ValidationPrefix} Raw result: {result?.ToString() ?? "NULL"}");

        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.True, "FindSymbolDefinition should succeed");
        
        // This class may or may not exist in the project
        if (dynamicResult.symbolResults != null)
        {
            LogMessage($"{ValidationPrefix} Found project class 'ManageIntegrationTests'");
        }
        else
        {
            LogMessage($"{ValidationPrefix} Project class 'ManageIntegrationTests' not found (expected if not in project)");
        }
        
        LogMessage($"<<<<<{ValidationPrefix} FindSymbolDefinition project class test: {ValidationSuccess}");
    }

    private IEnumerator TestFindSymbolDefinition_MultipleResults(ManageScriptToolTestSettings settings)
    {
        LogMessage($"{ValidationPrefix} Testing FindSymbolDefinition with multiple results for 'Debug'");
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "find_symbol_definition",
            symbol: "Debug",
            noOfResults: 3,
            timeoutSeconds: 35
        );
        
        yield return WaitForTask(resultTask, "FindSymbolDefinition_Debug_MultipleResults", 40);
        
        var result = resultTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.True, "FindSymbolDefinition should succeed for Debug");
        Assert.That(dynamicResult.symbolResults, Is.Not.Null, "Should have symbol results");
        
        LogMessage($"{ValidationPrefix} FindSymbolDefinition multiple results test: {ValidationSuccess}");
    }

    private IEnumerator TestFindSymbolDefinition_NonExistent(ManageScriptToolTestSettings settings)
    {
        string nonExistentSymbol = $"NonExistentClass_{Guid.NewGuid():N}";
        LogMessage($"{ValidationPrefix} Testing FindSymbolDefinition for non-existent symbol '{nonExistentSymbol}'");
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "find_symbol_definition",
            symbol: nonExistentSymbol,
            noOfResults: 1,
            timeoutSeconds: 20
        );
        
        yield return WaitForTask(resultTask, "FindSymbolDefinition_NonExistent", 25);
        
        var result = resultTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.True, "FindSymbolDefinition should complete even for non-existent symbols");
        
        // The message should indicate no results found
        string message = dynamicResult.message?.ToString() ?? "";
        Assert.That(message.Contains("0"), Is.True, "Message should indicate 0 results found");
        
        LogMessage($"{ValidationPrefix} FindSymbolDefinition non-existent symbol test: {ValidationSuccess}");
    }

    #endregion

    #region DecompileClass Tests

    private IEnumerator TestDecompileClass_UnityClass(ManageScriptToolTestSettings settings)
    {
        LogMessage($"{ValidationPrefix} Testing DecompileClass for UnityEngine.GameObject");
        
        string testOutputFile = $"TestDecompile_{Guid.NewGuid():N}.cs";
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "decompile_class",
            libraryPath: "C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll",
            libraryClass: "UnityEngine.GameObject",
            //outputFileName: testOutputFile,
            overrideExistingFile: true
        );
        
        yield return WaitForTask(resultTask, "DecompileClass_GameObject", 45);
        
        var result = resultTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");


        Console.WriteLine("Decompile result: " + result.ToString());

        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.True, "DecompileClass should succeed for GameObject");
        
        if (dynamicResult.success)
        {
            Assert.That(dynamicResult.filePath, Is.Not.Null, "Should have file path in result");
            LogMessage($"{ValidationPrefix} Successfully decompiled to: {dynamicResult.filePath}");
            
            // Clean up the test file if it was created
            try
            {
                if (File.Exists(testOutputFile))
                    File.Delete(testOutputFile);
            }
            catch (Exception ex)
            {
                LogMessage($"{ValidationPrefix} Warning: Could not delete test file: {ex.Message}");
            }
        }
        
        LogMessage($"{ValidationPrefix} DecompileClass Unity class test: {ValidationSuccess}");
    }

    #endregion

    #region Error Handling Tests

    private IEnumerator TestInvalidAction(ManageScriptToolTestSettings settings)
    {
        LogMessage($"{ValidationPrefix} Testing invalid action handling");
        
        var resultTask = settings.ManageScriptTool!.ManageScript(
            action: "invalid_action",
            symbol: "Test"
        );
        
        yield return WaitForTask(resultTask, "InvalidAction_Test", 10);
        
        var result = resultTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        dynamic dynamicResult = result;
        Assert.That(dynamicResult.success, Is.False, "Invalid action should fail");
        Assert.That(dynamicResult.error, Is.Not.Null, "Should have error message");
        
        string error = dynamicResult.error.ToString();
        Assert.That(error.Contains("Invalid action"), Is.True, "Error should mention invalid action");
        
        LogMessage($"{ValidationPrefix} Invalid action test: {ValidationSuccess}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Helper method to wait for task completion with timeout
    /// </summary>
    private IEnumerator WaitForTask<T>(Task<T> task, string operationName, int timeoutSeconds = 10)
    {
        var startTime = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        
        while (!task.IsCompleted)
        {
            if (DateTime.Now - startTime > timeout)
            {
                Assert.Fail($"{operationName} operation timed out after {timeoutSeconds} seconds");
                yield break;
            }
            
            yield return null; // Wait one frame
        }

        if (task.IsFaulted)
        {
            var baseException = task.Exception?.GetBaseException();
            Assert.Fail($"{operationName} operation failed: {baseException?.Message}\n{baseException?.StackTrace}");
        }
    }

    private void LogMessage(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    #endregion
}