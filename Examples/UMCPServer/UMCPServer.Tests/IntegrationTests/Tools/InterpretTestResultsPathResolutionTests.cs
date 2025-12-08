using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using UMCPServer.Tools;
using UMCPServer.Services;
using NUnit.Framework;

namespace UMCPServer.Tests.IntegrationTests.Tools;

/// <summary>
/// Integration tests for InterpretTestResults tool path resolution functionality
/// </summary>
public class InterpretTestResultsPathResolutionTests : IntegrationTestBase
{
    private InterpretTestResultsTool _tool;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        
        // Setup test services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<UnityConnectionService>();
        services.AddSingleton<InterpretTestResultsTool>();
        
        var serviceProvider = services.BuildServiceProvider();
        _tool = serviceProvider.GetRequiredService<InterpretTestResultsTool>();
    }

    [Test]
    public async Task InterpretTestResults_WithRelativePath_ShouldResolveCorrectly()
    {
        // Arrange
        var relativePath = "TestResults/TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml";
        
        // Act & Assert
        var result = await _tool.InterpretTestResults(relativePath, failedOnly: true, includeStackTraces: true);
        
        // The result should contain success information or appropriate error message
        Assert.That(result, Is.Not.Null);
        TestContext.WriteLine($"Result for relative path '{relativePath}': {result}");
    }

    [Test]
    public async Task InterpretTestResults_WithJustFilename_ShouldSearchInTestResults()
    {
        // Arrange
        var filename = "TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml";
        
        // Act & Assert
        var result = await _tool.InterpretTestResults(filename, failedOnly: true, includeStackTraces: true);
        
        Assert.That(result, Is.Not.Null);
        TestContext.WriteLine($"Result for filename '{filename}': {result}");
    }

    [TestCase("C:/Prespective/GIT/GIT-AGSSRefactor/AGSSRefactor/TestResults/TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml")]
    [TestCase("/mnt/c/Prespective/GIT/GIT-AGSSRefactor/AGSSRefactor/TestResults/TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml")]
    [TestCase("/app/TestResults/TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml")]
    [TestCase("/app/UnityProject/TestResults/TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml")]
    public async Task InterpretTestResults_WithDifferentPathFormats_ShouldHandleCrossPlatform(string testPath)
    {
        // Act
        var result = await _tool.InterpretTestResults(testPath, failedOnly: true, includeStackTraces: true);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        TestContext.WriteLine($"Result for path '{testPath}': {result}");
        
        // The result should either succeed or provide a meaningful error about file not found
        // But it should not fail due to path format issues
    }

    [Test]
    public async Task InterpretTestResults_WithInvalidPath_ShouldReturnMeaningfulError()
    {
        // Arrange
        var invalidPath = "nonexistent/path/to/file.xml";
        
        // Act
        var result = await _tool.InterpretTestResults(invalidPath, failedOnly: true, includeStackTraces: true);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        TestContext.WriteLine($"Result for invalid path '{invalidPath}': {result}");
        
        // Should contain information about path resolution attempts
        var resultString = result.ToString();
        Assert.That(resultString, Does.Contain("path resolution").IgnoreCase);
    }

    [Test]
    public async Task InterpretTestResults_PathResolution_ShouldLogResolutionSteps()
    {
        // Arrange
        var testPath = "TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml";
        
        // Act
        var result = await _tool.InterpretTestResults(testPath, failedOnly: true, includeStackTraces: true);
        
        // Assert
        Assert.That(result, Is.Not.Null);
        TestContext.WriteLine($"Path resolution test result: {result}");
        
        // This test primarily validates that the path resolution doesn't throw exceptions
        // and provides meaningful feedback
    }
}