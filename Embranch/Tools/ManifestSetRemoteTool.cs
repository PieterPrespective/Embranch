using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Embranch.Models;
using Embranch.Services;
using Embranch.Utilities;

namespace Embranch.Tools;

/// <summary>
/// PP13-81: MCP tool to update the remote URL in the Embranch manifest.
/// PP13-88: Enhanced to optionally configure the Dolt remote as well.
/// Allows configuration of remote repository without requiring restart.
/// This enables recovery from the empty repository initialization scenario.
/// </summary>
[McpServerToolType]
public class ManifestSetRemoteTool
{
    private readonly ILogger<ManifestSetRemoteTool> _logger;
    private readonly IEmbranchStateManifest _manifestService;
    private readonly ISyncStateChecker _syncStateChecker;
    private readonly IGitIntegration _gitIntegration;
    private readonly IDoltCli _doltCli;

    /// <summary>
    /// Initializes a new instance of the ManifestSetRemoteTool class
    /// </summary>
    public ManifestSetRemoteTool(
        ILogger<ManifestSetRemoteTool> logger,
        IEmbranchStateManifest manifestService,
        ISyncStateChecker syncStateChecker,
        IGitIntegration gitIntegration,
        IDoltCli doltCli)
    {
        _logger = logger;
        _manifestService = manifestService;
        _syncStateChecker = syncStateChecker;
        _gitIntegration = gitIntegration;
        _doltCli = doltCli;
    }

