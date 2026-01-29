using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-79: Interface for Embranch state manifest operations.
/// Manages reading, writing, and updating the .embranch/state.json file
/// (or legacy .dmms/state.json for backwards compatibility) that tracks
/// Dolt repository state and Git-Dolt commit mappings.
/// PP13-87-C2: Added support for .embranch folder name with .dmms backwards compatibility.
/// </summary>
public interface IEmbranchStateManifest
{
    /// <summary>
    /// Reads the state manifest from the project directory
    /// </summary>
    /// <param name="projectPath">Path to the project root (containing .embranch or .dmms folder)</param>
    /// <returns>The manifest if found and valid, null otherwise</returns>
    Task<DmmsManifest?> ReadManifestAsync(string projectPath);

    /// <summary>
    /// Writes/updates the state manifest to the project directory
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <param name="manifest">The manifest to write</param>
    Task WriteManifestAsync(string projectPath, DmmsManifest manifest);

    /// <summary>
    /// Checks if a manifest exists in the project
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <returns>True if .embranch/state.json or .dmms/state.json exists</returns>
    Task<bool> ManifestExistsAsync(string projectPath);

    /// <summary>
    /// Updates the Dolt commit reference in the manifest.
    /// This is called after Dolt commits to keep manifest in sync.
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <param name="commitHash">New Dolt commit hash</param>
    /// <param name="branch">Current Dolt branch name</param>
    Task UpdateDoltCommitAsync(string projectPath, string commitHash, string branch);

    /// <summary>
    /// Records a Git-Dolt commit mapping.
    /// This associates a Git commit with a Dolt commit for state reconstruction.
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <param name="gitCommit">Git commit hash</param>
    /// <param name="doltCommit">Corresponding Dolt commit hash</param>
    Task RecordGitMappingAsync(string projectPath, string gitCommit, string doltCommit);

    /// <summary>
    /// Gets the manifest file path for a project.
    /// PP13-87-C2: Checks .embranch first, then .dmms for backwards compatibility.
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <returns>Full path to .embranch/state.json or .dmms/state.json</returns>
    string GetManifestPath(string projectPath);

    /// <summary>
    /// PP13-87-C2: Gets the manifest directory path for a project.
    /// Checks .embranch first, then .dmms for backwards compatibility.
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <returns>Full path to .embranch or .dmms directory</returns>
    string GetManifestDirectoryPath(string projectPath);

    /// <summary>
    /// Gets the manifest directory path for a project.
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <returns>Full path to manifest directory</returns>
    [Obsolete("Use GetManifestDirectoryPath instead. This method is kept for backwards compatibility.")]
    string GetDmmsDirectoryPath(string projectPath);

    /// <summary>
    /// PP13-87-C1/C2: Searches for manifest in standard locations.
    /// Searches in order (checking .embranch first, then .dmms at each location):
    /// 1. {projectRoot}/.embranch/state.json, then {projectRoot}/.dmms/state.json
    /// 2. {dataPath}/../.embranch/state.json, then {dataPath}/../.dmms/state.json
    /// 3. {projectRoot}/mcpdata/*/.embranch/state.json, then .dmms/state.json
    /// </summary>
    /// <param name="projectPath">Path to the project root</param>
    /// <param name="dataPath">Optional data path from configuration (EMBRANCH_DATA_PATH)</param>
    /// <returns>Tuple of (found, manifestPath, searchedLocations)</returns>
    Task<(bool Found, string? ManifestPath, string[] SearchedLocations)> FindManifestAsync(string projectPath, string? dataPath = null);

    /// <summary>
    /// Validates a manifest structure
    /// </summary>
    /// <param name="manifest">The manifest to validate</param>
    /// <returns>True if the manifest is valid</returns>
    bool ValidateManifest(DmmsManifest manifest);

    /// <summary>
    /// Creates a default manifest with the specified remote URL
    /// </summary>
    /// <param name="remoteUrl">Optional Dolt remote URL</param>
    /// <param name="defaultBranch">Default branch name (default: "main")</param>
    /// <param name="initMode">Initialization mode (default: "auto")</param>
    /// <returns>A new DmmsManifest with default values</returns>
    DmmsManifest CreateDefaultManifest(string? remoteUrl = null, string defaultBranch = "main", string initMode = "auto");
}
