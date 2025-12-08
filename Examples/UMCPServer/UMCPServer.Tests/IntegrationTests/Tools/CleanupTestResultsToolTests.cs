using System.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using UMCPServer.Tools;

namespace UMCPServer.Tests.IntegrationTests.Tools;

/// <summary>
/// Integration tests for the CleanupTestResults tool
/// </summary>
[TestFixture]
public class CleanupTestResultsToolTests : IntegrationTestBase
{
    private Mock<ILogger<CleanupTestResultsTool>> _mockLogger = null!;
    private CleanupTestResultsTool _tool = null!;
    private string _testDirectory = null!;
    
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockLogger = new Mock<ILogger<CleanupTestResultsTool>>();
        _tool = new CleanupTestResultsTool(_mockLogger.Object);
        
        // Create a temporary test directory for cleanup testing
        _testDirectory = Path.Combine(Path.GetTempPath(), "UMCPTestCleanup_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);
    }
    
    [TearDown]
    public override void TearDown()
    {
        base.TearDown();
        
        // Clean up the test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }
    
    [Test]
    public void CleanupTestResults_PreviewMode_ShouldShowFilesWithoutDeleting()
    {
        // Execute the multi-step test
        ExecuteTestSteps(PreviewModeSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void CleanupTestResults_KeepMostRecent_ShouldRetainNewestFiles()
    {
        // Execute the multi-step test
        ExecuteTestSteps(KeepMostRecentSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void CleanupTestResults_OlderThanDays_ShouldDeleteOldFiles()
    {
        // Execute the multi-step test
        ExecuteTestSteps(OlderThanDaysSteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    [Test]
    public void CleanupTestResults_InvalidDirectory_ShouldReturnError()
    {
        // Execute the multi-step test
        ExecuteTestSteps(InvalidDirectorySteps());
        
        // Additional assertion to verify test completed
        Assert.That(TestCompleted, Is.True, "Test did not complete all steps");
    }
    
    /// <summary>
    /// Test steps for preview mode functionality
    /// </summary>
    private IEnumerator PreviewModeSteps()
    {
        // Step 1: Create test files
        Console.WriteLine($"Step {CurrentStep + 1}: Creating test files");
        var testFiles = new[]
        {
            "TestResults_EditMode_old.xml",
            "TestResults_PlayMode_newer.xml", 
            "TestResults_All_newest.xml"
        };
        
        foreach (var fileName in testFiles)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, "<test-results></test-results>");
        }
        
        // Set different creation times
        var oldFile = Path.Combine(_testDirectory, testFiles[0]);
        var newerFile = Path.Combine(_testDirectory, testFiles[1]);
        var newestFile = Path.Combine(_testDirectory, testFiles[2]);
        
        File.SetCreationTime(oldFile, DateTime.Now.AddDays(-5));
        File.SetCreationTime(newerFile, DateTime.Now.AddDays(-2));
        File.SetCreationTime(newestFile, DateTime.Now.AddHours(-1));
        
        yield return null;
        
        // Step 2: Call CleanupTestResults in preview mode
        Console.WriteLine($"Step {CurrentStep + 1}: Calling CleanupTestResults in preview mode");
        var task = _tool.CleanupTestResults(_testDirectory, keepMostRecent: 2, previewOnly: true);
        yield return task;
        
        // Step 3: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.previewMode, Is.True, "Preview mode should be true");
        yield return null;
        
        // Step 4: Verify files are marked for deletion but not actually deleted
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying files marked for deletion");
        Assert.That(resultObj.filesFound, Is.EqualTo(3), "Should find 3 files");
        Assert.That(resultObj.filesMarkedForDeletion, Is.EqualTo(1), "Should mark 1 file for deletion");
        Assert.That(resultObj.filesDeleted, Is.EqualTo(0), "Should not delete any files in preview mode");
        yield return null;
        
        // Step 5: Verify all files still exist
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying all files still exist");
        foreach (var fileName in testFiles)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            Assert.That(File.Exists(filePath), Is.True, $"File {fileName} should still exist");
        }
        yield return null;
        
        // Test complete
        Console.WriteLine("Preview mode test completed successfully");
    }
    
    /// <summary>
    /// Test steps for keep most recent functionality
    /// </summary>
    private IEnumerator KeepMostRecentSteps()
    {
        // Step 1: Create test files with different creation times
        Console.WriteLine($"Step {CurrentStep + 1}: Creating test files with different creation times");
        var testFiles = new[]
        {
            "TestResults_EditMode_oldest.xml",
            "TestResults_PlayMode_middle.xml",
            "TestResults_All_newest.xml"
        };
        
        foreach (var fileName in testFiles)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, "<test-results></test-results>");
        }
        
        // Set different creation times
        File.SetCreationTime(Path.Combine(_testDirectory, testFiles[0]), DateTime.Now.AddDays(-3));
        File.SetCreationTime(Path.Combine(_testDirectory, testFiles[1]), DateTime.Now.AddDays(-1));
        File.SetCreationTime(Path.Combine(_testDirectory, testFiles[2]), DateTime.Now.AddHours(-1));
        
        yield return null;
        
        // Step 2: Call CleanupTestResults keeping most recent 2 files
        Console.WriteLine($"Step {CurrentStep + 1}: Calling CleanupTestResults keeping most recent 2 files");
        var task = _tool.CleanupTestResults(_testDirectory, keepMostRecent: 2);
        yield return task;
        
        // Step 3: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.filesFound, Is.EqualTo(3), "Should find 3 files");
        Assert.That(resultObj.filesDeleted, Is.EqualTo(1), "Should delete 1 file");
        yield return null;
        
        // Step 4: Verify oldest file was deleted and newest files remain
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying file deletion results");
        Assert.That(File.Exists(Path.Combine(_testDirectory, testFiles[0])), Is.False, "Oldest file should be deleted");
        Assert.That(File.Exists(Path.Combine(_testDirectory, testFiles[1])), Is.True, "Middle file should remain");
        Assert.That(File.Exists(Path.Combine(_testDirectory, testFiles[2])), Is.True, "Newest file should remain");
        yield return null;
        
        // Test complete
        Console.WriteLine("Keep most recent test completed successfully");
    }
    
    /// <summary>
    /// Test steps for older than days functionality
    /// </summary>
    private IEnumerator OlderThanDaysSteps()
    {
        // Step 1: Create test files with different ages
        Console.WriteLine($"Step {CurrentStep + 1}: Creating test files with different ages");
        var testFiles = new[]
        {
            "TestResults_EditMode_veryold.xml",
            "TestResults_PlayMode_recent.xml"
        };
        
        foreach (var fileName in testFiles)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, "<test-results></test-results>");
        }
        
        // Set creation times - one very old, one recent
        File.SetCreationTime(Path.Combine(_testDirectory, testFiles[0]), DateTime.Now.AddDays(-10));
        File.SetCreationTime(Path.Combine(_testDirectory, testFiles[1]), DateTime.Now.AddHours(-1));
        
        yield return null;
        
        // Step 2: Call CleanupTestResults to delete files older than 5 days
        Console.WriteLine($"Step {CurrentStep + 1}: Calling CleanupTestResults for files older than 5 days");
        var task = _tool.CleanupTestResults(_testDirectory, olderThanDays: 5);
        yield return task;
        
        // Step 3: Verify response structure
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying response structure");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.True, "Request should be successful");
        Assert.That(resultObj.filesFound, Is.EqualTo(2), "Should find 2 files");
        Assert.That(resultObj.filesDeleted, Is.EqualTo(1), "Should delete 1 file");
        yield return null;
        
        // Step 4: Verify old file was deleted and recent file remains
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying file deletion results");
        Assert.That(File.Exists(Path.Combine(_testDirectory, testFiles[0])), Is.False, "Old file should be deleted");
        Assert.That(File.Exists(Path.Combine(_testDirectory, testFiles[1])), Is.True, "Recent file should remain");
        yield return null;
        
        // Test complete
        Console.WriteLine("Older than days test completed successfully");
    }
    
    /// <summary>
    /// Test steps for invalid directory handling
    /// </summary>
    private IEnumerator InvalidDirectorySteps()
    {
        // Step 1: Call CleanupTestResults with non-existent directory
        Console.WriteLine($"Step {CurrentStep + 1}: Calling CleanupTestResults with non-existent directory");
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistentDirectory_" + Guid.NewGuid().ToString("N")[..8]);
        var task = _tool.CleanupTestResults(invalidPath);
        yield return task;
        
        // Step 2: Verify error response
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying error response");
        var result = task.Result;
        Assert.That(result, Is.Not.Null, "Result should not be null");
        
        var resultObj = result as dynamic;
        Assert.That(resultObj.success, Is.False, "Request should fail");
        Assert.That(resultObj.error, Does.Contain("Directory not found"), "Error should mention directory not found");
        yield return null;
        
        // Test complete
        Console.WriteLine("Invalid directory test completed successfully");
    }
}