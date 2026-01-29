using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Embranch.Services;
using Embranch.Utilities;

namespace Embranch.Tools;

/// <summary>
/// PP13-88: MCP tool to manage Dolt remote repositories.
/// Provides add, remove, and list operations for Dolt remotes.
/// Fills the gap where DoltInit without remote_url left no way to add a remote afterward.
/// </summary>
[McpServerToolType]
public class DoltRemoteTool
{
    private readonly ILogger<DoltRemoteTool> _logger;
    private readonly IDoltCli _doltCli;
    private readonly IRepositoryStateDetector _stateDetector;
    private readonly ISyncStateChecker _syncStateChecker;

    /// <summary>
    /// Initializes a new instance of the DoltRemoteTool class
    /// </summary>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="doltCli">Dolt CLI service for remote operations</param>
    /// <param name="stateDetector">Repository state detector for enhanced error guidance</param>
    /// <param name="syncStateChecker">Sync state checker for project root resolution</param>
    public DoltRemoteTool(
        ILogger<DoltRemoteTool> logger,
        IDoltCli doltCli,
        IRepositoryStateDetector stateDetector,
        ISyncStateChecker syncStateChecker)
    {
        _logger = logger;
        _doltCli = doltCli;
        _stateDetector = stateDetector;
        _syncStateChecker = syncStateChecker;
    }

    /// <summary>
    /// Manage Dolt remote repositories. Use action="list" to view configured remotes,
    /// action="add" to add a new remote, or action="remove" to remove an existing remote.
    /// </summary>
    /// <param name="action">The operation to perform: "list" (default), "add", or "remove"</param>
    /// <param name="name">Remote name (required for add/remove, typically "origin")</param>
    /// <param name="url">Remote URL (required for add, e.g., "dolthub.com/username/repo")</param>
    /// <returns>Result object with operation status and remote information</returns>
    [McpServerTool]
    [Description("Manage Dolt remote repositories. Use action='list' to view configured remotes, action='add' to add a new remote (requires name and url), or action='remove' to remove an existing remote (requires name).")]
    public virtual async Task<object> DoltRemote(
        string action = "list",
        string? name = null,
        string? url = null)
    {
        const string toolName = nameof(DoltRemoteTool);
        const string methodName = nameof(DoltRemote);
        ToolLoggingUtility.LogToolStart(_logger, toolName, methodName,
            $"action: '{action}', name: '{name}', url: '{url}'");

        try
        {
            // Validate action parameter
            var normalizedAction = action.ToLowerInvariant();
            if (normalizedAction != "list" && normalizedAction != "add" && normalizedAction != "remove")
            {
                var error = $"Invalid action: '{action}'. Must be one of: list, add, remove";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = "INVALID_ACTION",
                    message = error,
                    valid_actions = new[] { "list", "add", "remove" }
                };
            }

            // Check if Dolt is available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                const string error = "DOLT_EXECUTABLE_NOT_FOUND";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
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
                // PP13-87: Use state detector for enhanced guidance
                var projectRoot = await _syncStateChecker.GetProjectRootAsync() ?? Directory.GetCurrentDirectory();
                var stateAnalysis = await _stateDetector.AnalyzeStateAsync(projectRoot);
                const string error = "NOT_INITIALIZED";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);

