using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using DMMS.Services;
using DMMS.Models;
using DMMS.Utilities;

namespace DMMS.Tools
{
    /// <summary>
    /// MCP tool for executing merge operations with conflict resolution
    /// Supports automatic resolution, field-level merging, and custom conflict resolution
    /// </summary>
    [McpServerToolType]
    public class ExecuteDoltMergeTool
    {
        private readonly ILogger<ExecuteDoltMergeTool> _logger;
        private readonly IDoltCli _doltCli;
        private readonly IMergeConflictResolver _conflictResolver;
        private readonly ISyncManagerV2 _syncManager;
        private readonly IConflictAnalyzer _conflictAnalyzer;

        /// <summary>
        /// Initializes a new instance of the ExecuteDoltMergeTool class
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="doltCli">Dolt CLI service for repository operations</param>
        /// <param name="conflictResolver">Service for resolving merge conflicts</param>
        /// <param name="syncManager">Sync manager for merge processing and ChromaDB sync</param>
        /// <param name="conflictAnalyzer">Service for analyzing conflicts during merge</param>
        public ExecuteDoltMergeTool(
            ILogger<ExecuteDoltMergeTool> logger,
            IDoltCli doltCli,
            IMergeConflictResolver conflictResolver,
            ISyncManagerV2 syncManager,
            IConflictAnalyzer conflictAnalyzer)
        {
            _logger = logger;
            _doltCli = doltCli;
            _conflictResolver = conflictResolver;
            _syncManager = syncManager;
            _conflictAnalyzer = conflictAnalyzer;
        }

