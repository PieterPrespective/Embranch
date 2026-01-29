using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-87: Implementation of repository bootstrapping from manifest.
/// Handles cloning Dolt repository, initializing Chroma, syncing to manifest state,
/// and fixing path misalignment issues.
/// </summary>
public class RepositoryBootstrapper : IRepositoryBootstrapper
{
    private readonly ILogger<RepositoryBootstrapper> _logger;
    private readonly IEmbranchStateManifest _manifestService;
    private readonly IDoltCli _doltCli;
    private readonly ISyncManagerV2 _syncManager;
    private readonly IRepositoryStateDetector _stateDetector;
    private readonly DoltConfiguration _doltConfig;
    private readonly ServerConfiguration _serverConfig;

    public RepositoryBootstrapper(
        ILogger<RepositoryBootstrapper> logger,
        IEmbranchStateManifest manifestService,
        IDoltCli doltCli,
        ISyncManagerV2 syncManager,
        IRepositoryStateDetector stateDetector,
        IOptions<DoltConfiguration> doltConfig,
        IOptions<ServerConfiguration> serverConfig)
    {
        _logger = logger;
        _manifestService = manifestService;
        _doltCli = doltCli;
        _syncManager = syncManager;
        _stateDetector = stateDetector;
        _doltConfig = doltConfig.Value;
        _serverConfig = serverConfig.Value;
    }

