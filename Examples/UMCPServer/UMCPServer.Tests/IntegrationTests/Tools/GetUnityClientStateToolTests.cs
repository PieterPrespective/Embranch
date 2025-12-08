using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools
{
    //[TestFixture]
    //public class GetUnityClientStateToolTests : IntegrationTestBase
    //{
    //    private GetUnityClientStateTool _tool;
    //    private Mock<ILogger<GetUnityClientStateTool>> _mockLogger;
    //    private Mock<UnityConnectionService> _mockUnityConnection;

    //    [SetUp]
    //    public void Setup()
    //    {
    //        _mockLogger = new Mock<ILogger<GetUnityClientStateTool>>();
    //        _mockUnityConnection = new Mock<UnityConnectionService>(
    //            Mock.Of<ILogger<UnityConnectionService>>(),
    //            Microsoft.Extensions.Options.Options.Create(new Models.ServerConfiguration())
    //        );
            
    //        _tool = new GetUnityClientStateTool(_mockLogger.Object, _mockUnityConnection.Object);
    //    }

    //    [Test]
    //    public async Task GetUnityClientState_WhenUnityNotConnected_ReturnsError()
    //    {
    //        // Arrange
    //        _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
    //        _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);

    //        // Act
    //        var result = await _tool.GetUnityClientState();

    //        // Assert
    //        dynamic dynamicResult = result;
    //        Assert.That(dynamicResult.success, Is.False);
    //        Assert.That(dynamicResult.error, Does.Contain("Unity Editor is not running"));
    //    }

    //    [Test]
    //    public async Task GetUnityClientState_WithCachedState_ReturnsCachedState()
    //    {
    //        // Arrange
    //        var cachedState = new JObject
    //        {
    //            ["runmode"] = "EditMode_Scene",
    //            ["context"] = "Running",
    //            ["canModifyProjectFiles"] = true,
    //            ["isEditorResponsive"] = true,
    //            ["timestamp"] = DateTime.UtcNow.ToString("o")
    //        };

    //        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
    //        _mockUnityConnection.Setup(x => x.CurrentUnityState).Returns(cachedState);

    //        // Act
    //        var result = await _tool.GetUnityClientState();

    //        // Assert
    //        dynamic dynamicResult = result;
    //        Assert.That(dynamicResult.success, Is.True);
    //        Assert.That(dynamicResult.runmode, Is.EqualTo("EditMode_Scene"));
    //        Assert.That(dynamicResult.context, Is.EqualTo("Running"));
    //        Assert.That(dynamicResult.canModifyProjectFiles, Is.True);
    //        Assert.That(dynamicResult.isEditorResponsive, Is.True);
    //    }

    //    [Test]
    //    public async Task GetUnityClientState_WithoutCachedState_RefreshesState()
    //    {
    //        // Arrange
    //        var refreshedState = new JObject
    //        {
    //            ["runmode"] = "PlayMode",
    //            ["context"] = "Switching",
    //            ["canModifyProjectFiles"] = false,
    //            ["isEditorResponsive"] = false,
    //            ["timestamp"] = DateTime.UtcNow.ToString("o")
    //        };

    //        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
    //        _mockUnityConnection.SetupSequence(x => x.CurrentUnityState)
    //            .Returns((JObject)null)  // First call returns null
    //            .Returns(refreshedState); // After refresh returns the state
            
    //        _mockUnityConnection.Setup(x => x.RefreshUnityState()).Returns(Task.CompletedTask);

    //        // Act
    //        var result = await _tool.GetUnityClientState();

    //        // Assert
    //        dynamic dynamicResult = result;
    //        Assert.That(dynamicResult.success, Is.True);
    //        Assert.That(dynamicResult.runmode, Is.EqualTo("PlayMode"));
    //        Assert.That(dynamicResult.context, Is.EqualTo("Switching"));
            
    //        // Verify RefreshUnityState was called
    //        _mockUnityConnection.Verify(x => x.RefreshUnityState(), Times.Once);
    //    }

    //    [Test]
    //    public async Task GetUnityClientState_WhenExceptionOccurs_ReturnsError()
    //    {
    //        // Arrange
    //        _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
    //        _mockUnityConnection.Setup(x => x.CurrentUnityState).Throws(new Exception("Test exception"));

    //        // Act
    //        var result = await _tool.GetUnityClientState();

    //        // Assert
    //        dynamic dynamicResult = result;
    //        Assert.That(dynamicResult.success, Is.False);
    //        Assert.That(dynamicResult.error, Does.Contain("Test exception"));
    //    }
    //}
}
