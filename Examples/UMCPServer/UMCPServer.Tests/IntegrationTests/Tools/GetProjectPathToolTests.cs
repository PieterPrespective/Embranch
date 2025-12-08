using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools;

[TestFixture]
public class GetProjectPathToolTests : IntegrationTestBase
{
    private Mock<ILogger<GetProjectPathTool>> _mockLogger = null!;
    private Mock<UnityConnectionService> _mockUnityConnection = null!;
    private GetProjectPathTool _tool = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockLogger = new Mock<ILogger<GetProjectPathTool>>();
        _mockUnityConnection = new Mock<UnityConnectionService>(
            MockBehavior.Default, // Loose mocking
            _mockLogger.Object,
            null // We're not testing the config so we don't need to set it up
        );
        _mockUnityConnection.CallBase = false; // Don't call base methods
        
        _tool = new GetProjectPathTool(_mockLogger.Object, _mockUnityConnection.Object);
    }
    
    [Test]
    public void GetProjectPath_WhenUnityIsConnected_ShouldReturnProjectPath()
    {
        // Execute the multi-step test
        ExecuteTestSteps(GetProjectPathWhenConnectedTestSteps());
        
        // Verify test completed all steps
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void GetProjectPath_WhenUnityIsNotConnected_ShouldReturnErrorResponse()
    {
        // Execute the multi-step test
        ExecuteTestSteps(GetProjectPathWhenNotConnectedTestSteps());
        
        // Verify test completed all steps
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    private IEnumerator GetProjectPathWhenConnectedTestSteps()
    {
        // Step 1: Setup Unity connection mock to indicate connected state
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (connected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(true);
        yield return null;
        
        // Step 2: Setup mock to return project path data
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up mock responses");
        var mockResult = new JObject
        {
            ["success"] = true,
            ["message"] = "Project path retrieved",
            ["data"] = new JObject
            {
                ["projectPath"] = @"C:\TestUnityProject",
                ["dataPath"] = @"C:\TestUnityProject\Assets",
                ["persistentDataPath"] = @"C:\Users\Test\AppData\LocalLow\TestCompany\TestProject",
                ["streamingAssetsPath"] = @"C:\TestUnityProject\Assets\StreamingAssets",
                ["temporaryCachePath"] = @"C:\Users\Test\AppData\Local\Temp\TestProject"
            }
        };
        
        _mockUnityConnection.Setup(m => m.SendCommandAsync(
                It.Is<string>(s => s == "get_project_path"),
                It.IsAny<JObject>(),
                It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)

            ))
            .ReturnsAsync(mockResult);
        yield return null;
        
        // Step 3: Execute the GetProjectPath method
        Console.WriteLine($"Step {CurrentStep + 1}: Executing GetProjectPath method");
        Task<object> projectPathTask = _tool.GetProjectPath();
        yield return projectPathTask;
        
        // Step 4: Verify the result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying project path result");
        var result = projectPathTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        // Extract the result as dynamic to access its properties
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Project path request should be successful");
        Assert.That(resultObj.projectPath, Is.EqualTo(@"C:\TestUnityProject"), "Project path should match mock data");
        Assert.That(resultObj.dataPath, Is.EqualTo(@"C:\TestUnityProject\Assets"), "Data path should match mock data");
        Assert.That(resultObj.persistentDataPath, Is.EqualTo(@"C:\Users\Test\AppData\LocalLow\TestCompany\TestProject"), "Persistent data path should match mock data");
        Assert.That(resultObj.streamingAssetsPath, Is.EqualTo(@"C:\TestUnityProject\Assets\StreamingAssets"), "Streaming assets path should match mock data");
        Assert.That(resultObj.temporaryCachePath, Is.EqualTo(@"C:\Users\Test\AppData\Local\Temp\TestProject"), "Temporary cache path should match mock data");
        yield return null;
        
        // Step 5: Verify the connection call was made with correct parameters
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying Unity connection service calls");
        _mockUnityConnection.Verify(m => m.SendCommandAsync(
            It.Is<string>(s => s == "get_project_path"),
            It.IsAny<JObject>(),
            It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
        ), Times.Once, "SendCommandAsync should be called once with 'get_project_path' command");
        yield return null;
        
        // Test complete
        Console.WriteLine("Test completed successfully");
    }
    
    private IEnumerator GetProjectPathWhenNotConnectedTestSteps()
    {
        // Step 1: Setup Unity connection mock to indicate disconnected state
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up Unity connection mock (disconnected)");
        _mockUnityConnection.Setup(m => m.IsConnected).Returns(false);
        _mockUnityConnection.Setup(m => m.ConnectAsync()).ReturnsAsync(false);
        yield return null;
        
        // Step 2: Execute the GetProjectPath method
        Console.WriteLine($"Step {CurrentStep + 1}: Executing GetProjectPath method");
        Task<object> projectPathTask = _tool.GetProjectPath();
        yield return projectPathTask;
        
        // Step 3: Verify the result is an error
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying error result");
        var result = projectPathTask.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null even on error");
        
        // Extract the result as dynamic to access its properties
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.False, "Request should fail when Unity is not connected");
        Assert.That(resultObj.error, Is.Not.Null.Or.Empty, "Error message should be provided");
        Assert.That(resultObj.error.ToString(), Does.Contain("Unity Editor is not running"), "Error should indicate Unity is not running");
        yield return null;
        
        // Step 4: Verify the connection attempts
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying connection attempts");
        _mockUnityConnection.Verify(m => m.ConnectAsync(), Times.Once, "ConnectAsync should be called once");
        _mockUnityConnection.Verify(m => m.SendCommandAsync(
            It.IsAny<string>(),
            It.IsAny<JObject>(),
            It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)
        ), Times.Never, "SendCommandAsync should not be called when not connected");
        yield return null;
        
        // Test complete
        Console.WriteLine("Test completed successfully");
    }
}