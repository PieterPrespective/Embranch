using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections;
using UMCPServer.Services;
using UMCPServer.Tests.IntegrationTests.Tools;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.UnityConnected
{
    /// <summary>
    /// Utility class containing harnas functions for running an integration test with an active Unity3D connection (full integration loop)
    /// </summary>
    internal class UnityConnectedTestUtility
    {
        /// <summary>
        /// Delegate ienumerator format for the actual integration test function
        /// </summary>
        /// <param name="_configuration">reference to the test configuration to use</param>
        /// <returns></returns>
        public delegate IEnumerator RunTestIENumerator(TestConfiguration _configuration);

        /// <summary>
        /// Model for the testconfiguration
        /// </summary>
        public class TestConfiguration
        {
            /// <summary>
            /// Name of this integration test
            /// </summary>
            public string Name = "UnnamedTest";

            /// <summary>
            /// Harnass to use on the Unity side
            /// </summary>
            public string SoughtHarnessName = "";

            /// <summary>
            /// Test to run after setting up the harnas at the unity side
            /// </summary>
            public required RunTestIENumerator TestAfterSetup;

            /// <summary>
            /// Whether to log the received TCP result string at reception (for debugging purposes)
            /// </summary>
            public bool LogResultAtReception = false;

            /// <summary>
            /// Timeout allowed for connecting with unity3d
            /// Default 10 seconds
            /// </summary>
            public long ConnectTimeoutS { get; set; } = 10; 

            /// <summary>
            /// Timeout allowed for forcing unity to update
            /// Default 20 seconds, asset reimport can take longer
            /// </summary>
            public long ForceUpdateTimeoutS { get; set; } = 10; 

            /// <summary>
            /// Gets or sets the maximum number of retry attempts allowed when attempting to recover a state.
            /// </summary>
            /// <remarks>This property determines how many times the system will retry an operation
            /// before giving up. Adjust this value based on the expected reliability of the operation or the desired
            /// tolerance for failures.</remarks>
            public int MaxStateRetries { get; set; } = 20; 

            /// <summary>
            /// Timeout to allow for retrieving the harnass options
            /// </summary>
            public long GetHarnessesTimeoutS { get; set; } = 10; // Default 10 seconds

            /// <summary>
            /// Gets or sets the interval, in seconds, at which the state is retrieved or updated.
            /// </summary>
            public float GetStateIntervalS { get; set; } = 0.5f; // Default 500ms

            /// <summary>
            /// Maximum timeout allowed for the current integration test state retrieval
            /// </summary>
            public long GetStateTimeoutS { get; set; } = 10; // Default 10 seconds

            /// <summary>
            /// Maximum timeout for calling the setup
            /// </summary>
            public long SetupTimeoutS { get; set; } = 10; // Default 10 seconds

            /// <summary>
            /// Maximum timeout for calling the cleanup
            /// </summary>
            public long CleanupTimeoutS { get; set; } = 10; // Default 10 seconds

            /// <summary>
            /// Reference to the overall service provider
            /// </summary>
            public ServiceProvider? ServiceProvider;

            /// <summary>
            /// Reference to the Unity connection
            /// </summary>
            public UnityConnectionService? UnityConnection;

            /// <summary>
            /// Reference to the state connection
            /// </summary>
            public UnityStateConnectionService? StateConnection;

            /// <summary>
            /// Reference to the Force update tool
            /// </summary>
            public ForceUpdateEditorTool? ForceUpdateTool;
        }

        /// <summary>
        /// Main test coroutine that runs a full integration test setup and cleanup
        /// </summary>
        /// <param name="_configuration">settings for this integration test</param>
        /// <returns>ienumerator</returns>
        public static IEnumerator RunUnityConnectedIntegrationTest(TestConfiguration _configuration)
        {
            Console.WriteLine($"=== Starting '{_configuration.Name}' Server-Side Integration Test ===");

            // Step 1: Ensure Unity is connected and responsive
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 01: Connecting to Unity...");
            yield return ConnectToUnity(_configuration);

            // Step 2: Force update editor to ensure compilation is up-to-date
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 02: Force updating Unity Editor...");
            yield return ForceUpdateEditor(_configuration);

            // Step 3: Get all harnesses to ensure DummyIntegrationTest exists
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 03: Getting all integration test harnesses...");
            yield return GetHarnesses(_configuration);

            // Step 4: Cleanup with polling
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 04: Cleaning up integration test...");
            yield return CleanupIntegrationTest(_configuration);

            // Step 5: Poll for cleanup completion
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 05: Polling for cleanup completion before test...");
            yield return PollForCleanupCompletion(_configuration);

            // Step 6: Get initial state
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 06: Getting initial integration test state...");
            yield return GetStateIsClean(_configuration, "initial");

            // Step 7: Setup DummyIntegrationTest with polling
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine($"Step 07: Setting up clientside'{_configuration.SoughtHarnessName}'...");
            yield return SetupIntegrationTest(_configuration);

            // Step 8: Poll for setup completion
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 08: Polling for setup completion...");
            yield return PollForSetupCompletion(_configuration);

            // Step 9: Verify expected logs were created
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 09: Actually running tests...");
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            yield return _configuration.TestAfterSetup(_configuration);
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");

            // Step 10: Cleanup with polling
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 10: Cleaning up integration test...");
            yield return CleanupIntegrationTest(_configuration);

            // Step 5: Poll for cleanup completion
            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 11: Polling for cleanup completion...");
            yield return PollForCleanupCompletion(_configuration);

            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Step 12: Getting final integration test state...");
            yield return GetStateIsClean(_configuration, "after");

            Console.WriteLine($"=== '{_configuration.Name}' Integration Test Completed Successfully ===");
        }

        /// <summary>
        /// Ienumerator to validate a working connection with the Unity3D UMCP Client
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator ConnectToUnity(TestConfiguration _configuration)
        {
            Assert.That(_configuration.UnityConnection, Is.Not.Null, "Unity connection service should be available");
            Assert.That(_configuration.StateConnection, Is.Not.Null, "Unity state connection service should be available");

            // Attempt connection
            var connected = _configuration.UnityConnection!.ConnectAsync();
            if (connected == null)
            {
                Assert.Fail("Failed to initiate connection to Unity");
            }

            var waitForConnection = Task.Delay(TimeSpan.FromSeconds(_configuration.ConnectTimeoutS));
            if (Task.WhenAny(connected, waitForConnection).Result == waitForConnection)
            {
                Assert.Fail("Connection to Unity timed out");
            }

            var stateConnected = _configuration.StateConnection!.ConnectAsync();
            waitForConnection = Task.Delay(TimeSpan.FromSeconds(_configuration.GetStateTimeoutS));
            if (Task.WhenAny(stateConnected, waitForConnection).Result == waitForConnection)
            {
                Assert.Fail("Connection to Unity state service timed out");
            }

            Assert.That(_configuration.UnityConnection.IsConnected, Is.True, "Unity connection should be established");

            Console.WriteLine("Successfully connected to Unity");
            yield return null;
        }

        /// <summary>
        /// Ienumerator to force an update of the Unity Editor
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator ForceUpdateEditor(TestConfiguration _configuration)
        {
            Assert.That(_configuration.ForceUpdateTool, Is.Not.Null, "Force update tool should be available");

            Task<object> forceUpdateTask = _configuration.ForceUpdateTool!.ForceUpdateEditor();
            if (forceUpdateTask == null)
            {
                Assert.Fail("Failed to initiate force update task");
            }
            var waitForUpdate = Task.Delay(TimeSpan.FromSeconds(_configuration.ForceUpdateTimeoutS));
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
        /// Function to get a listing with all available harnasses on the unity side
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator GetHarnesses(TestConfiguration _configuration)
        {
            var command = new JObject
            {
                ["action"] = "get_all_harnesses"
            };

            var getHarnasTask = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
            if (getHarnasTask == null)
            {
                Assert.Fail("Failed to initiate validate harnesses task");
            }
            var waitForValidation = Task.Delay(TimeSpan.FromSeconds(_configuration.GetHarnessesTimeoutS));
            if (Task.WhenAny(getHarnasTask, waitForValidation).Result == waitForValidation)
            {
                Assert.Fail("Validate harnesses task timed out");
            }

            JObject resultObjectClosure = getHarnasTask?.Result;
            Assert.That(resultObjectClosure, Is.Not.Null, "Validate harnesses should return a result");

            //Console.WriteLine("Validate harnesses result: {Result}", resultObjectClosure.ToString());

            var success = resultObjectClosure["success"]?.Value<bool>() ?? false;
            Assert.That(success, Is.True, $"Get all harnesses should succeed. Error: {resultObjectClosure?["error"] ?? "NULL"}");

            var data = resultObjectClosure["data"] as JObject;
            Assert.That(data, Is.Not.Null, "Get all harnesses should return data");

            var totalHarnesses = data!["totalHarnesses"]?.Value<int>() ?? 0;
            Assert.That(totalHarnesses, Is.GreaterThan(0), "Should find at least one integration test harness");

            var results = data["results"] as JArray;
            Assert.That(results, Is.Not.Null, "Should return harness results");

            // Look for DummyIntegrationTest specifically
            bool foundTest = false;
            foreach (JObject harnessResult in results!.Cast<JObject>())
            {
                var className = harnessResult["className"]?.ToString();
                if (className == _configuration.SoughtHarnessName)
                {
                    var isValidLocation = harnessResult["isValidLocation"]?.Value<bool>() ?? false;
                    Assert.That(isValidLocation, Is.True, $"{_configuration.SoughtHarnessName} should be in valid location");
                    foundTest = true;
                    break;
                }
            }

            Assert.That(foundTest, Is.True, $"Should find {_configuration.SoughtHarnessName} in the harness results");
            Console.WriteLine($"Found {totalHarnesses} integration test harnesses including {_configuration.SoughtHarnessName}");
            yield return null;
        }

        /// <summary>
        /// Function to determine if the Unity Integration testrunner is in a clean state
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <param name="_invocationMoment">indication of the timepoint in the test when invoked (before, after test) for loggin</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator GetStateIsClean(TestConfiguration _configuration, string _invocationMoment)
        {
            var command = new JObject
            {
                ["action"] = "get_integration_test_state"
            };

            var getStateCommand = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
            if (getStateCommand == null)
            {
                Assert.Fail("Failed to initiate get state command");
            }
            var waitForState = Task.Delay(TimeSpan.FromSeconds(_configuration.GetStateTimeoutS));
            if (Task.WhenAny(getStateCommand, waitForState).Result == waitForState)
            {
                Assert.Fail("Get state command timed out");
            }
            var result = getStateCommand.Result;
            Assert.That(result, Is.Not.Null, "Get state should return a result");

            var success = result?["success"]?.Value<bool>() ?? false;
            Assert.That(success, Is.True, $"Get state should succeed. Error: {result?["error"] ?? "NULL"}");

            var data = result["data"] as JObject;
            Assert.That(data, Is.Not.Null, "Get state should return data");

            var currentState = data!["currentTestState"]?.ToString();
            Console.WriteLine($"{_invocationMoment} integration test state: {currentState}");

            // Should typically be Clean initially
            Assert.That(currentState, Is.EqualTo("Clean"), $"{_invocationMoment} state should be Clean");
            yield return null;
        }

        /// <summary>
        /// Ienumerator that calls for a setup of an integration test at the unity side
        /// </summary>
        /// <param name="_configuration">reference to a test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator SetupIntegrationTest(TestConfiguration _configuration)
        {
            var command = new JObject
            {
                ["action"] = "setup",
                ["harnassClass"] = _configuration.SoughtHarnessName
            };

            var setupTestcommand = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
            if (setupTestcommand == null)
            {
                Assert.Fail("Failed to initiate setup command");
            }
            var waitForSetup = Task.Delay(TimeSpan.FromSeconds(_configuration.SetupTimeoutS));
            if (Task.WhenAny(setupTestcommand, waitForSetup).Result == waitForSetup)
            {
                Assert.Fail("Setup command timed out");
            }
            var result = setupTestcommand.Result;
            Assert.That(result, Is.Not.Null, "Setup should return a result");

            var success = result?["success"]?.Value<bool>() ?? false;
            Assert.That(success, Is.True, $"Setup should succeed. Error: {result?["error"] ?? "NULL"}");

            var data = result?["data"] as JObject;
            Assert.That(data, Is.Not.Null, "Setup should return data");

            var harnassClass = data!["harnassClass"]?.ToString();
            Assert.That(harnassClass, Is.EqualTo(_configuration.SoughtHarnessName), "Should confirm harness class");

            var state = data["state"]?.ToString();
            Assert.That(state, Is.EqualTo("SettingUp"), "Should be in SettingUp state after initiation");

            Console.WriteLine("Integration test setup initiated successfully");
            yield return null;
        }

        /// <summary>
        /// Function to poll the unity client for the current test setup state
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator PollForSetupCompletion(TestConfiguration _configuration)
        {
            var command = new JObject
            {
                ["action"] = "get_integration_test_state"
            };

            bool setupCompleted = false;
            int maxRetries = _configuration.MaxStateRetries; // 10 seconds with 500ms intervals
            int retryCount = 0;

            while (!setupCompleted && retryCount < maxRetries)
            {
                yield return new WaitForSeconds(0.5f); // Wait 500ms between polls
                retryCount++;
                Task<JObject?>? getStateCommand = null;
                try
                {
                    getStateCommand = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
                }
                // Prevent the occasional ObjectDisposedException from breaking the loop
                catch
                {
                    Console.WriteLine("Failed to setup getTestStatecommand!");
                    continue;
                } 
                
                if (getStateCommand == null)
                {
                    Assert.Fail("Failed to initiate poll command");
                }

                var waitForPoll = Task.Delay(TimeSpan.FromSeconds(_configuration.GetStateTimeoutS));
                if (Task.WhenAny(getStateCommand!, waitForPoll).Result == waitForPoll)
                {
                    Assert.Fail("Poll command timed out");
                }

                JObject? result = null;
                try
                {
                    result = getStateCommand!.Result;
                }
                // Prevent the occasional ObjectDisposedException from breaking the loop
                catch
                {
                    Console.WriteLine("Failed to get result!");
                    continue;
                }

                Assert.That(result, Is.Not.Null, "Poll state should return a result");

                var success = result!["success"]?.Value<bool>() ?? false;
                Assert.That(success, Is.True, $"Poll state should succeed. Error: {result?["error"] ?? "NULL"}");

                var data = result["data"] as JObject;
                Assert.That(data, Is.Not.Null, "Poll state should return data");

                var currentState = data!["currentTestState"]?.ToString();
                Console.WriteLine($"Polling setup completion: Current state = {currentState} (attempt {retryCount}/{maxRetries})");

                if (currentState == "SetUp")
                {
                    setupCompleted = true;
                    Console.WriteLine("Setup completed successfully!");
                }
                else if (currentState == "Clean")
                {
                    var lastError = data["lastError"]?.ToString();
                    Assert.Fail($"Setup failed and returned to Clean state. Error: {lastError}");
                }
            }

            Assert.That(setupCompleted, Is.True, $"Setup should complete within {maxRetries * 0.5f} seconds");
            yield return null;
        }

        /// <summary>
        /// Function to call for an integration test cleanup
        /// </summary>
        /// <param name="_configuration">test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator CleanupIntegrationTest(TestConfiguration _configuration)
        {
            var command = new JObject
            {
                ["action"] = "cleanup"
            };

            var cleanupCommand = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
            if (cleanupCommand == null)
            {
                Assert.Fail("Failed to initiate cleanup command");
            }
            var waitForCleanup = Task.Delay(TimeSpan.FromSeconds(_configuration.CleanupTimeoutS));
            if (Task.WhenAny(cleanupCommand!, waitForCleanup).Result == waitForCleanup)
            {
                Assert.Fail("Cleanup command timed out");
            }
            var result = cleanupCommand!.Result;
            Assert.That(result, Is.Not.Null, "Cleanup should return a result");

            var success = result["success"]?.Value<bool>() ?? false;
            Assert.That(success, Is.True, $"Cleanup should succeed. Error: {result?["error"] ?? "NULL"}");

            Console.WriteLine("Integration test cleanup initiated successfully");
            yield return null;
        }

        /// <summary>
        /// Function to poll the unity client for completion of the test cleanup
        /// </summary>
        /// <param name="_configuration">reference to the test configuration</param>
        /// <returns>ienumerator</returns>
        private static IEnumerator PollForCleanupCompletion(TestConfiguration _configuration)
        {
            var command = new JObject
            {
                ["action"] = "get_integration_test_state"
            };

            bool cleanupCompleted = false;
            int maxRetries = _configuration.MaxStateRetries; // 10 seconds with 500ms intervals
            int retryCount = 0;

            while (!cleanupCompleted && retryCount < maxRetries)
            {
                yield return new WaitForSeconds(_configuration.GetStateIntervalS); // Wait 500ms between polls
                retryCount++;

                Task<JObject?> resultCommand = null;
                try
                    {
                    resultCommand = _configuration.UnityConnection!.SendCommandAsync("HandleManageIntegrationTests", command, _logResult: _configuration.LogResultAtReception);
                }
                catch (ObjectDisposedException)
                {
                    // If the connection was disposed, we can skip this iteration
                    Console.WriteLine("Failed to setup getTestStatecommand!");
                    continue;
                }

                
                if (resultCommand == null)
                {
                    Assert.Fail("Failed to initiate poll command for cleanup");
                }
                var waitForPoll = Task.Delay(TimeSpan.FromSeconds(_configuration.GetStateTimeoutS));
                if (Task.WhenAny(resultCommand!, waitForPoll).Result == waitForPoll)
                {
                    Assert.Fail("Poll command for cleanup timed out");
                }

                JObject? result = null;   
                try
                {
                    result = resultCommand!.Result;
                }
                catch
                {
                    // Prevent the occasional ObjectDisposedException from breaking the loop
                    Console.WriteLine("Failed to get result!");
                    continue;
                }

                Assert.That(result, Is.Not.Null, "Poll cleanup state should return a result");

                var success = result["success"]?.Value<bool>() ?? false;
                Assert.That(success, Is.True, $"Poll cleanup state should succeed. Error: {result?["error"] ?? "NULL"}");

                var data = result["data"] as JObject;
                Assert.That(data, Is.Not.Null, "Poll cleanup state should return data");

                var currentState = data!["currentTestState"]?.ToString();
                Console.WriteLine($"Polling cleanup completion: Current state = {currentState} (attempt {retryCount}/{maxRetries})");

                if (currentState == "Clean")
                {
                    cleanupCompleted = true;
                    Console.WriteLine("Cleanup completed successfully!");
                }
            }

            Assert.That(cleanupCompleted, Is.True, $"Cleanup should complete within {maxRetries * 0.5f} seconds");
            yield return null;
        }

    }

    public class WaitForSeconds : YieldInstruction
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private readonly float _seconds;

        public WaitForSeconds(float seconds)
        {
            _seconds = seconds;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        }

        public override bool IsDone => _stopwatch.ElapsedMilliseconds >= (_seconds * 1000);
    }
}
