using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Utilities;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that commits the current ChromaDB state to the Dolt repository
/// </summary>
[McpServerToolType]
public class DoltCommitTool
{
    private readonly ILogger<DoltCommitTool> _logger;
    private readonly IDoltCli _doltCli;
    private readonly ISyncManagerV2 _syncManager;

    /// <summary>
    /// Initializes a new instance of the DoltCommitTool class
    /// </summary>
    public DoltCommitTool(ILogger<DoltCommitTool> logger, IDoltCli doltCli, ISyncManagerV2 syncManager)
    {
        _logger = logger;
        _doltCli = doltCli;
        _syncManager = syncManager;
    }

    /// <summary>
    /// Commit the current state of ChromaDB to the Dolt repository, creating a new version that can be pushed, shared, or reverted to
    /// </summary>
    [McpServerTool]
    [Description("Commit the current state of ChromaDB to the Dolt repository, creating a new version that can be pushed, shared, or reverted to.")]
    public virtual async Task<object> DoltCommit(string message, string? author = null)
    {
        const string toolName = nameof(DoltCommitTool);
        const string methodName = nameof(DoltCommit);
        
        try
        {
            ToolLoggingUtility.LogToolStart(_logger, toolName, methodName, 
                $"Message: '{message}', Author: '{author ?? "default"}''");

            // Validate message
            if (string.IsNullOrWhiteSpace(message))
            {
                const string error = "Commit message is required";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = "MESSAGE_REQUIRED",
                    message = error
                };
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Creating commit with message: {message}");

            // First check if Dolt is available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                const string error = "DOLT_EXECUTABLE_NOT_FOUND";
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


            // Check for local changes
            var localChanges = await _syncManager.GetLocalChangesAsync();
            if (localChanges == null || !localChanges.HasChanges)
            {
                const string error = "NO_CHANGES";
                const string errorMessage = "Nothing to commit (no local changes)";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                return new
                {
                    success = false,
                    error = error,
                    message = errorMessage
                };
            }

            // Get parent commit info
            var parentHash = await _doltCli.GetHeadCommitHashAsync();

            // Commit using sync manager (auto-stage enabled)
            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Processing commit with {localChanges.TotalChanges} changes");
            var commitResult = await _syncManager.ProcessCommitAsync(message, true, false);
            
            if (!commitResult.Success)
            {
                const string error = "COMMIT_FAILED";
                var errorMessage = commitResult.ErrorMessage ?? "Failed to create commit";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"{error}: {errorMessage}");
                return new
                {
                    success = false,
                    error = error,
                    message = errorMessage
                };
            }

            // Get new commit info
            var newCommitHash = await _doltCli.GetHeadCommitHashAsync();
            var timestamp = DateTime.UtcNow;

            var response = new
            {
                success = true,
                commit = new
                {
                    hash = newCommitHash ?? "",
                    short_hash = newCommitHash?.Substring(0, Math.Min(7, newCommitHash.Length)) ?? "",
                    message = message,
                    author = author ?? "user@example.com",
                    timestamp = timestamp.ToString("O"),
                    parent_hash = parentHash ?? ""
                },
                changes_committed = new
                {
                    added = localChanges.NewDocuments?.Count ?? 0,
                    modified = localChanges.ModifiedDocuments?.Count ?? 0,
                    deleted = localChanges.DeletedDocuments?.Count ?? 0,
                    total = localChanges.TotalChanges
                },
                message = $"Created commit {newCommitHash?.Substring(0, 7)} with {localChanges.TotalChanges} document changes."
            };

            var resultMessage = $"Created commit {newCommitHash?.Substring(0, 7)} with {localChanges.TotalChanges} changes";
            ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName, resultMessage);
            return response;
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to create commit: {ex.Message}"
            };
        }
    }
}