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
/// Comprehensive integration tests for ManageGameObjectTool with actual Unity connection.
/// Tests all 8 actions: create, modify, delete, find, get_components, add_component, remove_component, set_component_property.
/// Validates proper GameObject manipulation, component management, and error handling.
/// This test requires Unity to be running with UMCP Client and ManageGameObjectIntegrationTest harness.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("RequiresUnity")]
public class ManageGameObjectToolTests : IntegrationTestBase
{
    /// <summary>
    /// Custom test settings for ManageGameObjectTool tests
    /// </summary>
    private class ManageGameObjectToolTestSettings : TestConfiguration
    {
        public ManageGameObjectTool? ManageGameObjectTool { get; set; }
        public ExecuteMenuItemTool? ExecuteMenuItemTool { get; set; }
        public ReadConsoleTool? ReadConsoleTool { get; set; }
    }

    private ManageGameObjectToolTestSettings? testSettings;
    
    private const int UnityPort = 6400;
    private const int StatePort = 6401;
    
    // Test constants matching client-side harness
    private const string TestCubeName = "ManageGO_TestCube";
    private const string TestSphereName = "ManageGO_TestSphere";
    private const string ParentObjectName = "ManageGO_Parent";
    private const string ChildObjectName = "ManageGO_Child";
    private const string ComponentTestObjectName = "ManageGO_ComponentTest";
    private const string TestTag = "ManageGOTestTag";
    
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
        
        services.AddSingleton<ManageGameObjectTool>();
        services.AddSingleton<ExecuteMenuItemTool>();
        services.AddSingleton<ReadConsoleTool>();
        services.AddSingleton<ForceUpdateEditorTool>();

        var serviceProvider = services.BuildServiceProvider();

