using System.ComponentModel;

namespace DMMS.Models
{
    /// <summary>
    /// Detailed merge conflict information with GUID for tracking
    /// </summary>
    public class DetailedConflictInfo
    {
        /// <summary>
        /// Unique identifier for tracking this conflict across tool calls
        /// </summary>
        public string ConflictId { get; set; } = string.Empty;
        
        /// <summary>
        /// Name of the collection containing the conflicted document
        /// </summary>
        public string Collection { get; set; } = string.Empty;
        
        /// <summary>
        /// Unique identifier of the document in conflict
        /// </summary>
        public string DocumentId { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of conflict detected
        /// </summary>
        public ConflictType Type { get; set; }
        
        /// <summary>
        /// Whether this conflict can be automatically resolved
        /// </summary>
        public bool AutoResolvable { get; set; }
        
        /// <summary>
        /// System-suggested resolution strategy
        /// </summary>
        public string SuggestedResolution { get; set; } = string.Empty;
        
        /// <summary>
        /// Field-level conflict details
        /// </summary>
        public List<FieldConflict> FieldConflicts { get; set; } = new();
        
        /// <summary>
        /// Available resolution options for this conflict
        /// </summary>
        public List<string> ResolutionOptions { get; set; } = new();
        
        /// <summary>
        /// Base values from the common ancestor
        /// </summary>
        public Dictionary<string, object> BaseValues { get; set; } = new();
        
        /// <summary>
        /// Values from our branch
        /// </summary>
        public Dictionary<string, object> OurValues { get; set; } = new();
        
        /// <summary>
        /// Values from their branch (source branch)
        /// </summary>
        public Dictionary<string, object> TheirValues { get; set; } = new();
    }

    /// <summary>
    /// Field-level conflict information for precise conflict resolution
    /// </summary>
    public class FieldConflict
    {
        /// <summary>
        /// Name of the conflicted field
        /// </summary>
        public string FieldName { get; set; } = string.Empty;
        
        /// <summary>
        /// Value in the base (common ancestor) version
        /// </summary>
        public object? BaseValue { get; set; }
        
        /// <summary>
        /// Value in our branch
        /// </summary>
        public object? OurValue { get; set; }
        
        /// <summary>
        /// Value in their branch (source branch)
        /// </summary>
        public object? TheirValue { get; set; }
        
        /// <summary>
        /// Hash of the base value for change detection
        /// </summary>
        public string BaseHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Hash of our value for change detection
        /// </summary>
        public string OurHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Hash of their value for change detection
        /// </summary>
        public string TheirHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether this field conflict can be automatically merged
        /// </summary>
        public bool CanAutoMerge { get; set; }
    }

    /// <summary>
    /// Result of merge preview analysis
    /// </summary>
    public class MergePreviewResult
    {
        /// <summary>
        /// Whether the preview analysis succeeded
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// Whether the merge can be performed automatically without user intervention
        /// </summary>
        public bool CanAutoMerge { get; set; }
        
        /// <summary>
        /// Source branch being merged from
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;
        
        /// <summary>
        /// Target branch being merged into
        /// </summary>
        public string TargetBranch { get; set; } = string.Empty;
        
        /// <summary>
        /// Preview information about changes
        /// </summary>
        public MergePreviewInfo? Preview { get; set; }
        
        /// <summary>
        /// List of detected conflicts (filtered based on request)
        /// </summary>
        public List<DetailedConflictInfo> Conflicts { get; set; } = new();
        
        /// <summary>
        /// Total number of conflicts detected before any filtering
        /// </summary>
        public int TotalConflictsDetected { get; set; }
        
        /// <summary>
        /// Status of auxiliary tables
        /// </summary>
        public AuxiliaryTableStatus? AuxiliaryStatus { get; set; }
        
        /// <summary>
        /// Recommended action for the user
        /// </summary>
        public string RecommendedAction { get; set; } = string.Empty;
        
        /// <summary>
        /// Human-readable message about the preview results
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Preview information about merge changes
    /// </summary>
    public class MergePreviewInfo
    {
        /// <summary>
        /// Number of documents that would be added
        /// </summary>
        public int DocumentsAdded { get; set; }
        
        /// <summary>
        /// Number of documents that would be modified
        /// </summary>
        public int DocumentsModified { get; set; }
        
        /// <summary>
        /// Number of documents that would be deleted
        /// </summary>
        public int DocumentsDeleted { get; set; }
        
        /// <summary>
        /// Number of collections that would be affected
        /// </summary>
        public int CollectionsAffected { get; set; }
    }

    /// <summary>
    /// Status information about auxiliary tables
    /// </summary>
    public class AuxiliaryTableStatus
    {
        /// <summary>
        /// Whether sync state table has conflicts
        /// </summary>
        public bool SyncStateConflict { get; set; }
        
        /// <summary>
        /// Whether local changes table has conflicts
        /// </summary>
        public bool LocalChangesConflict { get; set; }
        
        /// <summary>
        /// Whether sync operations table has conflicts
        /// </summary>
        public bool SyncOperationsConflict { get; set; }
    }

    /// <summary>
    /// User-specified conflict resolution request
    /// </summary>
    public class ConflictResolutionRequest
    {
        /// <summary>
        /// The conflict ID being resolved
        /// </summary>
        public string ConflictId { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of resolution to apply
        /// </summary>
        public ResolutionType ResolutionType { get; set; }
        
        /// <summary>
        /// Field-level resolution choices (for field merge)
        /// </summary>
        public Dictionary<string, string> FieldResolutions { get; set; } = new();
        
        /// <summary>
        /// Custom values to use (for custom resolution)
        /// </summary>
        public Dictionary<string, object> CustomValues { get; set; } = new();
    }

    /// <summary>
    /// Container for multiple conflict resolutions with default strategy
    /// </summary>
    public class ConflictResolutionData
    {
        /// <summary>
        /// List of specific conflict resolutions
        /// </summary>
        public List<ConflictResolutionRequest> Resolutions { get; set; } = new();
        
        /// <summary>
        /// Default strategy for unspecified conflicts
        /// </summary>
        public string DefaultStrategy { get; set; } = "ours";
    }

    /// <summary>
    /// Types of resolution strategies available
    /// </summary>
    public enum ResolutionType
    {
        /// <summary>
        /// Keep our version (target branch)
        /// </summary>
        KeepOurs,
        
        /// <summary>
        /// Keep their version (source branch)
        /// </summary>
        KeepTheirs,
        
        /// <summary>
        /// Merge at field level with specified preferences
        /// </summary>
        FieldMerge,
        
        /// <summary>
        /// Use custom provided values
        /// </summary>
        Custom,
        
        /// <summary>
        /// Let system automatically resolve
        /// </summary>
        AutoResolve
    }

    /// <summary>
    /// Types of conflicts that can occur during merge
    /// </summary>
    public enum ConflictType
    {
        /// <summary>
        /// Same document modified in both branches
        /// </summary>
        ContentModification,
        
        /// <summary>
        /// Metadata conflicts (timestamps, etc.)
        /// </summary>
        MetadataConflict,
        
        /// <summary>
        /// Both branches added same document ID
        /// </summary>
        AddAdd,
        
        /// <summary>
        /// One branch deleted, other modified
        /// </summary>
        DeleteModify,
        
        /// <summary>
        /// Structural schema changes conflict
        /// </summary>
        SchemaConflict
    }

}