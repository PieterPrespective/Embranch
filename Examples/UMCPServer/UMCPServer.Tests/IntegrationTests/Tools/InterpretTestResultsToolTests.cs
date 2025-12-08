using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using UMCPServer.Tools;
using UMCPServer.Services;

namespace UMCPServer.Tests.IntegrationTests.Tools;

/// <summary>
/// Integration tests for the InterpretTestResults tool using the actual Unity test results XML file
/// </summary>
[TestFixture]
public class InterpretTestResultsToolTests : IntegrationTestBase
{
    private Mock<ILogger<InterpretTestResultsTool>> _mockLogger = null!;
    private Mock<UnityConnectionService> _mockUnityConnection = null!;
    private InterpretTestResultsTool _tool = null!;
    private string _testXmlPath = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockLogger = new Mock<ILogger<InterpretTestResultsTool>>();
        _mockUnityConnection = new Mock<UnityConnectionService>();
        _tool = new InterpretTestResultsTool(_mockLogger.Object, _mockUnityConnection.Object);
        
        // Set up the test XML file path - use absolute path to the project TestResults directory
        var projectRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
        _testXmlPath = Path.Combine(projectRoot, "TestResults", 
            "TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml");
    }
    
    [Test]
    public void InterpretTestResults_FindFailedTests_ShouldReturnTestFaultyAndFailTestFunction()
    {
        // Execute the multi-step test
        ExecuteTestSteps(FindFailedTestsSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void InterpretTestResults_FindSpecificTest_ShouldReturnTestLoadDXFBundleShapeDataWithAreaOutput()
    {
        // Execute the multi-step test
        ExecuteTestSteps(FindSpecificTestWithAreaOutputSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void InterpretTestResults_SearchInOutput_ShouldFindAreaValue()
    {
        // Execute the multi-step test
        ExecuteTestSteps(SearchInOutputSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void InterpretTestResults_GeneralStatistics_ShouldReturnCorrectOverallStats()
    {
        // Execute the multi-step test
        ExecuteTestSteps(GeneralStatisticsSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    /// <summary>
    /// Test steps for finding failed tests and verifying their stack traces
    /// </summary>
    private IEnumerator FindFailedTestsSteps()
    {
        // Step 1: Verify test XML file exists
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying test XML file exists");
        Assert.That(File.Exists(_testXmlPath), Is.True, $"Test XML file should exist at: {_testXmlPath}");
        yield return null;
        
        // Step 2: Call InterpretTestResults with failedOnly = true and includeStackTraces = true
        Console.WriteLine($"Step {CurrentStep + 1}: Calling InterpretTestResults for failed tests with stack traces");
        var task = _tool.InterpretTestResults(_testXmlPath, failedOnly: true, includeStackTraces: true);
        yield return task;
        
        // Step 3: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.testCases, Is.Not.Null, "Test cases should not be null");
        yield return null;
        
        // Step 4: Verify we found exactly 2 failed tests
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying failed test count");
        var testCases = resultObj.testCases as IList<object>;
        Assert.That(testCases, Is.Not.Null, "Test cases should be a list");
        Assert.That(testCases.Count, Is.EqualTo(2), "Should find exactly 2 failed tests");
        yield return null;
        
        // Step 5: Verify TestFaulty test is present with correct stack trace
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying TestFaulty test details");
        var testFaultyFound = false;
        foreach (var testCase in testCases)
        {
            var testObj = testCase as dynamic;
            if (testObj.name == "TestFaulty")
            {
                testFaultyFound = true;
                Assert.That(testObj.result, Is.EqualTo("Failed"), "TestFaulty should be marked as Failed");
                Assert.That(testObj.fullName, Does.Contain("SimpleEditModeTests.TestFaulty"), "Full name should contain SimpleEditModeTests.TestFaulty");
                Assert.That(testObj.failure, Is.Not.Null, "Failure info should not be null");
                Assert.That(testObj.failure.message, Does.Contain("This test is intentionally faulty"), "Failure message should contain expected text");
                Assert.That(testObj.failure.stackTrace, Does.Contain("SimpleEditModeTests.cs:35"), "Stack trace should contain file and line number");
                break;
            }
        }
        Assert.That(testFaultyFound, Is.True, "TestFaulty test should be found");
        yield return null;
        
        // Step 6: Verify FailTestFunction test is present with correct stack trace
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying FailTestFunction test details");
        var failTestFunctionFound = false;
        foreach (var testCase in testCases)
        {
            var testObj = testCase as dynamic;
            if (testObj.name == "FailTestFunction")
            {
                failTestFunctionFound = true;
                Assert.That(testObj.result, Is.EqualTo("Failed"), "FailTestFunction should be marked as Failed");
                Assert.That(testObj.fullName, Does.Contain("FailTest.FailTestFunction"), "Full name should contain FailTest.FailTestFunction");
                Assert.That(testObj.failure, Is.Not.Null, "Failure info should not be null");
                Assert.That(testObj.failure.message, Does.Contain("This test is designed to fail"), "Failure message should contain expected text");
                Assert.That(testObj.failure.stackTrace, Does.Contain("FailTest.cs:11"), "Stack trace should contain file and line number");
                break;
            }
        }
        Assert.That(failTestFunctionFound, Is.True, "FailTestFunction test should be found");
        yield return null;
        
        // Test complete
        Console.WriteLine("Failed tests search test completed successfully");
    }
    
    /// <summary>
    /// Test steps for finding specific test with area output
    /// </summary>
    private IEnumerator FindSpecificTestWithAreaOutputSteps()
    {
        // Step 1: Call InterpretTestResults with specific test name and include output
        Console.WriteLine($"Step {CurrentStep + 1}: Calling InterpretTestResults for TestLoadDXFBundleShapeData_AllTestFiles");
        var task = _tool.InterpretTestResults(_testXmlPath, testName: "TestLoadDXFBundleShapeData_AllTestFiles", includeOutput: true);
        yield return task;
        
        // Step 2: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.testCases, Is.Not.Null, "Test cases should not be null");
        yield return null;
        
        // Step 3: Verify we found the specific test
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying specific test was found");
        var testCases = resultObj.testCases as IList<object>;
        Assert.That(testCases, Is.Not.Null, "Test cases should be a list");
        Assert.That(testCases.Count, Is.EqualTo(1), "Should find exactly 1 test");
        yield return null;
        
        // Step 4: Verify test details and output contains area information
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying test details and area output");
        var testObj = testCases[0] as dynamic;
        Assert.That(testObj.name, Is.EqualTo("TestLoadDXFBundleShapeData_AllTestFiles"), "Test name should match");
        Assert.That(testObj.result, Is.EqualTo("Passed"), "Test should be marked as Passed");
        Assert.That(testObj.output, Is.Not.Null.And.Not.Empty, "Output should not be null or empty");
        Assert.That(testObj.output, Does.Contain("12in Pizza Layer1_134910_0"), "Output should contain the specific shape identifier");
        Assert.That(testObj.output, Does.Contain("Area=0.315219"), "Output should contain the specific area value");
        yield return null;
        
        // Test complete
        Console.WriteLine("Specific test with area output test completed successfully");
    }
    
    /// <summary>
    /// Test steps for searching in output
    /// </summary>
    private IEnumerator SearchInOutputSteps()
    {
        // Step 1: Call InterpretTestResults with search in output
        Console.WriteLine($"Step {CurrentStep + 1}: Calling InterpretTestResults searching for 'Area=0.315219'");
        var task = _tool.InterpretTestResults(_testXmlPath, searchInOutput: "Area=0.315219", includeOutput: true);
        yield return task;
        
        // Step 2: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.testCases, Is.Not.Null, "Test cases should not be null");
        yield return null;
        
        // Step 3: Verify we found at least one test containing the area value
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying tests with area value found");
        var testCases = resultObj.testCases as IList<object>;
        Assert.That(testCases, Is.Not.Null, "Test cases should be a list");
        Assert.That(testCases.Count, Is.GreaterThan(0), "Should find at least one test with area value");
        yield return null;
        
        // Step 4: Verify the found test contains the area value
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying found test contains area value");
        var testObj = testCases[0] as dynamic;
        Assert.That(testObj.output, Does.Contain("Area=0.315219"), "Found test should contain the area value");
        Assert.That(testObj.output, Does.Contain("12in Pizza Layer1_134910_0"), "Found test should contain the shape identifier");
        yield return null;
        
        // Test complete
        Console.WriteLine("Search in output test completed successfully");
    }
    
    /// <summary>
    /// Test steps for verifying general statistics
    /// </summary>
    private IEnumerator GeneralStatisticsSteps()
    {
        // Step 1: Call InterpretTestResults without specific filters
        Console.WriteLine($"Step {CurrentStep + 1}: Calling InterpretTestResults for general statistics");
        var task = _tool.InterpretTestResults(_testXmlPath);
        yield return task;
        
        // Step 2: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.statistics, Is.Not.Null, "Statistics should not be null");
        yield return null;
        
        // Step 3: Verify overall statistics
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying overall statistics");
        var stats = resultObj.statistics;
        Assert.That(stats.totalTests, Is.EqualTo(179), "Total tests should be 179");
        Assert.That(stats.passed, Is.EqualTo(177), "Passed tests should be 177");
        Assert.That(stats.failed, Is.EqualTo(2), "Failed tests should be 2");
        Assert.That(stats.inconclusive, Is.EqualTo(0), "Inconclusive tests should be 0");
        Assert.That(stats.skipped, Is.EqualTo(0), "Skipped tests should be 0");
        Assert.That(stats.overallResult, Is.EqualTo("Failed(Child)"), "Overall result should be Failed(Child)");
        yield return null;
        
        // Step 4: Verify timing and version information
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying timing and version information");
        Assert.That(stats.startTime, Is.Not.Null.And.Not.Empty, "Start time should not be null or empty");
        Assert.That(stats.endTime, Is.Not.Null.And.Not.Empty, "End time should not be null or empty");
        Assert.That(stats.duration, Is.Not.Null.And.Not.Empty, "Duration should not be null or empty");
        Assert.That(stats.engineVersion, Is.Not.Null.And.Not.Empty, "Engine version should not be null or empty");
        Assert.That(stats.clrVersion, Is.Not.Null.And.Not.Empty, "CLR version should not be null or empty");
        yield return null;
        
        // Step 5: Verify test cases are returned
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying test cases are returned");
        var testCases = resultObj.testCases as IList<object>;
        Assert.That(testCases, Is.Not.Null, "Test cases should be a list");
        Assert.That(testCases.Count, Is.EqualTo(179), "Should return all 179 test cases");
        yield return null;
        
        // Test complete
        Console.WriteLine("General statistics test completed successfully");
    }
}