    /// <summary>
    /// Update the remote URL in the Embranch manifest. Optionally also configure the Dolt remote.
    /// After setting, use DoltClone or DoltPush to interact with the remote.
    /// This tool enables recovery when Embranch started without a configured remote URL.
    /// </summary>
    /// <param name="remote_url">The remote repository URL (e.g., "dolthub.com/username/repo")</param>
    /// <param name="default_branch">Default branch name (defaults to "main")</param>
    /// <param name="project_root">Optional project root path override</param>
    /// <param name="configure_dolt_remote">PP13-88: If true, also add the remote to the Dolt repository</param>
    /// <param name="remote_name">PP13-88: Name for the Dolt remote (defaults to "origin")</param>
    [McpServerTool]
    [Description("Update the remote URL in the Embranch manifest. Set configure_dolt_remote=true to also add the remote to the Dolt repository. After setting, use DoltClone or DoltPush to interact with the remote.")]
    public virtual async Task<object> ManifestSetRemote(
        string remote_url,
        string? default_branch = null,
        string? project_root = null,
        bool configure_dolt_remote = false,
        string remote_name = "origin")
    {
        const string toolName = nameof(ManifestSetRemoteTool);
        const string methodName = nameof(ManifestSetRemote);
        ToolLoggingUtility.LogToolStart(_logger, toolName, methodName,
            $"remote_url: {remote_url}, default_branch: {default_branch}, configure_dolt_remote: {configure_dolt_remote}, remote_name: {remote_name}");

        try
        {
            // Validate remote URL
            if (string.IsNullOrWhiteSpace(remote_url))
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Remote URL is required");
                return new
                {
                    success = false,
                    error = "REMOTE_URL_REQUIRED",
                    message = "Remote URL is required"
                };
            }

            // Determine project root
            string resolvedProjectRoot;
            if (!string.IsNullOrEmpty(project_root))
            {
                resolvedProjectRoot = project_root;
            }
            else
            {
                // Try to get from sync state checker first
                var checkerRoot = await _syncStateChecker.GetProjectRootAsync();
                if (!string.IsNullOrEmpty(checkerRoot))
                {
                    resolvedProjectRoot = checkerRoot;
                }
                else
                {
                    // Fall back to Git root detection
                    var gitRoot = await _gitIntegration.GetGitRootAsync(Directory.GetCurrentDirectory());
                    resolvedProjectRoot = gitRoot ?? Directory.GetCurrentDirectory();
                }
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Using project root: {resolvedProjectRoot}");

            // Read existing manifest
            var existingManifest = await _manifestService.ReadManifestAsync(resolvedProjectRoot);
            var previousRemoteUrl = existingManifest?.Dolt.RemoteUrl;

            DmmsManifest updatedManifest;

            if (existingManifest != null)
            {
                // Update existing manifest
                ToolLoggingUtility.LogToolInfo(_logger, toolName,
                    $"Updating existing manifest. Previous remote: {previousRemoteUrl ?? "(none)"}");

                var updatedDolt = existingManifest.Dolt with
                {
                    RemoteUrl = remote_url,
                    DefaultBranch = default_branch ?? existingManifest.Dolt.DefaultBranch
                };

                updatedManifest = existingManifest with
                {
                    Dolt = updatedDolt,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "ManifestSetRemoteTool"
                };
            }
            else
            {
                // Create new manifest with remote URL
                ToolLoggingUtility.LogToolInfo(_logger, toolName, "No manifest found, creating new manifest with remote URL");

                updatedManifest = _manifestService.CreateDefaultManifest(
                    remoteUrl: remote_url,
                    defaultBranch: default_branch ?? "main",
                    initMode: "auto"
                );

                // Set the updated_by field
                updatedManifest = updatedManifest with
                {
                    UpdatedBy = "ManifestSetRemoteTool"
                };
            }

            // Write updated manifest
            await _manifestService.WriteManifestAsync(resolvedProjectRoot, updatedManifest);

            // Invalidate sync state cache so next check reflects the new remote URL
            _syncStateChecker.InvalidateCache();

            var manifestPath = _manifestService.GetManifestPath(resolvedProjectRoot);

            // PP13-88: Optionally configure Dolt remote as well
            bool doltRemoteConfigured = false;
            string? doltRemoteWarning = null;
            bool doltRemoteAlreadyExists = false;

            if (configure_dolt_remote)
            {
                var isDoltInitialized = await _doltCli.IsInitializedAsync();
                if (isDoltInitialized)
                {
                    // Check if remote already exists
                    var existingRemotes = await _doltCli.ListRemotesAsync();
                    var existingRemote = existingRemotes?.FirstOrDefault(r =>
                        r.Name.Equals(remote_name, StringComparison.OrdinalIgnoreCase));

                    if (existingRemote != null)
                    {
                        doltRemoteAlreadyExists = true;
                        doltRemoteWarning = $"Dolt remote '{remote_name}' already exists with URL: {existingRemote.Url}. " +
                            $"Use DoltRemote(action='remove', name='{remote_name}') first to replace it.";
                        ToolLoggingUtility.LogToolWarning(_logger, toolName, doltRemoteWarning);
                    }
                    else
                    {
                        // Add the Dolt remote
                        var addResult = await _doltCli.AddRemoteAsync(remote_name, remote_url);
                        if (addResult.Success)
                        {
                            doltRemoteConfigured = true;
                            ToolLoggingUtility.LogToolInfo(_logger, toolName,
                                $"Added Dolt remote '{remote_name}' with URL: {remote_url}");
                        }
                        else
                        {
                            doltRemoteWarning = $"Failed to add Dolt remote: {addResult.Error ?? addResult.Output}";
                            ToolLoggingUtility.LogToolWarning(_logger, toolName, doltRemoteWarning);
                        }
                    }
                }
                else
                {
                    doltRemoteWarning = "Dolt repository not initialized. Manifest updated, but Dolt remote not configured. " +
                        "Use DoltInit or DoltClone first, then call DoltRemote(action='add') to add the remote.";
                    ToolLoggingUtility.LogToolWarning(_logger, toolName, doltRemoteWarning);
                }
            }

            ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
                $"Remote URL set to: {remote_url}" + (doltRemoteConfigured ? $", Dolt remote '{remote_name}' also configured" : ""));

            // Build next_steps based on current state
            var nextSteps = new List<string>();
            if (doltRemoteConfigured)
            {
                nextSteps.Add($"Use DoltPush(remote='{remote_name}') to push commits to the remote");
                nextSteps.Add($"Use DoltPull(remote='{remote_name}') to pull changes from the remote");
            }
            else if (doltRemoteAlreadyExists)
            {
                nextSteps.Add($"Use DoltRemote(action='remove', name='{remote_name}') to remove existing remote first");
                nextSteps.Add("Then call ManifestSetRemote again with configure_dolt_remote=true");
            }
            else
            {
                nextSteps.Add("Use DoltClone to clone from the configured remote");
                nextSteps.Add("If a local repository already exists, use DoltClone with force=true to overwrite it");
            }

            return new
            {
                success = true,
                message = $"Remote URL updated to: {remote_url}" + (doltRemoteConfigured ? $" (Dolt remote '{remote_name}' also configured)" : ""),
                manifest = new
                {
                    path = manifestPath,
                    remote_url = remote_url,
                    default_branch = updatedManifest.Dolt.DefaultBranch,
                    previous_remote_url = previousRemoteUrl,
                    updated_at = updatedManifest.UpdatedAt.ToString("O")
                },
                dolt_remote = new
                {
                    configured = doltRemoteConfigured,
                    name = configure_dolt_remote ? remote_name : (string?)null,
                    already_exists = doltRemoteAlreadyExists,
                    warning = doltRemoteWarning
                },
                next_steps = nextSteps.ToArray()
            };
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to update manifest: {ex.Message}"
            };
        }
    }
}
