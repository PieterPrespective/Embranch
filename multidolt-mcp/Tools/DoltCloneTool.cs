using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Models;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that clones an existing Dolt repository from a remote
/// </summary>
[McpServerToolType]
public class DoltCloneTool
{
    private readonly ILogger<DoltCloneTool> _logger;
    private readonly IDoltCli _doltCli;
    private readonly ISyncManagerV2 _syncManager;

    /// <summary>
    /// Initializes a new instance of the DoltCloneTool class
    /// </summary>
    public DoltCloneTool(ILogger<DoltCloneTool> logger, IDoltCli doltCli, ISyncManagerV2 syncManager)
    {
        _logger = logger;
        _doltCli = doltCli;
        _syncManager = syncManager;
    }

    /// <summary>
    /// Clone an existing Dolt repository from DoltHub or another remote. This downloads the repository and populates the local ChromaDB with the documents from the specified branch/commit
    /// </summary>
    [McpServerTool]
    [Description("Clone an existing Dolt repository from DoltHub or another remote. This downloads the repository and populates the local ChromaDB with the documents from the specified branch/commit.")]
    public virtual async Task<object> DoltClone(string remote_url, string? branch = null, string? commit = null)
    {
        try
        {
            _logger.LogInformation($"[DoltCloneTool.DoltClone] Cloning from: {remote_url}, branch={branch}, commit={commit}");

            // First check if Dolt is available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                return new
                {
                    success = false,
                    error = "DOLT_EXECUTABLE_NOT_FOUND",
                    message = doltCheck.Error
                };
            }

            // Check if already initialized
            var isInitialized = await _doltCli.IsInitializedAsync();
            if (isInitialized)
            {
                return new
                {
                    success = false,
                    error = "ALREADY_INITIALIZED",
                    message = "Repository already exists. Use dolt_reset or manual cleanup."
                };
            }

            // Format the URL properly based on the input format
            string formattedUrl = remote_url;
            
            // Handle different URL formats
            if (!remote_url.StartsWith("http") && !remote_url.StartsWith("file://"))
            {
                // Check if it's a local file path (contains backslash or starts with drive letter)
                if (remote_url.Contains('\\') || remote_url.Contains(':') || remote_url.StartsWith('/'))
                {
                    // Local file path - use file:// protocol
                    // Convert backslashes to forward slashes and ensure proper file URI format
                    var normalizedPath = remote_url.Replace('\\', '/');
                    if (!normalizedPath.StartsWith('/'))
                    {
                        // Windows path like C:/path - needs three slashes after file:
                        formattedUrl = $"file:///{normalizedPath}";
                    }
                    else
                    {
                        // Unix-style path
                        formattedUrl = $"file://{normalizedPath}";
                    }
                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Formatted local path '{remote_url}' to '{formattedUrl}'");
                }
                else
                {
                    // Assume it's a DoltHub org/repo format
                    formattedUrl = $"https://doltremoteapi.dolthub.com/{remote_url}";
                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Formatted DoltHub repo '{remote_url}' to '{formattedUrl}'");
                }
            }

            // Clone the repository and check for success
            var cloneResult = await _doltCli.CloneAsync(formattedUrl, branch);
            bool isCloneSuccessful = cloneResult.Success;
            bool remoteConfigured = false;
            
            if (!cloneResult.Success)
            {
                _logger.LogWarning($"[DoltCloneTool.DoltClone] Clone operation failed: {cloneResult.Error}");
                _logger.LogInformation($"[DoltCloneTool.DoltClone] Clone command attempted with URL: '{formattedUrl}'");
                _logger.LogInformation($"[DoltCloneTool.DoltClone] Working directory: {Environment.CurrentDirectory}");
                
                // Check if the failure was due to empty repository (no Dolt data)
                bool isEmptyRepoError = cloneResult.Error?.Contains("no Dolt data", StringComparison.OrdinalIgnoreCase) == true;
                _logger.LogInformation($"[DoltCloneTool.DoltClone] Empty repository error detected: {isEmptyRepoError}");
                _logger.LogInformation($"[DoltCloneTool.DoltClone] Full error message: '{cloneResult.Error}'");
                
                if (isEmptyRepoError)
                {
                    _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Empty repository detected, attempting fallback: init + remote setup");
                    
                    string fallbackStep = "starting";
                    // Try fallback: initialize repository and manually add remote
                    try
                    {
                        // First, check if repository was partially initialized
                        fallbackStep = "checking initialization status";
                        _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback step: {fallbackStep}");
                        
                        var isPartiallyInitialized = await _doltCli.IsInitializedAsync();
                        _logger.LogInformation($"[DoltCloneTool.DoltClone] Repository initialization status: {isPartiallyInitialized}");
                        
                        if (!isPartiallyInitialized)
                        {
                            // Initialize a new repository
                            fallbackStep = "initializing repository";
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback step: {fallbackStep}");
                            
                            var initResult = await _doltCli.InitAsync();
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] Init result - Success: {initResult.Success}, Error: '{initResult.Error}', Output: '{initResult.Output}'");
                            
                            if (!initResult.Success)
                            {
                                _logger.LogError($"[DoltCloneTool.DoltClone] ❌ Fallback init failed at step '{fallbackStep}': {initResult.Error}");
                                throw new Exception($"Failed to initialize repository: {initResult.Error}");
                            }
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Repository initialized successfully");
                        }
                        else
                        {
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Repository already partially initialized, proceeding with remote setup");
                        }
                        
                        // Add the remote manually (similar to DoltInitTool)
                        fallbackStep = "adding remote origin";
                        _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback step: {fallbackStep} with URL: '{formattedUrl}'");
                        
                        var remoteResult = await _doltCli.AddRemoteAsync("origin", formattedUrl);
                        _logger.LogInformation($"[DoltCloneTool.DoltClone] Add remote result - Success: {remoteResult.Success}, Error: '{remoteResult.Error}', Output: '{remoteResult.Output}'");
                        
                        if (remoteResult.Success)
                        {
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Remote 'origin' configured successfully");
                            
                            // Create initial commit to make repository fully functional
                            fallbackStep = "creating initial commit";
                            _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback step: {fallbackStep}");
                            
                            try
                            {
                                // Create a temporary table to enable initial commit
                                await _doltCli.ExecuteAsync("CREATE TABLE IF NOT EXISTS __init_temp__ (id INT PRIMARY KEY, created_at DATETIME DEFAULT CURRENT_TIMESTAMP)");
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Created temporary initialization table");
                                
                                // Add and commit the table
                                var addResult = await _doltCli.AddAllAsync();
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] Add result - Success: {addResult.Success}, Error: '{addResult.Error}'");
                                
                                if (!addResult.Success)
                                {
                                    throw new Exception($"Failed to stage initial table: {addResult.Error}");
                                }
                                
                                var commitResult = await _doltCli.CommitAsync("Initial empty repository commit");
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] Initial commit result - Success: {commitResult.Success}, Hash: '{commitResult.CommitHash}', Message: '{commitResult.Message}'");
                                
                                if (!commitResult.Success)
                                {
                                    throw new Exception($"Failed to create initial commit: {commitResult.Message}");
                                }
                                
                                // Clean up the temporary table
                                await _doltCli.ExecuteAsync("DROP TABLE __init_temp__");
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Dropped temporary initialization table");
                                
                                // Add and commit the cleanup
                                var cleanupAddResult = await _doltCli.AddAllAsync();
                                if (cleanupAddResult.Success)
                                {
                                    var cleanupCommitResult = await _doltCli.CommitAsync("Cleaned up initialization table");
                                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Cleanup commit result - Success: {cleanupCommitResult.Success}, Hash: '{cleanupCommitResult.CommitHash}'");
                                }
                                
                                // Set up branch tracking (optional step - some Dolt versions may not support this)
                                fallbackStep = "setting up branch tracking";
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback step: {fallbackStep}");
                                
                                try
                                {
                                    var activeBranch = await _doltCli.GetCurrentBranchAsync();
                                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Current branch: {activeBranch}");
                                    
                                    // Note: Some versions of Dolt may not support branch tracking setup
                                    // This is optional and won't fail the entire operation if it doesn't work
                                }
                                catch (Exception trackingEx)
                                {
                                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Branch tracking setup not available: {trackingEx.Message}");
                                    // This is non-critical, continue without branch tracking
                                }
                                
                                remoteConfigured = true;
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Fallback successful: Repository initialized with initial commits and remote 'origin' configured");
                            }
                            catch (Exception commitEx)
                            {
                                _logger.LogError(commitEx, $"[DoltCloneTool.DoltClone] ❌ Failed to create initial commit: {commitEx.Message}");
                                throw new Exception($"Failed to create initial commit: {commitEx.Message}", commitEx);
                            }
                            
                            // Verify remote was actually added and repository state is correct
                            try
                            {
                                var remotesCheck = await _doltCli.ListRemotesAsync();
                                var remotesList = remotesCheck?.ToList() ?? new List<RemoteInfo>();
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Verification: Found {remotesList.Count} remotes configured");
                                foreach (var remote in remotesList)
                                {
                                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Remote: {remote.Name} -> {remote.Url}");
                                }
                                
                                // Verify we have commits
                                var headCommit = await _doltCli.GetHeadCommitHashAsync();
                                _logger.LogInformation($"[DoltCloneTool.DoltClone] ✓ Repository has HEAD commit: {headCommit}");
                            }
                            catch (Exception verifyEx)
                            {
                                _logger.LogWarning(verifyEx, $"[DoltCloneTool.DoltClone] Could not verify repository state: {verifyEx.Message}");
                            }
                        }
                        else
                        {
                            _logger.LogError($"[DoltCloneTool.DoltClone] ❌ Failed to configure remote at step '{fallbackStep}': {remoteResult.Error}");
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError(fallbackEx, $"[DoltCloneTool.DoltClone] ❌ Fallback initialization failed at step '{fallbackStep}': {fallbackEx.Message}");
                        
                        // Return specific fallback failure error
                        return new
                        {
                            success = false,
                            error = "FALLBACK_FAILED",
                            message = $"Repository is empty at '{formattedUrl}'. Attempted fallback initialization but failed at step '{fallbackStep}': {fallbackEx.Message}",
                            attempted_url = formattedUrl,
                            fallback_step = fallbackStep,
                            original_clone_error = cloneResult.Error
                        };
                    }
                    
                    // Log final fallback status
                    _logger.LogInformation($"[DoltCloneTool.DoltClone] Fallback completion status - Remote configured: {remoteConfigured}");
                }
                
