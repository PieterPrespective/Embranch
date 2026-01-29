using System.Text.Json.Serialization;

namespace Embranch.Models;

/// <summary>
/// PP13-87: Represents the possible states of an Embranch repository infrastructure.
/// Used by RepositoryStatusTool to report clear, actionable state to consumer LLMs.
/// </summary>
public enum RepositoryState
{
    /// <summary>
    /// Repository is fully initialized and operational.
    /// All infrastructure (manifest, Dolt repo, Chroma) is present and aligned.
    /// </summary>
    Ready,

    /// <summary>
    /// No manifest, no Dolt repo, no Chroma data - completely fresh project.
    /// Action: Use DoltClone to clone from a remote, or DoltInit to create a new repository.
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Manifest exists but Dolt repository is missing - needs bootstrap.
    /// Action: Use BootstrapRepository to clone from manifest's remote URL.
    /// </summary>
    ManifestOnly_NeedsDoltBootstrap,

    /// <summary>
    /// Manifest and Dolt exist but Chroma database missing - needs bootstrap.
    /// Action: Use BootstrapRepository to initialize Chroma and sync from Dolt.
    /// </summary>
    ManifestOnly_NeedsChromaBootstrap,

    /// <summary>
    /// Manifest exists but both Dolt and Chroma are missing - needs full bootstrap.
    /// Action: Use BootstrapRepository to initialize everything from manifest.
    /// </summary>
    ManifestOnly_NeedsFullBootstrap,

    /// <summary>
    /// Dolt repository exists but in a nested subdirectory (CLI clone issue).
    /// The configured DOLT_REPOSITORY_PATH contains a subdirectory with .dolt instead of .dolt directly.
    /// Action: Use BootstrapRepository with fix_path=true or manually reconfigure.
    /// </summary>
    PathMisaligned_DoltNested,

    /// <summary>
    /// Dolt and/or Chroma infrastructure exists but no manifest file.
    /// Action: Use InitManifest to create a manifest from current state.
    /// </summary>
    InfrastructureOnly_NeedsManifest,

    /// <summary>
    /// State inconsistency detected that may require manual intervention.
    /// Multiple issues detected or configuration conflicts.
    /// </summary>
    Inconsistent
}

/// <summary>
/// PP13-87: Information about the manifest file
/// </summary>
public record ManifestInfo
{
    /// <summary>
    /// The remote URL configured in the manifest
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>
    /// The current commit hash from the manifest
    /// </summary>
    public string? CurrentCommit { get; init; }

    /// <summary>
    /// The current branch from the manifest
    /// </summary>
    public string? CurrentBranch { get; init; }

    /// <summary>
    /// The default branch from the manifest
    /// </summary>
    public string? DefaultBranch { get; init; }

    /// <summary>
    /// Path to the manifest file
    /// </summary>
    public string? ManifestPath { get; init; }

    /// <summary>
    /// PP13-87-C1: Locations searched when looking for manifest (for diagnostics)
    /// </summary>
    public string[]? SearchedLocations { get; init; }
}

/// <summary>
/// PP13-87: Information about the Dolt repository infrastructure
/// </summary>
public record DoltInfrastructureInfo
{
    /// <summary>
    /// Path where Dolt repository is located
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Whether the Dolt repository is valid and initialized
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Current branch in the Dolt repository
    /// </summary>
    public string? CurrentBranch { get; init; }

    /// <summary>
    /// Current HEAD commit hash
    /// </summary>
    public string? CurrentCommit { get; init; }

    /// <summary>
    /// Whether this is a nested repository (CLI clone issue)
    /// </summary>
    public bool IsNested { get; init; }
}

/// <summary>
/// PP13-87: Information about the Chroma database infrastructure
/// </summary>
public record ChromaInfrastructureInfo
{
    /// <summary>
    /// Path where Chroma data is stored
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Whether Chroma database files exist
    /// </summary>
    public bool Exists { get; init; }

    /// <summary>
    /// Number of collections in the database
    /// </summary>
    public int CollectionCount { get; init; }
}

/// <summary>
/// PP13-87: Information about a path mismatch issue
/// </summary>
public record PathMismatchInfo
{
    /// <summary>
    /// The path configured in DOLT_REPOSITORY_PATH
    /// </summary>
    public string ConfiguredPath { get; init; } = "";

    /// <summary>
    /// The actual location where .dolt folder was found
    /// </summary>
    public string? ActualDotDoltLocation { get; init; }

    /// <summary>
    /// Human-readable description of the suggested fix
    /// </summary>
    public string SuggestedFix { get; init; } = "";

    /// <summary>
    /// PP13-87-C1: Path to rogue .dolt directory if detected at project root
    /// </summary>
    public string? RogueDoltPath { get; init; }

