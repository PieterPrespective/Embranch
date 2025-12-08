using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace UMCPServer.Tools;

/// <summary>
/// MCP tool for cleaning up old Unity test result files
/// </summary>
[McpServerToolType]
public class CleanupTestResultsTool
{
    private readonly ILogger<CleanupTestResultsTool> _logger;
    
    public CleanupTestResultsTool(ILogger<CleanupTestResultsTool> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Cleans up old test result files from the TestResults directory
    /// </summary>
    [McpServerTool]
    [Description("Clean up old Unity test result files from TestResults directory based on datetime criteria")]
    public async Task<object> CleanupTestResults(
        [Description("Directory path containing test results (default: TestResults)")]
        string directory = "TestResults",
        
        [Description("Remove files older than this many days (optional)")]
        int? olderThanDays = null,
        
        [Description("Remove files older than this datetime (ISO 8601 format, optional)")]
        string? olderThanDateTime = null,
        
        [Description("Keep only the most recent N files (optional)")]
        int? keepMostRecent = null,
        
        [Description("File pattern to match (default: TestResults_*.xml)")]
        string filePattern = "TestResults_*.xml",
        
        [Description("Preview mode - show what would be deleted without actually deleting (default: false)")]
        bool previewOnly = false,
        
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Cleaning up test results in directory: {Directory}", directory);
            
            // Convert relative path to absolute if needed
            string absolutePath = Path.IsPathRooted(directory) ? directory : Path.GetFullPath(directory);
            
            if (!Directory.Exists(absolutePath))
            {
                return new
                {
                    success = false,
                    error = $"Directory not found: {absolutePath}"
                };
            }
            
            // Get all matching files
            var files = Directory.GetFiles(absolutePath, filePattern, SearchOption.TopDirectoryOnly);
            
            if (files.Length == 0)
            {
                return new
                {
                    success = true,
                    message = $"No files matching pattern '{filePattern}' found in {absolutePath}",
                    filesFound = 0,
                    filesDeleted = 0
                };
            }
            
            // Get file information with creation times
            var fileInfos = files.Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();
            
            // Determine which files to delete
            var filesToDelete = new List<FileInfo>();
            
            // Apply datetime filters
            if (olderThanDays.HasValue)
            {
                var cutoffDate = DateTime.Now.AddDays(-olderThanDays.Value);
                filesToDelete.AddRange(fileInfos.Where(f => f.CreationTime < cutoffDate));
            }
            else if (!string.IsNullOrEmpty(olderThanDateTime))
            {
                if (DateTime.TryParse(olderThanDateTime, null, DateTimeStyles.RoundtripKind, out var cutoffDate))
                {
                    filesToDelete.AddRange(fileInfos.Where(f => f.CreationTime < cutoffDate));
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Invalid datetime format: {olderThanDateTime}. Please use ISO 8601 format (e.g., 2025-01-01T00:00:00Z)"
                    };
                }
            }
            else if (keepMostRecent.HasValue)
            {
                if (fileInfos.Count > keepMostRecent.Value)
                {
                    filesToDelete.AddRange(fileInfos.Skip(keepMostRecent.Value));
                }
            }
            else
            {
                return new
                {
                    success = false,
                    error = "Please specify one of: olderThanDays, olderThanDateTime, or keepMostRecent"
                };
            }
            
            // Remove duplicates and sort by name
            filesToDelete = filesToDelete.Distinct().OrderBy(f => f.Name).ToList();
            
            // Build result information
            var deletionInfo = filesToDelete.Select(f => new
            {
                fileName = f.Name,
                fullPath = f.FullName,
                creationTime = f.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                size = f.Length,
                sizeFormatted = FormatFileSize(f.Length)
            }).ToList();
            
            long totalBytesFreed = 0;
            int deletedCount = 0;
            
            // Delete files if not in preview mode
            if (!previewOnly)
            {
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        totalBytesFreed += file.Length;
                        file.Delete();
                        deletedCount++;
                        _logger.LogInformation("Deleted test result file: {FileName}", file.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file: {FileName}", file.Name);
                    }
                }
            }
            
            return new
            {
                success = true,
                directory = absolutePath,
                filePattern = filePattern,
                filesFound = fileInfos.Count,
                filesMarkedForDeletion = filesToDelete.Count,
                filesDeleted = deletedCount,
                totalBytesFreed = totalBytesFreed,
                totalBytesFreedFormatted = FormatFileSize(totalBytesFreed),
                previewMode = previewOnly,
                deletionDetails = deletionInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up test results");
            return new
            {
                success = false,
                error = $"Failed to cleanup test results: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Formats file size in human-readable format
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}