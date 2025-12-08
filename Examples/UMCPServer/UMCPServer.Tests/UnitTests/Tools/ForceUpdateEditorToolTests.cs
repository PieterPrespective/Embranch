using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.UnitTests.Tools;

[TestFixture]
public class ForceUpdateEditorToolTests
{
    private Mock<ILogger<ForceUpdateEditorTool>> _mockLogger = null!;
    private Mock<UnityConnectionService> _mockUnityConnection = null!;
    private Mock<UnityStateConnectionService> _mockStateConnection = null!;
    private ForceUpdateEditorTool _tool = null!;
    
    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<ForceUpdateEditorTool>>();
        _mockUnityConnection = new Mock<UnityConnectionService>(Mock.Of<ILogger<UnityConnectionService>>(), null);
        _mockStateConnection = new Mock<UnityStateConnectionService>(Mock.Of<ILogger<UnityStateConnectionService>>(), null);
        
        _tool = new ForceUpdateEditorTool(
            _mockLogger.Object, 
            _mockUnityConnection.Object, 
            _mockStateConnection.Object);
    }
    
    [Test]
    public async Task ForceUpdateEditor_WhenUnityNotConnected_ShouldReturnError()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
        _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);
        
        // Act
        var result = await _tool.ForceUpdateEditor();
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.False);
        Assert.That(resultObj.error.ToString(), Does.Contain("Unity Editor is not running"));
    }
    
    [Test]
    public async Task ForceUpdateEditor_WhenStateConnectionFails_ShouldReturnError()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.IsConnected).Returns(false);
        _mockStateConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);
        
        // Act
        var result = await _tool.ForceUpdateEditor();
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.False);
        Assert.That(resultObj.error.ToString(), Does.Contain("Unable to connect to Unity state monitoring"));
    }
    
    [Test]
    public async Task ForceUpdateEditor_WhenCommandFails_ShouldReturnError()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.CurrentUnityState).Returns(CreateInitialState());
        
        var failedResult = new JObject
        {
            ["success"] = false,
            ["error"] = "Unity command failed"
        };
        
        _mockUnityConnection.Setup(x => x.SendCommandAsync("force_update_editor", null, It.IsAny<CancellationToken>(),
                It.Is<bool>(b => b == false)))
            .ReturnsAsync(failedResult);
        
        // Act
        var result = await _tool.ForceUpdateEditor();
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.False);
        Assert.That(resultObj.error.ToString(), Does.Contain("Unity command failed"));
    }
    
    [Test]
    public async Task ForceUpdateEditor_WhenAlreadyInEditModeRunning_ShouldReturnImmediately()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.IsConnected).Returns(true);
        
        var editModeRunningState = CreateEditModeRunningState();
        _mockStateConnection.Setup(x => x.CurrentUnityState).Returns(editModeRunningState);
        
        var successResult = new JObject
        {
            ["success"] = true,
            ["data"] = new JObject
            {
                ["action"] = "updating_editor"
            }
        };
        
        _mockUnityConnection.Setup(x => x.SendCommandAsync("force_update_editor", null, It.IsAny<CancellationToken>(), It.Is<bool>(b => b == false)))
            .ReturnsAsync(successResult);
        
        // Act
        var result = await _tool.ForceUpdateEditor();
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.True);
        Assert.That(resultObj.message.ToString(), Does.Contain("completed successfully"));
        Assert.That(resultObj.finalState.runmode.ToString(), Is.EqualTo("EditMode_Scene"));
        Assert.That(resultObj.finalState.context.ToString(), Is.EqualTo("Running"));
    }
    
    [Test]
    public async Task ForceUpdateEditor_WithTimeout_ShouldReturnError()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.CurrentUnityState).Returns(CreatePlayModeState());
        
        var successResult = new JObject
        {
            ["success"] = true,
            ["data"] = new JObject
            {
                ["action"] = "exiting_playmode"
            }
        };
        
        _mockUnityConnection.Setup(x => x.SendCommandAsync("force_update_editor", null, It.IsAny<CancellationToken>(), It.Is<bool>(b => b == false)))
            .ReturnsAsync(successResult);
        
        // Act - use very short timeout to force timeout
        var result = await _tool.ForceUpdateEditor(timeoutMilliseconds: 100);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.False);
        Assert.That(resultObj.error.ToString(), Does.Contain("Timeout waiting for Unity"));
    }
    
    [Test]
    public async Task ForceUpdateEditor_WhenOperationCancelled_ShouldReturnError()
    {
        // Arrange
        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.IsConnected).Returns(true);
        _mockStateConnection.Setup(x => x.CurrentUnityState).Returns(CreatePlayModeState());
        
        var successResult = new JObject
        {
            ["success"] = true,
            ["data"] = new JObject
            {
                ["action"] = "exiting_playmode"
            }
        };
        
        _mockUnityConnection.Setup(x => x.SendCommandAsync("force_update_editor", null, It.IsAny<CancellationToken>(), It.Is<bool>(b => b == false)))
            .ReturnsAsync(successResult);
        
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately
        
        // Act
        var result = await _tool.ForceUpdateEditor(cancellationToken: cts.Token);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.False);
        Assert.That(resultObj.error.ToString(), Does.Contain("Operation was cancelled"));
    }
    
    private static JObject CreateInitialState()
    {
        return new JObject
        {
            ["runmode"] = "PlayMode",
            ["context"] = "Running",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };
    }
    
    private static JObject CreateEditModeRunningState()
    {
        return new JObject
        {
            ["runmode"] = "EditMode_Scene",
            ["context"] = "Running",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };
    }
    
    private static JObject CreatePlayModeState()
    {
        return new JObject
        {
            ["runmode"] = "PlayMode",
            ["context"] = "Running",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };
    }
}
