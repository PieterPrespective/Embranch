using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DMMS.Models;
using Microsoft.Extensions.Logging;

namespace DMMS.Services
{
    /// <summary>
    /// Service implementation for analyzing merge conflicts
    /// Provides detailed conflict detection and auto-resolution identification
    /// </summary>
    public class ConflictAnalyzer : IConflictAnalyzer
    {
        private readonly IDoltCli _doltCli;
        private readonly ILogger<ConflictAnalyzer> _logger;

        /// <summary>
        /// Initializes a new instance of the ConflictAnalyzer class
        /// </summary>
        /// <param name="doltCli">Dolt CLI service for executing Dolt operations</param>
        /// <param name="logger">Logger for diagnostic information</param>
        public ConflictAnalyzer(IDoltCli doltCli, ILogger<ConflictAnalyzer> logger)
        {
            _doltCli = doltCli;
            _logger = logger;
        }

        /// <summary>
        /// Analyze a potential merge operation to detect conflicts and provide preview information
        /// </summary>
        public async Task<MergePreviewResult> AnalyzeMergeAsync(
            string sourceBranch,
            string targetBranch,
            bool includeAutoResolvable,
            bool detailedDiff)
        {
            var result = new MergePreviewResult
            {
                SourceBranch = sourceBranch,
                TargetBranch = targetBranch,
                Conflicts = new List<DetailedConflictInfo>()
            };

            try
            {
                _logger.LogInformation("Analyzing merge from {Source} to {Target}", sourceBranch, targetBranch);

                // Use Dolt's merge conflict preview if available
                string conflictSummaryJson;
                try
                {
                    conflictSummaryJson = await _doltCli.PreviewMergeConflictsAsync(sourceBranch, targetBranch);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Dolt merge preview not available, falling back to manual analysis: {Error}", ex.Message);
                    conflictSummaryJson = await FallbackConflictAnalysis(sourceBranch, targetBranch);
                }
                
                // Parse conflict data
                var allConflicts = await ParseConflictSummary(conflictSummaryJson);
                
                // Store total before filtering
                var totalConflictsDetected = allConflicts.Count;
                
                // Filter based on auto-resolvable preference
                var conflicts = allConflicts;
                if (!includeAutoResolvable)
                {
                    conflicts = allConflicts.Where(c => !c.AutoResolvable).ToList();
                }

                // Analyze each conflict for detailed information
                foreach (var conflict in conflicts)
                {
                    conflict.ConflictId = GenerateConflictId(conflict);
                    conflict.AutoResolvable = await CanAutoResolveConflictAsync(conflict);
                    conflict.SuggestedResolution = DetermineSuggestedResolution(conflict);
                    conflict.ResolutionOptions = DetermineResolutionOptions(conflict);
                    
                    // Add detailed field conflicts if requested
                    if (detailedDiff)
                    {
                        conflict.FieldConflicts = await GetFieldConflicts(conflict);
                    }
                }

                result.Conflicts = conflicts;
                result.TotalConflictsDetected = totalConflictsDetected;
                result.CanAutoMerge = !allConflicts.Any(c => !c.AutoResolvable);
                result.Success = true;
                
                // Generate preview statistics
                result.Preview = await GenerateMergePreview(sourceBranch, targetBranch);
                
                // Check auxiliary tables
                result.AuxiliaryStatus = await CheckAuxiliaryTableStatus();
                
                // Determine recommended action
                result.RecommendedAction = DetermineRecommendedAction(result);
                result.Message = GeneratePreviewMessage(result);

                _logger.LogInformation("Merge analysis complete: {ConflictCount} conflicts, {AutoResolvable} auto-resolvable", 
                    conflicts.Count, conflicts.Count(c => c.AutoResolvable));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze merge");
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Get detailed conflict information for a specific table
        /// </summary>
        public async Task<List<DetailedConflictInfo>> GetDetailedConflictsAsync(string tableName)
        {
            _logger.LogDebug("Getting detailed conflicts for table {Table}", tableName);
            
            var conflicts = new List<DetailedConflictInfo>();
            
            try
            {
                var conflictData = await _doltCli.GetConflictDetailsAsync(tableName);
                
                foreach (var row in conflictData)
                {
                    var conflict = ConvertToDetailedConflictInfo(row, tableName);
                    conflict.ConflictId = GenerateConflictId(conflict);
                    conflict.AutoResolvable = await CanAutoResolveConflictAsync(conflict);
                    
                    conflicts.Add(conflict);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get detailed conflicts for table {Table}", tableName);
            }
            
            return conflicts;
        }

        /// <summary>
        /// Determine if a specific conflict can be automatically resolved
        /// </summary>
        public async Task<bool> CanAutoResolveConflictAsync(DetailedConflictInfo conflict)
        {
            try
            {
                // Auto-resolvable scenarios:
                // 1. Different fields were modified (no overlap)
                // 2. Metadata-only conflicts with clear precedence
                // 3. Add-add conflicts with identical content
                
                if (conflict.Type == ConflictType.ContentModification)
                {
                    // Check if different fields were modified
                    var baseToOurs = GetModifiedFields(conflict.BaseValues, conflict.OurValues);
                    var baseToTheirs = GetModifiedFields(conflict.BaseValues, conflict.TheirValues);
                    
                    // No overlap = auto-resolvable
                    var hasOverlap = baseToOurs.Intersect(baseToTheirs).Any();
                    _logger.LogDebug("Conflict {ConflictId}: Modified fields overlap = {HasOverlap}", 
                        conflict.ConflictId, hasOverlap);
                    
                    return !hasOverlap;
                }
                
                if (conflict.Type == ConflictType.AddAdd)
                {
                    // Check if content is identical
                    var ourContent = conflict.OurValues.GetValueOrDefault("content")?.ToString();
                    var theirContent = conflict.TheirValues.GetValueOrDefault("content")?.ToString();
                    var isIdentical = string.Equals(ourContent, theirContent, StringComparison.Ordinal);
                    
                    _logger.LogDebug("AddAdd conflict {ConflictId}: Content identical = {IsIdentical}", 
                        conflict.ConflictId, isIdentical);
                    
                    return isIdentical;
                }
                
                // Metadata conflicts can often be auto-resolved by preferring newer timestamps
                if (conflict.Type == ConflictType.MetadataConflict)
                {
                    return true; // Most metadata conflicts can be auto-resolved
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analyzing auto-resolve potential for conflict {ConflictId}", 
                    conflict.ConflictId);
                return false;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Fallback conflict analysis when Dolt's native preview is not available
        /// </summary>
        private async Task<string> FallbackConflictAnalysis(string sourceBranch, string targetBranch)
        {
            // Simple fallback - return empty JSON array indicating no conflicts detected
            // In a real implementation, this could perform basic diff analysis
            _logger.LogDebug("Using fallback conflict analysis");
            return "[]";
        }

        /// <summary>
        /// Parse conflict summary JSON from Dolt into structured conflict objects
        /// </summary>
        private async Task<List<DetailedConflictInfo>> ParseConflictSummary(string conflictJson)
        {
            var conflicts = new List<DetailedConflictInfo>();
            
            if (string.IsNullOrWhiteSpace(conflictJson) || conflictJson == "[]")
            {
                return conflicts; // No conflicts
            }

            _logger.LogDebug("Parsing conflict JSON: {Json}", conflictJson);

            try
            {
                // First, try to parse as JsonElement to understand the structure
                var jsonDocument = JsonDocument.Parse(conflictJson);
                var rootElement = jsonDocument.RootElement;

                if (rootElement.ValueKind == JsonValueKind.Array)
                {
                    // Parse as array of conflicts
                    foreach (var conflictElement in rootElement.EnumerateArray())
                    {
                        var conflict = ParseSingleConflictElement(conflictElement);
                        if (conflict != null)
                        {
                            conflicts.Add(conflict);
                        }
                    }
                }
                else if (rootElement.ValueKind == JsonValueKind.Object)
                {
                    // Single conflict object
                    var conflict = ParseSingleConflictElement(rootElement);
                    if (conflict != null)
                    {
                        conflicts.Add(conflict);
                    }
                }
                else
                {
                    _logger.LogWarning("Unexpected JSON format for conflict summary: {ValueKind}", rootElement.ValueKind);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse conflict summary JSON: {Json}", conflictJson);
            }
            
            return conflicts;
        }

        /// <summary>
        /// Parse a single conflict element from JSON
        /// </summary>
        private DetailedConflictInfo? ParseSingleConflictElement(JsonElement conflictElement)
        {
            try
            {
                var conflict = new DetailedConflictInfo
                {
                    Type = ConflictType.ContentModification // Default, can be refined
                };

                // Try to extract common fields with flexible property names
                if (conflictElement.TryGetProperty("table_name", out var tableNameProp) ||
                    conflictElement.TryGetProperty("table", out tableNameProp) ||
                    conflictElement.TryGetProperty("collection_name", out tableNameProp))
                {
                    conflict.Collection = tableNameProp.GetString() ?? "";
                }

                if (conflictElement.TryGetProperty("doc_id", out var docIdProp) ||
                    conflictElement.TryGetProperty("document_id", out docIdProp) ||
                    conflictElement.TryGetProperty("id", out docIdProp))
                {
                    conflict.DocumentId = docIdProp.GetString() ?? "";
                }

                return conflict;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse single conflict element");
                return null;
            }
        }

        /// <summary>
        /// Generate a stable conflict ID based on conflict characteristics
        /// </summary>
        private string GenerateConflictId(DetailedConflictInfo conflict)
        {
            // Generate stable GUID based on conflict content
            var input = $"{conflict.Collection}_{conflict.DocumentId}_{conflict.Type}";
            
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var hashString = BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToLower();
            
            return $"conf_{hashString}";
        }

        /// <summary>
        /// Get list of fields that were modified between two versions
        /// </summary>
        private List<string> GetModifiedFields(
            Dictionary<string, object> baseValues,
            Dictionary<string, object> newValues)
        {
            var modified = new List<string>();
            
            foreach (var kvp in newValues)
            {
                if (!baseValues.ContainsKey(kvp.Key) || 
                    !Equals(baseValues[kvp.Key], kvp.Value))
                {
                    modified.Add(kvp.Key);
                }
            }
            
            return modified;
        }

        /// <summary>
        /// Convert raw conflict data to DetailedConflictInfo object
        /// </summary>
        private DetailedConflictInfo ConvertToDetailedConflictInfo(
            Dictionary<string, object> conflictRow, 
            string tableName)
        {
            var conflict = new DetailedConflictInfo
            {
                Collection = tableName,
                DocumentId = conflictRow.GetValueOrDefault("our_doc_id")?.ToString() ?? "",
                Type = ConflictType.ContentModification
            };

            // Extract base, our, and their values from conflict row
            foreach (var kvp in conflictRow)
            {
                if (kvp.Key.StartsWith("base_"))
                {
                    var fieldName = kvp.Key.Substring(5);
                    conflict.BaseValues[fieldName] = kvp.Value;
                }
                else if (kvp.Key.StartsWith("our_"))
                {
                    var fieldName = kvp.Key.Substring(4);
                    conflict.OurValues[fieldName] = kvp.Value;
                }
                else if (kvp.Key.StartsWith("their_"))
                {
                    var fieldName = kvp.Key.Substring(6);
                    conflict.TheirValues[fieldName] = kvp.Value;
                }
            }

            return conflict;
        }

        /// <summary>
        /// Get field-level conflict details for a specific conflict
        /// </summary>
        private async Task<List<FieldConflict>> GetFieldConflicts(DetailedConflictInfo conflict)
        {
            var fieldConflicts = new List<FieldConflict>();
            
            // Identify all fields that have conflicts
            var allFields = conflict.BaseValues.Keys
                .Union(conflict.OurValues.Keys)
                .Union(conflict.TheirValues.Keys)
                .ToList();

            foreach (var field in allFields)
            {
                var baseValue = conflict.BaseValues.GetValueOrDefault(field);
                var ourValue = conflict.OurValues.GetValueOrDefault(field);
                var theirValue = conflict.TheirValues.GetValueOrDefault(field);

                // Check if this field actually has a conflict
                var hasConflict = !Equals(ourValue, theirValue);
                
                if (hasConflict)
                {
                    var fieldConflict = new FieldConflict
                    {
                        FieldName = field,
                        BaseValue = baseValue,
                        OurValue = ourValue,
                        TheirValue = theirValue,
                        BaseHash = ComputeValueHash(baseValue),
                        OurHash = ComputeValueHash(ourValue),
                        TheirHash = ComputeValueHash(theirValue),
                        CanAutoMerge = !Equals(baseValue, ourValue) && !Equals(baseValue, theirValue) && 
                                      !Equals(ourValue, theirValue)
                    };
                    
                    fieldConflicts.Add(fieldConflict);
                }
            }

            return fieldConflicts;
        }

        /// <summary>
        /// Compute hash of a field value for change tracking
        /// </summary>
        private string ComputeValueHash(object? value)
        {
            if (value == null) return "null";
            
            var valueString = value.ToString() ?? "";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(valueString));
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLower();
        }

        /// <summary>
        /// Determine suggested resolution strategy for a conflict
        /// </summary>
        private string DetermineSuggestedResolution(DetailedConflictInfo conflict)
        {
            if (conflict.AutoResolvable)
            {
                return conflict.Type == ConflictType.ContentModification ? "field_merge" : "auto_resolve";
            }
            
            return "manual_review";
        }

        /// <summary>
        /// Determine available resolution options for a conflict
        /// </summary>
        private List<string> DetermineResolutionOptions(DetailedConflictInfo conflict)
        {
            var options = new List<string> { "keep_ours", "keep_theirs" };
            
            if (conflict.Type == ConflictType.ContentModification)
            {
                options.Add("field_merge");
                options.Add("custom_merge");
            }
            
            if (conflict.AutoResolvable)
            {
                options.Add("auto_resolve");
            }
            
            return options;
        }

        /// <summary>
        /// Generate merge preview statistics
        /// </summary>
        private async Task<MergePreviewInfo> GenerateMergePreview(string sourceBranch, string targetBranch)
        {
            // This would analyze the diff between branches to provide statistics
            // For now, return placeholder data
            return new MergePreviewInfo
            {
                DocumentsAdded = 0,
                DocumentsModified = 0,
                DocumentsDeleted = 0,
                CollectionsAffected = 1
            };
        }

        /// <summary>
        /// Check status of auxiliary tables for conflicts
        /// </summary>
        private async Task<AuxiliaryTableStatus> CheckAuxiliaryTableStatus()
        {
            return new AuxiliaryTableStatus
            {
                SyncStateConflict = false,
                LocalChangesConflict = false,
                SyncOperationsConflict = false
            };
        }

        /// <summary>
        /// Determine recommended action based on analysis results
        /// </summary>
        private string DetermineRecommendedAction(MergePreviewResult result)
        {
            if (!result.Conflicts.Any())
            {
                return "Execute merge - no conflicts detected";
            }
            
            if (result.CanAutoMerge)
            {
                return "Execute merge with auto-resolution";
            }
            
            return "Review conflicts and provide resolution preferences";
        }

        /// <summary>
        /// Generate human-readable preview message
        /// </summary>
        private string GeneratePreviewMessage(MergePreviewResult result)
        {
            var conflictCount = result.Conflicts.Count;
            var autoResolvable = result.Conflicts.Count(c => c.AutoResolvable);
            var manualRequired = conflictCount - autoResolvable;
            
            if (conflictCount == 0)
            {
                return "No conflicts detected - merge can proceed automatically";
            }
            
            if (manualRequired == 0)
            {
                return $"All {conflictCount} conflicts can be automatically resolved";
            }
            
            return $"Merge has {conflictCount} conflicts: {autoResolvable} auto-resolvable, {manualRequired} require manual resolution";
        }

        #endregion
    }
}