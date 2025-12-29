using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DMMS.Models;

namespace DMMS.Services
{
    /// <summary>
    /// Service for tracking document deletions in an external database to survive git operations
    /// </summary>
    public interface IDeletionTracker
    {
        /// <summary>
        /// Initializes the deletion tracking database for a specific repository
        /// </summary>
        Task InitializeAsync(string repoPath);

        /// <summary>
        /// Tracks a document deletion in the external database
        /// </summary>
        Task TrackDeletionAsync(string repoPath, string docId, string collectionName, 
            string originalContentHash, Dictionary<string, object> originalMetadata, 
            string branchContext, string baseCommitHash);

        /// <summary>
        /// Gets all pending deletions for a specific repository and collection
        /// </summary>
        Task<List<DeletionRecord>> GetPendingDeletionsAsync(string repoPath, string collectionName);

        /// <summary>
        /// Gets all pending deletions for a specific repository (all collections)
        /// </summary>
        Task<List<DeletionRecord>> GetPendingDeletionsAsync(string repoPath);

        /// <summary>
        /// Marks a deletion as staged (ready for commit)
        /// </summary>
        Task MarkDeletionStagedAsync(string repoPath, string docId, string collectionName);

        /// <summary>
        /// Marks a deletion as committed (synced successfully)
        /// </summary>
        Task MarkDeletionCommittedAsync(string repoPath, string docId, string collectionName);

        /// <summary>
        /// Cleans up committed deletion records for a repository
        /// </summary>
        Task CleanupCommittedDeletionsAsync(string repoPath);

        /// <summary>
        /// Handles branch change operations (checkout, merge, pull) with keep changes logic
        /// </summary>
        Task HandleBranchChangeAsync(string repoPath, string fromBranch, string toBranch, 
            string fromCommit, string toCommit, bool keepChanges);

        /// <summary>
        /// Cleans up stale tracking records (old pending records that may be orphaned)
        /// </summary>
        Task CleanupStaleTrackingAsync(string repoPath);

        /// <summary>
        /// Removes a specific deletion tracking record (used when document is re-added)
        /// </summary>
        Task RemoveDeletionTrackingAsync(string repoPath, string docId, string collectionName);

        /// <summary>
        /// Checks if a document has a pending deletion record
        /// </summary>
        Task<bool> HasPendingDeletionAsync(string repoPath, string docId, string collectionName);

        /// <summary>
        /// Updates the context information for a deletion record (used during branch changes)
        /// </summary>
        Task UpdateDeletionContextAsync(string repoPath, string docId, string collectionName, 
            string newBranchContext, string newBaseCommitHash);
    }
}