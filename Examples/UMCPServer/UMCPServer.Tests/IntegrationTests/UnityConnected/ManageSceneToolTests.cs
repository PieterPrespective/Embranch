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
/// Integration tests for ManageSceneTool with actual Unity connection.
/// Tests all scene management operations including create, load, save, get_hierarchy, get_active, and get_build_settings.
/// This test requires Unity to be running with UMCP Client.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageSceneToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom tool settings for ManageSceneTool testing
    /// </summary>
    private class ManageSceneToolTestSettings : UnityConnectedTestUtility.TestConfiguration
    {
        public ManageSceneTool? ManageSceneTool;
        public ExecuteMenuItemTool? ExecuteMenuItemTool;
        public ReadConsoleTool? ReadConsoleTool;
    }

    /// <summary>
    /// Reference to the test settings shared in setup and run
    /// </summary>
    private ManageSceneToolTestSettings? manageSceneToolTestSettings;

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

        services.AddSingleton<ManageSceneTool>();
        services.AddSingleton<ExecuteMenuItemTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        ServiceProvider _serviceProvider = services.BuildServiceProvider();

        manageSceneToolTestSettings = new ManageSceneToolTestSettings()
        {
            Name = "TestManageSceneToolFlow",
            SoughtHarnessName = "ManageSceneIntegrationTest",
            TestAfterSetup = ValidateManageSceneTests,
            ServiceProvider = _serviceProvider!,
            UnityConnection = _serviceProvider.GetRequiredService<UnityConnectionService>()!,
            StateConnection = _serviceProvider.GetRequiredService<UnityStateConnectionService>()!,
            ForceUpdateTool = _serviceProvider.GetRequiredService<ForceUpdateEditorTool>()!,
            ManageSceneTool = _serviceProvider.GetRequiredService<ManageSceneTool>(),
            ExecuteMenuItemTool = _serviceProvider.GetRequiredService<ExecuteMenuItemTool>(),
            ReadConsoleTool = _serviceProvider.GetRequiredService<ReadConsoleTool>(),
        };
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();

        if (manageSceneToolTestSettings != null)
        {
            manageSceneToolTestSettings?.ServiceProvider?.Dispose();
        }
    }

    [Test]
    [Description("Tests the complete ManageSceneTool functionality with actual Unity connection")]
    public void TestManageSceneToolFlow()
    {
        ExecuteTestSteps(RunManageSceneToolFlow());
    }

    /// <summary>
    /// Use the UnityConnectedTestUtility to run the actual test flow
    /// </summary>
    /// <returns></returns>
    private IEnumerator RunManageSceneToolFlow()
    {
        yield return UnityConnectedTestUtility.RunUnityConnectedIntegrationTest(manageSceneToolTestSettings!);
    }

    /// <summary>
    /// Comprehensive validation of ManageSceneTool functionality
    /// </summary>
    /// <param name="_config">reference to the test configuration</param>
    /// <returns></returns>
    private IEnumerator ValidateManageSceneTests(TestConfiguration _config)
    {
        if (!(_config is ManageSceneToolTestSettings castConfig))
        {
            Assert.Fail($"Test configuration is not of correct type {typeof(ManageSceneToolTestSettings).Name}");
            yield break;
        }

        Assert.That(castConfig.ManageSceneTool, Is.Not.Null, "ManageSceneTool should be available");
        Assert.That(castConfig.ExecuteMenuItemTool, Is.Not.Null, "ExecuteMenuItemTool should be available");
        Assert.That(castConfig.ReadConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");
        
        // Test 1: Get Active Scene Info
        yield return TestGetActiveSceneInfo(castConfig);

        // Test 2: Get Scene Hierarchy
        yield return TestGetSceneHierarchy(castConfig);

        // Test 3: Get Build Settings
        yield return TestGetBuildSettings(castConfig);

        // Test 4: Create Scene
        yield return TestCreateScene(castConfig);
        
        // Test 5: Load Scene by Path
        yield return TestLoadSceneByPath(castConfig);
        
        // Test 6: Save Scene
        yield return TestSaveScene(castConfig);

        // Test 7: Load Scene by Build Index (if available)
        yield return TestLoadSceneByBuildIndex(castConfig);

        // Test 8: Error Handling - Invalid Action
        yield return TestInvalidAction(castConfig);

        // Test 9: Error Handling - Missing Required Parameters
        yield return TestMissingRequiredParameters(castConfig);
        
        Console.WriteLine("All ManageSceneTool tests completed successfully");
    }

    /// <summary>
    /// Test getting active scene information
    /// </summary>
    private IEnumerator TestGetActiveSceneInfo(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing GetActiveSceneInfo...");

        var getActiveTask = config.ManageSceneTool!.ManageScene(action: "get_active");
        yield return WaitForTask(getActiveTask, "GetActiveSceneInfo");

        var result = getActiveTask.Result;
        var resultObj = JObject.FromObject(result);


        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"GetActiveSceneInfo should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["name"], Is.Not.Null, "Active scene should have a name");
        Assert.That(resultObj["path"], Is.Not.Null, "Active scene should have a path");
        Assert.That(resultObj["buildIndex"], Is.Not.Null, "Active scene should have a build index");
        Assert.That(resultObj["isDirty"], Is.Not.Null, "Active scene should have isDirty property");
        Assert.That(resultObj["isLoaded"], Is.Not.Null, "Active scene should have isLoaded property");
        Assert.That(resultObj["rootCount"], Is.Not.Null, "Active scene should have rootCount property");

        Console.WriteLine($"GetActiveSceneInfo result: {resultObj}");

        // Validate active scene info using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath:"UMCP/Test Validation/ManageScene/Validate Active Scene Info");
        yield return WaitForTask(validateTask, "ValidateActiveSceneInfo");

        // Check console for validation result
        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Active Scene Info]", "Active scene info validation");
    }

    /// <summary>
    /// Test getting scene hierarchy
    /// </summary>
    private IEnumerator TestGetSceneHierarchy(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing GetSceneHierarchy...");

        var getHierarchyTask = config.ManageSceneTool!.ManageScene(action: "get_hierarchy");
        yield return WaitForTask(getHierarchyTask, "GetSceneHierarchy");

        var result = getHierarchyTask.Result;
        var resultObj = JObject.FromObject(result);

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"GetSceneHierarchy should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["hierarchy"], Is.Not.Null, "GetSceneHierarchy should return hierarchy data");

        var hierarchy = resultObj["hierarchy"] as JArray;
        Assert.That(hierarchy, Is.Not.Null, "Hierarchy should be an array");
        Assert.That(hierarchy!.Count, Is.GreaterThan(0), "Hierarchy should contain root objects");

        // Validate hierarchy structure contains test objects
        bool foundTestRoot = false;
        foreach (JObject gameObject in hierarchy.Cast<JObject>())
        {
            string? name = gameObject["name"]?.ToString();
            if (name == "ManageSceneTestRoot")
            {
                foundTestRoot = true;
                var children = gameObject["children"] as JArray;
                Assert.That(children, Is.Not.Null, "Test root should have children array");
                Assert.That(children!.Count, Is.GreaterThanOrEqualTo(2), "Test root should have at least 2 children");

                // Validate transform data is present and complete
                var transform = gameObject["transform"] as JObject;
                Assert.That(transform, Is.Not.Null, "GameObject should have transform data");
                Assert.That(transform!["position"], Is.Not.Null, "Transform should have position");
                Assert.That(transform["rotation"], Is.Not.Null, "Transform should have rotation");
                Assert.That(transform["scale"], Is.Not.Null, "Transform should have scale");

                break;
            }
        }

        Assert.That(foundTestRoot, Is.True, "Should find test hierarchy root in scene hierarchy");

        Console.WriteLine($"GetSceneHierarchy result: {resultObj}");

        // Validate hierarchy using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath: "UMCP/Test Validation/ManageScene/Validate Scene Hierarchy");
        yield return WaitForTask(validateTask, "ValidateSceneHierarchy");

        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Scene Hierarchy]", "Scene hierarchy validation");
    }

    /// <summary>
    /// Test getting build settings scenes
    /// </summary>
    private IEnumerator TestGetBuildSettings(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing GetBuildSettings...");

        var getBuildSettingsTask = config.ManageSceneTool!.ManageScene(action: "get_build_settings");
        yield return WaitForTask(getBuildSettingsTask, "GetBuildSettings");

        var result = getBuildSettingsTask.Result;
        var resultObj = JObject.FromObject(result);

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"GetBuildSettings should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["scenes"], Is.Not.Null, "GetBuildSettings should return scenes data");

        var scenes = resultObj["scenes"] as JArray;
        Assert.That(scenes, Is.Not.Null, "Scenes should be an array");

        if (scenes!.Count > 0)
        {
            // Validate structure of first scene entry
            var firstScene = scenes[0] as JObject;
            Assert.That(firstScene, Is.Not.Null, "First scene should be a valid object");
            Assert.That(firstScene!["path"], Is.Not.Null, "Scene should have path");
            Assert.That(firstScene["guid"], Is.Not.Null, "Scene should have GUID");
            Assert.That(firstScene["enabled"], Is.Not.Null, "Scene should have enabled property");
            Assert.That(firstScene["buildIndex"], Is.Not.Null, "Scene should have buildIndex property");
        }

        Console.WriteLine($"GetBuildSettings result: {resultObj}");

        // Validate build settings using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath: "UMCP/Test Validation/ManageScene/Validate Build Settings");
        yield return WaitForTask(validateTask, "ValidateBuildSettings");

        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Build Settings]", "Build settings validation");
    }

    /// <summary>
    /// Test creating a new scene
    /// </summary>
    private IEnumerator TestCreateScene(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing CreateScene...");

        var createTask = config.ManageSceneTool!.ManageScene(
            action: "create", 
            name: "ManageSceneTestScene", 
            path: "Scenes");
        yield return WaitForTask(createTask, "CreateScene");

        var result = createTask.Result;
        var resultObj = JObject.FromObject(result);

        Console.WriteLine($"CreateScene result: {resultObj}");

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"CreateScene should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["path"], Is.Not.Null, "CreateScene should return path");
        
        string? createdPath = resultObj["path"]?.ToString();
        Assert.That(createdPath, Does.Contain("ManageSceneTestScene.unity"), "Created scene path should contain scene name");

        

        // Validate scene creation using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath: "UMCP/Test Validation/ManageScene/Validate Scene Created");
        yield return WaitForTask(validateTask, "ValidateSceneCreated");

        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Scene Created]", "Scene creation validation");
    }

    /// <summary>
    /// Test loading a scene by path
    /// </summary>
    private IEnumerator TestLoadSceneByPath(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing LoadSceneByPath...");

        var loadTask = config.ManageSceneTool!.ManageScene(
            action: "load", 
            name: "ManageSceneTestScene", 
            path: "Scenes");
        yield return WaitForTask(loadTask, "LoadSceneByPath");

        var result = loadTask.Result;
        var resultObj = JObject.FromObject(result);

        Console.WriteLine($"LoadSceneByPath result: {resultObj}");

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"LoadSceneByPath should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["path"], Is.Not.Null, "LoadSceneByPath should return path");
        Assert.That(resultObj["name"], Is.Not.Null, "LoadSceneByPath should return name");

        string? loadedName = resultObj["name"]?.ToString();
        Assert.That(loadedName, Is.EqualTo("ManageSceneTestScene"), "Loaded scene should have correct name");

        

        // Validate scene loading using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath: "UMCP/Test Validation/ManageScene/Validate Scene Loaded");
        yield return WaitForTask(validateTask, "ValidateSceneLoaded");

        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Scene Loaded]", "Scene loading validation");
    }

    /// <summary>
    /// Test saving the current scene
    /// </summary>
    private IEnumerator TestSaveScene(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing SaveScene...");

        var saveTask = config.ManageSceneTool!.ManageScene(action: "save");
        yield return WaitForTask(saveTask, "SaveScene");

        var result = saveTask.Result;
        var resultObj = JObject.FromObject(result);

        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"SaveScene should succeed. Error: {resultObj?["error"] ?? "NULL"}");
        Assert.That(resultObj["path"], Is.Not.Null, "SaveScene should return path");
        Assert.That(resultObj["name"], Is.Not.Null, "SaveScene should return name");

        Console.WriteLine($"SaveScene result: {resultObj}");

        // Validate scene saving using menu item
        var validateTask = config.ExecuteMenuItemTool!.ExecuteMenuItem(menuPath: "UMCP/Test Validation/ManageScene/Validate Scene Saved");
        yield return WaitForTask(validateTask, "ValidateSceneSaved");

        yield return ValidateConsoleMessage(config, "VALIDATION_SUCCESS [Validate Scene Saved]", "Scene saving validation");
    }

    /// <summary>
    /// Test loading a scene by build index (if available)
    /// </summary>
    private IEnumerator TestLoadSceneByBuildIndex(ManageSceneToolTestSettings config)
    {
        Console.WriteLine(">>> Testing LoadSceneByBuildIndex...");

        // First check if there are scenes in build settings
        var getBuildSettingsTask = config.ManageSceneTool!.ManageScene(action: "get_build_settings");
        yield return WaitForTask(getBuildSettingsTask, "GetBuildSettingsForIndexTest");

        var buildSettingsResult = getBuildSettingsTask.Result;
        var buildSettingsObj = JObject.FromObject(buildSettingsResult);
        var scenes = buildSettingsObj["scenes"] as JArray;

        if (scenes != null && scenes.Count > 0)
        {
            // Try to load scene at build index 0
            var loadTask = config.ManageSceneTool!.ManageScene(action: "load", buildIndex: 0);
            yield return WaitForTask(loadTask, "LoadSceneByBuildIndex");

            var result = loadTask.Result;
            var resultObj = JObject.FromObject(result);

            int buildIndex = resultObj["buildIndex"]?.Value<int>() ?? -1;
            Console.WriteLine($"LoadSceneByBuildIndex result: {resultObj}");
            Console.WriteLine($"build index: {buildIndex}");

            Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, $"LoadSceneByBuildIndex should succeed. Error: {resultObj?["error"] ?? "NULL"}");
            Assert.That(resultObj["path"], Is.Not.Null, "LoadSceneByBuildIndex should return path");
            Assert.That(buildIndex, Is.EqualTo(0), "LoadSceneByBuildIndex should return correct build index");

        }
        else
        {
            Console.WriteLine("No scenes in build settings, skipping LoadSceneByBuildIndex test");
        }
    }

    /// <summary>
    /// Test error handling for invalid action
    /// </summary>
    private IEnumerator TestInvalidAction(ManageSceneToolTestSettings config)
    {
        Console.WriteLine("Testing InvalidAction error handling...");

        var invalidTask = config.ManageSceneTool!.ManageScene(action: "invalid_action");
        yield return WaitForTask(invalidTask, "InvalidAction");

        var result = invalidTask.Result;
        var resultObj = JObject.FromObject(result);

        Assert.That(resultObj["success"]?.Value<bool>() ?? true, Is.False, "Invalid action should fail");
        Assert.That(resultObj["error"], Is.Not.Null, "Invalid action should return error message");

        string? errorMessage = resultObj["error"]?.ToString();
        Assert.That(errorMessage, Does.Contain("Invalid action"), "Error message should mention invalid action");

        Console.WriteLine($"InvalidAction result: {resultObj}");
    }

    /// <summary>
    /// Test error handling for missing required parameters
    /// </summary>
    private IEnumerator TestMissingRequiredParameters(ManageSceneToolTestSettings config)
    {
        Console.WriteLine("Testing MissingRequiredParameters error handling...");

        // Test create action without name parameter
        var missingNameTask = config.ManageSceneTool!.ManageScene(action: "create");
        yield return WaitForTask(missingNameTask, "MissingRequiredParameters");

        var result = missingNameTask.Result;
        var resultObj = JObject.FromObject(result);

        Assert.That(resultObj["success"]?.Value<bool>() ?? true, Is.False, "Create without name should fail");
        Assert.That(resultObj["error"], Is.Not.Null, "Missing name should return error message");

        string? errorMessage = resultObj["error"]?.ToString();
        Assert.That(errorMessage, Does.Contain("name"), "Error message should mention missing name parameter");

        Console.WriteLine($"MissingRequiredParameters result: {resultObj}");
    }

    /// <summary>
    /// Helper method to wait for task completion with timeout
    /// </summary>
    private IEnumerator WaitForTask<T>(Task<T> task, string operationName, int timeoutSeconds = 10)
    {
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completedTask = Task.WhenAny(task, timeoutTask).Result;

        if (completedTask == timeoutTask)
        {
            Assert.Fail($"{operationName} operation timed out after {timeoutSeconds} seconds");
        }

        if (task.IsFaulted)
        {
            Assert.Fail($"{operationName} operation failed: {task.Exception?.GetBaseException().Message}");
        }

        yield return null; // Wait one frame
    }

    /// <summary>
    /// Helper method to validate console messages contain expected validation results
    /// </summary>
    private IEnumerator ValidateConsoleMessage(ManageSceneToolTestSettings config, string expectedResult, string context)
    {
        var readConsoleTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: expectedResult,
            count: 5
        );

        yield return WaitForTask(readConsoleTask, $"ReadConsole_{context}");

        var consoleResult = readConsoleTask.Result;
        var consoleObj = JObject.FromObject(consoleResult);

        Assert.That(consoleObj["success"]?.Value<bool>() ?? false, Is.True, $"ReadConsole for {context} should succeed");

        var entries = consoleObj["entries"] as JArray;
        Assert.That(entries, Is.Not.Null, $"Should return console entries for {context}");
        Assert.That(entries!.Count, Is.GreaterThan(0), $"Should find {expectedResult} message in console for {context}");

        Console.WriteLine($"{context} console validation passed");
    }
}