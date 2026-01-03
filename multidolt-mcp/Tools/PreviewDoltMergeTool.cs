using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Models;
using DMMS.Utilities;

namespace DMMS.Tools
{
    /// <summary>
    /// MCP tool for previewing merge operations before execution
    /// Provides detailed conflict analysis and auto-resolution identification
    /// </summary>
    [McpServerToolType]
    public class PreviewDoltMergeTool
    {
        private readonly ILogger<PreviewDoltMergeTool> _logger;
        private readonly IDoltCli _doltCli;
        private readonly IConflictAnalyzer _conflictAnalyzer;
        private readonly ISyncManagerV2 _syncManager;

        /// <summary>
        /// Initializes a new instance of the PreviewDoltMergeTool class
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="doltCli">Dolt CLI service for repository operations</param>
        /// <param name="conflictAnalyzer">Service for analyzing merge conflicts</param>
        /// <param name="syncManager">Sync manager for checking local changes</param>
        public PreviewDoltMergeTool(
            ILogger<PreviewDoltMergeTool> logger,
            IDoltCli doltCli,
            IConflictAnalyzer conflictAnalyzer,
            ISyncManagerV2 syncManager)
        {
            _logger = logger;
            _doltCli = doltCli;
            _conflictAnalyzer = conflictAnalyzer;
            _syncManager = syncManager;
        }

        /// <summary>
        /// Preview a merge operation to see potential conflicts and changes before executing. Returns detailed conflict information if conflicts would occur.
        /// </summary>
        [McpServerTool]
        [Description("Preview a merge operation to see potential conflicts and changes before executing. Returns detailed conflict information if conflicts would occur.")]
        public async Task<object> PreviewDoltMerge(
            string source_branch,
            string? target_branch = null,
            bool include_auto_resolvable = false,
            bool detailed_diff = false)
        {
            const string toolName = nameof(PreviewDoltMergeTool);
            const string methodName = nameof(PreviewDoltMerge);

            try
            {
                ToolLoggingUtility.LogToolStart(_logger, toolName, methodName,
                    $"source: {source_branch}, target: {target_branch}, auto_resolvable: {include_auto_resolvable}");

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

                // Verify branches exist
                var branches = await _doltCli.ListBranchesAsync();
                var branchNames = branches.Select(b => b.Name).ToHashSet();
                
                if (!branchNames.Contains(source_branch))
                {
                    const string error = "SOURCE_BRANCH_NOT_FOUND";
                    var errorMessage = $"Source branch '{source_branch}' does not exist";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                    return new
                    {
                        success = false,
                        error = error,
                        message = errorMessage,
                        available_branches = branchNames.ToList()
                    };
                }

                if (!branchNames.Contains(target_branch))
                {
                    const string error = "TARGET_BRANCH_NOT_FOUND";
                    var errorMessage = $"Target branch '{target_branch}' does not exist";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                    return new
                    {
                        success = false,
                        error = error,
                        message = errorMessage,
                        available_branches = branchNames.ToList()
                    };
                }

                // Check for local changes
                var localChanges = await _syncManager.GetLocalChangesAsync();
                
                ToolLoggingUtility.LogToolInfo(_logger, toolName, 
                    $"Analyzing merge from {source_branch} to {target_branch}");

                // Analyze the merge
                var mergePreview = await _conflictAnalyzer.AnalyzeMergeAsync(
                    source_branch, 
                    target_branch,
                    include_auto_resolvable,
                    detailed_diff);

                if (!mergePreview.Success)
                {
                    const string error = "ANALYSIS_FAILED";
                    ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {mergePreview.Message}");
                    return new
                    {
                        success = false,
                        error = error,
                        message = mergePreview.Message
                    };
                }

                // Build response structure
                var response = new
                {
                    success = true,
                    can_auto_merge = mergePreview.CanAutoMerge,
                    source_branch = source_branch,
                    target_branch = target_branch,
                    merge_preview = new
                    {
                        has_conflicts = mergePreview.TotalConflictsDetected > 0,
                        total_conflicts = mergePreview.TotalConflictsDetected,
                        auto_resolvable = mergePreview.Conflicts?.Count(c => c.AutoResolvable) ?? 0,
                        requires_manual = mergePreview.Conflicts?.Count(c => !c.AutoResolvable) ?? 0,
                        affected_collections = mergePreview.Conflicts?
                            .Select(c => c.Collection)
                            .Distinct()
                            .OrderBy(c => c)
                            .ToList() ?? new List<string>(),
                        changes_preview = new
                        {
                            documents_added = mergePreview.Preview?.DocumentsAdded ?? 0,
                            documents_modified = mergePreview.Preview?.DocumentsModified ?? 0,
                            documents_deleted = mergePreview.Preview?.DocumentsDeleted ?? 0,
                            collections_affected = mergePreview.Preview?.CollectionsAffected ?? 0
                        }
                    },
                    conflicts = mergePreview.Conflicts?.Select(c => (object)new
                    {
                        conflict_id = c.ConflictId,
                        collection = c.Collection,
                        document_id = c.DocumentId,
                        conflict_type = c.Type.ToString().ToLowerInvariant(),
                        auto_resolvable = c.AutoResolvable,
                        suggested_resolution = c.SuggestedResolution,
                        field_conflicts = c.FieldConflicts?.Select(fc => (object)new
                        {
                            field = fc.FieldName,
                            base_value = detailed_diff ? fc.BaseValue : null,
                            our_value = detailed_diff ? fc.OurValue : null,
                            their_value = detailed_diff ? fc.TheirValue : null,
                            base_hash = fc.BaseHash,
                            our_hash = fc.OurHash,
                            their_hash = fc.TheirHash,
                            can_auto_merge = fc.CanAutoMerge
                        }).ToList() ?? new List<object>(),
                        resolution_options = c.ResolutionOptions ?? new List<string>()
                    }).ToList() ?? new List<object>(),
                    auxiliary_table_status = new
                    {
                        sync_state_conflict = mergePreview.AuxiliaryStatus?.SyncStateConflict ?? false,
                        local_changes_conflict = mergePreview.AuxiliaryStatus?.LocalChangesConflict ?? false,
                        sync_operations_conflict = mergePreview.AuxiliaryStatus?.SyncOperationsConflict ?? false,
                        local_changes_present = localChanges?.HasChanges ?? false,
                        local_changes_count = localChanges?.TotalChanges ?? 0
                    },
                    recommended_action = mergePreview.RecommendedAction,
                    message = mergePreview.Message
                };

                var conflictCount = mergePreview.Conflicts?.Count ?? 0;
                var autoResolvableCount = mergePreview.Conflicts?.Count(c => c.AutoResolvable) ?? 0;
                
                ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
                    $"Preview complete: {conflictCount} conflicts ({autoResolvableCount} auto-resolvable)");

                return response;
            }
            catch (Exception ex)
            {
                ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
                return new
                {
                    success = false,
                    error = "OPERATION_FAILED",
                    message = $"Failed to preview merge: {ex.Message}"
                };
            }
        }
    }
}