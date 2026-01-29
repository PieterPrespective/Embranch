using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-87: Implementation of repository state detection.
/// Analyzes manifest, Dolt repository, and Chroma database states
/// to provide clear, actionable guidance for consumer LLMs.
/// </summary>
public class RepositoryStateDetector : IRepositoryStateDetector
{
    private readonly ILogger<RepositoryStateDetector> _logger;
    private readonly IEmbranchStateManifest _manifestService;
    private readonly IDoltCli _doltCli;
    private readonly IChromaDbService _chromaService;
    private readonly DoltConfiguration _doltConfig;
    private readonly ServerConfiguration _serverConfig;

    // Cache for effective path to avoid repeated filesystem checks
    private string? _cachedEffectivePath;
    private string? _cachedConfiguredPath;

    public RepositoryStateDetector(
        ILogger<RepositoryStateDetector> logger,
        IEmbranchStateManifest manifestService,
        IDoltCli doltCli,
        IChromaDbService chromaService,
        IOptions<DoltConfiguration> doltConfig,
        IOptions<ServerConfiguration> serverConfig)
    {
        _logger = logger;
        _manifestService = manifestService;
        _doltCli = doltCli;
        _chromaService = chromaService;
        _doltConfig = doltConfig.Value;
        _serverConfig = serverConfig.Value;
    }

