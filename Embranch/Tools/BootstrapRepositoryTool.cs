using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Embranch.Models;
using Embranch.Services;
using Embranch.Utilities;

namespace Embranch.Tools;

/// <summary>
/// PP13-87: MCP tool for bootstrapping repository infrastructure from manifest.
/// Use when RepositoryStatus indicates ManifestOnly_* states.
/// This is the primary recovery mechanism for fresh clone scenarios.
/// </summary>
[McpServerToolType]
public class BootstrapRepositoryTool
{
    private readonly ILogger<BootstrapRepositoryTool> _logger;
    private readonly IRepositoryStateDetector _stateDetector;
    private readonly IRepositoryBootstrapper _bootstrapper;
    private readonly IGitIntegration _gitIntegration;
    private readonly ServerConfiguration _serverConfig;

    public BootstrapRepositoryTool(
        ILogger<BootstrapRepositoryTool> logger,
        IRepositoryStateDetector stateDetector,
        IRepositoryBootstrapper bootstrapper,
        IGitIntegration gitIntegration,
        IOptions<ServerConfiguration> serverConfig)
    {
        _logger = logger;
        _stateDetector = stateDetector;
        _bootstrapper = bootstrapper;
        _gitIntegration = gitIntegration;
        _serverConfig = serverConfig.Value;
    }

