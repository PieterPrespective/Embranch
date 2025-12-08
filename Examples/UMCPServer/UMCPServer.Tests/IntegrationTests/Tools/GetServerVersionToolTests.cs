using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools;

[TestFixture]
public class GetServerVersionToolTests : IntegrationTestBase
{
    private Mock<ILogger<GetServerVersionTool>> _mockLogger = null!;
    private GetServerVersionTool _tool = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockLogger = new Mock<ILogger<GetServerVersionTool>>();
        _tool = new GetServerVersionTool(_mockLogger.Object);
    }
    
    [Test]
    public void GetServerVersion_ShouldReturnVersionInfo()
    {
        // Execute the multi-step test
        ExecuteTestSteps(GetServerVersionTestSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    /// <summary>
    /// A multi-step test for the GetServerVersion tool.
    /// Each yield return represents a logical step in the test sequence.
    /// </summary>
    private IEnumerator GetServerVersionTestSteps()
    {
        // Step 1: Create a tool instance
        Console.WriteLine($"Step {CurrentStep + 1}: Creating GetServerVersionTool instance");
        yield return null; // Simulate async operation
        
        // Step 2: Verify tool is correctly instantiated
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying tool is instantiated");
        Assert.That(_tool, Is.Not.Null, "Tool should be created");
        yield return null;
        
        // Step 3: Execute the GetServerVersion method
        Console.WriteLine($"Step {CurrentStep + 1}: Executing GetServerVersion method");
        Task<object> versionTask = _tool.GetServerVersion();
        // Yield the task to let the executor handle its completion
        yield return versionTask;
        
        // Step 4: Verify the result
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying version result");
        var result = versionTask.Result;
        Assert.That(result, Is.Not.Null, "Version result should not be null");
        
        // Extract the result as dynamic to access its properties
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Version request should be successful");
        Assert.That(resultObj.message, Is.EqualTo("Server version retrieved successfully"));
        Assert.That(resultObj.version, Is.Not.Null, "Version info should not be null");
        
        // Step 5: Check the specific version properties
        Console.WriteLine($"Step {CurrentStep + 1}: Checking version properties");
        Assert.That(resultObj.version.informationalVersion, Is.Not.Null.Or.Empty, "Informational version should not be null or empty");
        Assert.That(resultObj.version.assemblyVersion, Is.Not.Null.Or.Empty, "Assembly version should not be null or empty");
        Assert.That(resultObj.version.fileVersion, Is.Not.Null.Or.Empty, "File version should not be null or empty");
        yield return null;
        
        // Test complete
        Console.WriteLine("Test completed successfully");
    }
}