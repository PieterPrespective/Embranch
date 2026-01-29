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
/// PP13-87: MCP tool for comprehensive repository state reporting.
/// This should be the FIRST tool an LLM calls when interacting with a project
/// to understand the current state and determine available actions.
/// </summary>
[McpServerToolType]
public class RepositoryStatusTool
{
    private readonly ILogger<RepositoryStatusTool> _logger;
    private readonly IRepositoryStateDetector _stateDetector;
    private readonly IEmbranchStateManifest _manifestService;
    private readonly IGitIntegration _gitIntegration;
    private readonly ServerConfiguration _serverConfig;

    public RepositoryStatusTool(
        ILogger<RepositoryStatusTool> logger,
        IRepositoryStateDetector stateDetector,
        IEmbranchStateManifest manifestService,
        IGitIntegration gitIntegration,
        IOptions<ServerConfiguration> serverConfig)
    {
        _logger = logger;
        _stateDetector = stateDetector;
        _manifestService = manifestService;
        _gitIntegration = gitIntegration;
        _serverConfig = serverConfig.Value;
    }

    /// <summary>
    /// Reports comprehensive repository state with clear actionable guidance.
    /// Call this FIRST when working with a project to understand its state.
    /// </summary>
    /// <returns>Repository status including state, available actions, and component details</returns>
    [McpServerTool]
    [Description("Reports comprehensive repository state with clear actionable guidance. Call this FIRST when working with a project to understand if it's ready for operations, needs bootstrapping, or has path issues.")]
    public async Task<RepositoryStatusResponse> RepositoryStatus()
    {
        ToolLoggingUtility.LogToolStart(_logger, nameof(RepositoryStatusTool), nameof(RepositoryStatus));

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

            // Analyze state
            var analysis = await _stateDetector.AnalyzeStateAsync(projectRoot);

            // Build response
            var response = new RepositoryStatusResponse
            {
                State = analysis.State.ToString(),
                StateDescription = analysis.StateDescription,
                IsReady = analysis.State == RepositoryState.Ready,
                AvailableActions = analysis.AvailableActions,
                RecommendedAction = analysis.RecommendedAction,
                ProjectRoot = projectRoot
            };

            // Add manifest info with PP13-87-C1 diagnostics
            if (analysis.Manifest != null)
            {
                response.Manifest = new ManifestInfoResponse
                {
                    Exists = true,
                    RemoteUrl = analysis.Manifest.RemoteUrl,
                    CurrentCommit = analysis.Manifest.CurrentCommit,
                    CurrentBranch = analysis.Manifest.CurrentBranch,
                    DefaultBranch = analysis.Manifest.DefaultBranch,
                    ManifestPath = analysis.Manifest.ManifestPath,
                    SearchedLocations = analysis.Manifest.SearchedLocations
                };
            }
            else
            {
                // PP13-87-C1: Still report searched locations even if manifest not found
                // Pass dataPath to ensure consistent search behavior
                var (_, _, searchedLocations) = await _manifestService.FindManifestAsync(projectRoot, _serverConfig.DataPath);
                response.Manifest = new ManifestInfoResponse
                {
                    Exists = false,
                    SearchedLocations = searchedLocations
                };
            }

            // Add Dolt info with PP13-87-C1 diagnostics
            if (analysis.DoltInfo != null)
            {
                response.Dolt = new DoltInfoResponse
                {
                    Exists = true,
                    Path = analysis.DoltInfo.Path,
                    ConfiguredPath = analysis.PathIssue?.ConfiguredPath ?? analysis.DoltInfo.Path,
                    EffectivePath = analysis.DoltInfo.Path,  // The actual path being used
                    IsValid = analysis.DoltInfo.IsValid,
                    CurrentBranch = analysis.DoltInfo.CurrentBranch,
                    CurrentCommit = analysis.DoltInfo.CurrentCommit,
                    IsNested = analysis.DoltInfo.IsNested,
                    IsIncomplete = analysis.PathIssue?.IsDoltIncomplete ?? false
                };
            }
            else
            {
                response.Dolt = new DoltInfoResponse { Exists = false };
            }

            // Add Chroma info
            if (analysis.ChromaInfo != null)
            {
                response.Chroma = new ChromaInfoResponse
                {
                    Exists = analysis.ChromaInfo.Exists,
                    Path = analysis.ChromaInfo.Path,
                    CollectionCount = analysis.ChromaInfo.CollectionCount
                };
            }
            else
            {
                response.Chroma = new ChromaInfoResponse { Exists = false };
            }

            // Add path issue if present with PP13-87-C1 rogue detection
            if (analysis.PathIssue != null)
            {
                response.PathIssue = new PathIssueResponse
                {
                    ConfiguredPath = analysis.PathIssue.ConfiguredPath,
                    ActualPath = analysis.PathIssue.ActualDotDoltLocation,
                    SuggestedFix = analysis.PathIssue.SuggestedFix,
                    RogueDoltPath = analysis.PathIssue.RogueDoltPath,
                    RogueDoltDetected = analysis.PathIssue.RogueDoltDetected,
                    IsDoltIncomplete = analysis.PathIssue.IsDoltIncomplete
                };
            }

            _logger.LogInformation("[RepositoryStatusTool] State: {State}, IsReady: {IsReady}", response.State, response.IsReady);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RepositoryStatusTool] Error getting repository status");