    /// <summary>
    /// PP13-87-C1: Whether a rogue .dolt was detected
    /// </summary>
    public bool RogueDoltDetected => !string.IsNullOrEmpty(RogueDoltPath);

    /// <summary>
    /// PP13-87-C1: Whether the .dolt directory is incomplete (contains only tmp/)
    /// </summary>
    public bool IsDoltIncomplete { get; init; }

    /// <summary>
    /// PP13-87-C1: All .dolt directories found during search (for diagnostics)
    /// </summary>
    public string[]? AllDoltLocationsFound { get; init; }
}

/// <summary>
/// PP13-87: Complete analysis of repository state with actionable guidance
/// </summary>
public record RepositoryStateAnalysis
{
    /// <summary>
    /// The detected repository state
    /// </summary>
    public RepositoryState State { get; init; }

    /// <summary>
    /// Human-readable description of the current state
    /// </summary>
    public string StateDescription { get; init; } = "";

    /// <summary>
    /// List of available actions the LLM can take in this state
    /// </summary>
    public string[] AvailableActions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The recommended action to resolve the current state
    /// </summary>
    public string RecommendedAction { get; init; } = "";

    /// <summary>
    /// Information about the manifest, if present
    /// </summary>
    public ManifestInfo? Manifest { get; init; }

    /// <summary>
    /// Information about Dolt infrastructure, if present
    /// </summary>
    public DoltInfrastructureInfo? DoltInfo { get; init; }

    /// <summary>
    /// Information about Chroma infrastructure, if present
    /// </summary>
    public ChromaInfrastructureInfo? ChromaInfo { get; init; }

    /// <summary>
    /// Information about path mismatch, if detected
    /// </summary>
    public PathMismatchInfo? PathIssue { get; init; }
}

/// <summary>
/// PP13-87: Result of path validation
/// </summary>
public record PathValidationResult
{
    /// <summary>
    /// Whether all paths are correctly aligned
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// List of path issues detected
    /// </summary>
    public PathMismatchInfo[] Issues { get; init; } = Array.Empty<PathMismatchInfo>();

    /// <summary>
    /// The effective Dolt path to use (may differ from configured path)
    /// </summary>
    public string? EffectiveDoltPath { get; init; }
}

/// <summary>
/// PP13-87: Strategy for fixing path misalignment
/// </summary>
public enum PathFixStrategy
{
    /// <summary>
    /// Move .dolt folder contents to the configured path
    /// </summary>
    MoveToConfiguredPath,

    /// <summary>
    /// Update configuration to match actual location
    /// </summary>
    UpdateConfiguration,

    /// <summary>
    /// Clone fresh to correct location, discard misaligned data
    /// </summary>
    CloneFreshDiscardMisaligned
}

/// <summary>
/// PP13-87: Options for bootstrap operation
/// </summary>
public record BootstrapOptions
{
    /// <summary>
    /// Whether to bootstrap Dolt repository
    /// </summary>
    public bool BootstrapDolt { get; init; } = true;

    /// <summary>
    /// Whether to bootstrap Chroma database
    /// </summary>
    public bool BootstrapChroma { get; init; } = true;

    /// <summary>
    /// Whether to sync to the manifest's commit after cloning
    /// </summary>
    public bool SyncToManifestCommit { get; init; } = true;

    /// <summary>
    /// Whether to create a work branch after bootstrap
    /// </summary>
    public bool CreateWorkBranch { get; init; }

    /// <summary>
    /// Name of the work branch to create
    /// </summary>
    public string? WorkBranchName { get; init; }

    /// <summary>
    /// Strategy for fixing path issues (if applicable)
    /// </summary>
    public PathFixStrategy? PathFixStrategy { get; init; }
}

/// <summary>
/// PP13-87: Result of bootstrap operation
/// </summary>
public record BootstrapResult
{
    /// <summary>
    /// Whether the bootstrap operation succeeded
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The resulting repository state after bootstrap
    /// </summary>
    public RepositoryState ResultingState { get; init; }

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// List of actions that were performed
    /// </summary>
    public string[] ActionsPerformed { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Error detail if bootstrap failed
    /// </summary>
    public string? ErrorDetail { get; init; }

    /// <summary>
    /// The commit hash after bootstrap (if successful)
    /// </summary>
    public string? CommitHash { get; init; }

    /// <summary>
    /// The branch name after bootstrap (if successful)
    /// </summary>
    public string? BranchName { get; init; }
}

/// <summary>
/// PP13-87: Result of path fix operation
/// </summary>
public record PathFixResult
{
    /// <summary>
    /// Whether the path fix succeeded
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Strategy that was used
    /// </summary>
    public PathFixStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// The new effective path after fix
    /// </summary>
    public string? NewPath { get; init; }

    /// <summary>
    /// Error detail if fix failed
    /// </summary>
    public string? ErrorDetail { get; init; }
}