    /// <inheritdoc />
    public async Task<RepositoryStateAnalysis> AnalyzeStateAsync(string projectRoot)
    {
        _logger.LogDebug("[RepositoryStateDetector.AnalyzeStateAsync] Analyzing state for project root: {ProjectRoot}", projectRoot);

        try
        {
            // Step 1: Check manifest using FindManifestAsync for multi-location search
            // PP13-87-C1: Now searches mcpdata subdirectories for server instance manifests
            // Pass dataPath to derive manifest location from EMBRANCH_DATA_PATH configuration
            var (hasManifest, discoveredManifestPath, searchedLocations) = await _manifestService.FindManifestAsync(projectRoot, _serverConfig.DataPath);
            DmmsManifest? manifest = null;
            ManifestInfo? manifestInfo = null;

            if (hasManifest && discoveredManifestPath != null)
            {
                manifest = await _manifestService.ReadManifestAsync(projectRoot);
                if (manifest != null)
                {
                    manifestInfo = new ManifestInfo
                    {
                        RemoteUrl = manifest.Dolt.RemoteUrl,
                        CurrentCommit = manifest.Dolt.CurrentCommit,
                        CurrentBranch = manifest.Dolt.CurrentBranch,
                        DefaultBranch = manifest.Dolt.DefaultBranch,
                        ManifestPath = discoveredManifestPath,  // Use actual discovered path
                        SearchedLocations = searchedLocations   // Include search locations for diagnostics
                    };
                }
            }
            else
            {
                // Store searched locations even if manifest not found (for diagnostics)
                _logger.LogDebug("[RepositoryStateDetector.AnalyzeStateAsync] Manifest not found. Searched: {Locations}",
                    string.Join(", ", searchedLocations));
            }

            // Step 2: Check Dolt repository
            var configuredDoltPath = _doltConfig.RepositoryPath;
            var (doltExists, actualDoltPath, isNested) = await FindDoltRepositoryAsync(configuredDoltPath);

            // PP13-87-C1: Check for rogue .dolt at project root
            var rogueDoltPath = DetectRogueDoltAtProjectRoot(projectRoot);

            // PP13-87-C1: Check if the .dolt directory is incomplete (contains only tmp/)
            var isDoltIncomplete = false;
            if (doltExists && actualDoltPath != null)
            {
                var doltDir = Path.Combine(actualDoltPath, ".dolt");
                isDoltIncomplete = Directory.Exists(doltDir) && !ValidateDoltDirectory(doltDir);
            }

            DoltInfrastructureInfo? doltInfo = null;
            PathMismatchInfo? pathIssue = null;

            if (doltExists)
            {
                string? currentBranch = null;
                string? currentCommit = null;

                try
                {
                    // Only try to get state if repo is valid
                    var isInitialized = await _doltCli.IsInitializedAsync();
                    if (isInitialized)
                    {
                        currentBranch = await _doltCli.GetCurrentBranchAsync();
                        currentCommit = await _doltCli.GetHeadCommitHashAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[RepositoryStateDetector.AnalyzeStateAsync] Could not get Dolt state: {Error}", ex.Message);
                }

                doltInfo = new DoltInfrastructureInfo
                {
                    Path = actualDoltPath ?? configuredDoltPath,
                    IsValid = currentBranch != null && !isDoltIncomplete,
                    CurrentBranch = currentBranch,
                    CurrentCommit = currentCommit,
                    IsNested = isNested
                };

                // Build path issue if nested OR rogue .dolt detected
                if (isNested || rogueDoltPath != null || isDoltIncomplete)
                {
                    var suggestedFix = isNested
                        ? $"Move contents of '{actualDoltPath}' to '{configuredDoltPath}', or update DOLT_REPOSITORY_PATH to '{actualDoltPath}'"
                        : rogueDoltPath != null
                            ? $"Remove rogue .dolt directory at '{rogueDoltPath}' - it may interfere with Dolt operations"
                            : $"The .dolt directory at '{actualDoltPath}' is incomplete. Consider removing and re-cloning.";

                    pathIssue = new PathMismatchInfo
                    {
                        ConfiguredPath = configuredDoltPath,
                        ActualDotDoltLocation = actualDoltPath,
                        SuggestedFix = suggestedFix,
                        RogueDoltPath = rogueDoltPath,
                        IsDoltIncomplete = isDoltIncomplete
                    };
                }
            }
            else if (rogueDoltPath != null)
            {
                // No valid .dolt at configured path but rogue exists at project root
                pathIssue = new PathMismatchInfo
                {
                    ConfiguredPath = configuredDoltPath,
                    RogueDoltPath = rogueDoltPath,
                    SuggestedFix = $"Found .dolt at project root '{rogueDoltPath}' but no valid repository at configured path. Remove the rogue .dolt or update configuration."
                };
            }

            // Step 3: Check Chroma database
            var chromaPath = _serverConfig.ChromaDataPath;
            var chromaExists = await ChromaDataExistsAsync(chromaPath);
            var collectionCount = 0;

            if (chromaExists)
            {
                try
                {
                    var collections = await _chromaService.ListCollectionsAsync();
                    collectionCount = collections?.Count() ?? 0;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[RepositoryStateDetector.AnalyzeStateAsync] Could not get Chroma collections: {Error}", ex.Message);
                }
            }

            ChromaInfrastructureInfo? chromaInfo = new ChromaInfrastructureInfo
            {
                Path = chromaPath,
                Exists = chromaExists,
                CollectionCount = collectionCount
            };

            // Step 4: Determine state
            var state = DetermineState(hasManifest, manifestInfo, doltExists, doltInfo, chromaExists, isNested);
            var (description, actions, recommended) = GetStateGuidance(state, manifestInfo, doltInfo, pathIssue);

            _logger.LogInformation("[RepositoryStateDetector.AnalyzeStateAsync] Detected state: {State}", state);

            return new RepositoryStateAnalysis
            {
                State = state,
                StateDescription = description,
                AvailableActions = actions,
                RecommendedAction = recommended,
                Manifest = manifestInfo,
                DoltInfo = doltInfo,
                ChromaInfo = chromaInfo,
                PathIssue = pathIssue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryStateDetector.AnalyzeStateAsync] Error analyzing state");

            return new RepositoryStateAnalysis
            {
                State = RepositoryState.Inconsistent,
                StateDescription = $"Error analyzing repository state: {ex.Message}",
                AvailableActions = new[] { "RepositoryStatus", "Check logs for details" },
                RecommendedAction = "Investigate error and retry RepositoryStatus"
            };
        }
    }

    /// <inheritdoc />
    public async Task<PathValidationResult> ValidatePathsAsync(string projectRoot)
    {
        var issues = new List<PathMismatchInfo>();
        var configuredPath = _doltConfig.RepositoryPath;
        var (doltExists, actualPath, isNested) = await FindDoltRepositoryAsync(configuredPath);

        if (isNested && actualPath != null)
        {
            issues.Add(new PathMismatchInfo
            {
                ConfiguredPath = configuredPath,
                ActualDotDoltLocation = actualPath,
                SuggestedFix = $"Dolt repository is in nested directory. Move to configured path or update configuration."
            });
        }

        return new PathValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues.ToArray(),
            EffectiveDoltPath = doltExists ? (actualPath ?? configuredPath) : null
        };
    }