            return new RepositoryStatusResponse
            {
                State = RepositoryState.Inconsistent.ToString(),
                StateDescription = $"Error analyzing repository state: {ex.Message}",
                IsReady = false,
                AvailableActions = new[] { "Check logs", "Verify configuration" },
                RecommendedAction = "Investigate error and retry",
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// PP13-87: Response model for RepositoryStatus tool
/// </summary>
public class RepositoryStatusResponse
{
    /// <summary>
    /// The detected repository state
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    /// <summary>
    /// Human-readable description of the current state
    /// </summary>
    [JsonPropertyName("state_description")]
    public string StateDescription { get; set; } = "";

    /// <summary>
    /// Whether the repository is ready for normal operations
    /// </summary>
    [JsonPropertyName("is_ready")]
    public bool IsReady { get; set; }

    /// <summary>
    /// List of available actions in this state
    /// </summary>
    [JsonPropertyName("available_actions")]
    public string[] AvailableActions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The recommended action to take
    /// </summary>
    [JsonPropertyName("recommended_action")]
    public string RecommendedAction { get; set; } = "";

    /// <summary>
    /// Path to the project root
    /// </summary>
    [JsonPropertyName("project_root")]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Manifest information
    /// </summary>
    [JsonPropertyName("manifest")]
    public ManifestInfoResponse? Manifest { get; set; }

    /// <summary>
    /// Dolt repository information
    /// </summary>
    [JsonPropertyName("dolt")]
    public DoltInfoResponse? Dolt { get; set; }

    /// <summary>
    /// Chroma database information
    /// </summary>
    [JsonPropertyName("chroma")]
    public ChromaInfoResponse? Chroma { get; set; }

    /// <summary>
    /// Path issue information (if any)
    /// </summary>
    [JsonPropertyName("path_issue")]
    public PathIssueResponse? PathIssue { get; set; }

    /// <summary>
    /// Error message if status check failed
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Manifest information in status response
/// </summary>
public class ManifestInfoResponse
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("remote_url")]
    public string? RemoteUrl { get; set; }

    [JsonPropertyName("current_commit")]
    public string? CurrentCommit { get; set; }

    [JsonPropertyName("current_branch")]
    public string? CurrentBranch { get; set; }

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// PP13-87-C1: The actual path where manifest was found
    /// </summary>
    [JsonPropertyName("manifest_path")]
    public string? ManifestPath { get; set; }

    /// <summary>
    /// PP13-87-C1: Locations searched when looking for manifest (for diagnostics)
    /// </summary>
    [JsonPropertyName("searched_locations")]
    public string[]? SearchedLocations { get; set; }
}

/// <summary>
/// Dolt information in status response
/// </summary>
public class DoltInfoResponse
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// PP13-87-C1: The configured path (DOLT_REPOSITORY_PATH)
    /// </summary>
    [JsonPropertyName("configured_path")]
    public string? ConfiguredPath { get; set; }

    /// <summary>
    /// PP13-87-C1: The effective path being used for operations (may differ from configured)
    /// </summary>
    [JsonPropertyName("effective_path")]
    public string? EffectivePath { get; set; }

    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("current_branch")]
    public string? CurrentBranch { get; set; }

    [JsonPropertyName("current_commit")]
    public string? CurrentCommit { get; set; }

    [JsonPropertyName("is_nested")]
    public bool IsNested { get; set; }

    /// <summary>
    /// PP13-87-C1: Whether the .dolt directory is incomplete (e.g., only contains tmp/)
    /// </summary>
    [JsonPropertyName("is_incomplete")]
    public bool IsIncomplete { get; set; }
}

/// <summary>
/// Chroma information in status response
/// </summary>
public class ChromaInfoResponse
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("collection_count")]
    public int CollectionCount { get; set; }
}

/// <summary>
/// Path issue information in status response
/// </summary>
public class PathIssueResponse
{
    [JsonPropertyName("configured_path")]
    public string? ConfiguredPath { get; set; }

    [JsonPropertyName("actual_path")]
    public string? ActualPath { get; set; }

    [JsonPropertyName("suggested_fix")]
    public string? SuggestedFix { get; set; }

    /// <summary>
    /// PP13-87-C1: Path to rogue .dolt directory if detected at project root
    /// </summary>
    [JsonPropertyName("rogue_dolt_path")]
    public string? RogueDoltPath { get; set; }

    /// <summary>
    /// PP13-87-C1: Whether a rogue .dolt was detected
    /// </summary>
    [JsonPropertyName("rogue_dolt_detected")]
    public bool RogueDoltDetected { get; set; }

    /// <summary>
    /// PP13-87-C1: Whether the .dolt directory is incomplete
    /// </summary>
    [JsonPropertyName("is_dolt_incomplete")]
    public bool IsDoltIncomplete { get; set; }
}
