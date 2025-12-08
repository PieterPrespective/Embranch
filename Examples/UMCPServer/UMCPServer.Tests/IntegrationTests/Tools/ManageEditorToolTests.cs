//using Microsoft.Extensions.Logging;
//using Moq;
//using Newtonsoft.Json.Linq;
//using UMCPServer.Services;
//using UMCPServer.Tools;
//using NUnit.Framework;

//namespace UMCPServer.Tests.IntegrationTests.Tools;

///// <summary>
///// Integration tests for ManageEditorTool server-side functionality.
///// Tests MCP interface, parameter validation, Unity connection, and response handling.
///// </summary>
//public class ManageEditorToolTests
//{
//    private readonly Mock<ILogger<ManageEditorTool>> _mockLogger;
//    private readonly Mock<UnityConnectionService> _mockUnityConnection;
//    private readonly ManageEditorTool _tool;

//    public ManageEditorToolTests()
//    {
//        _mockLogger = new Mock<ILogger<ManageEditorTool>>();
//        _mockUnityConnection = new Mock<UnityConnectionService>();
//        _tool = new ManageEditorTool(_mockLogger.Object, _mockUnityConnection.Object);
//    }

//    #region Parameter Validation Tests

//    [Test]
//    public async Task ManageEditor_ReturnsError_WhenActionIsNull()
//    {
//        // Act
//        var result = await _tool.ManageEditor(null);

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("action", StringComparison.OrdinalIgnoreCase) ?? false, Is.True);
//    }

//    [Test]
//    public async Task ManageEditor_ReturnsError_WhenActionIsEmpty()
//    {
//        // Act
//        var result = await _tool.ManageEditor("");

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("action", StringComparison.OrdinalIgnoreCase) ?? false, Is.True);
//    }

//    [Test]
//    public async Task ManageEditor_ReturnsError_WhenActionIsInvalid()
//    {
//        // Act
//        var result = await _tool.ManageEditor("invalid_action");

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("Invalid action") ?? false, Is.True);
//    }

//    [TestCase("add_tag")]
//    [TestCase("remove_tag")]
//    public async Task ManageEditor_ReturnsError_WhenTagNameRequiredButMissing(string action)
//    {
//        // Act
//        var result = await _tool.ManageEditor(action);

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("tagName") ?? false, Is.True);
//    }

//    [TestCase("add_layer")]
//    [TestCase("remove_layer")]
//    public async Task ManageEditor_ReturnsError_WhenLayerNameRequiredButMissing(string action)
//    {
//        // Act
//        var result = await _tool.ManageEditor(action);

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("layerName") ?? false, Is.True);
//    }

//    [Test]
//    public async Task ManageEditor_ReturnsError_WhenToolNameRequiredButMissing()
//    {
//        // Act
//        var result = await _tool.ManageEditor("set_active_tool");

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("toolName") ?? false, Is.True);
//    }

//    #endregion

//    #region Unity Connection Tests

//    [Test]
//    public async Task ManageEditor_ReturnsError_WhenUnityNotConnected()
//    {
//        // Arrange
//        _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
//        _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);

//        // Act
//        var result = await _tool.ManageEditor("get_state");

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>()?.Contains("Unity Editor is not running") ?? false, Is.True);
//    }

//    [Test]
//    public async Task ManageEditor_AttemptsConnection_WhenNotConnected()
//    {
//        // Arrange
//        _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
//        _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(true);
//        _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_editor", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                           .ReturnsAsync(JObject.Parse("{\"success\":true,\"data\":{}}"));

//        // Act
//        await _tool.ManageEditor("get_state");

//        // Assert
//        _mockUnityConnection.Verify(x => x.ConnectAsync(), Times.Once);
//    }

//    #endregion

//    #region Play Mode Action Tests

//    [TestCase("play")]
//    [TestCase("pause")]
//    [TestCase("stop")]
//    public async Task ManageEditor_HandlesPlayModeActions_Successfully(string action)
//    {
//        // Arrange
//        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//        var unityResponse = JObject.Parse($"{{\"success\":true,\"data\":{{\"message\":\"{action} mode operation completed\"}}}}");
//        _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_editor", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                           .ReturnsAsync(unityResponse);

//        // Act
//        var result = await _tool.ManageEditor(action);

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.True);
//        Assert.That(response["message"], Is.Not.Null);
//        Assert.That(response["editorState"], Is.Not.Null);
//    }

//    #endregion

//    #region Error Handling Tests

//    [Test]
//    public async Task ManageEditor_HandlesUnityError_Gracefully()
//    {
//        // Arrange
//        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//        var unityResponse = JObject.Parse("{\"success\":false,\"error\":\"Unity operation failed\"}");
//        _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_editor", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                           .ReturnsAsync(unityResponse);

//        // Act
//        var result = await _tool.ManageEditor("get_state");

//        // Assert
//        var response = JObject.FromObject(result);
//        Assert.That(response["success"]?.Value<bool>() ?? false, Is.False);
//        Assert.That(response["error"]?.Value<string>(), Is.EqualTo("Unity operation failed"));
//    }

//    #endregion
//}