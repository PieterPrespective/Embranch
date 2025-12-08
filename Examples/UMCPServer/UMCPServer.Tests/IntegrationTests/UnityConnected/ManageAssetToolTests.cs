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
/// Integration tests for ManageAssetTool with actual Unity connection.
/// Tests all 11 asset management actions including creation, modification, deletion, search, and more.
/// This test requires Unity to be running with UMCP Client and ManageAssetIntegrationTest harness.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageAssetToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom test settings for ManageAssetTool tests
    /// </summary>
    private class ManageAssetToolTestSettings : TestConfiguration
    {
        public ManageAssetTool? ManageAssetTool { get; set; }
        public ExecuteMenuItemTool? ExecuteMenuItemTool { get; set; }
        public ReadConsoleTool? ReadConsoleTool { get; set; }
    }

    private ManageAssetToolTestSettings? testSettings;
    
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
        
        services.AddSingleton<ManageAssetTool>();
        services.AddSingleton<ExecuteMenuItemTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        var serviceProvider = services.BuildServiceProvider();

        testSettings = new ManageAssetToolTestSettings()
        {
            Name = "TestManageAssetTool",
            SoughtHarnessName = "ManageAssetIntegrationTest",
            TestAfterSetup = ValidateManageAssetTests,
            ServiceProvider = serviceProvider,
            UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
            StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
            ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
            ManageAssetTool = serviceProvider.GetRequiredService<ManageAssetTool>(),
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
    
    public const string ValidationPrefix = "[ManageAssetValidation]";

    public const string ValidationSuccess = "VALIDATION_SUCCESS";

    public const string ValidationFailure = "VALIDATION_FAILURE";



    private IEnumerator ValidateManageAssetTests(TestConfiguration configuration)
    {
        var settings = configuration as ManageAssetToolTestSettings;
        Assert.That(settings, Is.Not.Null, "Settings should be ManageAssetToolTestSettings");
        Assert.That(settings!.ManageAssetTool, Is.Not.Null, "ManageAssetTool should be available");
        Assert.That(settings.ExecuteMenuItemTool, Is.Not.Null, "ExecuteMenuItemTool should be available");
        Assert.That(settings.ReadConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");
        
        yield return new WaitForSeconds(0.5f);
        
        
        // Test 1: Create Material Asset
        yield return TestCreateMaterialAsset(settings);
  
        // Test 2: Create Folder
        yield return TestCreateFolder(settings);

        
        // Test 3: Get Asset Info
        yield return TestGetAssetInfo(settings);


        
        // Test 3: Modify Asset Properties
        yield return TestModifyAsset(settings);
        /*
        // Test 5: Duplicate Asset
        yield return TestDuplicateAsset(settings);
        
        // Test 6: Move Asset
        yield return TestMoveAsset(settings);
        
        // Test 7: Rename Asset
        yield return TestRenameAsset(settings);
        
        // Test 8: Search Assets
        yield return TestSearchAssets(settings);
        
        // Test 9: Get Components from Prefab
        yield return TestGetComponents(settings);
        
        // Test 10: Import/Reimport Asset
        yield return TestImportAsset(settings);
        
        // Test 11: Delete Asset
        yield return TestDeleteAsset(settings);
        
        // Error Handling Tests
        yield return TestInvalidAction(settings);
        yield return TestMissingRequiredParameters(settings);
        yield return TestNonExistentAsset(settings);
        */
    }

    private IEnumerator TestCreateMaterialAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestCreateMaterialAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "create",
            path: "Assets/UMCP_Test_Assets/NewTestMaterial2.mat",
            assetType: "material",
            properties: "{\"color\":{\"r\":0.0,\"g\":0.0,\"b\":1.0,\"a\":1.0},\"mainTexture\":\"\"}"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Create material task should complete");
        var result = task.Result;

        Console.WriteLine("Create Material Result: " + JObject.FromObject(result).ToString());

        // Convert to JObject for validation
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Create material should succeed");
        Assert.That(jResult["path"]?.Value<string>(), Does.Contain("NewTestMaterial2"), "Path should contain material name");
        
        // Validate no empty objects or arrays
        Assert.That(jResult["assetData"], Is.Not.Null, "Asset data should not be null");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Material Created"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "TestCreateMaterialAsset");

        Console.WriteLine(">>>>> TestCreateMaterialAsset completed");
    }

    





    private IEnumerator TestCreateFolder(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestCreateFolder");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "create_folder",
            path: "Assets/UMCP_Test_Assets/NewTestFolder"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Create folder task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Create folder should succeed");
        Assert.That(jResult["path"]?.Value<string>(), Does.Contain("NewTestFolder"), "Path should contain folder name");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Folder Created"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "TestCreateFolder");

        Console.WriteLine(">>>>> TestCreateFolder completed");
    }
    
   
    
    private IEnumerator TestGetAssetInfo(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestGetAssetInfo");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "get_info",
            path: "Assets/UMCP_Test_Assets/TestMaterial.mat",
            generatePreview: false
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Get asset info task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Get asset info should succeed");
        Assert.That(jResult["assetData"], Is.Not.Null, "Asset data should not be null");
        
        // Validate asset data structure
        var assetData = jResult["assetData"] as JObject;
        if (assetData != null)
        {
            // Check for expected fields
            Assert.That(assetData.HasValues, Is.True, "Asset data should have values");
            // The specific fields depend on Unity's implementation
        }
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Info"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestGetAssetInfo]");

        Console.WriteLine(">>>>> TestGetAssetInfo completed");
    }

    private IEnumerator TestModifyAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestModifyAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "modify",
            path: "Assets/UMCP_Test_Assets/TestMaterial.mat",
            properties: "{\"color\":{\"r\":0.0,\"g\":1.0,\"b\":0.0,\"a\":1.0}}"
        );

        yield return WaitForTask(task);

        Assert.That(task.IsCompletedSuccessfully, Is.True, "Modify asset task should complete");
        var result = task.Result;

        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Modify asset should succeed");

        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Modified"
        );
        yield return WaitForTask(menuTask);

        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestModifyAsset]");

        Console.WriteLine(">>>>> TestModifyAsset completed");
    }



    private IEnumerator TestDuplicateAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestDuplicateAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "duplicate",
            path: "Assets/UMCP_Test_Assets/TestPrefab.prefab",
            destination: "Assets/UMCP_Test_Assets/TestPrefab_Copy.prefab"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Duplicate asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Duplicate asset should succeed");
        Assert.That(jResult["newPath"]?.Value<string>(), Does.Contain("Copy"), "New path should contain Copy");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Duplicated"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestDuplicateAsset]");

        Console.WriteLine(">>>>> TestDuplicateAsset completed");
    }
    
    private IEnumerator TestMoveAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestMoveAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "move",
            path: "Assets/UMCP_Test_Assets/TestScriptableObject.asset",
            destination: "Assets/UMCP_Test_Assets/TestSubFolder/MovedScriptableObject.asset"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Move asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Move asset should succeed");
        Assert.That(jResult["newPath"]?.Value<string>(), Does.Contain("TestSubFolder"), "New path should contain subfolder");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Moved"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestMoveAsset]");

        Console.WriteLine(">>>>> TestMoveAsset completed");
    }
    
    private IEnumerator TestRenameAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestRenameAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "rename",
            path: "Assets/UMCP_Test_Assets/SearchTests/SearchMaterial_1.mat",
            destination: "Assets/UMCP_Test_Assets/SearchTests/RenamedMaterial.mat"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Rename asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Rename asset should succeed");
        Assert.That(jResult["newPath"]?.Value<string>(), Does.Contain("RenamedMaterial"), "New path should contain new name");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Renamed"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestRenameAsset]");

        Console.WriteLine(">>>>> TestRenameAsset completed");
    }
    
    private IEnumerator TestSearchAssets(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestSearchAssets");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "search",
            searchPattern: "Material",
            filterType: "Material",
            pageSize: 10,
            pageNumber: 1
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Search assets task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Search assets should succeed");
        Assert.That(jResult["totalAssets"]?.Value<int>(), Is.GreaterThan(0), "Should find some assets");
        
        // Validate assets array is not empty
        var assets = jResult["assets"] as JArray;
        Assert.That(assets, Is.Not.Null, "Assets array should not be null");
        Assert.That(assets!.Count, Is.GreaterThan(0), "Assets array should not be empty");
        
        // Check each asset has data
        foreach (var asset in assets)
        {
            if (asset is JObject assetObj)
            {
                Assert.That(assetObj.HasValues, Is.True, "Each asset should have values");
            }
        }
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Search Results"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestSearchAssets]");

        Console.WriteLine(">>>>> TestSearchAssets completed");
    }
    
    private IEnumerator TestGetComponents(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestGetComponents");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "get_components",
            path: "Assets/UMCP_Test_Assets/TestPrefab.prefab"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Get components task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Get components should succeed");
        
        // Validate components array is not empty
        var components = jResult["components"] as JArray;
        Assert.That(components, Is.Not.Null, "Components array should not be null");
        Assert.That(components!.Count, Is.GreaterThan(0), "Components array should not be empty");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Components Info"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestGetComponents]");

        Console.WriteLine(">>>>> TestGetComponents completed");
    }
    
    private IEnumerator TestImportAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestImportAsset");

        var task = settings.ManageAssetTool!.ManageAsset(
            action: "import",
            path: "Assets/UMCP_Test_Assets/TestTexture.png"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Import asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Import asset should succeed");
        
        // Validate via MenuItem
        var menuTask = settings.ExecuteMenuItemTool!.ExecuteMenuItem(
            menuPath: "UMCP/Integration Tests/ManageAsset/Validate Asset Reimported"
        );
        yield return WaitForTask(menuTask);
        
        yield return new WaitForSeconds(0.5f);

        yield return ValidateConsoleMessage(settings, "[TestImportAsset]");

        Console.WriteLine(">>>>> TestImportAsset completed");
    }
    
    private IEnumerator TestDeleteAsset(ManageAssetToolTestSettings settings)
    {
        Console.WriteLine(">>>>> Starting TestDeleteAsset");

        // First create an asset to delete
        var createTask = settings.ManageAssetTool!.ManageAsset(
            action: "create",
            path: "Assets/UMCP_Test_Assets/AssetToDelete.mat",
            assetType: "material"
        );
        yield return WaitForTask(createTask);
        
        // Now delete it
        var task = settings.ManageAssetTool!.ManageAsset(
            action: "delete",
            path: "Assets/UMCP_Test_Assets/AssetToDelete.mat"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Delete asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.True, "Delete asset should succeed");

        Console.WriteLine(">>>>> TestDeleteAsset completed");
    }
    
    // Error handling tests
    
    private IEnumerator TestInvalidAction(ManageAssetToolTestSettings settings)
    {
        var task = settings.ManageAssetTool!.ManageAsset(
            action: "invalid_action",
            path: "Assets/TestPath"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Invalid action task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.False, "Invalid action should fail");
        Assert.That(jResult["error"]?.Value<string>(), Does.Contain("Invalid action"), "Should have error message about invalid action");
    }
    
    private IEnumerator TestMissingRequiredParameters(ManageAssetToolTestSettings settings)
    {
        // Test create without assetType
        var task = settings.ManageAssetTool!.ManageAsset(
            action: "create",
            path: "Assets/UMCP_Test_Assets/TestAsset.mat"
            // Missing assetType parameter
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Missing parameter task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        Assert.That(jResult["success"]?.Value<bool>(), Is.False, "Missing parameter should fail");
        Assert.That(jResult["error"]?.Value<string>(), Does.Contain("assetType"), "Should have error message about missing assetType");
    }
    
    private IEnumerator TestNonExistentAsset(ManageAssetToolTestSettings settings)
    {
        var task = settings.ManageAssetTool!.ManageAsset(
            action: "get_info",
            path: "Assets/NonExistent/Asset.mat"
        );
        
        yield return WaitForTask(task);
        
        Assert.That(task.IsCompletedSuccessfully, Is.True, "Non-existent asset task should complete");
        var result = task.Result;
        
        var jResult = JObject.FromObject(result);
        // This might succeed with null data or fail - depends on Unity implementation
        // Just check that we get a proper response structure
        Assert.That(jResult, Is.Not.Null, "Should get a response for non-existent asset");
    }



    /// <summary>
    /// Helper method to validate console messages contain expected validation results
    /// </summary>
    private IEnumerator ValidateConsoleMessage(ManageAssetToolTestSettings config, string functionSignature)
    {
        //Create a read console task
        var readConsoleTask = config.ReadConsoleTool!.ReadConsole(
            action: "get",
            filterText: ValidationPrefix,
            count: 20
        );

        // Wait for the task to complete
        yield return WaitForTask(readConsoleTask, $"ReadConsole_{functionSignature}");

        //Parse the result
        var consoleResult = readConsoleTask.Result;
        var consoleObj = JObject.FromObject(consoleResult);

        Assert.That(consoleObj["success"]?.Value<bool>() ?? false, Is.True, $"ReadConsole for {functionSignature} should succeed");

        var entries = consoleObj["entries"] as JArray;
        Assert.That(entries, Is.Not.Null, $"Should return console entries for {functionSignature}");
        Assert.That(entries!.Count, Is.GreaterThan(0), $"Should find {ValidationPrefix} message in console for {functionSignature}");

        //Loop the results for either a success or failure message
        bool found = false;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            //Console.WriteLine($"Console Entry {i}: {entry.ToString()}");
            var message = entries[i]?["message"]?.Value<string>() ?? "";
            bool hasAllParts = true;

            //CASE this is not a validation message, skip it
            if (!message.StartsWith(ValidationPrefix))
            {
                continue;
            }

            //CASE : this is a success message
            if(message.Contains(ValidationSuccess) && message.Contains(functionSignature))
            {
                Console.WriteLine($"Found validation success message for {functionSignature}: {message}");
                found = true;
                yield break;
            }

            if(message.Contains(ValidationFailure) && message.Contains(functionSignature))
            {
                Assert.Fail($"Validation failure for {functionSignature}: {message}");
            }
        }

        if (!found)
        {
            Assert.Fail($"Did not find expected console message containing '{functionSignature}'");
        }

        Console.WriteLine($"{functionSignature} console validation passed");
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








    private static IEnumerator WaitForTask(Task task, int timeoutSeconds = 10)
    {
        var startTime = DateTime.Now;
        while (!task.IsCompleted)
        {
            if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
            {
                Assert.Fail($"Task timed out after {timeoutSeconds} seconds");
                yield break;
            }
            yield return new WaitForSeconds(0.1f);
        }
        
        if (task.IsFaulted)
        {
            Assert.Fail($"Task failed with exception: {task.Exception?.GetBaseException().Message}");
        }
    }
    
    // Helper class for simulating Unity's WaitForSeconds in tests
    private class WaitForSeconds
    {
        public float Seconds { get; }
        
        public WaitForSeconds(float seconds)
        {
            Seconds = seconds;
        }
    }
}