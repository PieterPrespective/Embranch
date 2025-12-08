using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using UMCPServer.Tools;

namespace UMCPServer.Tests.UnitTests.Tools;

[TestFixture]
public class GetServerVersionToolTests
{
    private Mock<ILogger<GetServerVersionTool>> _mockLogger = null!;
    private GetServerVersionTool _tool = null!;
    
    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<GetServerVersionTool>>();
        _tool = new GetServerVersionTool(_mockLogger.Object);
    }
    
    [Test]
    public async Task GetServerVersion_ShouldReturnSuccess()
    {
        // Act
        var result = await _tool.GetServerVersion();
        
        // Assert
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        // Convert to dynamic to access properties easily
        dynamic resultObj = result;
        Assert.That(resultObj.success, Is.True, "Version request should be successful");
        Assert.That(resultObj.message, Is.EqualTo("Server version retrieved successfully"));
        
        // Check version object
        Assert.That(resultObj.version, Is.Not.Null);
        Assert.That(resultObj.version.informationalVersion, Is.Not.Null);
        Assert.That(resultObj.version.assemblyVersion, Is.Not.Null);
        Assert.That(resultObj.version.fileVersion, Is.Not.Null);
    }
    
    [Test]
    public void GetServerVersion_WhenExceptionOccurs_ShouldReturnError()
    {
        // Arrange - create a tool that will throw an exception when accessed
        var mockLoggerThrow = new Mock<ILogger<GetServerVersionTool>>();
        mockLoggerThrow.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws(new InvalidOperationException("Test exception"));
        
        var toolThatThrows = new ThrowingGetServerVersionTool(mockLoggerThrow.Object);
        
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            var result = await toolThatThrows.GetServerVersion();
            
            // Convert to dynamic to access properties easily
            dynamic resultObj = result;
            Assert.That(resultObj.success, Is.False, "Should indicate failure");
            Assert.That(resultObj.error, Is.Not.Null.Or.Empty, "Should have error message");
            Assert.That(resultObj.error.ToString(), Does.Contain("Failed to get server version"), "Error should describe the failure");
        });
    }
    
    /// <summary>
    /// A special implementation of GetServerVersionTool that throws exceptions for testing error handling
    /// </summary>
    private class ThrowingGetServerVersionTool : GetServerVersionTool
    {
        public ThrowingGetServerVersionTool(ILogger<GetServerVersionTool> logger) : base(logger)
        {
        }
        
        public override Task<object> GetServerVersion()
        {
            throw new InvalidOperationException("Test exception");
        }
    }
}