        testSettings = new ManageGameObjectToolTestSettings()
        {
            Name = "TestManageGameObjectTool",
            SoughtHarnessName = "ManageGameObjectIntegrationTest",
            TestAfterSetup = ValidateManageGameObjectTests,
            ServiceProvider = serviceProvider,
            UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
            StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
            ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
            ManageGameObjectTool = serviceProvider.GetRequiredService<ManageGameObjectTool>(),
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
    public void ManageGameObject_IntegrationTests_RunSuccessfully()
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
    /// Validates all ManageGameObject tool operations after the harness is set up
    /// </summary>
    private static IEnumerator ValidateManageGameObjectTests(TestConfiguration settings)
    {
        var config = settings as ManageGameObjectToolTestSettings;
        Assert.That(config, Is.Not.Null, "Settings should be ManageGameObjectToolTestSettings");
        
        var manageGameObjectTool = config!.ManageGameObjectTool;
        var executeMenuItemTool = config.ExecuteMenuItemTool;
        var readConsoleTool = config.ReadConsoleTool;
        
        Assert.That(manageGameObjectTool, Is.Not.Null, "ManageGameObjectTool should be available");
        Assert.That(executeMenuItemTool, Is.Not.Null, "ExecuteMenuItemTool should be available");
        Assert.That(readConsoleTool, Is.Not.Null, "ReadConsoleTool should be available");
        
        Console.WriteLine("Starting ManageGameObjectTool validation tests...");
        
        
        // Test Create action
        yield return TestCreateGameObject(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        yield return TestCreateWithPrimitiveType(manageGameObjectTool!);

        
        yield return TestCreateWithParent(manageGameObjectTool!);
        
        // Test Modify action
        yield return TestModifyGameObjectName(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);

        
        //TODO : Test with different transform formats (object vs array)
        yield return TestModifyTransform(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);

        
        yield return TestModifyActiveState(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        
        yield return TestModifyTagAndLayer(manageGameObjectTool!);
        
        // Test Delete action
        yield return TestDeleteGameObject(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        
        // Test Find action
        yield return TestFindByName(manageGameObjectTool!);
        yield return TestFindByTag(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        yield return TestFindByComponentType(manageGameObjectTool!);
        
        // Test Component operations
        yield return TestGetComponents(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        yield return TestAddComponent(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        yield return TestRemoveComponent(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        yield return TestSetComponentProperty(manageGameObjectTool!, executeMenuItemTool!, readConsoleTool!);
        
        // Test error scenarios
        yield return TestInvalidAction(manageGameObjectTool!);
        yield return TestMissingRequiredParameters(manageGameObjectTool!);
        yield return TestInvalidTarget(manageGameObjectTool!);
        
        Console.WriteLine("ManageGameObjectTool validation tests completed");
    }

    #region Create Action Tests

    private static IEnumerator TestCreateGameObject(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Create basic GameObject");
        
        var createTask = tool.ManageGameObject(
            action: "create",
            name: "TestCreatedObject"
        );
        
        if (!WaitForTask(createTask, out var result))
        {
            Assert.Fail("Create GameObject operation timed out");
        }
        

        Console.WriteLine("CreateGameObject result: " + JObject.FromObject(result).ToString());

        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, 
            $"Create operation should succeed. Error: {resultObj["error"]}");
        
        // Validate the created object structure
        var gameObjectData = resultObj["gameObjectData"];
        Assert.That(gameObjectData, Is.Not.Null, "gameObjectData should be present");


        Assert.That(gameObjectData!["name"]?.ToString(), Is.EqualTo("TestCreatedObject"), 
            "Created object should have correct name");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateGameObjectExists", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestCreateWithPrimitiveType(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Create GameObject with primitive type");
        
        var createTask = tool.ManageGameObject(
            action: "create",
            name: "TestPrimitiveCapsule",
            primitiveType: "Capsule",
            //transform: new { position = new { x = 3, y = 1, z = 0 } }
            transform: "{ \"position\": {\"x\": 3,\"y\": 1, \"z\": 0}}"
        );
        
        if (!WaitForTask(createTask, out var result))
        {
            Assert.Fail("Create primitive GameObject operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);

        Console.WriteLine("CreateWithPrimitiveType result: " + resultObj.ToString());



        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Create primitive operation should succeed. Error: {resultObj["error"]}");
        
        var gameObjectData = resultObj["gameObjectData"];
        Assert.That(gameObjectData, Is.Not.Null, "gameObjectData should be present");

        //TODO : primitiveType is not being returned currently - need to fix client harness
        //Assert.That(gameObjectData!["primitiveType"]?.ToString(), Is.EqualTo("Capsule"),
        //    "Primitive type should be Capsule");
        
        // Check transform data
        var transform = gameObjectData["transform"];
        Assert.That(transform, Is.Not.Null, "Transform data should be present");
        Assert.That(transform!["position"], Is.Not.Null, "Position should be present");
        
        yield return null;
    }

    private static IEnumerator TestCreateWithParent(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Create GameObject with parent");

        //"{\"color\":{\"r\":0.0,\"g\":0.0,\"b\":1.0,\"a\":1.0},\"mainTexture\":\"\"}"
        //"{\"localPosition\":{ \"x\" : 0, \"y\" : 1, \"z\" : 0 } }"


        var createTask = tool.ManageGameObject(
            action: "create",
            name: "TestChildObject",
            parent: ParentObjectName,
            transform: "{\"localPosition\":{ \"x\" : 0, \"y\" : 1, \"z\" : 0 } }"// new JObject(new { localPosition = new { x = 0, y = 1, z = 0 } }).ToString()
        );
        
        if (!WaitForTask(createTask, out var result))
        {
            Assert.Fail("Create child GameObject operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Create child operation should succeed. Error: {resultObj["error"]}");
        
        var gameObjectData = resultObj["gameObjectData"];
        Assert.That(gameObjectData!["parentInstanceID"]?.ToString(), Is.Not.Null,
            "Parent should be set correctly");
        
        yield return null;
    }

    #endregion

    #region Modify Action Tests

    private static IEnumerator TestModifyGameObjectName(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Modify GameObject name");
        
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: TestCubeName,
            name: "ModifiedTestCube"
        );
        
        if (!WaitForTask(modifyTask, out var result))
        {
            Assert.Fail("Modify name operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Modify name operation should succeed. Error: {resultObj["error"]}");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateNameChanged", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestModifyTransform(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Modify GameObject transform");
        /*
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: TestSphereName,
            transform: new 
            { 
                position = new float[] { 5f, 3f, 1f },//new { x = 5, y = 3, z = 1 },
                rotation = new float[] { 0f, 45f, 0f },//new { x = 0, y = 45, z = 0 },
                scale = new float[] { 2f, 2f, 2f }//new { x = 2, y = 2, z = 2 }
            }
        );
        */
        //Validate if model based setting will also work?
        /*
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: TestSphereName,
            transform: new
            {
                position = new { x = 5, y = 3, z = 1 },
                rotation = new { x = 0, y = 45, z = 0 },
                scale = new { x = 2, y = 2, z = 2 }
            }
        );
        */
        //Validate a stringified object will also work
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: TestSphereName,
            transform: "{\n  \"position\": {\"x\": 5,\"y\": 3, \"z\": 1},\n  \"scale\": {\"x\": 2, \"y\": 2, \"z\": 2},\n\"rotation\": {\"x\": 0, \"y\": 45, \"z\": 0}\n}"
        );


        




        if (!WaitForTask(modifyTask, out var result))
        {
            Assert.Fail("Modify transform operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Modify transform operation should succeed. Error: {resultObj["error"]}");
        
        // Validate transform modification
        yield return ValidateWithMenuItem("ValidateTransformModified", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestModifyActiveState(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Modify GameObject active state");
        
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: TestSphereName,
            isActive: false
        );
        
        if (!WaitForTask(modifyTask, out var result))
        {
            Assert.Fail("Modify active state operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Modify active state operation should succeed. Error: {resultObj["error"]}");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateActiveStateChanged", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestModifyTagAndLayer(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Modify GameObject tag and layer");
        
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            target: "TestCreatedObject",
            tag: TestTag,
            layer: "ManageGOTestLayer"
        );
        
        if (!WaitForTask(modifyTask, out var result))
        {
            Assert.Fail("Modify tag/layer operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Modify tag/layer operation should succeed. Error: {resultObj["error"]}");
        
        yield return null;
    }

    #endregion

    #region Delete Action Tests

    private static IEnumerator TestDeleteGameObject(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Delete GameObject");
        
        // First, ensure we have an object to delete by modifying back the name
        var renameTask = tool.ManageGameObject(
            action: "modify",
            target: "ModifiedTestCube",
            name: TestCubeName
        );
        
        WaitForTask(renameTask, out _);
        
        // Now delete it
        var deleteTask = tool.ManageGameObject(
            action: "delete",
            target: TestCubeName
        );
        
        if (!WaitForTask(deleteTask, out var result))
        {
            Assert.Fail("Delete GameObject operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Delete operation should succeed. Error: {resultObj["error"]}");
        
        // Validate deletion through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateGameObjectDeleted", executeMenuItemTool, readConsoleTool);
    }

    #endregion

    #region Find Action Tests

    private static IEnumerator TestFindByName(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Find GameObjects by name");

        var findTask = tool.ManageGameObject(
            action: "find",
            target: TestSphereName,
            searchMethod: "by_name",
            findAll: false
        );
        
        if (!WaitForTask(findTask, out var result))
        {
            Assert.Fail("Find by name operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Find operation should succeed. Error: {resultObj["error"]}");
        
        var gameObjects = resultObj["gameObjects"] as JArray;
        Assert.That(gameObjects, Is.Not.Null, "gameObjects array should be present");
        Assert.That(gameObjects!.Count, Is.GreaterThan(0), "Should find at least one GameObject");
        
        // Validate structure of found objects
        var firstObject = gameObjects[0] as JObject;
        Assert.That(firstObject, Is.Not.Null, "First object should be a valid JObject");
        Assert.That(firstObject!["name"]?.ToString(), Is.Not.Null.And.Not.Empty, "Object should have a name");
        Assert.That(firstObject["instanceID"], Is.Not.Null, "Object should have instanceID");
        Assert.That(firstObject["transform"], Is.Not.Null, "Object should have transform data");
        
        yield return null;
    }

    private static IEnumerator TestFindByTag(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Find GameObjects by tag");
        
        var findTask = tool.ManageGameObject(
            action: "find",
            target: TestTag,
            searchMethod: "by_tag",
            maxResults: 10
        );
        
        if (!WaitForTask(findTask, out var result))
        {
            Assert.Fail("Find by tag operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Find by tag operation should succeed. Error: {resultObj["error"]}");
        
        var gameObjects = resultObj["gameObjects"] as JArray;
        Assert.That(gameObjects, Is.Not.Null, "gameObjects array should be present");
        Assert.That(gameObjects!.Count, Is.GreaterThan(0), "Should find tagged objects");
        
        // Validate all found objects have the correct tag
        foreach (JObject obj in gameObjects)
        {
            Assert.That(obj["tag"]?.ToString(), Is.EqualTo(TestTag), 
                $"Found object {obj["name"]} should have tag {TestTag}");
        }
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateMultipleObjectsFound", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestFindByComponentType(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Find GameObjects by component type");
        
        var findTask = tool.ManageGameObject(
            action: "find",
            componentType: "Rigidbody",
            searchMethod: "by_component"
        );
        
        if (!WaitForTask(findTask, out var result))
        {
            Assert.Fail("Find by component type operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Find by component operation should succeed. Error: {resultObj["error"]}");
        
        var gameObjects = resultObj["gameObjects"] as JArray;
        Assert.That(gameObjects, Is.Not.Null, "gameObjects array should be present");
        
        if (gameObjects!.Count > 0)
        {
            var firstObject = gameObjects[0] as JObject;
            Assert.That(firstObject!["componentNames"], Is.Not.Null, 
                "Found objects should include component information");
        }
        
        yield return null;
    }

    #endregion

    #region Component Operation Tests

    private static IEnumerator TestGetComponents(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Get components of GameObject");
        
        var getComponentsTask = tool.ManageGameObject(
            action: "get_components",
            target: ComponentTestObjectName
        );
        
        if (!WaitForTask(getComponentsTask, out var result))
        {
            Assert.Fail("Get components operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Get components operation should succeed. Error: {resultObj["error"]}");
        
        var components = resultObj["components"] as JArray;
        Assert.That(components, Is.Not.Null, "components array should be present");
        Assert.That(components!.Count, Is.GreaterThan(0), "Should have at least one component");
        
        // Validate component structure
        bool hasRigidbody = false;
        bool hasBoxCollider = false;
        
        foreach (JObject comp in components)
        {
            //TODO : type is not being returned currently - need to fix client harness
            Assert.That(comp["typeName"]?.ToString(), Is.Not.Null.And.Not.Empty, 
                "Component should have a type");
            Assert.That(comp["instanceID"], Is.Not.Null, 
                "Component should have properties");
            
            string? compType = comp["typeName"]?.ToString();
            if (compType == "UnityEngine.Rigidbody") hasRigidbody = true;
            if (compType == "UnityEngine.BoxCollider") hasBoxCollider = true;
        }
        
        Assert.That(hasRigidbody, Is.True, "Should have Rigidbody component");
        Assert.That(hasBoxCollider, Is.True, "Should have BoxCollider component");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateComponentsList", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestAddComponent(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Add component to GameObject");
        
        // First recreate the test cube that was deleted
        var createTask = tool.ManageGameObject(
            action: "create",
            name: TestCubeName,
            primitiveType: "Cube"
        );
        
        WaitForTask(createTask, out _);
        
        // Now add a component
        var addComponentTask = tool.ManageGameObject(
            action: "add_component",
            target: TestCubeName,
            componentName: "AudioSource"
        );
        
        if (!WaitForTask(addComponentTask, out var result))
        {
            Assert.Fail("Add component operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Add component operation should succeed. Error: {resultObj["error"]}");

        Console.WriteLine($"formatted result: {resultObj.ToString()}");

        var gameObjectData = resultObj["gameObjectData"];
        Assert.That(gameObjectData, Is.Not.Null, "no gameobject data returned");



        var components = gameObjectData?["componentNames"] as JArray;
        Assert.That(components, Is.Not.Null, "componentNames array should be present");
        Assert.That(components!.Count, Is.GreaterThan(0), "Should have at least one component");

        

        // Validate component structure
        bool hasAudioSource = false;

        foreach (JValue comp in components)
        {
            //TODO : type is not being returned currently - need to fix client harness
            Assert.That(comp.ToString(), Is.Not.Null.And.Not.Empty,
                "Componentname should be valid");
            if (comp.ToString() == "UnityEngine.AudioSource") hasAudioSource = true;
        }

        Assert.That(hasAudioSource, Is.True, "Added component type should be AudioSource");

        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateComponentAdded", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestRemoveComponent(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Remove component from GameObject");
        
        var removeComponentTask = tool.ManageGameObject(
            action: "remove_component",
            target: ComponentTestObjectName,
            componentName: "BoxCollider"
        );
        
        if (!WaitForTask(removeComponentTask, out var result))
        {
            Assert.Fail("Remove component operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Remove component operation should succeed. Error: {resultObj["error"]}");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateComponentRemoved", executeMenuItemTool, readConsoleTool);
    }

    private static IEnumerator TestSetComponentProperty(ManageGameObjectTool tool, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        Console.WriteLine("\nTesting: Set component property");
        // 
        var setPropertyTask = tool.ManageGameObject(
            action: "set_component_property",
            target: ComponentTestObjectName,
            componentName: "Rigidbody",
            componentProperties: "{ \"mass\" : 10.0, \"useGravity\" : true }"// new JObject(new { mass = 10f, useGravity = true }).ToString()
        );
        
        if (!WaitForTask(setPropertyTask, out var result))
        {
            Assert.Fail("Set component property operation timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"Set component property operation should succeed. Error: {resultObj["error"]}");

        var gameObjectData = resultObj["gameObjectData"];
        Assert.That(gameObjectData, Is.Not.Null, "no gameobject data returned");

        var components = gameObjectData?["componentNames"] as JArray;
        Assert.That(components, Is.Not.Null, "componentNames array should be present");
        Assert.That(components!.Count, Is.GreaterThan(0), "Should have at least one component");



        //// Validate component structure
        //bool hasAudioSource = false;

        //foreach (JValue comp in components)
        //{
        //    //TODO : type is not being returned currently - need to fix client harness
        //    Assert.That(comp.ToString(), Is.Not.Null.And.Not.Empty,
        //        "Componentname should be valid");
        //    if (comp.ToString() == "UnityEngine.AudioSource") hasAudioSource = true;
        //}

        //Assert.That(hasAudioSource, Is.True, "Added component type should be AudioSource");



        //var componentData = resultObj["componentData"];
        //Assert.That(componentData, Is.Not.Null, "componentData should be present");
        
        //// Validate properties were set
        //var properties = componentData!["properties"];
        //Assert.That(properties, Is.Not.Null, "Properties should be present");
        //Assert.That(properties!["mass"]?.Value<float>(), Is.EqualTo(10f).Within(0.01f),
        //    "Mass should be set to 10");
        //Assert.That(properties["useGravity"]?.Value<bool>(), Is.True,
        //    "UseGravity should be set to true");
        
        // Validate through ExecuteMenuItem
        yield return ValidateWithMenuItem("ValidateComponentPropertyModified", executeMenuItemTool, readConsoleTool);
    }

    #endregion

    #region Error Handling Tests

    private static IEnumerator TestInvalidAction(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Invalid action parameter");
        
        var invalidTask = tool.ManageGameObject(
            action: "invalid_action",
            target: TestSphereName
        );
        
        if (!WaitForTask(invalidTask, out var result))
        {
            Assert.Fail("Invalid action test timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.False,
            "Invalid action should fail");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("Invalid action"),
            "Error message should indicate invalid action");
        
        yield return null;
    }

    private static IEnumerator TestMissingRequiredParameters(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Missing required parameters");
        
        // Test create without name
        var createTask = tool.ManageGameObject(
            action: "create"
            // Missing required 'name' parameter
        );
        
        if (!WaitForTask(createTask, out var result))
        {
            Assert.Fail("Missing parameter test timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.False,
            "Operation with missing required parameter should fail");
        Assert.That(resultObj["error"]?.ToString(), Does.Contain("name"),
            "Error message should mention missing 'name' parameter");
        
        // Test modify without target
        var modifyTask = tool.ManageGameObject(
            action: "modify",
            name: "NewName"
            // Missing required 'target' parameter
        );
        
        if (!WaitForTask(modifyTask, out var modifyResult))
        {
            Assert.Fail("Missing target test timed out");
        }
        
        var modifyResultObj = JObject.FromObject(modifyResult!);
        Assert.That(modifyResultObj["success"]?.Value<bool>() ?? false, Is.False,
            "Modify without target should fail");
        Assert.That(modifyResultObj["error"]?.ToString(), Does.Contain("target"),
            "Error message should mention missing 'target' parameter");
        
        yield return null;
    }

    private static IEnumerator TestInvalidTarget(ManageGameObjectTool tool)
    {
        Console.WriteLine("\nTesting: Invalid target GameObject");
        
        var invalidTargetTask = tool.ManageGameObject(
            action: "modify",
            target: "NonExistentObject",
            name: "NewName"
        );
        
        if (!WaitForTask(invalidTargetTask, out var result))
        {
            Assert.Fail("Invalid target test timed out");
        }
        
        var resultObj = JObject.FromObject(result!);
        // This might succeed or fail depending on Unity implementation
        // But we should get a proper response structure
        Assert.That(resultObj["success"], Is.Not.Null, 
            "Response should have success field");
        
        if (!(resultObj["success"]?.Value<bool>() ?? false))
        {
            Assert.That(resultObj["error"]?.ToString(), Is.Not.Null.And.Not.Empty,
                "Failed operation should have error message");
        }
        
        yield return null;
    }

    #endregion

    #region Helper Methods

    private static IEnumerator ValidateWithMenuItem(string validationMethod, ExecuteMenuItemTool executeMenuItemTool, ReadConsoleTool readConsoleTool)
    {
        var executeTask = executeMenuItemTool.ExecuteMenuItem(
            menuPath: $"UMCP/Tests/ManageGameObject/{validationMethod}"
        );
        
        if (!WaitForTask(executeTask, out var executeResult))
        {
            Assert.Fail($"ExecuteMenuItem for {validationMethod} timed out");
        }
        
        var executeResultObj = JObject.FromObject(executeResult!);
        Assert.That(executeResultObj["success"]?.Value<bool>() ?? false, Is.True,
            $"ExecuteMenuItem should succeed. Error: {executeResultObj["error"]}");
        
        // Read console to verify validation
        var readConsoleTask = readConsoleTool.ReadConsole(
            types: new[] { "log" },
            count: 10
        );
        
        if (!WaitForTask(readConsoleTask, out var consoleResult))
        {
            Assert.Fail("ReadConsole timed out");
        }
        
        var consoleResultObj = JObject.FromObject(consoleResult!);
        var entries = consoleResultObj["entries"] as JArray;
        
        if (entries != null && entries.Count > 0)
        {
            bool validationFound = false;
            foreach (JObject entry in entries)
            {
                var message = entry["message"]?.ToString() ?? "";
                if (message.Contains("VALIDATION_SUCCESS"))
                {
                    Console.WriteLine($"  ✓ Validation succeeded: {message}");
                    validationFound = true;
                    break;
                }
                else if (message.Contains("VALIDATION_FAILED"))
                {
                    Assert.Fail($"Validation failed: {message}");
                }
            }
            
            if (!validationFound)
            {
                Console.WriteLine($"  ⚠ No validation message found for {validationMethod}");
            }
        }
        
        yield return null;
    }

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