    /// <summary>
    /// Bootstraps repository infrastructure from manifest.
    /// Use when RepositoryStatus shows ManifestOnly_* or PathMisaligned_* states.
    /// </summary>
    /// <param name="sync_to_manifest">Sync to the manifest's commit after cloning (default: true)</param>
    /// <param name="create_work_branch">Create and checkout a work branch after bootstrap (default: false)</param>
    /// <param name="work_branch_name">Name for the work branch (required if create_work_branch is true)</param>
    /// <param name="fix_path">Attempt to fix path misalignment if detected (default: false)</param>
    /// <param name="path_fix_strategy">Strategy for fixing path: move_to_configured, update_config, clone_fresh (default: move_to_configured)</param>
    /// <returns>Bootstrap result with success status and actions performed</returns>
    [McpServerTool]
    [Description("Bootstraps repository infrastructure from manifest. Use when RepositoryStatus shows ManifestOnly_* or PathMisaligned_* states. This clones the Dolt repository, syncs to manifest state, and initializes Chroma.")]
    public async Task<BootstrapRepositoryResponse> BootstrapRepository(
        [Description("Sync to the manifest's commit after cloning (default: true)")]
        bool sync_to_manifest = true,
        [Description("Create and checkout a work branch after bootstrap (default: false)")]
        bool create_work_branch = false,
        [Description("Name for the work branch (required if create_work_branch is true)")]
        string? work_branch_name = null,
        [Description("Attempt to fix path misalignment if detected (default: false)")]
        bool fix_path = false,
        [Description("Strategy for fixing path: move_to_configured, update_config, clone_fresh (default: move_to_configured)")]
        string path_fix_strategy = "move_to_configured")
    {
        ToolLoggingUtility.LogToolStart(_logger, nameof(BootstrapRepositoryTool), nameof(BootstrapRepository),
            $"sync_to_manifest={sync_to_manifest}, create_work_branch={create_work_branch}, work_branch_name={work_branch_name}, fix_path={fix_path}");

        try
        {
            // Determine project root
            string projectRoot = _serverConfig.ProjectRoot ?? Directory.GetCurrentDirectory();

            if (_serverConfig.AutoDetectProjectRoot)
            {
                var gitRoot = await _gitIntegration.GetGitRootAsync(Directory.GetCurrentDirectory());
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    projectRoot = gitRoot;
                }
            }

            // Check current state first
            var currentState = await _stateDetector.AnalyzeStateAsync(projectRoot);

            // Handle Ready state
            if (currentState.State == RepositoryState.Ready)
            {
                return new BootstrapRepositoryResponse
                {
                    Success = true,
                    Message = "Repository already initialized and ready.",
                    State = RepositoryState.Ready.ToString(),
                    ActionsPerformed = new[] { "Verified existing infrastructure" },
                    CommitHash = currentState.DoltInfo?.CurrentCommit,
                    BranchName = currentState.DoltInfo?.CurrentBranch
                };
            }

            // Handle Uninitialized state
            if (currentState.State == RepositoryState.Uninitialized)
            {
                return new BootstrapRepositoryResponse
                {
                    Success = false,
                    Message = "No manifest found. Use DoltClone to clone from a remote, or DoltInit to create a new repository.",
                    State = RepositoryState.Uninitialized.ToString(),
                    AvailableActions = new[] { "DoltClone", "DoltInit" },
                    ErrorDetail = "Cannot bootstrap without a manifest. The manifest contains the remote URL needed for cloning."
                };
            }

            // Handle InfrastructureOnly state
            if (currentState.State == RepositoryState.InfrastructureOnly_NeedsManifest)
            {
                return new BootstrapRepositoryResponse
                {
                    Success = false,
                    Message = "Infrastructure exists but no manifest. Use InitManifest to create a manifest from current state.",
                    State = RepositoryState.InfrastructureOnly_NeedsManifest.ToString(),
                    AvailableActions = new[] { "InitManifest", "UpdateManifest" },
                    ErrorDetail = "Bootstrap requires a manifest. Create one first with InitManifest."
                };
            }

            // Parse path fix strategy
            PathFixStrategy? fixStrategy = null;
            if (fix_path)
            {
                fixStrategy = path_fix_strategy.ToLowerInvariant() switch
                {
                    "move_to_configured" => PathFixStrategy.MoveToConfiguredPath,
                    "update_config" => PathFixStrategy.UpdateConfiguration,
                    "clone_fresh" => PathFixStrategy.CloneFreshDiscardMisaligned,
                    _ => PathFixStrategy.MoveToConfiguredPath
                };
            }

            // Build options
            var options = new BootstrapOptions
            {
                BootstrapDolt = true,
                BootstrapChroma = true,
                SyncToManifestCommit = sync_to_manifest,
                CreateWorkBranch = create_work_branch,
                WorkBranchName = work_branch_name,
                PathFixStrategy = fixStrategy
            };

            // Execute bootstrap
            var result = await _bootstrapper.BootstrapFromManifestAsync(projectRoot, options);

            _logger.LogInformation("[BootstrapRepositoryTool] Result: {Success}, State: {State}, Actions: {Actions}",
                result.Success, result.ResultingState, string.Join(", ", result.ActionsPerformed));

            return new BootstrapRepositoryResponse
            {
                Success = result.Success,
                Message = result.Message,
                State = result.ResultingState.ToString(),
                ActionsPerformed = result.ActionsPerformed,
                ErrorDetail = result.ErrorDetail,
                CommitHash = result.CommitHash,
                BranchName = result.BranchName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BootstrapRepositoryTool] Error during bootstrap");

            return new BootstrapRepositoryResponse
            {
                Success = false,
                Message = $"Bootstrap failed: {ex.Message}",
                State = RepositoryState.Inconsistent.ToString(),
                ErrorDetail = ex.ToString()
            };
        }
    }
}

/// <summary>
/// PP13-87: Response model for BootstrapRepository tool
/// </summary>
public class BootstrapRepositoryResponse
{
    /// <summary>
    /// Whether the bootstrap operation succeeded
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// The resulting repository state after bootstrap
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    /// <summary>
    /// List of actions that were performed
    /// </summary>
    [JsonPropertyName("actions_performed")]
    public string[] ActionsPerformed { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Available actions if bootstrap failed or was not possible
    /// </summary>
    [JsonPropertyName("available_actions")]
    public string[]? AvailableActions { get; set; }

    /// <summary>
    /// Error detail if bootstrap failed
    /// </summary>
    [JsonPropertyName("error_detail")]
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// The commit hash after bootstrap (if successful)
    /// </summary>
    [JsonPropertyName("commit_hash")]
    public string? CommitHash { get; set; }

    /// <summary>
    /// The branch name after bootstrap (if successful)
    /// </summary>
    [JsonPropertyName("branch_name")]
    public string? BranchName { get; set; }
}