                return new
                {
                    success = false,
                    error = error,
                    message = "No Dolt repository initialized. Cannot manage remotes without a repository.",
                    repository_state = stateAnalysis.State.ToString(),
                    suggested_action = "Call RepositoryStatus to assess the current state, then use BootstrapRepository, DoltInit, or DoltClone to initialize.",
                    available_actions = new[]
                    {
                        "RepositoryStatus() - Check current state and get guidance",
                        "BootstrapRepository() - Initialize from manifest if available",
                        "DoltInit() - Create a new local repository",
                        "DoltClone(url) - Clone from a remote repository"
                    }
                };
            }

            // Dispatch to appropriate handler
            return normalizedAction switch
            {
                "list" => await HandleListAsync(toolName, methodName),
                "add" => await HandleAddAsync(toolName, methodName, name, url),
                "remove" => await HandleRemoveAsync(toolName, methodName, name),
                _ => throw new InvalidOperationException($"Unexpected action: {normalizedAction}")
            };
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to manage remotes: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle the "list" action - returns all configured remotes
    /// </summary>
    private async Task<object> HandleListAsync(string toolName, string methodName)
    {
        var remotes = await _doltCli.ListRemotesAsync();
        var remoteList = remotes?.ToList() ?? new List<Models.RemoteInfo>();

        ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
            $"Found {remoteList.Count} remote(s)");

        return new
        {
            success = true,
            action = "list",
            remotes = remoteList.Select(r => new
            {
                name = r.Name,
                url = r.Url
            }).ToArray(),
            count = remoteList.Count,
            message = remoteList.Count == 0
                ? "No remotes configured. Use DoltRemote(action='add', name='origin', url='...') to add one."
                : $"Found {remoteList.Count} configured remote(s)."
        };
    }

    /// <summary>
    /// Handle the "add" action - adds a new remote
    /// </summary>
    private async Task<object> HandleAddAsync(string toolName, string methodName, string? name, string? url)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(name))
        {
            const string error = "REMOTE_NAME_REQUIRED";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
            return new
            {
                success = false,
                error = error,
                message = "Remote name is required for 'add' action. Typically use 'origin' for the primary remote.",
                example = "DoltRemote(action='add', name='origin', url='dolthub.com/username/repo')"
            };
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            const string error = "REMOTE_URL_REQUIRED";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
            return new
            {
                success = false,
                error = error,
                message = "Remote URL is required for 'add' action.",
                example = "DoltRemote(action='add', name='origin', url='dolthub.com/username/repo')"
            };
        }

        // Check if remote already exists
        var existingRemotes = await _doltCli.ListRemotesAsync();
        var existingRemote = existingRemotes?.FirstOrDefault(r =>
            r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existingRemote != null)
        {
            const string error = "REMOTE_EXISTS";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName,
                $"Remote '{name}' already exists with URL: {existingRemote.Url}");
            return new
            {
                success = false,
                error = error,
                message = $"Remote '{name}' already exists with URL: {existingRemote.Url}",
                existing_remote = new
                {
                    name = existingRemote.Name,
                    url = existingRemote.Url
                },
                suggestion = $"Use DoltRemote(action='remove', name='{name}') first if you want to replace it."
            };
        }

        // Add the remote
        var result = await _doltCli.AddRemoteAsync(name, url);

        if (!result.Success)
        {
            const string error = "ADD_REMOTE_FAILED";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, result.Error ?? "Unknown error");
            return new
            {
                success = false,
                error = error,
                message = $"Failed to add remote: {result.Error ?? result.Output}"
            };
        }

        ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
            $"Added remote '{name}' with URL: {url}");

        return new
        {
            success = true,
            action = "add",
            remote = new
            {
                name = name,
                url = url
            },
            message = $"Successfully added remote '{name}' pointing to {url}",
            next_steps = new[]
            {
                $"Use DoltPush(remote='{name}') to push commits to the remote",
                $"Use DoltPull(remote='{name}') to pull changes from the remote",
                $"Use DoltFetch(remote='{name}') to fetch without merging"
            }
        };
    }

    /// <summary>
    /// Handle the "remove" action - removes an existing remote
    /// </summary>
    private async Task<object> HandleRemoveAsync(string toolName, string methodName, string? name)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(name))
        {
            const string error = "REMOTE_NAME_REQUIRED";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
            return new
            {
                success = false,
                error = error,
                message = "Remote name is required for 'remove' action.",
                example = "DoltRemote(action='remove', name='origin')"
            };
        }

        // Check if remote exists
        var existingRemotes = await _doltCli.ListRemotesAsync();
        var existingRemote = existingRemotes?.FirstOrDefault(r =>
            r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existingRemote == null)
        {
            var availableRemotes = existingRemotes?.Select(r => r.Name).ToList() ?? new List<string>();
            const string error = "REMOTE_NOT_FOUND";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName,
                $"Remote '{name}' not found");
            return new
            {
                success = false,
                error = error,
                message = availableRemotes.Count > 0
                    ? $"Remote '{name}' not found. Available remotes: {string.Join(", ", availableRemotes)}"
                    : $"Remote '{name}' not found. No remotes are currently configured.",
                available_remotes = availableRemotes
            };
        }

        // Remove the remote
        var result = await _doltCli.RemoveRemoteAsync(name);

        if (!result.Success)
        {
            const string error = "REMOVE_REMOTE_FAILED";
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, result.Error ?? "Unknown error");
            return new
            {
                success = false,
                error = error,
                message = $"Failed to remove remote: {result.Error ?? result.Output}"
            };
        }

        ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName,
            $"Removed remote '{name}'");

        return new
        {
            success = true,
            action = "remove",
            removed_remote = new
            {
                name = existingRemote.Name,
                url = existingRemote.Url
            },
            message = $"Successfully removed remote '{name}' (was pointing to {existingRemote.Url})"
        };
    }
}
