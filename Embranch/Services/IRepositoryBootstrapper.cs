using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-87: Interface for bootstrapping repository infrastructure from manifest.
/// Provides one-command initialization for fresh clone scenarios where manifest
/// exists but Dolt/Chroma infrastructure is missing.
/// </summary>
public interface IRepositoryBootstrapper
{
    /// <summary>
    /// Bootstraps repository infrastructure from manifest state.
    /// This is the primary recovery mechanism for fresh clone scenarios.
    /// </summary>
    /// <param name="projectRoot">Path to the project root directory</param>
    /// <param name="options">Bootstrap options controlling what to initialize</param>
    /// <returns>Result indicating success and actions performed</returns>
    Task<BootstrapResult> BootstrapFromManifestAsync(string projectRoot, BootstrapOptions options);

    /// <summary>
    /// Fixes path misalignment by moving repository or updating configuration.
    /// Used when Dolt repository is found in a nested directory (CLI clone case).
    /// </summary>
    /// <param name="projectRoot">Path to the project root directory</param>
    /// <param name="mismatch">Information about the path mismatch</param>
    /// <param name="strategy">Strategy to use for fixing the path</param>
    /// <returns>Result indicating success and new path</returns>
    Task<PathFixResult> FixPathMisalignmentAsync(string projectRoot, PathMismatchInfo mismatch, PathFixStrategy strategy);

    /// <summary>
    /// Clones Dolt repository from remote URL to the configured path.
    /// This is a lower-level operation used by BootstrapFromManifestAsync.
    /// </summary>
    /// <param name="remoteUrl">The remote repository URL</param>
    /// <param name="targetPath">The target path for the clone</param>
    /// <returns>Result indicating success and any error details</returns>
    Task<(bool Success, string? Error)> CloneDoltRepositoryAsync(string remoteUrl, string targetPath);

    /// <summary>
    /// Initializes Chroma database directory and syncs from Dolt state.
    /// </summary>
    /// <param name="projectRoot">Path to the project root directory</param>
    /// <returns>Result indicating success and collection count</returns>
    Task<(bool Success, int CollectionCount, string? Error)> InitializeChromaAsync(string projectRoot);

    /// <summary>
    /// Syncs Dolt repository to a specific commit from the manifest.
    /// </summary>
    /// <param name="commitHash">The commit hash to sync to</param>
    /// <returns>Result indicating success</returns>
    Task<(bool Success, string? Error)> SyncToCommitAsync(string commitHash);
}