        /// <summary>
        /// Execute a merge operation with specified conflict resolutions. Use preview_dolt_merge first to identify conflicts and their IDs.
        /// </summary>
        [McpServerTool]
        [Description("Execute a merge operation with specified conflict resolutions. Use preview_dolt_merge first to identify conflicts and their IDs.")]
        public async Task<object> ExecuteDoltMerge(
            string source_branch,
            string? target_branch = null,
            string? conflict_resolutions = null,
            bool auto_resolve_remaining = true,
            bool force_merge = false,
            string? merge_message = null)
        {
            const string toolName = nameof(ExecuteDoltMergeTool);
            const string methodName = nameof(ExecuteDoltMerge);

            try
            {
                ToolLoggingUtility.LogToolStart(_logger, toolName, methodName,
                    $"source: {source_branch}, target: {target_branch}, force: {force_merge}");

                // Validate required parameters
                if (string.IsNullOrWhiteSpace(source_branch))
                {
                    const string error = "source_branch parameter is required";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                    return new
                    {
                        success = false,
                        error = "INVALID_PARAMETERS",
                        message = error
                    };
                }

                // Validate Dolt availability
                var doltCheck = await _doltCli.CheckDoltAvailableAsync();
                if (!doltCheck.Success)
                {
                    const string error = "DOLT_NOT_AVAILABLE";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {doltCheck.Error}");
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
                    const string errorMessage = "No Dolt repository configured. Use dolt_init or dolt_clone first.";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                    return new
                    {
                        success = false,
                        error = error,
                        message = errorMessage
                    };
                }

                // Get current branch if target not specified
                if (string.IsNullOrEmpty(target_branch))
                {
                    target_branch = await _doltCli.GetCurrentBranchAsync();
                    ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Using current branch as target: {target_branch}");
                }

                // Parse conflict resolution preferences
                List<ConflictResolutionRequest>? resolutions = null;
                string? defaultStrategy = "ours";
                
                if (!string.IsNullOrEmpty(conflict_resolutions))
                {
                    try
                    {
                        var resolutionData = JsonSerializer.Deserialize<ConflictResolutionData>(
                            conflict_resolutions,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        resolutions = resolutionData?.Resolutions;
                        defaultStrategy = resolutionData?.DefaultStrategy ?? "ours";
                        
                        ToolLoggingUtility.LogToolInfo(_logger, toolName, 
                            $"Parsed {resolutions?.Count ?? 0} specific resolutions with default strategy: {defaultStrategy}");
                    }
                    catch (JsonException ex)
                    {
                        const string error = "INVALID_RESOLUTION_JSON";
                        var errorMessage = $"Failed to parse conflict resolutions: {ex.Message}";
                        ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                        return new
                        {
                            success = false,
                            error = error,
                            message = errorMessage
                        };
                    }
                }

                // Get state before merge
                var beforeCommit = await _doltCli.GetHeadCommitHashAsync();
                
                // Execute the merge using sync manager
                ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Starting merge from {source_branch} to {target_branch}");
                var mergeResult = await _syncManager.ProcessMergeAsync(source_branch, force_merge);

                // Track resolution statistics
                int resolvedCount = 0;
                int autoResolved = 0;
                int manuallyResolved = 0;

                // Handle conflicts if they exist
                if (mergeResult.HasConflicts && mergeResult.Conflicts.Any())
                {
                    ToolLoggingUtility.LogToolInfo(_logger, toolName, 
                        $"Processing {mergeResult.Conflicts.Count} merge conflicts");

                    // Get detailed conflicts for resolution
                    var detailedConflicts = await _conflictAnalyzer.GetDetailedConflictsAsync("documents");

                    // Apply user-specified resolutions first
                    if (resolutions != null && resolutions.Any())
                    {
                        foreach (var resolution in resolutions)
                        {
                            var conflict = detailedConflicts.FirstOrDefault(
                                c => c.ConflictId == resolution.ConflictId);
                            
                            if (conflict != null)
                            {
                                ToolLoggingUtility.LogToolInfo(_logger, toolName, 
                                    $"Applying user resolution for conflict {resolution.ConflictId}: {resolution.ResolutionType}");
                                
                                var resolved = await _conflictResolver.ResolveConflictAsync(conflict, resolution);
                                if (resolved)
                                {
                                    resolvedCount++;
                                    manuallyResolved++;
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to resolve conflict {ConflictId}", resolution.ConflictId);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Conflict ID {ConflictId} not found in merge conflicts", resolution.ConflictId);
                            }
                        }
                    }

                    // Auto-resolve remaining conflicts if requested
                    if (auto_resolve_remaining)
                    {
                        var unresolvedConflicts = detailedConflicts
                            .Where(c => resolutions == null || 
                                       !resolutions.Any(r => r.ConflictId == c.ConflictId))
                            .ToList();
                        
                        if (unresolvedConflicts.Any())
                        {
                            ToolLoggingUtility.LogToolInfo(_logger, toolName, 
                                $"Auto-resolving {unresolvedConflicts.Count} remaining conflicts");
                            
                            autoResolved = await _conflictResolver.AutoResolveConflictsAsync(unresolvedConflicts);
                            resolvedCount += autoResolved;
                        }
                    }

                    // Check if all conflicts are resolved
                    var remainingConflicts = await _doltCli.HasConflictsAsync();
                    if (remainingConflicts)
                    {
                        var unresolved = mergeResult.Conflicts.Count - resolvedCount;
                        const string error = "UNRESOLVED_CONFLICTS";
                        var errorMessage = $"Not all conflicts could be resolved: {unresolved} conflicts remain";
                        ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                        
                        return new
                        {
                            success = false,
                            error = error,
                            message = errorMessage,
                            conflicts_total = mergeResult.Conflicts.Count,
                            conflicts_resolved = resolvedCount,
                            conflicts_remaining = unresolved,
                            resolution_breakdown = new
                            {
                                manually_resolved = manuallyResolved,
                                auto_resolved = autoResolved
                            }
                        };
                    }

                    // Complete the merge with a commit
                    var commitMessage = merge_message ?? 
                        $"Merge {source_branch} into {target_branch ?? "current"} with {resolvedCount} conflicts resolved";
                    
                    ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Committing merge: {commitMessage}");
                    var commitResult = await _doltCli.CommitAsync(commitMessage);
                    
                    if (!commitResult.Success)
                    {
                        const string error = "MERGE_COMMIT_FAILED";
                        var errorMessage = $"Failed to commit merge: {commitResult.Message}";
                        ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                        return new
                        {
                            success = false,
                            error = error,
                            message = errorMessage
                        };
                    }
                }

                // Get final state after merge
                var afterCommit = await _doltCli.GetHeadCommitHashAsync();
                var mergeCommitHash = mergeResult.MergeCommitHash ?? afterCommit;

                // Build success response
                var response = new
                {
                    success = true,
                    merge_result = new
                    {
                        merge_commit = mergeCommitHash ?? "",
                        source_branch = source_branch,
                        target_branch = target_branch,
                        conflicts_total = mergeResult.Conflicts?.Count ?? 0,
                        conflicts_resolved = resolvedCount,
                        auto_resolved = autoResolved,
                        manually_resolved = manuallyResolved,
                        merge_timestamp = DateTime.UtcNow.ToString("O"),
                        before_commit = beforeCommit ?? "",
                        after_commit = afterCommit ?? ""
                    },
                    sync_result = new
                    {
                        collections_synced = mergeResult.CollectionsSynced ?? 0,
                        documents_added = mergeResult.Added,
                        documents_modified = mergeResult.Modified,
                        documents_deleted = mergeResult.Deleted,
                        chunks_processed = mergeResult.ChunksProcessed
                    },
                    auxiliary_tables_updated = new
                    {
                        sync_state = true,
                        local_changes = true,
                        sync_operations = true
                    },
                    message = GenerateSuccessMessage(mergeResult, resolvedCount, source_branch, target_branch)
                };

                ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
                    $"Merge completed: {resolvedCount} conflicts resolved, {mergeResult.Added + mergeResult.Modified + mergeResult.Deleted} documents synced");

                return response;
            }
            catch (Exception ex)
            {
                ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
                return new
                {
                    success = false,
                    error = "OPERATION_FAILED",
                    message = $"Failed to execute merge: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Generate an appropriate success message based on merge results
        /// </summary>
        private string GenerateSuccessMessage(Services.MergeSyncResultV2 mergeResult, int resolvedCount, string sourceBranch, string targetBranch)
        {
            if (!mergeResult.HasConflicts || !mergeResult.Conflicts.Any())
            {
                return $"Successfully merged {sourceBranch} into {targetBranch} with no conflicts";
            }
            
            var totalChanges = mergeResult.Added + mergeResult.Modified + mergeResult.Deleted;
            return $"Successfully merged {sourceBranch} into {targetBranch} with {resolvedCount} conflicts resolved and {totalChanges} documents synchronized";
        }
    }
}