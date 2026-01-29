using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-79: Implementation of Embranch state manifest operations.
/// Manages the .embranch/state.json (or legacy .dmms/state.json) file that tracks
/// Dolt repository state and Git-Dolt commit mappings for project synchronization.
/// PP13-87-C2: Added support for .embranch folder name with .dmms backwards compatibility.
/// </summary>
public class EmbranchStateManifest : IEmbranchStateManifest
{
    private readonly ILogger<EmbranchStateManifest> _logger;

    /// <summary>PP13-87-C2: New manifest directory name for new setups</summary>
    private const string EmbranchDirectoryName = ".embranch";

    /// <summary>PP13-87-C2: Legacy manifest directory name for backwards compatibility</summary>
    private const string LegacyDmmsDirectoryName = ".dmms";

    private const string ManifestFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public EmbranchStateManifest(ILogger<EmbranchStateManifest> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DmmsManifest?> ReadManifestAsync(string projectPath)
    {
        try
        {
            // PP13-87-C1: Use FindManifestAsync to search multiple locations
            var (found, manifestPath, searchedLocations) = await FindManifestAsync(projectPath);

            if (!found || manifestPath == null)
            {
                _logger.LogDebug("[EmbranchStateManifest.ReadManifestAsync] Manifest not found. Searched: {Locations}",
                    string.Join(", ", searchedLocations));
                return null;
            }

            var json = await File.ReadAllTextAsync(manifestPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("[EmbranchStateManifest.ReadManifestAsync] Manifest file is empty at: {Path}", manifestPath);
                return null;
            }

            var manifest = JsonSerializer.Deserialize<DmmsManifest>(json, JsonOptions);

            if (manifest == null)
            {
                _logger.LogWarning("[EmbranchStateManifest.ReadManifestAsync] Failed to deserialize manifest at: {Path}", manifestPath);
                return null;
            }

            if (!ValidateManifest(manifest))
            {
                _logger.LogWarning("[EmbranchStateManifest.ReadManifestAsync] Invalid manifest structure at: {Path}", manifestPath);
                return null;
            }

            _logger.LogDebug("[EmbranchStateManifest.ReadManifestAsync] Successfully read manifest from: {Path}", manifestPath);
            return manifest;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[EmbranchStateManifest.ReadManifestAsync] JSON parse error reading manifest");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmbranchStateManifest.ReadManifestAsync] Error reading manifest");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteManifestAsync(string projectPath, DmmsManifest manifest)
    {
        try
        {
            // PP13-87-C2: Determine which directory to use
            // If an existing manifest is in .dmms, continue using it (don't migrate)
            // For new manifests, use .embranch
            var (manifestDir, manifestPath) = GetWriteManifestPaths(projectPath);

            // Ensure manifest directory exists
            if (!Directory.Exists(manifestDir))
            {
                Directory.CreateDirectory(manifestDir);
                _logger.LogDebug("[EmbranchStateManifest.WriteManifestAsync] Created manifest directory at: {Path}", manifestDir);
            }

            // Update timestamp
            var updatedManifest = manifest with { UpdatedAt = DateTime.UtcNow };

            var json = JsonSerializer.Serialize(updatedManifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, json);

            _logger.LogInformation("[EmbranchStateManifest.WriteManifestAsync] Successfully wrote manifest to: {Path}", manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmbranchStateManifest.WriteManifestAsync] Error writing manifest to: {Path}",
                GetManifestPath(projectPath));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ManifestExistsAsync(string projectPath)
    {
        // PP13-87-C1: Use FindManifestAsync to search multiple locations
        var (found, manifestPath, _) = await FindManifestAsync(projectPath);
        _logger.LogDebug("[EmbranchStateManifest.ManifestExistsAsync] Manifest exists check for {ProjectPath}: {Exists}, Path: {Path}",
            projectPath, found, manifestPath ?? "not found");
        return found;
    }

    /// <inheritdoc />
    public async Task UpdateDoltCommitAsync(string projectPath, string commitHash, string branch)
    {
        try
        {
            var manifest = await ReadManifestAsync(projectPath);

            if (manifest == null)
            {
                _logger.LogWarning("[EmbranchStateManifest.UpdateDoltCommitAsync] No manifest found at: {Path}, cannot update Dolt commit", projectPath);
                return;
            }

            var updatedDolt = manifest.Dolt with
            {
                CurrentCommit = commitHash,
                CurrentBranch = branch
            };

            var updatedManifest = manifest with
            {
                Dolt = updatedDolt,
                UpdatedAt = DateTime.UtcNow
            };

            await WriteManifestAsync(projectPath, updatedManifest);

            _logger.LogInformation("[EmbranchStateManifest.UpdateDoltCommitAsync] Updated Dolt commit to {Commit} on branch {Branch}",
                commitHash.Substring(0, Math.Min(7, commitHash.Length)), branch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmbranchStateManifest.UpdateDoltCommitAsync] Error updating Dolt commit in manifest");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordGitMappingAsync(string projectPath, string gitCommit, string doltCommit)
    {
        try
        {
            var manifest = await ReadManifestAsync(projectPath);

            if (manifest == null)
            {
                _logger.LogWarning("[EmbranchStateManifest.RecordGitMappingAsync] No manifest found at: {Path}, cannot record Git mapping", projectPath);
                return;
            }

            if (!manifest.GitMapping.Enabled)
            {
                _logger.LogDebug("[EmbranchStateManifest.RecordGitMappingAsync] Git mapping is disabled, skipping");
                return;
            }

            var updatedGitMapping = manifest.GitMapping with
            {
                LastGitCommit = gitCommit,
                DoltCommitAtGitCommit = doltCommit
            };

            var updatedManifest = manifest with
            {
                GitMapping = updatedGitMapping,
                UpdatedAt = DateTime.UtcNow
            };

            await WriteManifestAsync(projectPath, updatedManifest);

            _logger.LogInformation("[EmbranchStateManifest.RecordGitMappingAsync] Recorded Git mapping: Git {GitCommit} -> Dolt {DoltCommit}",
                gitCommit.Substring(0, Math.Min(7, gitCommit.Length)),
                doltCommit.Substring(0, Math.Min(7, doltCommit.Length)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmbranchStateManifest.RecordGitMappingAsync] Error recording Git mapping in manifest");
            throw;
        }
    }

    /// <inheritdoc />
    public string GetManifestPath(string projectPath)
    {
        // PP13-87-C2: Check .embranch first, then .dmms for backwards compatibility
        var embranchPath = Path.Combine(projectPath, EmbranchDirectoryName, ManifestFileName);
        if (File.Exists(embranchPath))
        {
            return embranchPath;
        }

        var legacyPath = Path.Combine(projectPath, LegacyDmmsDirectoryName, ManifestFileName);
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        // Default to new .embranch location for new manifests
        return embranchPath;
    }

    /// <inheritdoc />
    public string GetManifestDirectoryPath(string projectPath)
    {
        // PP13-87-C2: Check .embranch first, then .dmms for backwards compatibility
        var embranchDir = Path.Combine(projectPath, EmbranchDirectoryName);
        if (Directory.Exists(embranchDir))
        {
            return embranchDir;
        }

        var legacyDir = Path.Combine(projectPath, LegacyDmmsDirectoryName);
        if (Directory.Exists(legacyDir))
        {
            return legacyDir;
        }

        // Default to new .embranch location for new setups
        return embranchDir;
    }

    /// <inheritdoc />
    [Obsolete("Use GetManifestDirectoryPath instead. This method is kept for backwards compatibility.")]
    public string GetDmmsDirectoryPath(string projectPath)
    {
        return GetManifestDirectoryPath(projectPath);
    }

    /// <summary>
    /// PP13-87-C2: Gets the write paths for manifest, preferring existing location.
    /// If manifest already exists in .dmms, continue using it.
    /// For new manifests, use .embranch.
    /// </summary>
    private (string Directory, string ManifestPath) GetWriteManifestPaths(string projectPath)
    {
        // Check if existing manifest is in legacy .dmms location
        var legacyDir = Path.Combine(projectPath, LegacyDmmsDirectoryName);
        var legacyManifest = Path.Combine(legacyDir, ManifestFileName);
        if (File.Exists(legacyManifest))
        {
            _logger.LogDebug("[EmbranchStateManifest] Using existing legacy .dmms location for manifest");
            return (legacyDir, legacyManifest);
        }

        // Check if .embranch already exists (even without manifest)
        var embranchDir = Path.Combine(projectPath, EmbranchDirectoryName);
        var embranchManifest = Path.Combine(embranchDir, ManifestFileName);
        if (Directory.Exists(embranchDir))
        {
            return (embranchDir, embranchManifest);
        }

        // New setup: use .embranch
        _logger.LogDebug("[EmbranchStateManifest] Using new .embranch location for manifest");
        return (embranchDir, embranchManifest);
    }

    /// <inheritdoc />
    public Task<(bool Found, string? ManifestPath, string[] SearchedLocations)> FindManifestAsync(string projectPath, string? dataPath = null)
    {
        var searchedLocations = new List<string>();

        try
        {
            // PP13-87-C2: Search order (at each location, check .embranch first, then .dmms):
            // 1. Standard location: {projectRoot}/.embranch/state.json, then {projectRoot}/.dmms/state.json
            var standardEmbranchPath = Path.Combine(projectPath, EmbranchDirectoryName, ManifestFileName);
            var standardLegacyPath = Path.Combine(projectPath, LegacyDmmsDirectoryName, ManifestFileName);
            searchedLocations.Add(standardEmbranchPath);
            searchedLocations.Add(standardLegacyPath);

            if (File.Exists(standardEmbranchPath))
            {
                _logger.LogDebug("[EmbranchStateManifest.FindManifestAsync] Found manifest at standard .embranch location: {Path}", standardEmbranchPath);
                return Task.FromResult((true, (string?)standardEmbranchPath, searchedLocations.ToArray()));
            }

            if (File.Exists(standardLegacyPath))
            {
                _logger.LogDebug("[EmbranchStateManifest.FindManifestAsync] Found manifest at legacy .dmms location: {Path}", standardLegacyPath);
                return Task.FromResult((true, (string?)standardLegacyPath, searchedLocations.ToArray()));
            }

            // 2. Relative to EMBRANCH_DATA_PATH: {dataPath}/../.embranch/state.json or .dmms/state.json
            // PP13-87-C1: Use EMBRANCH_DATA_PATH to find server instance manifest
            // e.g., if DataPath is "./mcpdata/PSKD/data", manifest is at "./mcpdata/PSKD/.embranch/state.json"
            if (!string.IsNullOrEmpty(dataPath))
            {
                var absoluteDataPath = Path.GetFullPath(dataPath);
                // Navigate up from data path: data -> PSKD (server instance root)
                var serverInstanceRoot = Path.GetDirectoryName(absoluteDataPath);
                if (!string.IsNullOrEmpty(serverInstanceRoot))
                {
                    var dataRelativeEmbranchPath = Path.Combine(serverInstanceRoot, EmbranchDirectoryName, ManifestFileName);
                    var dataRelativeLegacyPath = Path.Combine(serverInstanceRoot, LegacyDmmsDirectoryName, ManifestFileName);
                    searchedLocations.Add(dataRelativeEmbranchPath);
                    searchedLocations.Add(dataRelativeLegacyPath);

                    if (File.Exists(dataRelativeEmbranchPath))
                    {
                        _logger.LogInformation("[EmbranchStateManifest.FindManifestAsync] Found manifest relative to EMBRANCH_DATA_PATH (.embranch): {Path}", dataRelativeEmbranchPath);
                        return Task.FromResult((true, (string?)dataRelativeEmbranchPath, searchedLocations.ToArray()));
                    }

                    if (File.Exists(dataRelativeLegacyPath))
                    {
                        _logger.LogInformation("[EmbranchStateManifest.FindManifestAsync] Found manifest relative to EMBRANCH_DATA_PATH (.dmms): {Path}", dataRelativeLegacyPath);
                        return Task.FromResult((true, (string?)dataRelativeLegacyPath, searchedLocations.ToArray()));
                    }
                }
            }

            // 3. Fallback: Server instance locations: {projectRoot}/mcpdata/*/.embranch/state.json or .dmms/state.json
            var mcpdataPath = Path.Combine(projectPath, "mcpdata");
            if (Directory.Exists(mcpdataPath))
            {
                foreach (var serverDir in Directory.GetDirectories(mcpdataPath))
                {
                    var serverEmbranchPath = Path.Combine(serverDir, EmbranchDirectoryName, ManifestFileName);
                    var serverLegacyPath = Path.Combine(serverDir, LegacyDmmsDirectoryName, ManifestFileName);
                    searchedLocations.Add(serverEmbranchPath);
                    searchedLocations.Add(serverLegacyPath);

                    if (File.Exists(serverEmbranchPath))
                    {
                        _logger.LogInformation("[EmbranchStateManifest.FindManifestAsync] Found manifest at server instance location (.embranch): {Path}", serverEmbranchPath);
                        return Task.FromResult((true, (string?)serverEmbranchPath, searchedLocations.ToArray()));
                    }

                    if (File.Exists(serverLegacyPath))
                    {
                        _logger.LogInformation("[EmbranchStateManifest.FindManifestAsync] Found manifest at server instance location (.dmms): {Path}", serverLegacyPath);
                        return Task.FromResult((true, (string?)serverLegacyPath, searchedLocations.ToArray()));
                    }
                }
            }

            _logger.LogDebug("[EmbranchStateManifest.FindManifestAsync] No manifest found. Searched: {Locations}", string.Join(", ", searchedLocations));
            return Task.FromResult((false, (string?)null, searchedLocations.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmbranchStateManifest.FindManifestAsync] Error searching for manifest");
            return Task.FromResult((false, (string?)null, searchedLocations.ToArray()));
        }
    }

    /// <inheritdoc />
    public bool ValidateManifest(DmmsManifest manifest)
    {
        // Check version is supported
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            _logger.LogDebug("[EmbranchStateManifest.ValidateManifest] Invalid: missing version");
            return false;
        }

        // Currently only support version 1.0
        if (manifest.Version != "1.0")
        {
            _logger.LogDebug("[EmbranchStateManifest.ValidateManifest] Invalid: unsupported version {Version}", manifest.Version);
            return false;
        }

        // Validate initialization mode if specified
        if (!string.IsNullOrEmpty(manifest.Initialization.Mode) &&
            !InitializationMode.IsValid(manifest.Initialization.Mode))
        {
            _logger.LogDebug("[EmbranchStateManifest.ValidateManifest] Invalid: unsupported initialization mode {Mode}",
                manifest.Initialization.Mode);
            return false;
        }

        // Validate on_clone behavior if specified
        if (!string.IsNullOrEmpty(manifest.Initialization.OnClone) &&
            !OnCloneBehavior.IsValid(manifest.Initialization.OnClone))
        {
            _logger.LogDebug("[EmbranchStateManifest.ValidateManifest] Invalid: unsupported on_clone behavior {Behavior}",
                manifest.Initialization.OnClone);
            return false;
        }

        // Validate on_branch_change behavior if specified
        if (!string.IsNullOrEmpty(manifest.Initialization.OnBranchChange) &&
            !OnBranchChangeBehavior.IsValid(manifest.Initialization.OnBranchChange))
        {
            _logger.LogDebug("[EmbranchStateManifest.ValidateManifest] Invalid: unsupported on_branch_change behavior {Behavior}",
                manifest.Initialization.OnBranchChange);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public DmmsManifest CreateDefaultManifest(string? remoteUrl = null, string defaultBranch = "main", string initMode = "auto")
    {
        return new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = remoteUrl,
                DefaultBranch = defaultBranch,
                CurrentBranch = defaultBranch
            },
            GitMapping = new GitMappingConfig
            {
                Enabled = true
            },
            Initialization = new InitializationConfig
            {
                Mode = initMode,
                OnClone = OnCloneBehavior.SyncToManifest,
                OnBranchChange = OnBranchChangeBehavior.PreserveLocal
            },
            Collections = new CollectionTrackingConfig
            {
                Tracked = new List<string> { "*" },
                Excluded = new List<string>()
            },
            UpdatedAt = DateTime.UtcNow
        };
    }
}
