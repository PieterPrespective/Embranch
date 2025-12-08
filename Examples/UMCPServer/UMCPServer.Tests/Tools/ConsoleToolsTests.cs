using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMCPServer.Services;
using UMCPServer.Tools;

namespace UMCPServer.Tests.Tools
{
    [TestFixture]
    public class ConsoleToolsTests
    {
        private Mock<ILogger<ReadConsoleTool>> _mockReadConsoleLogger;
        private Mock<ILogger<MarkStartOfNewStepTool>> _mockMarkStepLogger;
        private Mock<ILogger<RequestStepLogsTool>> _mockRequestLogsLogger;
        private Mock<UnityConnectionService> _mockUnityConnection;
        
        [SetUp]
        public void SetUp()
        {
            _mockReadConsoleLogger = new Mock<ILogger<ReadConsoleTool>>();
            _mockMarkStepLogger = new Mock<ILogger<MarkStartOfNewStepTool>>();
            _mockRequestLogsLogger = new Mock<ILogger<RequestStepLogsTool>>();
            _mockUnityConnection = new Mock<UnityConnectionService>(
                new Mock<ILogger<UnityConnectionService>>().Object,
                Microsoft.Extensions.Options.Options.Create(new Models.ServerConfiguration())
            );
        }
        
        [Test]
        public async Task ReadConsoleTool_GetLogs_Success()
        {
            // Arrange
            var tool = new ReadConsoleTool(_mockReadConsoleLogger.Object, _mockUnityConnection.Object);
            
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            _mockUnityConnection.Setup(x => x.SendCommandAsync(
                    "read_console",
                    It.IsAny<JObject>(),
                    default,
                It.Is<bool>(b => b == false)))
                .ReturnsAsync(new JObject
                {
                    ["status"] = "success",
                    ["message"] = "Retrieved 2 log entries",
                    ["data"] = new JArray(
                        new JObject { ["type"] = "Log", ["message"] = "Test log 1" },
                        new JObject { ["type"] = "Warning", ["message"] = "Test warning" }
                    )
                });
            
            // Act
            var result = await tool.ReadConsole(
                action: "get",
                types: new[] { "log", "warning" },
                count: 10,
                filterText: null,
                format: "detailed",
                includeStacktrace: true);
            
            // Assert
            dynamic dynamicResult = result;
            Assert.That(dynamicResult.success, Is.True);
            Assert.That(dynamicResult.count, Is.EqualTo(2));
            Assert.That(dynamicResult.entries, Is.Not.Null);
        }
        
        [Test]
        public async Task ReadConsoleTool_ClearConsole_Success()
        {
            // Arrange
            var tool = new ReadConsoleTool(_mockReadConsoleLogger.Object, _mockUnityConnection.Object);
            
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            _mockUnityConnection.Setup(x => x.SendCommandAsync(
                    "read_console",
                    It.IsAny<JObject>(),
                    default,
                It.Is<bool>(b => b == false)))
                .ReturnsAsync(new JObject
                {
                    ["status"] = "success",
                    ["message"] = "Console cleared successfully"
                });
            
            // Act
            var result = await tool.ReadConsole(action: "clear");
            
            // Assert
            dynamic dynamicResult = result;
            Assert.That(dynamicResult.success, Is.True);
            Assert.That(dynamicResult.message.ToString(), Is.EqualTo("cleared"));
        }
        
        [Test]
        public async Task MarkStartOfNewStepTool_Success()
        {
            // Arrange
            var tool = new MarkStartOfNewStepTool(_mockMarkStepLogger.Object, _mockUnityConnection.Object);
            var stepName = "TestStep1";
            
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            _mockUnityConnection.Setup(x => x.SendCommandAsync(
                    "mark_start_of_new_step",
                    It.IsAny<JObject>(),
                    default,
                It.Is<bool>(b => b == false)))
                .ReturnsAsync(new JObject
                {
                    ["status"] = "success",
                    ["message"] = $"Successfully marked start of step '{stepName}'",
                    ["data"] = new JObject
                    {
                        ["stepName"] = stepName,
                        ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        ["markerMessage"] = $"[UMCP_STEP_START] Step: '{stepName}' | Started at: ..."
                    }
                });
            
            // Act
            var result = await tool.MarkStartOfNewStep(stepName);
            
            // Assert
            dynamic dynamicResult = result;

            Assert.That(dynamicResult.success, Is.True);
            Assert.That(dynamicResult.stepName.ToString(), Is.EqualTo(stepName));
            Assert.That(dynamicResult.timestamp, Is.Not.Null);
            Assert.That(dynamicResult.markerMessage, Is.Not.Null);
        }
        