                // If not an empty repo error, or fallback didn't succeed, return error
                if (!remoteConfigured)
                {
                    _logger.LogWarning($"[DoltCloneTool.DoltClone] Final error handling - Empty repo error: {isEmptyRepoError}, Remote configured: {remoteConfigured}");
                    
                    if (isEmptyRepoError)
                    {
                        // This should have been handled by fallback logic above, but something went wrong
                        _logger.LogError($"[DoltCloneTool.DoltClone] ❌ Empty repository detected but fallback did not succeed");
                        return new
                        {
                            success = false,
                            error = "FALLBACK_INCOMPLETE",
                            message = $"Repository is empty at '{formattedUrl}'. Fallback was attempted but remote configuration was not completed successfully.",
                            attempted_url = formattedUrl,
                            original_clone_error = cloneResult.Error
                        };
                    }
                    else
                    {
                        // Determine specific error type for non-empty repo failures
                        string errorCode = "CLONE_FAILED";
                        string errorMessage = cloneResult.Error ?? "Failed to clone repository";
                        
                        if (errorMessage.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                        {
                            errorCode = "AUTHENTICATION_FAILED";
                        }
                        else if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        {
                            errorCode = "REMOTE_NOT_FOUND";
                            errorMessage = $"Repository not found at '{formattedUrl}'";
                        }
                        else if (errorMessage.Contains("Invalid repository ID", StringComparison.OrdinalIgnoreCase))
                        {
                            errorCode = "INVALID_URL";
                            errorMessage = $"Invalid repository URL format: '{formattedUrl}'";
                        }
                        
                        _logger.LogInformation($"[DoltCloneTool.DoltClone] Returning non-empty repository error: {errorCode}");
                        
                        return new
                        {
                            success = false,
                            error = errorCode,
                            message = errorMessage,
                            attempted_url = formattedUrl,
                            clone_error = cloneResult.Error
                        };
                    }
                }
            }
            else
            {
                remoteConfigured = true; // Clone succeeded, remote should be set automatically
                _logger.LogInformation($"[DoltCloneTool.DoltClone] Clone succeeded from '{formattedUrl}'");
            }

