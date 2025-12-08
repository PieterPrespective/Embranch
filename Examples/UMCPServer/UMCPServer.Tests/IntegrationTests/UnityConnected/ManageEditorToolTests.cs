using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Threading.Tasks;
using UMCPServer.Models;
using UMCPServer.Services;
using UMCPServer.Tests.IntegrationTests.Tools;
using UMCPServer.Tools;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected;

/// <summary>
/// Integration tests for ManageEditorTool with actual Unity connection.
/// Tests editor operations including play mode control, tags/layers management, and state queries.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageEditorToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom test settings for ManageEditorTool tests
    /// </summary>
    private class ManageEditorToolTestSettings : TestConfiguration
    {
        public ManageEditorTool? ManageEditorTool { get; set; }
        public ExecuteMenuItemTool? ExecuteMenuItemTool { get; set; }
        public ReadConsoleTool? ReadConsoleTool { get; set; }
    }

    private ManageEditorToolTestSettings? testSettings;
    
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
        
        services.AddSingleton<ManageEditorTool>();
        services.AddSingleton<ExecuteMenuItemTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        var serviceProvider = services.BuildServiceProvider();

        testSettings = new ManageEditorToolTestSettings()
        {
            Name = "TestManageEditorTool",
            SoughtHarnessName = "ManageEditorIntegrationTest",
            TestAfterSetup = ValidateManageEditorTests,
            ServiceProvider = serviceProvider,
            UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
            StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
            ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
            ManageEditorTool = serviceProvider.GetRequiredService<ManageEditorTool>(),
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
    [Timeout(120000)] // 2 minutes timeout
    public void ManageEditor_IntegrationTests_RunSuccessfully()
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

    /// <summary>
    /// Validates various ManageEditor operations after the harness is set up
    /// </summary>
    private static IEnumerator ValidateManageEditorTests(TestConfiguration settings)
    {
        var config = settings as ManageEditorToolTestSettings;
        Assert.That(config, Is.Not.Null, "Settings should be ManageEditorToolTestSettings");
        
        var manageEditorTool = config!.ManageEditorTool;
        var executeMenuItemTool = config.ExecuteMenuItemTool;
        var readConsoleTool = config.ReadConsoleTool;
        
        Assert.That(manageEditorTool, Is.Not.Null, "ManageEditorTool should be available");
        Assert.That(executeMenuItemTool, Is.Not.Null, "ExecuteMenuItemTool should be available");
        Assert.That(readConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");

        // Test 1: Get Editor State
        //yield return TestGetEditorState(manageEditorTool!);
        
        // Test 2: Get and validate tags
        Console.WriteLine("[ManageEditorToolTests] Starting ManageEditorTool tests...");
        yield return TestGetTags(manageEditorTool!);
        
        // Test 3: Add a new tag
        Console.WriteLine("[ManageEditorToolTests] Testing AddTag...");
        yield return TestAddTag(manageEditorTool!, executeMenuItemTool!, readConsoleTool!);
        
        // Test 4: Remove the tag
        Console.WriteLine("[ManageEditorToolTests] Testing RemoveTag...");
        yield return TestRemoveTag(manageEditorTool!, executeMenuItemTool!, readConsoleTool!);
        
        // Test 5: Get layers
        Console.WriteLine("[ManageEditorToolTests] Testing GetLayers...");
        yield return TestGetLayers(manageEditorTool!);
        
        // Test 6: Get selection
        Console.WriteLine("[ManageEditorToolTests] Testing GetSelection...");
        yield return TestGetSelection(manageEditorTool!);
        
        // Test 7: Get active tool
        Console.WriteLine("[ManageEditorToolTests] Testing GetActiveTool...");
        yield return TestGetActiveTool(manageEditorTool!);
        
        // Test 8: Set active tool
        Console.WriteLine("[ManageEditorToolTests] Testing SetActiveTool...");
        yield return TestSetActiveTool(manageEditorTool!, executeMenuItemTool!, readConsoleTool!);
        
        // Test 9: Get windows
        Console.WriteLine("[ManageEditorToolTests] Testing GetWindows...");
        yield return TestGetWindows(manageEditorTool!);
        
        // Test 10: Test invalid action
        Console.WriteLine("[ManageEditorToolTests] Testing InvalidAction...");
        yield return TestInvalidAction(manageEditorTool!);
        
        // Test 11: Test missing required parameters
        Console.WriteLine("[ManageEditorToolTests] Testing MissingRequiredParameters...");
        yield return TestMissingRequiredParameters(manageEditorTool!);
        
    }

    #region Individual Test Methods

    //private static IEnumerator TestGetEditorState(ManageEditorTool tool)
    //{
    //    Console.WriteLine("Testing GetEditorState...");
        
    //    var getStateTask = tool.ManageEditor("get_state");
    //    yield return WaitForTask(getStateTask, 10000);
        
    //    Assert.That(getStateTask.IsCompletedSuccessfully, Is.True, "GetState task should complete successfully");
        
    //    var result = getStateTask.Result;
    //    var resultObj = JObject.FromObject(result);
        
    //    Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetState should succeed");
    //    Assert.That(resultObj["editorState"], Is.Not.Null, "Should return editor state");
        
    //    Console.WriteLine($"Editor state result: {resultObj}");
    //}

    private static IEnumerator TestGetTags(ManageEditorTool tool)
    {
        Console.WriteLine("Testing GetTags...");
        
        var getTagsTask = tool.ManageEditor("get_tags");
        yield return WaitForTask(getTagsTask, 10000);
        
        Assert.That(getTagsTask.IsCompletedSuccessfully, Is.True, "GetTags task should complete successfully");
        
        var result = getTagsTask.Result;

       

        var resultObj = JObject.FromObject(result);
        //Console.WriteLine($"GetTags result: {resultObj.ToString()}");

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetTags should succeed");
        Assert.That(resultObj["tags"], Is.Not.Null, "Should return tags list");
        
        var tags = resultObj["tags"] as JArray;
        Assert.That(tags, Is.Not.Null, "Tags should be an array");
        Assert.That(tags!.Count, Is.GreaterThan(0), "Should have at least one tag");
        
        // Check if test tag from harness exists
        bool hasTestTag = tags.Any(t => t.ToString() == "ManageEditorTestTag");
        Assert.That(hasTestTag, Is.True, "Test tag from harness should exist");
        
        Console.WriteLine($"Found {tags.Count} tags");
    }

    private static IEnumerator TestAddTag(ManageEditorTool tool, ExecuteMenuItemTool executeMenuItem, ReadConsoleTool readConsole)
    {
        Console.WriteLine("Testing AddTag...");
        
        // First create an additional tag via menu item
        var createTagTask = executeMenuItem.ExecuteMenuItem(menuPath:"UMCP/Tests/ManageEditor/Create Additional Test Tag");
        yield return WaitForTask(createTagTask, 10000);
        Assert.That(createTagTask.IsCompletedSuccessfully, Is.True, "Create tag menu item should execute");
        
        // Verify tag was created via ManageEditor
        var getTagsTask = tool.ManageEditor("get_tags");
        yield return WaitForTask(getTagsTask, 10000);
        
        var result = getTagsTask.Result;
        var resultObj = JObject.FromObject(result);
        var tags = resultObj["tags"] as JArray;


        
        bool hasAdditionalTag = tags?.Any(t => t.ToString() == "ManageEditorTestTag2") ?? false;
        Assert.That(hasAdditionalTag, Is.True, "Additional test tag should exist after creation");
        
        Console.WriteLine("Additional tag created successfully");
    }

    private static IEnumerator TestRemoveTag(ManageEditorTool tool, ExecuteMenuItemTool executeMenuItem, ReadConsoleTool readConsole)
    {
        Console.WriteLine("Testing RemoveTag...");
        
        // Remove the additional tag
        var removeTagTask = tool.ManageEditor("remove_tag", tagName: "ManageEditorTestTag2");
        yield return WaitForTask(removeTagTask, 10000);
        
        Assert.That(removeTagTask.IsCompletedSuccessfully, Is.True, "RemoveTag task should complete successfully");
        
        var result = removeTagTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "RemoveTag should succeed");
        
        // Verify tag was removed
        var getTagsTask = tool.ManageEditor("get_tags");
        yield return WaitForTask(getTagsTask, 10000);
        
        result = getTagsTask.Result;
        resultObj = JObject.FromObject(result);
        var tags = resultObj["tags"] as JArray;
        
        bool hasAdditionalTag = tags?.Any(t => t.ToString() == "ManageEditorTestTag2") ?? false;
        Assert.That(hasAdditionalTag, Is.False, "Additional test tag should be removed");
        
        Console.WriteLine("Tag removed successfully");
    }

    private static IEnumerator TestGetLayers(ManageEditorTool tool)
    {
        Console.WriteLine("Testing GetLayers...");
        
        var getLayersTask = tool.ManageEditor("get_layers");
        yield return WaitForTask(getLayersTask, 10000);
        
        Assert.That(getLayersTask.IsCompletedSuccessfully, Is.True, "GetLayers task should complete successfully");
        
        var result = getLayersTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetLayers should succeed");
        Assert.That(resultObj["layers"], Is.Not.Null, "Should return layers");
        
        Console.WriteLine($"Layers result: {resultObj["layers"]}");
    }

    private static IEnumerator TestGetSelection(ManageEditorTool tool)
    {
        Console.WriteLine("Testing GetSelection...");
        
        var getSelectionTask = tool.ManageEditor("get_selection");
        yield return WaitForTask(getSelectionTask, 10000);
        
        Assert.That(getSelectionTask.IsCompletedSuccessfully, Is.True, "GetSelection task should complete successfully");
        
        var result = getSelectionTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetSelection should succeed");
        Assert.That(resultObj["selection"], Is.Not.Null, "Should return selection info");
        
        Console.WriteLine($"Selection result: {resultObj["selection"]}");
    }

    private static IEnumerator TestGetActiveTool(ManageEditorTool tool)
    {
        Console.WriteLine("Testing GetActiveTool...");
        
        var getActiveToolTask = tool.ManageEditor("get_active_tool");
        yield return WaitForTask(getActiveToolTask, 10000);
        
        Assert.That(getActiveToolTask.IsCompletedSuccessfully, Is.True, "GetActiveTool task should complete successfully");
        
        var result = getActiveToolTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetActiveTool should succeed");
        Assert.That(resultObj["toolInfo"], Is.Not.Null, "Should return tool info");
        
        Console.WriteLine($"Active tool: {resultObj["toolInfo"]}");
    }

    private static IEnumerator TestSetActiveTool(ManageEditorTool tool, ExecuteMenuItemTool executeMenuItem, ReadConsoleTool readConsole)
    {
        Console.WriteLine("Testing SetActiveTool...");
        
        // Set tool to Rotate
        var setToolTask = tool.ManageEditor("set_active_tool", toolName: "Rotate");
        yield return WaitForTask(setToolTask, 10000);
        
        Assert.That(setToolTask.IsCompletedSuccessfully, Is.True, "SetActiveTool task should complete successfully");
        
        var result = setToolTask.Result;
        var resultObj = JObject.FromObject(result);

        Console.WriteLine($"SetActiveTool result: {resultObj.ToString()}");


        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "SetActiveTool should succeed");
        
        // Validate tool was changed via menu item
        var validateTask = executeMenuItem.ExecuteMenuItem(menuPath: "UMCP/Tests/ManageEditor/Validate Tool Changed");
        yield return WaitForTask(validateTask, 10000);
        yield return new WaitForSeconds(10); // Wait a bit for console log to be written

        // Check console for validation result
        var consoleTask = readConsole.ReadConsole(action: "get", types: new[] { "log", "error" }, count: 10);
        yield return WaitForTask(consoleTask, 10000);
        yield return new WaitForSeconds(2); // Wait a bit for console log to be written

        result = consoleTask.Result;
        resultObj = JObject.FromObject(result);
        var entries = resultObj["entries"] as JArray;
        
        bool foundValidation = entries?.Any(e => 
            e["message"]?.ToString().Contains("VALIDATION_SUCCESS: Tool changed to Rotate") == true) ?? false;
        
        Assert.That(foundValidation, Is.True, "Tool change should be validated successfully");
        
        Console.WriteLine("Tool changed successfully");
    }

    private static IEnumerator TestGetWindows(ManageEditorTool tool)
    {
        Console.WriteLine("Testing GetWindows...");
        
        var getWindowsTask = tool.ManageEditor("get_windows");
        yield return WaitForTask(getWindowsTask, 10000);
        
        Assert.That(getWindowsTask.IsCompletedSuccessfully, Is.True, "GetWindows task should complete successfully");
        
        var result = getWindowsTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "GetWindows should succeed");
        Assert.That(resultObj["windows"], Is.Not.Null, "Should return windows list");
        
        var windows = resultObj["windows"] as JArray;
        Assert.That(windows, Is.Not.Null, "Windows should be an array");
        
        Console.WriteLine($"Found {windows?.Count ?? 0} window(s)");
    }

    private static IEnumerator TestInvalidAction(ManageEditorTool tool)
    {
        Console.WriteLine("Testing invalid action...");
        
        var invalidActionTask = tool.ManageEditor("invalid_action");
        yield return WaitForTask(invalidActionTask, 10000);
        
        Assert.That(invalidActionTask.IsCompletedSuccessfully, Is.True, "Task should complete even with invalid action");
        
        var result = invalidActionTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? true, Is.False, "Invalid action should return failure");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("Invalid action"), "Should return appropriate error message");
        
        Console.WriteLine("Invalid action handled correctly");
    }

    private static IEnumerator TestMissingRequiredParameters(ManageEditorTool tool)
    {
        Console.WriteLine("Testing missing required parameters...");
        
        // Try to add tag without providing tagName
        var missingParamTask = tool.ManageEditor("add_tag");
        yield return WaitForTask(missingParamTask, 10000);
        
        Assert.That(missingParamTask.IsCompletedSuccessfully, Is.True, "Task should complete even with missing parameters");
        
        var result = missingParamTask.Result;
        var resultObj = JObject.FromObject(result);
        
        Assert.That(resultObj["success"]?.Value<bool>() ?? true, Is.False, "Missing required parameter should return failure");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("tagName"), "Should mention the missing parameter");
        
        Console.WriteLine("Missing parameter handled correctly");
    }

    #endregion

    #region Helper Methods

    private static IEnumerator WaitForTask(Task task, int timeoutMs)
    {
        var startTime = DateTime.Now;
        while (!task.IsCompleted && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
        {
            yield return null;
        }
        
        if (!task.IsCompleted)
        {
            throw new TimeoutException($"Task did not complete within {timeoutMs}ms");
        }
    }

    #endregion
}