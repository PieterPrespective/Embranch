using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Embranch.Models;
using Embranch.Services;
using Embranch.Utilities;

namespace Embranch.Tools;

/// <summary>
/// MCP tool that shows detailed information about a specific commit
/// </summary>
[McpServerToolType]
public class DoltShowTool
{
    private readonly ILogger<DoltShowTool> _logger;
    private readonly IDoltCli _doltCli;
    private readonly ISyncStateTracker _syncStateTracker;
    private readonly DoltConfiguration _doltConfig;

    /// <summary>
    /// Initializes a new instance of the DoltShowTool class
    /// </summary>
    public DoltShowTool(
        ILogger<DoltShowTool> logger,
        IDoltCli doltCli,
        ISyncStateTracker syncStateTracker,
        IOptions<DoltConfiguration> doltConfig)
    {
        _logger = logger;
        _doltCli = doltCli;
        _syncStateTracker = syncStateTracker;
        _doltConfig = doltConfig.Value;
    }

    /// <summary>
    /// Show detailed information about a specific commit, including the list of documents that were added, modified, or deleted
    /// </summary>
    [McpServerTool]
    [Description("Show detailed information about a specific commit, including the list of documents that were added, modified, or deleted.")]
    public virtual async Task<object> DoltShow(string commit, bool include_diff = false, int diff_limit = 10)
    {
        const string toolName = nameof(DoltShowTool);
        const string methodName = nameof(DoltShow);
        
        try
        {
            ToolLoggingUtility.LogToolStart(_logger, toolName, methodName,
                $"commit: '{commit}', include_diff: {include_diff}, diff_limit: {diff_limit}");

            // First check if Dolt is available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                const string error = "DOLT_EXECUTABLE_NOT_FOUND";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error,
                    message = doltCheck.Error
                };
            }

