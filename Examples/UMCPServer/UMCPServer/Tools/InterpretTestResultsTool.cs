using System.ComponentModel;
using System.Xml;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using UMCPServer.Services;
using Newtonsoft.Json.Linq;

namespace UMCPServer.Tools;

/// <summary>
/// MCP tool for interpreting Unity test results from XML files
/// </summary>
[McpServerToolType]
public class InterpretTestResultsTool
{
    private readonly ILogger<InterpretTestResultsTool> _logger;
    private readonly UnityConnectionService _unityConnection;
    
    public InterpretTestResultsTool(ILogger<InterpretTestResultsTool> logger, UnityConnectionService unityConnection)
    {
        _logger = logger;
        _unityConnection = unityConnection;
    }
    
    /// <summary>
    /// Interprets Unity test results from XML files with various query options
    /// </summary>
    [McpServerTool]
    [Description("Extract knowledge from Unity test results XML files - query by test name, find failed tests, or extract specific test output")]
    public async Task<object> InterpretTestResults(
        [Description("Relative or absolute path to the test results XML file")]
        string filePath,
        
        [Description("Name of specific test to find (optional)")]
        string? testName = null,
        
        [Description("Whether to return only failed tests (default: false)")]
        bool failedOnly = false,
        
        [Description("Whether to return only passed tests (default: false)")]
        bool passedOnly = false,
        
        [Description("Text to search for in test output (optional)")]
        string? searchInOutput = null,
        
        [Description("Whether to include stack traces in results (default: false)")]
        bool includeStackTraces = false,
        
        [Description("Whether to include test output in results (default: false)")]
        bool includeOutput = false,
        
        [Description("Maximum response length in tokens to prevent overflow (default: 25000)")]
        int maxResponseLength = 25000,
        
        [Description("Target page for pagination, starting from 0 (default: 0)")]
        int targetPage = 0,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Interpreting test results from: {FilePath}", filePath);
            
            // Enhanced path resolution with Unity project path detection
            string resolvedPath = await ResolveTestResultsFilePath(filePath, cancellationToken);
            
            if (!File.Exists(resolvedPath))
            {
                return new
                {
                    success = false,
                    error = $"Test results file not found: {resolvedPath}. Tried path resolution from Unity project and common locations."
                };
            }
            
            // Load and parse XML document
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(resolvedPath);
            
            // Extract overall test run statistics
            var testRunStats = ExtractTestRunStatistics(xmlDoc);
            
            // Extract test cases based on query parameters
            var allTestCases = ExtractTestCases(xmlDoc, testName, failedOnly, passedOnly, searchInOutput, includeStackTraces, includeOutput);
            
            // Apply pagination if response might be too large
            var paginationResult = ApplyPagination(allTestCases, maxResponseLength, targetPage, testRunStats, resolvedPath);
            
            return paginationResult;
        }
        catch (XmlException ex)
        {
            _logger.LogError(ex, "Error parsing XML file: {FilePath}", filePath);
            return new
            {
                success = false,
                error = $"Invalid XML format: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interpreting test results: {FilePath}", filePath);
            return new
            {
                success = false,
                error = $"Failed to interpret test results: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Resolves the test results file path using Unity project path detection and cross-platform support
    /// </summary>
    private async Task<string> ResolveTestResultsFilePath(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving test results file path: {FilePath}", filePath);
        
        // If absolute path and exists, return as-is
        if (Path.IsPathRooted(filePath) && File.Exists(filePath))
        {
            _logger.LogDebug("Absolute path exists: {FilePath}", filePath);
            return filePath;
        }
        
        // If relative path from current directory exists, convert to absolute
        if (!Path.IsPathRooted(filePath))
        {
            string currentDirPath = Path.GetFullPath(filePath);
            if (File.Exists(currentDirPath))
            {
                _logger.LogDebug("Relative path resolved from current directory: {FilePath}", currentDirPath);
                return currentDirPath;
            }
        }
        
        // Try to get Unity project path and resolve from there
        try
        {
            if (_unityConnection.IsConnected || await _unityConnection.ConnectAsync())
            {
                var projectPathResponse = await GetUnityProjectPath(cancellationToken);
                if (projectPathResponse != null)
                {
                    string resolvedPath = TryResolveFromUnityProject(filePath, projectPathResponse);
                    if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                    {
                        _logger.LogDebug("Resolved path from Unity project: {FilePath}", resolvedPath);
                        return resolvedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get Unity project path for file resolution");
        }
        
        // Try common path translations for Docker/WSL environments
        string translatedPath = TranslatePath(filePath);
        if (!string.IsNullOrEmpty(translatedPath) && File.Exists(translatedPath))
        {
            _logger.LogDebug("Resolved path through path translation: {FilePath}", translatedPath);
            return translatedPath;
        }
        
        // If nothing worked, return the original path (will likely result in file not found)
        _logger.LogWarning("Could not resolve test results file path: {FilePath}", filePath);
        return Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);
    }
    
    /// <summary>
    /// Gets Unity project path using the Unity connection service
    /// </summary>
    private async Task<string?> GetUnityProjectPath(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _unityConnection.SendCommandAsync("get_project_path", null, cancellationToken);
            if (result?.Value<bool>("success") == true)
            {
                return result["data"]?.Value<string>("projectPath");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Unity project path");
        }
        return null;
    }
    
    /// <summary>
    /// Tries to resolve file path from Unity project directory
    /// </summary>
    private string TryResolveFromUnityProject(string filePath, string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return string.Empty;
        
        // Handle different path formats
        string cleanFilePath = filePath.Replace("\\", "/");
        
        // If it's just a filename, look in TestResults directory
        if (!cleanFilePath.Contains("/") && !cleanFilePath.Contains("\\"))
        {
            string testResultsPath = Path.Combine(projectPath, "TestResults", cleanFilePath);
            if (File.Exists(testResultsPath))
                return testResultsPath;
        }
        
        // Try combining with project path
        string combinedPath = Path.Combine(projectPath, cleanFilePath);
        if (File.Exists(combinedPath))
            return combinedPath;
        
        // Try TestResults subdirectory
        string testResultsSubPath = Path.Combine(projectPath, "TestResults", cleanFilePath);
        if (File.Exists(testResultsSubPath))
            return testResultsSubPath;
        
        return string.Empty;
    }
    
    /// <summary>
    /// Translates paths for cross-platform compatibility (Docker/WSL)
    /// </summary>
    private string TranslatePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;
        
        // Windows to Docker/Linux translation
        if (filePath.StartsWith("C:") || filePath.StartsWith("c:"))
        {
            // Convert Windows path to WSL/Docker mount
            string linuxPath = filePath.Replace("C:", "/mnt/c").Replace("c:", "/mnt/c").Replace("\\", "/");
            if (File.Exists(linuxPath))
                return linuxPath;
        }
        
        // Linux/Docker to Windows translation
        if (filePath.StartsWith("/mnt/c/"))
        {
            string windowsPath = filePath.Replace("/mnt/c/", "C:/").Replace("/", "\\");
            if (File.Exists(windowsPath))
                return windowsPath;
        }
        
        // Try Docker internal paths
        if (filePath.StartsWith("/app/"))
        {
            // Try to map /app/ to current working directory or common Unity project locations
            string relativePath = filePath.Substring(5); // Remove /app/
            string currentDirPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(currentDirPath))
                return currentDirPath;
        }
        
        // Try Docker volume mount paths (from docker-compose.yml)
        // Check /app/TestResults mount
        if (!filePath.Contains("TestResults") && !filePath.StartsWith("/app/"))
        {
            string testResultsMount = Path.Combine("/app/TestResults", Path.GetFileName(filePath));
            if (File.Exists(testResultsMount))
                return testResultsMount;
        }
        
        // Check /app/UnityProject mount
        if (!filePath.StartsWith("/app/"))
        {
            string unityProjectMount = Path.Combine("/app/UnityProject", filePath);
            if (File.Exists(unityProjectMount))
                return unityProjectMount;
            
            // Try TestResults subdirectory in Unity project mount
            string testResultsInProject = Path.Combine("/app/UnityProject/TestResults", Path.GetFileName(filePath));
            if (File.Exists(testResultsInProject))
                return testResultsInProject;
        }
        
        return string.Empty;
    }
    
    /// <summary>
    /// Extracts overall test run statistics from the XML document
    /// </summary>
    private static object ExtractTestRunStatistics(XmlDocument xmlDoc)
    {
        var testRunNode = xmlDoc.SelectSingleNode("//test-run");
        if (testRunNode?.Attributes == null)
        {
            return new { error = "No test-run node found in XML" };
        }
        
        return new
        {
            totalTests = int.Parse(testRunNode.Attributes["testcasecount"]?.Value ?? "0"),
            passed = int.Parse(testRunNode.Attributes["passed"]?.Value ?? "0"),
            failed = int.Parse(testRunNode.Attributes["failed"]?.Value ?? "0"),
            inconclusive = int.Parse(testRunNode.Attributes["inconclusive"]?.Value ?? "0"),
            skipped = int.Parse(testRunNode.Attributes["skipped"]?.Value ?? "0"),
            overallResult = testRunNode.Attributes["result"]?.Value ?? "Unknown",
            startTime = testRunNode.Attributes["start-time"]?.Value ?? "Unknown",
            endTime = testRunNode.Attributes["end-time"]?.Value ?? "Unknown",
            duration = testRunNode.Attributes["duration"]?.Value ?? "Unknown",
            engineVersion = testRunNode.Attributes["engine-version"]?.Value ?? "Unknown",
            clrVersion = testRunNode.Attributes["clr-version"]?.Value ?? "Unknown"
        };
    }
    
    /// <summary>
    /// Extracts test cases from the XML document based on query parameters
    /// </summary>
    private static List<object> ExtractTestCases(XmlDocument xmlDoc, string? testName, bool failedOnly, bool passedOnly, 
        string? searchInOutput, bool includeStackTraces, bool includeOutput)
    {
        var testCases = new List<object>();
        var testCaseNodes = xmlDoc.SelectNodes("//test-case");
        
        if (testCaseNodes == null) return testCases;
        
        foreach (XmlNode testCase in testCaseNodes)
        {
            var attributes = testCase.Attributes;
            if (attributes == null) continue;
            
            var name = attributes["name"]?.Value ?? "Unknown";
            var fullName = attributes["fullname"]?.Value ?? "Unknown";
            var result = attributes["result"]?.Value ?? "Unknown";
            var duration = attributes["duration"]?.Value ?? "0";
            var className = attributes["classname"]?.Value ?? "Unknown";
            var methodName = attributes["methodname"]?.Value ?? "Unknown";
            
            // Apply filters
            if (!string.IsNullOrEmpty(testName) && !name.Contains(testName, StringComparison.OrdinalIgnoreCase) && 
                !fullName.Contains(testName, StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (failedOnly && result != "Failed")
                continue;
            
            if (passedOnly && result != "Passed")
                continue;
            
            // Extract output if needed
            string? output = null;
            if (includeOutput || !string.IsNullOrEmpty(searchInOutput))
            {
                var outputNode = testCase.SelectSingleNode("output");
                output = outputNode?.InnerText ?? "";
            }
            
            // Apply output search filter
            if (!string.IsNullOrEmpty(searchInOutput) && (string.IsNullOrEmpty(output) || 
                !output.Contains(searchInOutput, StringComparison.OrdinalIgnoreCase)))
                continue;
            
            // Extract failure information if present
            object? failureInfo = null;
            if (result == "Failed")
            {
                var failureNode = testCase.SelectSingleNode("failure");
                if (failureNode != null)
                {
                    var message = failureNode.SelectSingleNode("message")?.InnerText ?? "";
                    var stackTrace = failureNode.SelectSingleNode("stack-trace")?.InnerText ?? "";
                    
                    failureInfo = new
                    {
                        message = message,
                        stackTrace = includeStackTraces ? stackTrace : null
                    };
                }
            }
            
            // Build test case object
            var testCaseObj = new
            {
                name = name,
                fullName = fullName,
                result = result,
                duration = duration,
                className = className,
                methodName = methodName,
                startTime = attributes["start-time"]?.Value,
                endTime = attributes["end-time"]?.Value,
                failure = failureInfo,
                output = includeOutput ? output : null
            };
            
            testCases.Add(testCaseObj);
        }
        
        return testCases;
    }
    
    /// <summary>
    /// Applies pagination to test cases based on estimated response size
    /// </summary>
    private object ApplyPagination(List<object> allTestCases, int maxResponseLength, int targetPage, object testRunStats, string resolvedPath)
    {
        // If no test cases or maxResponseLength is 0, return all results
        if (allTestCases.Count == 0 || maxResponseLength <= 0)
        {
            return new
            {
                success = true,
                filePath = resolvedPath,
                statistics = testRunStats,
                testCases = allTestCases,
                totalFound = allTestCases.Count,
                pagination = new
                {
                    currentPage = targetPage,
                    totalPages = 1,
                    totalItems = allTestCases.Count,
                    hasNextPage = false,
                    hasPreviousPage = false
                }
            };
        }
        
        // Estimate token usage and split into pages by test boundaries
        var pages = new List<List<object>>();
        var currentPage = new List<object>();
        int currentEstimatedTokens = EstimateBaseResponseTokens(testRunStats, resolvedPath);
        
        foreach (var testCase in allTestCases)
        {
            int testCaseTokens = EstimateTestCaseTokens(testCase);
            
            // If adding this test case would exceed the limit and we have at least one test case in current page
            if (currentEstimatedTokens + testCaseTokens > maxResponseLength && currentPage.Count > 0)
            {
                pages.Add(new List<object>(currentPage));
                currentPage.Clear();
                currentEstimatedTokens = EstimateBaseResponseTokens(testRunStats, resolvedPath);
            }
            
            currentPage.Add(testCase);
            currentEstimatedTokens += testCaseTokens;
        }
        
        // Add the last page if it has content
        if (currentPage.Count > 0)
        {
            pages.Add(currentPage);
        }
        
        // Ensure we have at least one page
        if (pages.Count == 0)
        {
            pages.Add(new List<object>());
        }
        
        // Validate target page
        int totalPages = pages.Count;
        if (targetPage < 0) targetPage = 0;
        if (targetPage >= totalPages) targetPage = totalPages - 1;
        
        var pageTestCases = pages[targetPage];
        
        // Final safety check: if the current page still exceeds maxResponseLength,
        // create a truncated response with error message
        var tempResponse = new
        {
            success = true,
            filePath = resolvedPath,
            statistics = testRunStats,
            testCases = pageTestCases,
            totalFound = allTestCases.Count,
            pagination = new
            {
                currentPage = targetPage,
                totalPages = totalPages,
                totalItems = allTestCases.Count,
                itemsOnCurrentPage = pageTestCases.Count,
                hasNextPage = targetPage < totalPages - 1,
                hasPreviousPage = targetPage > 0,
                maxResponseLength = maxResponseLength
            }
        };
        
        // Check if response is still too large
        string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(tempResponse);
        int actualTokens = responseJson.Length / 3; // Conservative token estimate
        
        if (actualTokens > maxResponseLength)
        {
            // Response is still too large, return with reduced content and warning
            var reducedTestCases = pageTestCases.Take(Math.Max(1, pageTestCases.Count / 2)).ToList();
            
            return new
            {
                success = true,
                filePath = resolvedPath,
                statistics = testRunStats,
                testCases = reducedTestCases,
                totalFound = allTestCases.Count,
                warning = $"Response was still too large ({actualTokens} tokens > {maxResponseLength}). Reduced to {reducedTestCases.Count} test cases. Consider using more restrictive filters or smaller maxResponseLength.",
                pagination = new
                {
                    currentPage = targetPage,
                    totalPages = totalPages * 2, // Approximation since we're splitting pages
                    totalItems = allTestCases.Count,
                    itemsOnCurrentPage = reducedTestCases.Count,
                    hasNextPage = targetPage < totalPages - 1,
                    hasPreviousPage = targetPage > 0,
                    maxResponseLength = maxResponseLength,
                    actualTokens = actualTokens
                }
            };
        }
        
        return tempResponse;
    }
    
    /// <summary>
    /// Estimates the base response tokens (excluding test cases)
    /// </summary>
    private static int EstimateBaseResponseTokens(object testRunStats, string resolvedPath)
    {
        // Rough estimation: base response structure + statistics + file path
        // This is a conservative estimate for JSON overhead and base fields
        return 500 + resolvedPath.Length / 3; // Approximate token count
    }
    
    /// <summary>
    /// Estimates the token count for a single test case
    /// </summary>
    private static int EstimateTestCaseTokens(object testCase)
    {
        // Convert to JSON and estimate tokens
        // This is a rough approximation: 1 token ≈ 3-4 characters for JSON
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(testCase);
        return json.Length / 3; // Conservative estimate
    }
}