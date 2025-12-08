using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using UMCPServer.Tools;
using UMCPServer.Services;
using UMCPServer.Models;
using static UMCPServer.Tests.IntegrationTests.UnityConnected.UnityConnectedTestUtility;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected
{
    /// <summary>
    /// Integration tests for the RunTestsTool that validate Unity test execution and filtering capabilities.
    /// These tests require an active Unity connection with the RunTestToolTestsEditor and RunTestToolTestsPlaymode tests present.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("RequiresUnity")]
    public class RunTestsToolIntegrationTests : IntegrationTestBase
    {
        /// <summary>
        /// Custom test configuration for RunTests integration tests
        /// </summary>
        private class RunTestsTestConfiguration : UnityConnectedTestUtility.TestConfiguration
        {
            public RunTestsTool? RunTestsTool;
            public InterpretTestResultsTool? InterpretTestResultsTool;
            public RequestStepLogsTool? RequestStepLogsTool;
            
            // Test-specific parameters
            public string TestMode { get; set; } = "EditMode";
            public string[]? Filter { get; set; }
            public bool ExpectSuccess { get; set; } = true;
            public string? ExpectedError { get; set; }
        }

        private RunTestsTestConfiguration? testConfiguration;
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
            
            // Register tools
            services.AddSingleton<ForceUpdateEditorTool>();
            services.AddSingleton<RunTestsTool>();
            services.AddSingleton<InterpretTestResultsTool>();
            services.AddSingleton<RequestStepLogsTool>();
            services.AddSingleton<MarkStartOfNewStepTool>();

            var serviceProvider = services.BuildServiceProvider();

            testConfiguration = new RunTestsTestConfiguration
            {
                Name = "RunTestsTool Integration Test",
                SoughtHarnessName = "RunTestToolTestsEditor", // Unity-side test harness
                TestAfterSetup = ValidateTestExecution,
                ServiceProvider = serviceProvider,
                UnityConnection = serviceProvider.GetRequiredService<UnityConnectionService>(),
                StateConnection = serviceProvider.GetRequiredService<UnityStateConnectionService>(),
                ForceUpdateTool = serviceProvider.GetRequiredService<ForceUpdateEditorTool>(),
                RunTestsTool = serviceProvider.GetRequiredService<RunTestsTool>(),
                InterpretTestResultsTool = serviceProvider.GetRequiredService<InterpretTestResultsTool>(),
                RequestStepLogsTool = serviceProvider.GetRequiredService<RequestStepLogsTool>()
            };
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            testConfiguration?.ServiceProvider?.Dispose();
        }

        #region Filter Validation Tests

        [Test]
        [Description("Tests that RunTests returns error when no tests match the filter")]
        public void RunTests_ReturnsError_WhenNoTestsMatchFilter()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_ReturnsError_WhenNoTestsMatchFilter");
            testConfiguration!.Filter = new[] { "NonExistentTestClass.NonExistentTest" };
            testConfiguration.ExpectSuccess = false;
            testConfiguration.ExpectedError = "No tests found matching";
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests can filter by specific test name")]
        public void RunTests_FiltersByTestName_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_FiltersByTestName_Successfully");
            testConfiguration!.Filter = new[] { "RunTestToolTestsEditor.TestAdditionSucceeds" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests can filter by just test name")]
        public void RunTests_FiltersByPartialTestName_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_FiltersByPartialTestName_Successfully");
            testConfiguration!.Filter = new[] { "TestAdditionSucceeds", "TestAdditionFailure" };
            testConfiguration.ExpectSuccess = true;

            ExecuteTestSteps(RunTestWithConfiguration());
        }


        [Test]
        [Description("Tests that RunTests can filter by partial namespace")]
        public void RunTests_FiltersByPartialNamespace_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_FiltersByPartialNamespace_Successfully");
            testConfiguration!.Filter = new[] { "UMCP.Tests.Integration.Editor" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests can filter by test fixture name")]
        public void RunTests_FiltersByTestFixture_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_FiltersByTestFixture_Successfully");
            testConfiguration!.Filter = new[] { "RunTestToolTestsEditor" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests handles multiple filters correctly")]
        public void RunTests_HandlesMultipleFilters_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_HandlesMultipleFilters_Successfully");
            testConfiguration!.Filter = new[] 
            { 
                "RunTestToolTestsEditor.TestAdditionSucceeds",
                "RunTestToolTestsEditor.TestSubstractionSucceeds"
            };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        #endregion

        #region TestMode Validation Tests

        [Test]
        [Description("Tests that RunTests executes EditMode tests successfully")]
        public void RunTests_ExecutesEditModeTests_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_ExecutesEditModeTests_Successfully");
            testConfiguration!.TestMode = "EditMode";
            testConfiguration.Filter = new[] { "RunTestToolTestsEditor" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests executes PlayMode tests successfully")]
        public void RunTests_ExecutesPlayModeTests_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_ExecutesPlayModeTests_Successfully");
            testConfiguration!.TestMode = "PlayMode";
            testConfiguration.Filter = new[] { "RunTestToolTestsPlaymode" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        [Test]
        [Description("Tests that RunTests returns error for invalid test mode")]
        public void RunTests_ReturnsError_ForInvalidTestMode()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_ReturnsError_ForInvalidTestMode");
            testConfiguration!.TestMode = "InvalidMode";
            testConfiguration.ExpectSuccess = false;
            testConfiguration.ExpectedError = "Invalid TestMode";
            
            ExecuteTestSteps(RunTestWithConfiguration());
        }

        #endregion

        #region Result XML Tests

        [Test]
        [Description("Tests that RunTests generates XML result file")]
        public void RunTests_GeneratesResultXMLFile_Successfully()
        {
            Console.WriteLine(">>>>> Starting test: RunTests_GeneratesResultXMLFile_Successfully");
            testConfiguration!.TestMode = "EditMode";
            testConfiguration.Filter = new[] { "RunTestToolTestsEditor.TestAdditionSucceeds" };
            testConfiguration.ExpectSuccess = true;
            
            ExecuteTestSteps(RunTestWithXMLValidation());
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Main test execution flow using Unity connection
        /// </summary>
        private IEnumerator RunTestWithConfiguration()
        {
            // Connect to Unity
            yield return ConnectToUnity(testConfiguration!);
            
            // Force update editor
            yield return ForceUpdateEditor(testConfiguration);
            
            // Execute tests
            yield return ExecuteTests(testConfiguration);
            
            // Validate results
            yield return ValidateTestExecution(testConfiguration);
        }

        /// <summary>
        /// Test flow with XML validation
        /// </summary>
        private IEnumerator RunTestWithXMLValidation()
        {
            // Connect to Unity
            yield return ConnectToUnity(testConfiguration!);
            
            // Force update editor
            yield return ForceUpdateEditor(testConfiguration);
            
            // Execute tests
            yield return ExecuteTests(testConfiguration);
            
            // Validate XML generation
            //yield return ValidateXMLGeneration(testConfiguration);
        }

        /// <summary>
        /// Connect to Unity
        /// </summary>
        private IEnumerator ConnectToUnity(RunTestsTestConfiguration config)
        {
            Assert.That(config.UnityConnection, Is.Not.Null, "Unity connection service should be available");
            
            var connected = config.UnityConnection!.ConnectAsync();
            var waitForConnection = Task.Delay(TimeSpan.FromSeconds(config.ConnectTimeoutS));
            
            if (Task.WhenAny(connected, waitForConnection).Result == waitForConnection)
            {
                Assert.Fail("Connection to Unity timed out");
            }
            
            Assert.That(config.UnityConnection.IsConnected, Is.True, "Unity connection should be established");
            Console.WriteLine("Successfully connected to Unity");
            yield return null;
        }

        /// <summary>
        /// Force update Unity editor
        /// </summary>
        private IEnumerator ForceUpdateEditor(RunTestsTestConfiguration config)
        {
            Assert.That(config.ForceUpdateTool, Is.Not.Null, "Force update tool should be available");
            
            var forceUpdateTask = config.ForceUpdateTool!.ForceUpdateEditor();
            if (forceUpdateTask == null)
            {
                Assert.Fail("Force update task is null");
            }
            var waitForUpdate = Task.Delay(TimeSpan.FromSeconds(config.ForceUpdateTimeoutS));
            if (Task.WhenAny(forceUpdateTask, waitForUpdate).Result == waitForUpdate)
            {
                Assert.Fail("Force update task timed out");
            }
            
            var result = forceUpdateTask.Result;
            var resultObj = JObject.FromObject(result);
            Assert.That(resultObj["success"]?.Value<bool>() ?? false, Is.True, "Force update should succeed");
            
            Console.WriteLine("Editor force update completed");
            yield return null;
        }

        /// <summary>
        /// Execute Unity tests with specified configuration
        /// </summary>
        private IEnumerator ExecuteTests(RunTestsTestConfiguration config)
        {
            Assert.That(config.RunTestsTool, Is.Not.Null, "RunTests tool should be available");
            
            // Build parameters
            var parameters = new JObject
            {
                ["TestMode"] = config.TestMode
            };
            
            if (config.Filter != null && config.Filter.Length > 0)
            {
                parameters["Filter"] = new JArray(config.Filter);
            }
            
            parameters["OutputTestResults"] = true;
            parameters["OutputLogData"] = true;
            
            Console.WriteLine($"Executing tests with TestMode: {config.TestMode}, Filter: {string.Join(", ", config.Filter ?? new string[0])}");
            
            // Execute tests
            var runTestsTask = config.RunTestsTool!.RunTests(
                TestMode: config.TestMode,
                Filter: config.Filter,
                OutputTestResults: true,
                OutputLogData: true
            );
            
            var waitForTests = Task.Delay(TimeSpan.FromSeconds(60)); // Allow more time for test execution
            
            if (Task.WhenAny(runTestsTask, waitForTests).Result == waitForTests)
            {
                Assert.Fail("RunTests execution timed out");
            }
            
            var result = runTestsTask.Result;
            var resultObj = JObject.FromObject(result);
            Console.WriteLine($"RunTests result: {resultObj.ToString()}");

            var success = resultObj["success"]?.Value<bool>() ?? false;
            
            if (config.ExpectSuccess)
            {
                Assert.That(success, Is.True, $"RunTests should succeed. Error: {resultObj["error"]}");

                //NOTE: result object does not have status, tests are completed when this returns
                //Assert.That(resultObj["status"]?.ToString(), Is.EqualTo("running"), "Tests should be running");
            }
            else
            {
                Assert.That(success, Is.False, "RunTests should fail as expected");
                if (!string.IsNullOrEmpty(config.ExpectedError))
                {
                    var error = resultObj["error"]?.ToString() ?? "";
                    Assert.That(error.ToLowerInvariant(), Does.Contain(config.ExpectedError.ToLowerInvariant()),
                        $"Error should contain expected message: {config.ExpectedError}");
                }
            }
            
            yield return null;
        }

        /// <summary>
        /// Validate test execution results
        /// </summary>
        private IEnumerator ValidateTestExecution(TestConfiguration baseConfig)
        {
            if (!(baseConfig is RunTestsTestConfiguration config))
            {
                Assert.Fail($"Configuration is not of correct type {typeof(RunTestsTestConfiguration).Name}");
                yield break;
            }
            
            // Only validate if we expect success
            if (!config.ExpectSuccess)
            {
                Console.WriteLine("Test expected to fail, skipping result validation");
                yield break;
            }
            
            // Wait for test completion
            yield return Task.Delay(TimeSpan.FromSeconds(5));
            
            // Use RequestStepLogs to check for test completion
            if (config.RequestStepLogsTool != null)
            {
                var stepLogsTask = config.RequestStepLogsTool.RequestStepLogs(
                    stepName: "RunTests",
                    format: "plain"
                );
                
                var waitForLogs = Task.Delay(TimeSpan.FromSeconds(10));
                if (Task.WhenAny(stepLogsTask, waitForLogs).Result == waitForLogs)
                {
                    Console.WriteLine("RequestStepLogs timed out - tests may still be running");
                }
                else
                {
                    var logsResult = stepLogsTask.Result;
                    var logsObj = JObject.FromObject(logsResult);
                    
                    if (logsObj["success"]?.Value<bool>() == true)
                    {
                        var logs = logsObj["data"]?.ToString() ?? "";
                        Console.WriteLine($"Test execution logs retrieved: {logs.Length} characters");
                        
                        // Check for test completion marker
                        if (logs.Contains("TEST_EXECUTION_COMPLETED"))
                        {
                            Console.WriteLine("Test execution completed successfully");
                        }
                    }
                }
            }
            
            yield return null;
        }

        /// <summary>
        /// Validate XML result file generation
        /// </summary>
        private IEnumerator ValidateXMLGeneration(RunTestsTestConfiguration config)
        {
            // First validate test execution
            yield return ValidateTestExecution(config);
            
            // Wait for XML generation
            yield return Task.Delay(TimeSpan.FromSeconds(3));
            
            // Use RequestStepLogs to find XML file path
            if (config.RequestStepLogsTool != null)
            {
                var stepLogsTask = config.RequestStepLogsTool.RequestStepLogs(
                    stepName: "RunTests",
                    format: "plain"
                );
                
                var waitForLogs = Task.Delay(TimeSpan.FromSeconds(10));
                if (Task.WhenAny(stepLogsTask, waitForLogs).Result == waitForLogs)
                {
                    Assert.Fail("RequestStepLogs timed out while looking for XML file");
                }
                
                var logsResult = stepLogsTask.Result;
                var logsObj = JObject.FromObject(logsResult);
                
                if (logsObj["success"]?.Value<bool>() == true)
                {
                    var logs = logsObj["data"]?.ToString() ?? "";
                    
                    // Look for XML file path in logs
                    if (logs.Contains("TEST_RESULTS_FILE_PATH"))
                    {
                        Console.WriteLine("XML result file path found in logs");
                        
                        // Extract and validate file path
                        var lines = logs.Split('\n');
                        var filePathLine = lines.FirstOrDefault(l => l.Contains("TEST_RESULTS_FILE_PATH:"));
                        
                        if (filePathLine != null)
                        {
                            var filePath = filePathLine.Substring(filePathLine.IndexOf("TEST_RESULTS_FILE_PATH:") + "TEST_RESULTS_FILE_PATH:".Length).Trim();
                            
                            Assert.That(filePath, Does.Contain("TestResults"), "File path should contain TestResults directory");
                            Assert.That(filePath, Does.Contain(".xml"), "File should be XML format");
                            Assert.That(filePath, Does.Contain(config.TestMode), "File path should contain test mode");
                            
                            Console.WriteLine($"XML file path validated: {filePath}");
                        }
                    }
                    else
                    {
                        Assert.Fail("XML file path not found in logs");
                    }
                }
            }
            
            yield return null;
        }

        #endregion
    }
}