            // Get current state after clone (handle empty repositories)
            string? currentBranch = null;
            string? currentCommitHash = null;
            
            try
            {
                currentBranch = await _doltCli.GetCurrentBranchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DoltCloneTool.DoltClone] Failed to get current branch, likely empty repository. Defaulting to 'main'");
                currentBranch = branch ?? "main"; // Use requested branch or default to 'main'
            }
            
            try
            {
                currentCommitHash = await _doltCli.GetHeadCommitHashAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DoltCloneTool.DoltClone] Failed to get current commit hash, likely empty repository");
                currentCommitHash = null; // Empty repository has no commits
            }
            
            // If specific commit requested, checkout to it
            if (!string.IsNullOrEmpty(commit))
            {
                try
                {
                    await _doltCli.CheckoutAsync(commit, false);
                    currentCommitHash = await _doltCli.GetHeadCommitHashAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to checkout commit: {commit}");
                    return new
                    {
                        success = false,
                        error = "COMMIT_NOT_FOUND",
                        message = $"Repository cloned but commit '{commit}' not found"
                    };
                }
            }

            // Get commit info (handle empty repositories)
            IEnumerable<CommitInfo>? commits = null;
            CommitInfo? currentCommit = null;
            
            try
            {
                commits = await _doltCli.GetLogAsync(1);
                currentCommit = commits?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DoltCloneTool.DoltClone] Failed to get commit log, likely empty repository");
                // Empty repository has no commits, this is fine
            }

            // Sync to ChromaDB
            int documentsLoaded = 0;
            List<string> collectionsCreated = new();
            
            try
            {
                // Perform full sync from Dolt to ChromaDB
                await _syncManager.FullSyncAsync();
                
                // TODO: Get actual counts from sync result
                documentsLoaded = 0; // Would need to be returned from FullSyncAsync
                collectionsCreated.Add(currentBranch ?? "main");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync to ChromaDB after clone");
            }

            return new
            {
                success = true,
                repository = new
                {
                    path = "./data/dolt-repo",
                    remote_url = formattedUrl
                },
                checkout = new
                {
                    branch = currentBranch ?? "main",
                    commit = new
                    {
                        hash = currentCommitHash ?? "",
                        message = currentCommit?.Message ?? "",
                        timestamp = currentCommit?.Date.ToString("O") ?? ""
                    }
                },
                sync_summary = new
                {
                    documents_loaded = documentsLoaded,
                    collections_created = collectionsCreated.ToArray()
                },
                message = !isCloneSuccessful 
                    ? $"Repository was empty at '{formattedUrl}'. Initialized local repository with initial commits and configured remote 'origin'. Repository is now ready for use."
                    : currentCommit is null 
                        ? $"Successfully cloned empty repository from '{formattedUrl}'. Repository has no commits yet."
                        : $"Successfully cloned repository from '{formattedUrl}' and synced {documentsLoaded} documents to ChromaDB"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error cloning repository from '{remote_url}'");
            
            // Determine error type
            string errorCode = "OPERATION_FAILED";
            if (ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                errorCode = "AUTHENTICATION_FAILED";
            else if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                errorCode = "REMOTE_NOT_FOUND";
            
            return new
            {
                success = false,
                error = errorCode,
                message = $"Failed to clone repository: {ex.Message}"
            };
        }
    }
}