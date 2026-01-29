using Embranch.Models;

namespace Embranch.Services;

/// <summary>
/// PP13-87: Interface for detecting and analyzing repository infrastructure state.
/// Provides comprehensive state analysis to enable consumer LLMs to understand
/// the current repository status and take appropriate actions.
/// </summary>
public interface IRepositoryStateDetector
{
    /// <summary>
    /// Performs comprehensive state analysis of repository infrastructure.
    /// Detects manifest presence, Dolt repository status, Chroma database status,
    /// and path alignment issues.
    /// </summary>
    /// <param name="projectRoot">Path to the project root directory</param>
    /// <returns>Complete analysis with state, description, and available actions</returns>
    Task<RepositoryStateAnalysis> AnalyzeStateAsync(string projectRoot);

    /// <summary>
    /// Validates that all configured paths are correctly aligned.
    /// Detects issues like nested repositories from CLI clones.
    /// </summary>
    /// <param name="projectRoot">Path to the project root directory</param>
    /// <returns>Validation result with any path issues detected</returns>
    Task<PathValidationResult> ValidatePathsAsync(string projectRoot);

    /// <summary>
    /// Checks if a Dolt repository exists at the configured path or in a nested directory.
    /// </summary>
    /// <param name="configuredPath">The configured DOLT_REPOSITORY_PATH</param>
    /// <returns>Tuple of (exists, actualPath, isNested)</returns>
    Task<(bool Exists, string? ActualPath, bool IsNested)> FindDoltRepositoryAsync(string configuredPath);

    /// <summary>
    /// Checks if Chroma database files exist at the configured path.
    /// </summary>
    /// <param name="chromaPath">The configured CHROMA_DATA_PATH</param>
    /// <returns>True if Chroma data files exist</returns>
    Task<bool> ChromaDataExistsAsync(string chromaPath);

    /// <summary>
    /// Gets the effective Dolt repository path, handling nested directories.
    /// This should be used by all Dolt operations for consistent path resolution.
    /// </summary>
    /// <param name="configuredPath">The configured DOLT_REPOSITORY_PATH</param>
    /// <returns>The effective path to use for Dolt operations</returns>
    string GetEffectiveDoltPath(string configuredPath);
}