    /// <inheritdoc />
    public async Task<(bool Exists, string? ActualPath, bool IsNested)> FindDoltRepositoryAsync(string configuredPath)
    {
        try
        {
            var absolutePath = Path.GetFullPath(configuredPath);

            // Check if .dolt exists directly in configured path
            var directDoltPath = Path.Combine(absolutePath, ".dolt");
            if (Directory.Exists(directDoltPath))
            {
                // PP13-87-C1: Validate that .dolt is a real repository, not just tmp/
                if (ValidateDoltDirectory(directDoltPath))
                {
                    _logger.LogDebug("[RepositoryStateDetector.FindDoltRepositoryAsync] Found valid .dolt at configured path: {Path}", absolutePath);
                    return (true, absolutePath, false);
                }
                else
                {
                    _logger.LogWarning("[RepositoryStateDetector.FindDoltRepositoryAsync] Found incomplete .dolt at configured path (missing essential files): {Path}", absolutePath);
                    // Continue searching for nested valid .dolt
                }
            }

            // Check for nested directory (CLI clone case)
            if (Directory.Exists(absolutePath))
            {
                var subdirs = Directory.GetDirectories(absolutePath);

                foreach (var subdir in subdirs)
                {
                    var nestedDoltPath = Path.Combine(subdir, ".dolt");
                    if (Directory.Exists(nestedDoltPath) && ValidateDoltDirectory(nestedDoltPath))
                    {
                        _logger.LogInformation("[RepositoryStateDetector.FindDoltRepositoryAsync] Found valid .dolt in nested directory: {Path}", subdir);
                        return (true, subdir, true);
                    }
                }
            }

            // If outer .dolt exists but is incomplete, still report it
            if (Directory.Exists(directDoltPath))
            {
                _logger.LogDebug("[RepositoryStateDetector.FindDoltRepositoryAsync] Only incomplete .dolt found at: {Path}", absolutePath);
                return (true, absolutePath, false);  // Report it exists but IsValid will be false later
            }

            _logger.LogDebug("[RepositoryStateDetector.FindDoltRepositoryAsync] No .dolt directory found at or under: {Path}", absolutePath);
            return (false, null, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RepositoryStateDetector.FindDoltRepositoryAsync] Error checking for Dolt repository");
            return (false, null, false);
        }
    }

    /// <summary>
    /// PP13-87-C1: Validates that a .dolt directory contains essential files for a valid repository.
    /// A valid .dolt should contain at least: noms/ directory, OR config file, OR chunks/ directory.
    /// A .dolt with only tmp/ is incomplete and not a valid repository.
    /// </summary>
    /// <param name="doltPath">Path to the .dolt directory to validate</param>
    /// <returns>True if the .dolt directory appears to be a valid repository</returns>
    private bool ValidateDoltDirectory(string doltPath)
    {
        try
        {
            var nomsPath = Path.Combine(doltPath, "noms");
            var configPath = Path.Combine(doltPath, "config");
            var chunksPath = Path.Combine(doltPath, "chunks");

            var isValid = Directory.Exists(nomsPath) ||
                          File.Exists(configPath) ||
                          Directory.Exists(chunksPath);

            if (!isValid)
            {
                // Log what we found for diagnostics
                var contents = Directory.GetFileSystemEntries(doltPath).Select(Path.GetFileName);
                _logger.LogDebug("[RepositoryStateDetector.ValidateDoltDirectory] .dolt at {Path} contains only: {Contents}",
                    doltPath, string.Join(", ", contents));
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RepositoryStateDetector.ValidateDoltDirectory] Error validating .dolt at {Path}", doltPath);
            return false;
        }
    }