    /// <inheritdoc />
    public async Task<BootstrapResult> BootstrapFromManifestAsync(string projectRoot, BootstrapOptions options)
    {
        _logger.LogInformation("[RepositoryBootstrapper.BootstrapFromManifestAsync] Starting bootstrap from manifest at: {ProjectRoot}", projectRoot);

        var actions = new List<string>();

        try
        {
            // Step 1: Load and validate manifest
            var manifest = await _manifestService.ReadManifestAsync(projectRoot);
            if (manifest == null)
            {
                return new BootstrapResult
                {
                    Success = false,
                    ResultingState = RepositoryState.Uninitialized,
                    Message = "No manifest found. Use DoltClone to clone from a remote, or DoltInit to create a new repository.",
                    ErrorDetail = "Manifest file (.dmms/state.json) not found at project root."
                };
            }

            // Step 2: Check current state
            var currentState = await _stateDetector.AnalyzeStateAsync(projectRoot);

            // If already ready, nothing to do
            if (currentState.State == RepositoryState.Ready)
            {
                return new BootstrapResult
                {
                    Success = true,
                    ResultingState = RepositoryState.Ready,
                    Message = "Repository already initialized and ready.",
                    ActionsPerformed = new[] { "Verified existing infrastructure" },
                    CommitHash = currentState.DoltInfo?.CurrentCommit,
                    BranchName = currentState.DoltInfo?.CurrentBranch
                };
            }

            // Step 3: Handle path misalignment first if present
            // PP13-87-C1: Set effective path on DoltCli before operations
            if (currentState.State == RepositoryState.PathMisaligned_DoltNested && currentState.PathIssue != null)
            {
                if (options.PathFixStrategy.HasValue)
                {
                    var fixResult = await FixPathMisalignmentAsync(projectRoot, currentState.PathIssue, options.PathFixStrategy.Value);
                    if (!fixResult.Success)
                    {
                        return new BootstrapResult
                        {
                            Success = false,
                            ResultingState = RepositoryState.PathMisaligned_DoltNested,
                            Message = $"Failed to fix path misalignment: {fixResult.Message}",
                            ErrorDetail = fixResult.ErrorDetail
                        };
                    }
                    actions.Add($"Fixed path misalignment using {fixResult.StrategyUsed} strategy");
                }
                else if (!string.IsNullOrEmpty(currentState.PathIssue.ActualDotDoltLocation))
                {
                    // PP13-87-C1: Set effective path on DoltCli to enable operations
                    _doltCli.SetEffectiveRepositoryPath(currentState.PathIssue.ActualDotDoltLocation);
                    _logger.LogInformation("[RepositoryBootstrapper.BootstrapFromManifestAsync] Set effective path to: {Path}",
                        currentState.PathIssue.ActualDotDoltLocation);
                    actions.Add($"Using nested Dolt path: {currentState.PathIssue.ActualDotDoltLocation}");
                }
            }
            else if (currentState.DoltInfo != null && currentState.DoltInfo.IsNested)
            {
                // PP13-87-C1: Even if not in PathMisaligned state, set effective path if nested
                _doltCli.SetEffectiveRepositoryPath(currentState.DoltInfo.Path);
                _logger.LogInformation("[RepositoryBootstrapper.BootstrapFromManifestAsync] Set effective path for nested repo: {Path}",
                    currentState.DoltInfo.Path);
            }

            // Step 3.5: PP13-87-C2: Sync to manifest branch/commit for any state where Dolt exists
            // This handles PathMisaligned states after path fix, and any other state where
            // Dolt is present but may be on wrong branch/commit
            if (currentState.DoltInfo != null && options.SyncToManifestCommit)
            {
                var (syncSuccess, syncError) = await SyncToManifestStateAsync(manifest);
                if (!syncSuccess)
                {
                    _logger.LogWarning("[RepositoryBootstrapper.BootstrapFromManifestAsync] Could not sync to manifest state: {Error}. Continuing.", syncError);
                    actions.Add($"Could not sync to manifest state: {syncError}");
                }
                else if (!string.IsNullOrEmpty(syncError))
                {
                    // syncError contains the action summary when successful
                    actions.Add(syncError);
                }
            }

            // Step 4: Bootstrap Dolt if needed
            if (options.BootstrapDolt &&
                (currentState.State == RepositoryState.ManifestOnly_NeedsFullBootstrap ||
                 currentState.State == RepositoryState.ManifestOnly_NeedsDoltBootstrap))
            {
                if (string.IsNullOrEmpty(manifest.Dolt.RemoteUrl))
                {
                    return new BootstrapResult
                    {
                        Success = false,
                        ResultingState = currentState.State,
                        Message = "Cannot bootstrap: No remote URL configured in manifest.",
                        ActionsPerformed = actions.ToArray(),
                        ErrorDetail = "Use ManifestSetRemote to configure a remote URL, or DoltClone to clone directly."
                    };
                }

                var (cloneSuccess, cloneError) = await CloneDoltRepositoryAsync(manifest.Dolt.RemoteUrl, _doltConfig.RepositoryPath);
                if (!cloneSuccess)
                {
                    return new BootstrapResult
                    {
                        Success = false,
                        ResultingState = currentState.State,
                        Message = $"Failed to clone Dolt repository: {cloneError}",
                        ActionsPerformed = actions.ToArray(),
                        ErrorDetail = cloneError
                    };
                }
                actions.Add($"Cloned Dolt repository from {manifest.Dolt.RemoteUrl}");

                // Sync to manifest commit if specified
                if (options.SyncToManifestCommit && !string.IsNullOrEmpty(manifest.Dolt.CurrentCommit))
                {
                    var (syncSuccess, syncError) = await SyncToCommitAsync(manifest.Dolt.CurrentCommit);
                    if (!syncSuccess)
                    {
                        _logger.LogWarning("[RepositoryBootstrapper.BootstrapFromManifestAsync] Could not sync to manifest commit: {Error}. Continuing with HEAD.", syncError);
                        actions.Add($"Could not sync to manifest commit {manifest.Dolt.CurrentCommit[..Math.Min(8, manifest.Dolt.CurrentCommit.Length)]}: {syncError}");
                    }
                    else
                    {
                        actions.Add($"Synced to manifest commit {manifest.Dolt.CurrentCommit[..Math.Min(8, manifest.Dolt.CurrentCommit.Length)]}");
                    }
                }
                else if (!string.IsNullOrEmpty(manifest.Dolt.CurrentBranch))
                {
                    // Checkout the branch
                    try
                    {
                        var checkoutResult = await _doltCli.CheckoutAsync(manifest.Dolt.CurrentBranch);
                        if (checkoutResult.Success)
                        {
                            actions.Add($"Checked out branch {manifest.Dolt.CurrentBranch}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[RepositoryBootstrapper.BootstrapFromManifestAsync] Could not checkout branch: {Branch}", manifest.Dolt.CurrentBranch);
                    }
                }
            }

            // Step 5: Bootstrap Chroma if needed
            if (options.BootstrapChroma &&
                (currentState.State == RepositoryState.ManifestOnly_NeedsFullBootstrap ||
                 currentState.State == RepositoryState.ManifestOnly_NeedsChromaBootstrap))
            {
                var (chromaSuccess, collectionCount, chromaError) = await InitializeChromaAsync(projectRoot);
                if (!chromaSuccess)
                {
                    _logger.LogWarning("[RepositoryBootstrapper.BootstrapFromManifestAsync] Chroma initialization had issues: {Error}", chromaError);
                    actions.Add($"Chroma initialization warning: {chromaError}");
                }
                else
                {
                    actions.Add($"Initialized Chroma directory with {collectionCount} collections synced");
                }
            }

            // Step 6: Create work branch if requested
            if (options.CreateWorkBranch && !string.IsNullOrEmpty(options.WorkBranchName))
            {
                try
                {
                    var branchResult = await _doltCli.CreateBranchAsync(options.WorkBranchName);
                    if (branchResult.Success)
                    {
                        var checkoutResult = await _doltCli.CheckoutAsync(options.WorkBranchName);
                        if (checkoutResult.Success)
                        {
                            actions.Add($"Created and checked out work branch {options.WorkBranchName}");
                        }
                        else
                        {
                            actions.Add($"Created work branch {options.WorkBranchName} (checkout failed: {checkoutResult.Error})");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[RepositoryBootstrapper.BootstrapFromManifestAsync] Could not create work branch: {Error}", branchResult.Error);
                        actions.Add($"Could not create work branch: {branchResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RepositoryBootstrapper.BootstrapFromManifestAsync] Error creating work branch");
                }
            }

            // Step 7: Get final state
            string? finalCommit = null;
            string? finalBranch = null;
            try
            {
                finalCommit = await _doltCli.GetHeadCommitHashAsync();
                finalBranch = await _doltCli.GetCurrentBranchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RepositoryBootstrapper.BootstrapFromManifestAsync] Could not get final state");
            }

            _logger.LogInformation("[RepositoryBootstrapper.BootstrapFromManifestAsync] Bootstrap complete. Actions: {Actions}", string.Join(", ", actions));

            return new BootstrapResult
            {
                Success = true,
                ResultingState = RepositoryState.Ready,
                Message = "Repository bootstrapped successfully from manifest.",
                ActionsPerformed = actions.ToArray(),
                CommitHash = finalCommit,
                BranchName = finalBranch
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.BootstrapFromManifestAsync] Bootstrap failed with exception");

            return new BootstrapResult
            {
                Success = false,
                ResultingState = RepositoryState.Inconsistent,
                Message = $"Bootstrap failed: {ex.Message}",
                ActionsPerformed = actions.ToArray(),
                ErrorDetail = ex.ToString()
            };
        }
    }

    /// <inheritdoc />
    public async Task<PathFixResult> FixPathMisalignmentAsync(string projectRoot, PathMismatchInfo mismatch, PathFixStrategy strategy)
    {
        _logger.LogInformation("[RepositoryBootstrapper.FixPathMisalignmentAsync] Fixing path misalignment with strategy: {Strategy}", strategy);

        try
        {
            switch (strategy)
            {
                case PathFixStrategy.MoveToConfiguredPath:
                    return await MoveRepositoryToConfiguredPathAsync(mismatch);

                case PathFixStrategy.UpdateConfiguration:
                    // Note: We can't actually update environment variables at runtime
                    // This strategy would require user intervention
                    return new PathFixResult
                    {
                        Success = true,
                        StrategyUsed = strategy,
                        Message = $"Path resolution will use effective path. To permanently fix, update DOLT_REPOSITORY_PATH to '{mismatch.ActualDotDoltLocation}'",
                        NewPath = mismatch.ActualDotDoltLocation
                    };

                case PathFixStrategy.CloneFreshDiscardMisaligned:
                    return await CloneFreshAndDiscardAsync(projectRoot, mismatch);

                default:
                    return new PathFixResult
                    {
                        Success = false,
                        StrategyUsed = strategy,
                        Message = "Unknown path fix strategy",
                        ErrorDetail = $"Strategy {strategy} is not implemented"
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.FixPathMisalignmentAsync] Error fixing path misalignment");
            return new PathFixResult
            {
                Success = false,
                StrategyUsed = strategy,
                Message = $"Path fix failed: {ex.Message}",
                ErrorDetail = ex.ToString()
            };
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> CloneDoltRepositoryAsync(string remoteUrl, string targetPath)
    {
        _logger.LogInformation("[RepositoryBootstrapper.CloneDoltRepositoryAsync] Cloning from {RemoteUrl} to {TargetPath}", remoteUrl, targetPath);

        try
        {
            // Ensure target directory exists
            var absolutePath = Path.GetFullPath(targetPath);
            if (!Directory.Exists(absolutePath))
            {
                Directory.CreateDirectory(absolutePath);
            }

            // Check if target already has content
            var dotDoltPath = Path.Combine(absolutePath, ".dolt");
            if (Directory.Exists(dotDoltPath))
            {
                return (false, "Target path already contains a Dolt repository. Use force=true to overwrite.");
            }

            // Execute clone
            var result = await _doltCli.CloneAsync(remoteUrl);

            if (!result.Success)
            {
                return (false, result.Error ?? "Clone operation failed");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.CloneDoltRepositoryAsync] Clone failed");
            return (false, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, int CollectionCount, string? Error)> InitializeChromaAsync(string projectRoot)
    {
        _logger.LogInformation("[RepositoryBootstrapper.InitializeChromaAsync] Initializing Chroma for project: {ProjectRoot}", projectRoot);

        try
        {
            // Ensure Chroma data directory exists
            var chromaPath = Path.GetFullPath(_serverConfig.ChromaDataPath);
            if (!Directory.Exists(chromaPath))
            {
                Directory.CreateDirectory(chromaPath);
                _logger.LogDebug("[RepositoryBootstrapper.InitializeChromaAsync] Created Chroma directory: {Path}", chromaPath);
            }

            // Perform full sync from Dolt to Chroma
            var syncResult = await _syncManager.FullSyncAsync(collectionName: null, forceSync: true);

            if (!syncResult.Success)
            {
                return (false, 0, syncResult.ErrorMessage ?? "Sync operation failed");
            }

            return (true, syncResult.TotalChanges, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.InitializeChromaAsync] Chroma initialization failed");
            return (false, 0, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> SyncToCommitAsync(string commitHash)
    {
        _logger.LogInformation("[RepositoryBootstrapper.SyncToCommitAsync] Syncing to commit: {CommitHash}", commitHash[..Math.Min(8, commitHash.Length)]);

        try
        {
            var result = await _doltCli.CheckoutAsync(commitHash);

            if (!result.Success)
            {
                return (false, result.Error ?? "Checkout failed");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.SyncToCommitAsync] Sync to commit failed");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// PP13-87-C2: Syncs the Dolt repository to the state specified in the manifest.
    /// Handles both branch checkout and commit alignment.
    /// </summary>
    /// <param name="manifest">The manifest containing target branch/commit state</param>
    /// <returns>Tuple of (Success, ActionSummary or ErrorMessage)</returns>
    private async Task<(bool Success, string? Message)> SyncToManifestStateAsync(DmmsManifest manifest)
    {
        var actions = new List<string>();

        try
        {
            // Step 1: Checkout manifest branch if different from current
            if (!string.IsNullOrEmpty(manifest.Dolt.CurrentBranch))
            {
                var currentBranch = await _doltCli.GetCurrentBranchAsync();
                if (currentBranch != manifest.Dolt.CurrentBranch)
                {
                    _logger.LogInformation("[RepositoryBootstrapper.SyncToManifestStateAsync] Switching from branch '{Current}' to '{Target}'",
                        currentBranch, manifest.Dolt.CurrentBranch);

                    var checkoutResult = await _doltCli.CheckoutAsync(manifest.Dolt.CurrentBranch);
                    if (!checkoutResult.Success)
                    {
                        // Try to fetch and checkout if branch doesn't exist locally
                        try
                        {
                            await _doltCli.FetchAsync();
                            checkoutResult = await _doltCli.CheckoutAsync(manifest.Dolt.CurrentBranch);
                        }
                        catch (Exception fetchEx)
                        {
                            _logger.LogWarning(fetchEx, "[RepositoryBootstrapper.SyncToManifestStateAsync] Fetch failed, checkout may fail");
                        }

                        if (!checkoutResult.Success)
                        {
                            return (false, $"Failed to checkout branch '{manifest.Dolt.CurrentBranch}': {checkoutResult.Error}");
                        }
                    }
                    actions.Add($"Checked out branch '{manifest.Dolt.CurrentBranch}'");
                }
            }

            // Step 2: Reset to manifest commit if specified and different from current
            if (!string.IsNullOrEmpty(manifest.Dolt.CurrentCommit))
            {
                var currentCommit = await _doltCli.GetHeadCommitHashAsync();
                if (currentCommit != manifest.Dolt.CurrentCommit)
                {
                    _logger.LogInformation("[RepositoryBootstrapper.SyncToManifestStateAsync] Syncing from commit '{Current}' to '{Target}'",
                        currentCommit?[..Math.Min(8, currentCommit?.Length ?? 0)],
                        manifest.Dolt.CurrentCommit[..Math.Min(8, manifest.Dolt.CurrentCommit.Length)]);

                    var (success, error) = await SyncToCommitAsync(manifest.Dolt.CurrentCommit);
                    if (!success)
                    {
                        return (false, $"Failed to sync to commit '{manifest.Dolt.CurrentCommit[..Math.Min(8, manifest.Dolt.CurrentCommit.Length)]}': {error}");
                    }
                    actions.Add($"Synced to commit '{manifest.Dolt.CurrentCommit[..Math.Min(8, manifest.Dolt.CurrentCommit.Length)]}'");
                }
            }

            if (actions.Count == 0)
            {
                return (true, null); // Already in sync, no action needed
            }

            return (true, string.Join("; ", actions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryBootstrapper.SyncToManifestStateAsync] Failed to sync to manifest state");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Moves repository contents from nested directory to configured path
    /// </summary>
    private async Task<PathFixResult> MoveRepositoryToConfiguredPathAsync(PathMismatchInfo mismatch)
    {
        try
        {
            var sourcePath = mismatch.ActualDotDoltLocation;
            var targetPath = Path.GetFullPath(mismatch.ConfiguredPath);

            if (string.IsNullOrEmpty(sourcePath))
            {
                return new PathFixResult
                {
                    Success = false,
                    StrategyUsed = PathFixStrategy.MoveToConfiguredPath,
                    Message = "Source path not available",
                    ErrorDetail = "ActualDotDoltLocation is null"
                };
            }

            // Get directory name (repository name from CLI clone)
            var repoName = Path.GetFileName(sourcePath);

            _logger.LogInformation("[RepositoryBootstrapper.MoveRepositoryToConfiguredPathAsync] Moving from {Source} to {Target}", sourcePath, targetPath);

            // Move contents of nested directory to target
            var sourceFiles = Directory.GetFiles(sourcePath);
            var sourceDirs = Directory.GetDirectories(sourcePath);

            foreach (var file in sourceFiles)
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(targetPath, fileName);
                if (File.Exists(destFile))
                {
                    File.Delete(destFile);
                }
                File.Move(file, destFile);
            }

            foreach (var dir in sourceDirs)
            {
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(targetPath, dirName);
                if (Directory.Exists(destDir))
                {
                    Directory.Delete(destDir, true);
                }
                Directory.Move(dir, destDir);
            }

            // Remove empty source directory
            if (Directory.Exists(sourcePath) && !Directory.EnumerateFileSystemEntries(sourcePath).Any())
            {
                Directory.Delete(sourcePath);
            }

            return new PathFixResult
            {
                Success = true,
                StrategyUsed = PathFixStrategy.MoveToConfiguredPath,
                Message = $"Moved repository from {sourcePath} to {targetPath}",
                NewPath = targetPath
            };
        }
        catch (Exception ex)
        {
            return new PathFixResult
            {
                Success = false,
                StrategyUsed = PathFixStrategy.MoveToConfiguredPath,
                Message = $"Failed to move repository: {ex.Message}",
                ErrorDetail = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Clones fresh to correct location and discards misaligned data
    /// </summary>
    private async Task<PathFixResult> CloneFreshAndDiscardAsync(string projectRoot, PathMismatchInfo mismatch)
    {
        try
        {
            var manifest = await _manifestService.ReadManifestAsync(projectRoot);
            if (manifest == null || string.IsNullOrEmpty(manifest.Dolt.RemoteUrl))
            {
                return new PathFixResult
                {
                    Success = false,
                    StrategyUsed = PathFixStrategy.CloneFreshDiscardMisaligned,
                    Message = "Cannot clone fresh: No manifest or remote URL available",
                    ErrorDetail = "Manifest or remote URL is missing"
                };
            }

            // Remove misaligned directory
            if (!string.IsNullOrEmpty(mismatch.ActualDotDoltLocation) && Directory.Exists(mismatch.ActualDotDoltLocation))
            {
                _logger.LogWarning("[RepositoryBootstrapper.CloneFreshAndDiscardAsync] Removing misaligned directory: {Path}", mismatch.ActualDotDoltLocation);
                Directory.Delete(mismatch.ActualDotDoltLocation, true);
            }

            // Clone to correct path
            var (success, error) = await CloneDoltRepositoryAsync(manifest.Dolt.RemoteUrl, mismatch.ConfiguredPath);

            if (!success)
            {
                return new PathFixResult
                {
                    Success = false,
                    StrategyUsed = PathFixStrategy.CloneFreshDiscardMisaligned,
                    Message = $"Clone failed: {error}",
                    ErrorDetail = error
                };
            }

            return new PathFixResult
            {
                Success = true,
                StrategyUsed = PathFixStrategy.CloneFreshDiscardMisaligned,
                Message = "Cloned fresh to correct location",
                NewPath = Path.GetFullPath(mismatch.ConfiguredPath)
            };
        }
        catch (Exception ex)
        {
            return new PathFixResult
            {
                Success = false,
                StrategyUsed = PathFixStrategy.CloneFreshDiscardMisaligned,
                Message = $"Clone fresh failed: {ex.Message}",
                ErrorDetail = ex.ToString()
            };
        }
    }
}
