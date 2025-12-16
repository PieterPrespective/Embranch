using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Models;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that manages branches in the Dolt repository - supports listing, creating, renaming, and deleting branches
/// </summary>
[McpServerToolType]
public class DoltBranchesTool
{
    private readonly ILogger<DoltBranchesTool> _logger;
    private readonly IDoltCli _doltCli;

    /// <summary>
    /// Initializes a new instance of the DoltBranchesTool class
    /// </summary>
    public DoltBranchesTool(ILogger<DoltBranchesTool> logger, IDoltCli doltCli)
    {
        _logger = logger;
        _doltCli = doltCli;
    }

    /// <summary>
    /// Manage branches in the Dolt repository - list, create, rename, or delete branches
    /// </summary>
    [McpServerTool]
    [Description("Manage branches in the Dolt repository. Actions: 'list' (default) - list branches with optional filter; 'create' - create new branch using filter as name; 'delete' - delete branch using filter as name; 'rename' - rename branch using filter as JSON {'old': 'oldname', 'new': 'newname', 'force': false}.")]
    public virtual async Task<object> DoltBranches(string action = "list", bool include_local = true, string? filter = null)
    {
        try
        {
            _logger.LogInformation($"[DoltBranchesTool.DoltBranches] Action: {action}, include_local={include_local}, filter={filter}");

            // Check if repository is initialized
            var isInitialized = await _doltCli.IsInitializedAsync();
            if (!isInitialized)
            {
                return new
                {
                    success = false,
                    error = "NOT_INITIALIZED",
                    message = "No Dolt repository configured. Use dolt_init or dolt_clone first."
                };
            }

            // Handle different actions
            switch (action.ToLowerInvariant())
            {
                case "list":
                    return await ListBranchesAsync(include_local, filter);
                
                case "create":
                    if (string.IsNullOrEmpty(filter))
                    {
                        return new
                        {
                            success = false,
                            error = "INVALID_PARAMETER",
                            message = "Branch name is required for create action. Provide it as 'filter' parameter."
                        };
                    }
                    return await CreateBranchAsync(filter);
                
                case "delete":
                    if (string.IsNullOrEmpty(filter))
                    {
                        return new
                        {
                            success = false,
                            error = "INVALID_PARAMETER", 
                            message = "Branch name is required for delete action. Provide it as 'filter' parameter."
                        };
                    }
                    return await DeleteBranchAsync(filter);
                
                case "rename":
                    if (string.IsNullOrEmpty(filter))
                    {
                        return new
                        {
                            success = false,
                            error = "INVALID_PARAMETER",
                            message = "Rename parameters required. Provide JSON as 'filter': {\"old\": \"oldname\", \"new\": \"newname\", \"force\": false}"
                        };
                    }
                    return await RenameBranchAsync(filter);
                
                default:
                    return new
                    {
                        success = false,
                        error = "INVALID_ACTION",
                        message = $"Unknown action '{action}'. Supported actions: 'list', 'create', 'delete', 'rename'"
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in branch operation");
            
            // Check if this is a dolt executable not found error
            if (ex.Message.Contains("Dolt executable not found") || 
                ex.Message.Contains("not found") ||
                ex.Message.Contains("cannot find"))
            {
                return new
                {
                    success = false,
                    error = "DOLT_EXECUTABLE_NOT_FOUND",
                    message = "Dolt executable not found. Please ensure Dolt is installed and added to PATH environment variable."
                };
            }
            
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to perform branch operation: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Lists branches in the repository with optional filtering
    /// </summary>
    private async Task<object> ListBranchesAsync(bool include_local, string? filter)
    {
        // Get current branch
        var currentBranch = await _doltCli.GetCurrentBranchAsync();

        // Get all branches
        var allBranches = await _doltCli.ListBranchesAsync();
        
        var branches = new List<object>();
        foreach (var branch in allBranches ?? Enumerable.Empty<BranchInfo>())
        {
            // Apply filter if provided
            if (!string.IsNullOrEmpty(filter))
            {
                if (!branch.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            branches.Add(new
            {
                name = branch.Name,
                is_current = branch.IsCurrent,
                is_local = true, // BranchInfo only has local branches
                is_remote = false, // BranchInfo only has local branches
                latest_commit = new
                {
                    hash = branch.LastCommitHash ?? "",
                    short_hash = branch.LastCommitHash?.Substring(0, Math.Min(7, branch.LastCommitHash.Length)) ?? "",
                    message = "", // TODO: Get commit message
                    timestamp = "" // TODO: Get commit timestamp
                },
                ahead = 0, // TODO: Calculate if local branch
                behind = 0  // TODO: Calculate if local branch
            });
        }

        return new
        {
            success = true,
            current_branch = currentBranch ?? "main",
            branches = branches.ToArray(),
            total_count = branches.Count,
            message = $"Found {branches.Count} branches"
        };
    }

    /// <summary>
    /// Creates a new branch with the specified name
    /// </summary>
    private async Task<object> CreateBranchAsync(string branchName)
    {
        var result = await _doltCli.CreateBranchAsync(branchName);
        
        if (result.Success)
        {
            return new
            {
                success = true,
                branch_name = branchName,
                message = $"Branch '{branchName}' created successfully"
            };
        }
        else
        {
            return new
            {
                success = false,
                error = "CREATE_FAILED",
                message = $"Failed to create branch '{branchName}': {result.Error}"
            };
        }
    }

    /// <summary>
    /// Deletes a branch with the specified name
    /// </summary>
    private async Task<object> DeleteBranchAsync(string branchName)
    {
        var result = await _doltCli.DeleteBranchAsync(branchName, false);
        
        if (result.Success)
        {
            return new
            {
                success = true,
                branch_name = branchName,
                message = $"Branch '{branchName}' deleted successfully"
            };
        }
        else
        {
            return new
            {
                success = false,
                error = "DELETE_FAILED",
                message = $"Failed to delete branch '{branchName}': {result.Error}"
            };
        }
    }

    /// <summary>
    /// Renames a branch using JSON parameters
    /// </summary>
    private async Task<object> RenameBranchAsync(string jsonParameters)
    {
        try
        {
            var renameParams = System.Text.Json.JsonSerializer.Deserialize<RenameParameters>(jsonParameters);
            
            if (string.IsNullOrEmpty(renameParams?.Old) || string.IsNullOrEmpty(renameParams?.New))
            {
                return new
                {
                    success = false,
                    error = "INVALID_PARAMETERS",
                    message = "Both 'old' and 'new' branch names are required. Format: {\"old\": \"oldname\", \"new\": \"newname\", \"force\": false}"
                };
            }

            var result = await _doltCli.RenameBranchAsync(renameParams.Old, renameParams.New, renameParams.Force ?? false);
            
            if (result.Success)
            {
                return new
                {
                    success = true,
                    old_name = renameParams.Old,
                    new_name = renameParams.New,
                    message = $"Branch '{renameParams.Old}' renamed to '{renameParams.New}' successfully"
                };
            }
            else
            {
                return new
                {
                    success = false,
                    error = "RENAME_FAILED",
                    message = $"Failed to rename branch '{renameParams.Old}' to '{renameParams.New}': {result.Error}"
                };
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            return new
            {
                success = false,
                error = "INVALID_JSON",
                message = $"Invalid JSON parameters: {ex.Message}. Expected format: {{\"old\": \"oldname\", \"new\": \"newname\", \"force\": false}}"
            };
        }
    }

    /// <summary>
    /// Parameters for branch rename operation
    /// </summary>
    private class RenameParameters
    {
        [System.Text.Json.Serialization.JsonPropertyName("old")]
        public string? Old { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("new")]
        public string? New { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("force")]
        public bool? Force { get; set; }
    }
}