    /// <summary>
    /// PP13-87-C1: Detects rogue .dolt directories at the project root.
    /// A rogue .dolt is one that exists outside the configured Dolt path and may interfere with operations.
    /// </summary>
    /// <param name="projectRoot">Path to the project root</param>
    /// <returns>Path to rogue .dolt if found, null otherwise</returns>
    public string? DetectRogueDoltAtProjectRoot(string projectRoot)
    {
        try
        {
            var configuredPath = Path.GetFullPath(_doltConfig.RepositoryPath);
            var projectRootDoltPath = Path.Combine(Path.GetFullPath(projectRoot), ".dolt");

            // Check if project root is different from configured Dolt path
            var configuredParent = Path.GetDirectoryName(configuredPath);

            if (Directory.Exists(projectRootDoltPath))
            {
                // Only consider it rogue if the project root is not the configured Dolt location
                if (!projectRootDoltPath.Equals(Path.Combine(configuredPath, ".dolt"), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[RepositoryStateDetector.DetectRogueDoltAtProjectRoot] Found rogue .dolt at project root: {Path}", projectRootDoltPath);
                    return projectRootDoltPath;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RepositoryStateDetector.DetectRogueDoltAtProjectRoot] Error checking for rogue .dolt");
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> ChromaDataExistsAsync(string chromaPath)
    {
        try
        {
            var absolutePath = Path.GetFullPath(chromaPath);

            if (!Directory.Exists(absolutePath))
            {
                return Task.FromResult(false);
            }

            // Check for SQLite files (Chroma uses SQLite for persistent storage)
            var sqliteFiles = Directory.GetFiles(absolutePath, "*.sqlite*", SearchOption.AllDirectories);
            if (sqliteFiles.Any())
            {
                _logger.LogDebug("[RepositoryStateDetector.ChromaDataExistsAsync] Found Chroma data files at: {Path}", absolutePath);
                return Task.FromResult(true);
            }

            // Also check for chroma.sqlite3 specifically
            var chromaSqlite = Path.Combine(absolutePath, "chroma.sqlite3");
            if (File.Exists(chromaSqlite))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RepositoryStateDetector.ChromaDataExistsAsync] Error checking for Chroma data");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public string GetEffectiveDoltPath(string configuredPath)
    {
        // Use cached value if configuration hasn't changed
        if (_cachedConfiguredPath == configuredPath && _cachedEffectivePath != null)
        {
            return _cachedEffectivePath;
        }

        var absolutePath = Path.GetFullPath(configuredPath);

        // Check if .dolt exists directly
        if (Directory.Exists(Path.Combine(absolutePath, ".dolt")))
        {
            _cachedConfiguredPath = configuredPath;
            _cachedEffectivePath = absolutePath;
            return absolutePath;
        }

        // Check for nested directory
        if (Directory.Exists(absolutePath))
        {
            var subdirs = Directory.GetDirectories(absolutePath);

            if (subdirs.Length == 1)
            {
                var nested = subdirs[0];
                if (Directory.Exists(Path.Combine(nested, ".dolt")))
                {
                    _logger.LogInformation(
                        "[RepositoryStateDetector.GetEffectiveDoltPath] Using nested directory {Nested} as effective path",
                        nested);
                    _cachedConfiguredPath = configuredPath;
                    _cachedEffectivePath = nested;
                    return nested;
                }
            }
        }

        // Return configured path (operations may fail with clear error)
        _cachedConfiguredPath = configuredPath;
        _cachedEffectivePath = absolutePath;
        return absolutePath;
    }

    /// <summary>
    /// Determines the repository state based on component availability
    /// </summary>
    private RepositoryState DetermineState(
        bool hasManifest,
        ManifestInfo? manifestInfo,
        bool doltExists,
        DoltInfrastructureInfo? doltInfo,
        bool chromaExists,
        bool isNested)
    {
        // Path misalignment takes priority - needs resolution before other operations
        if (isNested)
        {
            return RepositoryState.PathMisaligned_DoltNested;
        }

        // No manifest, no Dolt, no Chroma = Uninitialized
        if (!hasManifest && !doltExists && !chromaExists)
        {
            return RepositoryState.Uninitialized;
        }

        // Infrastructure exists but no manifest
        if (!hasManifest && (doltExists || chromaExists))
        {
            return RepositoryState.InfrastructureOnly_NeedsManifest;
        }

        // Manifest exists, check what infrastructure is missing
        if (hasManifest)
        {
            if (!doltExists && !chromaExists)
            {
                return RepositoryState.ManifestOnly_NeedsFullBootstrap;
            }

            if (!doltExists)
            {
                return RepositoryState.ManifestOnly_NeedsDoltBootstrap;
            }

            if (!chromaExists)
            {
                return RepositoryState.ManifestOnly_NeedsChromaBootstrap;
            }

            // Check if Dolt is valid
            if (doltInfo != null && !doltInfo.IsValid)
            {
                return RepositoryState.ManifestOnly_NeedsDoltBootstrap;
            }

            // Everything present and valid
            return RepositoryState.Ready;
        }

        // Fallback - shouldn't reach here
        return RepositoryState.Inconsistent;
    }

    /// <summary>
    /// Gets human-readable guidance for a given state
    /// </summary>
    private (string Description, string[] Actions, string Recommended) GetStateGuidance(
        RepositoryState state,
        ManifestInfo? manifestInfo,
        DoltInfrastructureInfo? doltInfo,
        PathMismatchInfo? pathIssue)
    {
        return state switch
        {
            RepositoryState.Ready => (
                "Repository is fully initialized and operational.",
                new[] { "DoltStatus", "DoltBranches", "ChromaListCollections", "DoltCheckout", "DoltCommit", "DoltPush" },
                "Proceed with normal operations"
            ),

            RepositoryState.Uninitialized => (
                "No manifest, Dolt repository, or Chroma database found. This is a fresh project.",
                new[] { "DoltClone", "DoltInit", "InitManifest" },
                "DoltClone(remote_url) to clone from a remote repository, or DoltInit to create a new local repository"
            ),

            RepositoryState.ManifestOnly_NeedsFullBootstrap => (
                $"Manifest found{(manifestInfo?.RemoteUrl != null ? $" with remote '{manifestInfo.RemoteUrl}'" : "")} but Dolt repository and Chroma database need initialization.",
                new[] { "BootstrapRepository", "DoltClone" },
                "BootstrapRepository(sync_to_manifest=true) to initialize from manifest"
            ),

            RepositoryState.ManifestOnly_NeedsDoltBootstrap => (
                $"Manifest found{(manifestInfo?.RemoteUrl != null ? $" with remote '{manifestInfo.RemoteUrl}'" : "")} but Dolt repository needs initialization.",
                new[] { "BootstrapRepository", "DoltClone" },
                "BootstrapRepository(bootstrap_dolt=true) to clone Dolt repository from manifest"
            ),

            RepositoryState.ManifestOnly_NeedsChromaBootstrap => (
                "Manifest and Dolt repository exist but Chroma database needs initialization.",
                new[] { "BootstrapRepository", "SyncToManifest" },
                "BootstrapRepository(bootstrap_chroma=true) to initialize Chroma from Dolt state"
            ),

            RepositoryState.PathMisaligned_DoltNested => (
                $"Dolt repository exists but in a nested location. Configured: '{pathIssue?.ConfiguredPath}', Actual: '{pathIssue?.ActualDotDoltLocation}'",
                new[] { "BootstrapRepository(fix_path=true)", "Manual reconfiguration" },
                $"BootstrapRepository(fix_path=true) or update DOLT_REPOSITORY_PATH to '{pathIssue?.ActualDotDoltLocation}'"
            ),

            RepositoryState.InfrastructureOnly_NeedsManifest => (
                "Dolt and/or Chroma infrastructure exists but no manifest file. Manifest needed for state tracking.",
                new[] { "InitManifest", "UpdateManifest" },
                "InitManifest to create a manifest from current state"
            ),

            RepositoryState.Inconsistent => (
                "Repository state is inconsistent. Multiple issues detected or configuration conflicts exist.",
                new[] { "RepositoryStatus", "Manual investigation" },
                "Check logs and investigate configuration. Consider BootstrapRepository(force=true) if safe."
            ),

            _ => (
                "Unknown repository state.",
                new[] { "RepositoryStatus" },
                "Call RepositoryStatus for detailed analysis"
            )
        };
    }
}