            // Check if repository is initialized
            var isInitialized = await _doltCli.IsInitializedAsync();
            if (!isInitialized)
            {
                const string error = "NOT_INITIALIZED";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error,
                    message = "No Dolt repository configured. Use dolt_init or dolt_clone first."
                };
            }

            // Resolve commit reference (HEAD, HEAD~1, etc.)
            string commitHash = commit;
            if (commit.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                commitHash = await _doltCli.GetHeadCommitHashAsync() ?? "";
            }
            // TODO: Handle other references like HEAD~1, branch names, etc.

            // Get commit info from log
            var commits = await _doltCli.GetLogAsync(50); // Get enough to find the commit
            var targetCommit = commits?.FirstOrDefault(c => 
                c.Hash?.StartsWith(commitHash, StringComparison.OrdinalIgnoreCase) ?? false
            );

            if (targetCommit == null)
            {
                const string error = "COMMIT_NOT_FOUND";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error,
                    message = $"Commit '{commit}' not found"
                };
            }

            // Get parent commit for diff
            var parentCommit = commits?.SkipWhile(c => c.Hash != targetCommit.Hash).Skip(1).FirstOrDefault();

            // PP13-99: Get actual diff information using DeltaDetectorV2
            var changes = await GetCommitChangesAsync(targetCommit.Hash!, parentCommit?.Hash, include_diff, diff_limit);

            // Get branches containing this commit
            var allBranches = await _doltCli.ListBranchesAsync();
            var containingBranches = allBranches?
                .Where(b => b.LastCommitHash == targetCommit.Hash)
                .Select(b => b.Name)
                .ToArray() ?? Array.Empty<string>();

            var response = new
            {
                success = true,
                commit = new
                {
                    hash = targetCommit.Hash ?? "",
                    short_hash = targetCommit.Hash?.Substring(0, Math.Min(7, targetCommit.Hash.Length)) ?? "",
                    message = targetCommit.Message ?? "",
                    author = targetCommit.Author ?? "",
                    timestamp = targetCommit.Date.ToString("O"),
                    parent_hash = parentCommit?.Hash ?? ""
                },
                changes = changes,
                branches = containingBranches,
                message = $"Commit '{targetCommit.Hash?.Substring(0, 7)}': {targetCommit.Message}"
            };
            
            ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName, 
                $"Successfully showed commit '{targetCommit.Hash?.Substring(0, 7)}': {targetCommit.Message}");
            return response;
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to show commit: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// PP13-99: Get actual diff information for a commit using DeltaDetectorV2.
    /// Returns change summary and document list for the commit.
    /// </summary>
    /// <param name="targetCommitHash">The commit to analyze</param>
    /// <param name="parentCommitHash">The parent commit (null for root commit)</param>
    /// <param name="includeDiff">Whether to include diff details</param>
    /// <param name="diffLimit">Maximum number of documents to include in the response</param>
    /// <returns>Object containing summary counts and document list</returns>
    private async Task<object> GetCommitChangesAsync(
        string targetCommitHash,
        string? parentCommitHash,
        bool includeDiff,
        int diffLimit)
    {
        // Default empty changes (used when include_diff=false or on error)
        var emptyChanges = new
        {
            summary = new
            {
                added = 0,
                modified = 0,
                deleted = 0,
                total = 0
            },
            documents = new List<object>()
        };

        // If include_diff is false, return empty changes
        if (!includeDiff)
        {
            return emptyChanges;
        }

        try
        {
            // Create DeltaDetectorV2 instance (same pattern as SyncManagerV2)
            var deltaDetector = new DeltaDetectorV2(
                _doltCli, _syncStateTracker, _doltConfig.RepositoryPath, logger: null);

            // Get all collection names to check for changes
            var collections = await deltaDetector.GetAvailableCollectionNamesAsync();

            if (!collections.Any())
            {
                _logger.LogDebug("PP13-99: No collections found in repository");
                return emptyChanges;
            }

            // Handle root commit (no parent) - all documents at target are "added"
            if (string.IsNullOrEmpty(parentCommitHash))
            {
                return await GetRootCommitChangesAsync(deltaDetector, targetCommitHash, collections, diffLimit);
            }

            // Normal commit: diff between parent and target
            var allDiffs = new List<DiffRowV2>();

            foreach (var collection in collections)
            {
                try
                {
                    var diffs = await deltaDetector.GetCommitDiffAsync(
                        parentCommitHash, targetCommitHash, collection);
                    allDiffs.AddRange(diffs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PP13-99: Failed to get diff for collection {Collection}", collection);
                    // Continue with other collections
                }
            }

            // Build summary counts (DeltaDetectorV2 uses "removed" instead of "deleted")
            var addedCount = allDiffs.Count(d => d.DiffType == "added");
            var modifiedCount = allDiffs.Count(d => d.DiffType == "modified");
            var deletedCount = allDiffs.Count(d => d.DiffType == "removed");

            // Apply limit to document list
            var limitedDiffs = allDiffs.Take(diffLimit).ToList();

            return new
            {
                summary = new
                {
                    added = addedCount,
                    modified = modifiedCount,
                    deleted = deletedCount,
                    total = addedCount + modifiedCount + deletedCount
                },
                documents = limitedDiffs.Select(d => new
                {
                    id = d.DocId,
                    collection = d.CollectionName,
                    change_type = d.DiffType == "removed" ? "deleted" : d.DiffType,
                    title = d.Title ?? ""
                }).ToList<object>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PP13-99: Failed to get diff for commit {Hash}", targetCommitHash);
            return emptyChanges;
        }
    }

    /// <summary>
    /// PP13-99: Handle root commit (no parent) - all documents at the commit are treated as "added"
    /// </summary>
    private async Task<object> GetRootCommitChangesAsync(
        DeltaDetectorV2 deltaDetector,
        string commitHash,
        List<string> collections,
        int diffLimit)
    {
        var documents = new List<object>();
        var addedCount = 0;

        foreach (var collection in collections)
        {
            if (documents.Count >= diffLimit)
                break;

            try
            {
                var collectionDocs = await deltaDetector.GetAllDocumentsAsync(collection);
                foreach (var doc in collectionDocs)
                {
                    addedCount++;
                    if (documents.Count < diffLimit)
                    {
                        documents.Add(new
                        {
                            id = doc.DocId,
                            collection = doc.CollectionName,
                            change_type = "added",
                            title = doc.Title ?? ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PP13-99: Failed to get documents for collection {Collection} at root commit", collection);
            }
        }

        return new
        {
            summary = new
            {
                added = addedCount,
                modified = 0,
                deleted = 0,
                total = addedCount
            },
            documents = documents
        };
    }
}