        [Test]
        public async Task MarkStartOfNewStepTool_EmptyStepName_ReturnsError()
        {
            // Arrange
            var tool = new MarkStartOfNewStepTool(_mockMarkStepLogger.Object, _mockUnityConnection.Object);
            
            // Act
            var result = await tool.MarkStartOfNewStep("");
            
            // Assert
            dynamic dynamicResult = result;
            Assert.That(dynamicResult.success, Is.False);
            Assert.That(dynamicResult.error.ToString().ToLower(), Contains.Value("empty"));
        }
        
        [Test]
        public async Task RequestStepLogsTool_Success()
        {
            // Arrange
            var tool = new RequestStepLogsTool(_mockRequestLogsLogger.Object, _mockUnityConnection.Object);
            var stepName = "TestStep1";
            
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            _mockUnityConnection.Setup(x => x.SendCommandAsync(
                    "request_step_logs",
                    It.IsAny<JObject>(),
                    default, It.Is<bool>(b => b == false)))
                .ReturnsAsync(new JObject
                {
                    ["status"] = "success",
                    ["message"] = $"Retrieved 3 log entries for step '{stepName}'",
                    ["data"] = new JArray(
                        new JObject { ["type"] = "Log", ["message"] = "[UMCP_STEP_START] Step: 'TestStep1' ..." },
                        new JObject { ["type"] = "Log", ["message"] = "Processing step..." },
                        new JObject { ["type"] = "Warning", ["message"] = "Step completed with warnings" }
                    )
                });
            
            // Act
            var result = await tool.RequestStepLogs(
                stepName: stepName,
                includeStacktrace: true,
                format: "detailed");
            
            // Assert
            dynamic dynamicResult = result;
            Assert.That(dynamicResult.success, Is.True);

            Assert.That(dynamicResult.stepName.ToString(), Is.EqualTo(stepName));

            Assert.That(dynamicResult.count, Is.EqualTo(3));

            Assert.That(dynamicResult.entries, Is.Not.Null);
        }
        
        [Test]
        public async Task RequestStepLogsTool_NoStepFound_ReturnsError()
        {
            // Arrange
            var tool = new RequestStepLogsTool(_mockRequestLogsLogger.Object, _mockUnityConnection.Object);
            var stepName = "NonExistentStep";
            
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            _mockUnityConnection.Setup(x => x.SendCommandAsync(
                    "request_step_logs",
                    It.IsAny<JObject>(),
                    default, It.Is<bool>(b => b == false)))
                .ReturnsAsync(new JObject
                {
                    ["status"] = "error",
                    ["error"] = $"No start marker found for step '{stepName}'"
                });
            
            // Act
            var result = await tool.RequestStepLogs(stepName: stepName);
            
            // Assert
            dynamic dynamicResult = result;

            Assert.That(dynamicResult.success, Is.False);
            Assert.That(dynamicResult.error.ToString(), Contains.Substring("No start marker found for step"));
        }
        
        [Test]
        public async Task AllTools_UnityNotConnected_ReturnsError()
        {
            // Arrange
            _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
            _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);
            
            var readConsoleTool = new ReadConsoleTool(_mockReadConsoleLogger.Object, _mockUnityConnection.Object);
            var markStepTool = new MarkStartOfNewStepTool(_mockMarkStepLogger.Object, _mockUnityConnection.Object);
            var requestLogsTool = new RequestStepLogsTool(_mockRequestLogsLogger.Object, _mockUnityConnection.Object);
            
            // Act & Assert - ReadConsoleTool
            var readResult = await readConsoleTool.ReadConsole();
            dynamic readDynamic = readResult;
            Assert.That(readDynamic.success, Is.False);
            Assert.That(readDynamic.error.ToString(), Contains.Substring("Unity Editor is not running"));

            
            // Act & Assert - MarkStartOfNewStepTool
            var markResult = await markStepTool.MarkStartOfNewStep("TestStep");
            dynamic markDynamic = markResult;
            Assert.That(markDynamic.success, Is.False); 
            Assert.That(markDynamic.error.ToString(), Contains.Substring("Unity Editor is not running"));
            
            // Act & Assert - RequestStepLogsTool
            var requestResult = await requestLogsTool.RequestStepLogs("TestStep");
            dynamic requestDynamic = requestResult;

            Assert.That(requestDynamic.success, Is.False);
            Assert.That(requestDynamic.error.ToString(), Contains.Substring("Unity Editor is not running"));
        }
    }
}
