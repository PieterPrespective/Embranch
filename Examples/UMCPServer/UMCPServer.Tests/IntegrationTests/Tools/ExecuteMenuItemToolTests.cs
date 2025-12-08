using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools;

[TestFixture]
public class ExecuteMenuItemToolTests : IntegrationTestBase
{
    private Mock<ILogger<ExecuteMenuItemTool>> _mockLogger = null!;
    private Mock<UnityConnectionService> _mockUnityConnection = null!;
    private ExecuteMenuItemTool _tool = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockLogger = new Mock<ILogger<ExecuteMenuItemTool>>();
        _mockUnityConnection = new Mock<UnityConnectionService>(
            MockBehavior.Default,
            _mockLogger.Object,
            null
        );
        _mockUnityConnection.CallBase = false;
        
        _tool = new ExecuteMenuItemTool(_mockLogger.Object, _mockUnityConnection.Object);
    }
    
    [Test]
    public void ExecuteMenuItem_WhenExecutingValidMenuItem_ShouldReturnSuccess()
    {
        ExecuteTestSteps(ExecuteMenuItemWhenConnectedTestSteps());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void ExecuteMenuItem_WithGetAvailableMenusAction_ShouldReturnMenuList()
    {
        ExecuteTestSteps(GetAvailableMenusTestSteps());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void ExecuteMenuItem_WhenUnityIsNotConnected_ShouldReturnError()
    {
        ExecuteTestSteps(ExecuteMenuItemWhenNotConnectedTestSteps());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void ExecuteMenuItem_WithMissingMenuPath_ShouldReturnError()
    {
        ExecuteTestSteps(ExecuteMenuItemWithMissingPathTestSteps());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void ExecuteMenuItem_WithCustomScriptMenuItem_ShouldCreateFile()
    {
        ExecuteTestSteps(ExecuteCustomMenuItemTestSteps());
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    private IEnumerator ExecuteMenuItemWhenConnectedTestSteps()
    {
        // Step 1: Setup Unity connection mock
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (connected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(true);
        yield return null;
        
        // Step 2: Setup mock to return success response
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up mock responses");
        var mockResult = new JObject
        {
            ["status"] = "success",
            ["result"] = "Attempted to execute menu item: 'GameObject/Create Empty'. Check Unity logs for confirmation or errors."
        };
        
        _mockUnityConnection.Setup(m => m.SendCommandAsync(
                It.Is<string>(s => s == "execute_menu_item"),
                It.IsAny<JObject>(),
                It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
            ))
            .ReturnsAsync(mockResult);
        yield return null;
        
        // Step 3: Execute the menu item
        Console.WriteLine($"Step {CurrentStep + 1}: Executing ExecuteMenuItem method");
        Task<object> executeTask = _tool.ExecuteMenuItem(
            action: "execute",
            menuPath: "GameObject/Create Empty"
        );
        yield return executeTask;
        
        // Step 4: Verify the result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying execution result");
        var result = executeTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Execution should be successful");
        Assert.That(resultObj.message, Is.Not.Null.Or.Empty, "Success message should be provided");
        yield return null;
        
        // Step 5: Verify the connection call
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying Unity connection service calls");
        _mockUnityConnection.Verify(m => m.SendCommandAsync(
            It.Is<string>(s => s == "execute_menu_item"),
            It.Is<JObject>(obj => 
                obj["action"].ToString() == "execute" &&
                obj["menu_path"].ToString() == "GameObject/Create Empty"
            ),
            It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
        ), Times.Once);
        yield return null;
        
        Console.WriteLine("Test completed successfully");
    }
    
    private IEnumerator GetAvailableMenusTestSteps()
    {
        // Step 1: Setup Unity connection mock
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (connected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(true);
        yield return null;
        
        // Step 2: Setup mock to return menu list response
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up mock responses for get_available_menus");
        var mockResult = new JObject
        {
            ["status"] = "success",
            ["result"] = new JObject
            {
                ["message"] = "'get_available_menus' action is not fully implemented. Returning empty list.",
                ["data"] = new JArray()
            }
        };
        
        _mockUnityConnection.Setup(m => m.SendCommandAsync(
                It.Is<string>(s => s == "execute_menu_item"),
                It.IsAny<JObject>(),
                It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
            ))
            .ReturnsAsync(mockResult);
        yield return null;
        
        // Step 3: Execute get_available_menus
        Console.WriteLine($"Step {CurrentStep + 1}: Executing ExecuteMenuItem with get_available_menus action");
        Task<object> executeTask = _tool.ExecuteMenuItem(
            action: "get_available_menus"
        );
        yield return executeTask;
        
        // Step 4: Verify the result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying menu list result");
        var result = executeTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.menuItems, Is.Not.Null, "Menu items list should be present");
        yield return null;
        
        Console.WriteLine("Test completed successfully");
    }
    
    private IEnumerator ExecuteMenuItemWhenNotConnectedTestSteps()
    {
        // Step 1: Setup Unity connection mock (disconnected)
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (disconnected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(false);
        _mockUnityConnection.Setup(m => m.ConnectAsync()).ReturnsAsync(false);
        yield return null;
        
        // Step 2: Execute the menu item
        Console.WriteLine($"Step {CurrentStep + 1}: Executing ExecuteMenuItem method");
        Task<object> executeTask = _tool.ExecuteMenuItem(
            action: "execute",
            menuPath: "GameObject/Create Empty"
        );
        yield return executeTask;
        
        // Step 3: Verify error result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying error result");
        var result = executeTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.False, "Request should fail when Unity is not connected");
        Assert.That(resultObj.error.ToString(), Does.Contain("Unity Editor is not running"), "Error should indicate Unity is not running");
        yield return null;
        
        Console.WriteLine("Test completed successfully");
    }
    
    private IEnumerator ExecuteMenuItemWithMissingPathTestSteps()
    {
        // Step 1: Setup Unity connection mock
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (connected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(true);
        yield return null;
        
        // Step 2: Execute without menu path
        Console.WriteLine($"Step {CurrentStep + 1}: Executing ExecuteMenuItem without menu path");
        Task<object> executeTask = _tool.ExecuteMenuItem(
            action: "execute",
            menuPath: null
        );
        yield return executeTask;
        
        // Step 3: Verify error result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying error result");
        var result = executeTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.False, "Request should fail when menu path is missing");
        Assert.That(resultObj.error.ToString(), Does.Contain("Required parameter 'menuPath' is missing"), "Error should indicate missing menu path");
        yield return null;
        
        Console.WriteLine("Test completed successfully");
    }
    
    private IEnumerator ExecuteCustomMenuItemTestSteps()
    {
        // This test simulates executing a custom menu item that would create a file
        // In a real integration test with Unity, this would actually create the file
        
        string testFileName = "test_menu_item_output.txt";
        string simulatedFilePath = Path.Combine(Path.GetTempPath(), testFileName);
        
        // Step 1: Setup Unity connection mock
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (connected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(true);
        yield return null;
        
        // Step 2: Setup mock to simulate file creation by the menu item
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up mock to simulate file creation");
        var mockResult = new JObject
        {
            ["status"] = "success",
            ["result"] = $"Attempted to execute menu item: 'Test/Create Test File'. Check Unity logs for confirmation or errors."
        };
        
        _mockUnityConnection.Setup(m => m.SendCommandAsync(
                It.Is<string>(s => s == "execute_menu_item"),
                It.IsAny<JObject>(),
                It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
            ))
            .ReturnsAsync(mockResult)
            .Callback(() =>
            {
                // Simulate the menu item creating a file
                File.WriteAllText(simulatedFilePath, "Test file created by menu item");
            });
        yield return null;
        
        // Step 3: Execute the custom menu item
        Console.WriteLine($"Step {CurrentStep + 1}: Executing custom menu item");
        Task<object> executeTask = _tool.ExecuteMenuItem(
            action: "execute",
            menuPath: "Test/Create Test File"
        );
        yield return executeTask;
        
        // Step 4: Verify the result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying execution result");
        var result = executeTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Execution should be successful");
        yield return null;
        
        // Step 5: Use System.IO to verify the file was created
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying file creation using System.IO");
        Assert.That(File.Exists(simulatedFilePath), Is.True, "Test file should exist after menu execution");
        
        string fileContent = File.ReadAllText(simulatedFilePath);
        Assert.That(fileContent, Is.EqualTo("Test file created by menu item"), "File content should match expected");
        yield return null;
        
        // Step 6: Clean up - remove the test file
        Console.WriteLine($"Step {CurrentStep + 1}: Cleaning up test file");
        if (File.Exists(simulatedFilePath))
        {
            File.Delete(simulatedFilePath);
        }
        Assert.That(File.Exists(simulatedFilePath), Is.False, "Test file should be deleted after cleanup");
        yield return null;
        
        Console.WriteLine("Test completed successfully");
    }
    
    [TearDown]
    public override void TearDown()
    {
        base.TearDown();
        
        // Additional cleanup - ensure no test files are left behind
        string testFileName = "test_menu_item_output.txt";
        string simulatedFilePath = Path.Combine(Path.GetTempPath(), testFileName);
        
        if (File.Exists(simulatedFilePath))
        {
            try
            {
                File.Delete(simulatedFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete test file in teardown: {ex.Message}");
            }
        }
    }
}
