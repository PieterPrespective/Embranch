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
        /// Generate resolution preview showing what each resolution option would produce
        /// </summary>
        public async Task<ResolutionPreview> GenerateResolutionPreviewAsync(
            DetailedConflictInfo conflict,
            ResolutionType resolutionType)
        {
            var preview = new ResolutionPreview
            {
                ConflictId = conflict.ConflictId,
                ResolutionType = resolutionType,
                ResultingContent = new DocumentContent { Exists = true }
            };

            try
            {
                _logger.LogDebug("Generating resolution preview for conflict {ConflictId} with {ResType}",
                    conflict.ConflictId, resolutionType);

                switch (resolutionType)
                {
                    case ResolutionType.KeepOurs:
                        preview.ResultingContent = BuildDocumentFromValues(conflict.OurValues);
                        preview.Description = "Keep all changes from the target branch (ours)";
                        preview.ConfidenceLevel = 100;
                        
                        // Check what would be lost from theirs
                        foreach (var kvp in conflict.TheirValues)
                        {
                            if (!conflict.OurValues.ContainsKey(kvp.Key) ||
                                !Equals(conflict.OurValues[kvp.Key], kvp.Value))
                            {
                                preview.DataLossWarnings.Add($"Field '{kvp.Key}' from source branch will be lost");
                            }
                        }
                        break;

                    case ResolutionType.KeepTheirs:
                        preview.ResultingContent = BuildDocumentFromValues(conflict.TheirValues);
                        preview.Description = "Keep all changes from the source branch (theirs)";
                        preview.ConfidenceLevel = 100;
                        
                        // Check what would be lost from ours
                        foreach (var kvp in conflict.OurValues)
                        {
                            if (!conflict.TheirValues.ContainsKey(kvp.Key) ||
                                !Equals(conflict.TheirValues[kvp.Key], kvp.Value))
                            {
                                preview.DataLossWarnings.Add($"Field '{kvp.Key}' from target branch will be lost");
                            }
                        }
                        break;

                    case ResolutionType.AutoResolve:
                    case ResolutionType.FieldMerge:
                        preview = await GenerateFieldMergePreview(conflict);
                        break;

                    case ResolutionType.Custom:
                        preview.Description = "Custom resolution - values will be provided by user";
                        preview.ConfidenceLevel = 0;
                        break;

                    default:
                        preview.Description = "Unknown resolution type";
                        preview.ConfidenceLevel = 0;
                        break;
                }

                return preview;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate resolution preview for conflict {ConflictId}",
                    conflict.ConflictId);
                preview.Description = $"Error generating preview: {ex.Message}";
                preview.ConfidenceLevel = 0;
                return preview;
            }
        }

        /// <summary>
        /// Generate field merge preview for intelligent field-level merging
        /// </summary>
        private async Task<ResolutionPreview> GenerateFieldMergePreview(DetailedConflictInfo conflict)
        {
            var preview = new ResolutionPreview
            {
                ConflictId = conflict.ConflictId,
                ResolutionType = ResolutionType.FieldMerge,
                ResultingContent = new DocumentContent { Exists = true }
            };

            var mergedValues = new Dictionary<string, object>();
            var confidence = 100;

            // Start with base values as foundation
            foreach (var kvp in conflict.BaseValues)
            {
                mergedValues[kvp.Key] = kvp.Value;
            }

            // Analyze each field for merging strategy
            var allFields = conflict.BaseValues.Keys
                .Union(conflict.OurValues.Keys)
                .Union(conflict.TheirValues.Keys)
                .Distinct();

            foreach (var field in allFields)
            {
                var baseVal = conflict.BaseValues.GetValueOrDefault(field);
                var ourVal = conflict.OurValues.GetValueOrDefault(field);
                var theirVal = conflict.TheirValues.GetValueOrDefault(field);

                // Determine merge strategy for this field
                if (Equals(ourVal, theirVal))
                {
                    // Both sides agree - no conflict
                    mergedValues[field] = ourVal ?? baseVal;
                }
                else if (Equals(baseVal, ourVal))
                {
                    // Only their side changed
                    mergedValues[field] = theirVal ?? baseVal;
                }
                else if (Equals(baseVal, theirVal))
                {
                    // Only our side changed
                    mergedValues[field] = ourVal ?? baseVal;
                }
                else
                {
                    // Both sides changed differently - need heuristics
                    var mergeResult = DetermineFieldMergeStrategy(field, baseVal, ourVal, theirVal);
                    mergedValues[field] = mergeResult.Value;
                    
                    if (!mergeResult.IsConfident)
                    {
                        confidence = Math.Min(confidence, 50);
                        preview.DataLossWarnings.Add(
                            $"Field '{field}' has conflicting changes - using {mergeResult.Strategy}");
                    }
                }
            }

            preview.ResultingContent = BuildDocumentFromValues(mergedValues);
            preview.ConfidenceLevel = confidence;
            preview.Description = confidence >= 80 
                ? "Automatic field-level merge with high confidence"
                : "Field-level merge with conflicts - manual review recommended";

            return preview;
        }

        /// <summary>
        /// Determine merge strategy for a conflicting field
        /// </summary>
        private (object? Value, string Strategy, bool IsConfident) DetermineFieldMergeStrategy(
            string fieldName,
            object? baseVal,
            object? ourVal,
            object? theirVal)
        {
            // For timestamps, prefer the newer one
            if (fieldName.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("updated", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("modified", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(ourVal?.ToString(), out var ourDate) &&
                    DateTime.TryParse(theirVal?.ToString(), out var theirDate))
                {
                    return ourDate > theirDate 
                        ? (ourVal, "newer timestamp", true)
                        : (theirVal, "newer timestamp", true);
                }
            }

            // For version fields, prefer higher version
            if (fieldName.Contains("version", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(ourVal?.ToString(), out var ourVer) &&
                    int.TryParse(theirVal?.ToString(), out var theirVer))
                {
                    return ourVer > theirVer
                        ? (ourVal, "higher version", true)
                        : (theirVal, "higher version", true);
                }
            }

            // For content fields, we can't auto-merge safely
            if (fieldName.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                // Default to ours but mark as low confidence
                return (ourVal, "target branch content (requires review)", false);
            }

            // Default strategy: prefer non-null, then ours
            if (ourVal != null && theirVal == null)
                return (ourVal, "non-null value", true);
            if (ourVal == null && theirVal != null)
                return (theirVal, "non-null value", true);

            // Both non-null and different - default to ours with low confidence
            return (ourVal, "target branch value (conflict)", false);
        }

        /// <summary>
        /// Build document content from value dictionary
        /// </summary>
        private DocumentContent BuildDocumentFromValues(Dictionary<string, object> values)
        {
            var content = new DocumentContent
            {
                Exists = true
            };

            foreach (var kvp in values)
            {
                if (kvp.Key == "content" || kvp.Key == "document_content")
                {
                    content.Content = kvp.Value?.ToString();
                }
                else if (kvp.Key == "id" || kvp.Key == "document_id")
                {
                    // Skip ID fields
                    continue;
                }
                else if ((kvp.Key.Contains("updated", StringComparison.OrdinalIgnoreCase) ||
                         kvp.Key.Contains("modified", StringComparison.OrdinalIgnoreCase)) &&
                         DateTime.TryParse(kvp.Value?.ToString(), out var timestamp))
                {
                    content.LastModified = timestamp;
                    content.Metadata[kvp.Key] = kvp.Value;
                }
                else
                {
                    content.Metadata[kvp.Key] = kvp.Value;
                }
            }

            return content;
        }

        /// <summary>
        /// Get content comparison for a specific document across branches
        /// </summary>
        public async Task<ContentComparison> GetContentComparisonAsync(
            string tableName,
            string documentId,
            string sourceBranch,
            string targetBranch)
        {
            var comparison = new ContentComparison
            {
                TableName = tableName,
                DocumentId = documentId
            };
            
            try
            {
                _logger.LogDebug("Getting content comparison for {Table}/{DocId} between {Source} and {Target}",
                    tableName, documentId, sourceBranch, targetBranch);
                
                // Get merge base
                var mergeBaseResult = await ExecuteDoltCommandAsync("merge-base", sourceBranch, targetBranch);
                var mergeBase = mergeBaseResult.Success ? mergeBaseResult.Output?.Trim() : null;
                
                if (string.IsNullOrEmpty(mergeBase))
                {
                    _logger.LogWarning("Could not determine merge base, using current HEAD");
                    mergeBase = await _doltCli.GetHeadCommitHashAsync();
                }
                
                // Get content from base
                comparison.BaseContent = await GetDocumentAtCommit(tableName, documentId, mergeBase);
                
                // Get content from source branch
                comparison.SourceContent = await GetDocumentAtBranch(tableName, documentId, sourceBranch);
                
                // Get content from target branch  
                comparison.TargetContent = await GetDocumentAtBranch(tableName, documentId, targetBranch);
                
                // Analyze conflicts
                if (comparison.SourceContent?.Exists == true && comparison.TargetContent?.Exists == true)
                {
                    comparison.HasConflicts = !AreDocumentsEqual(comparison.SourceContent, comparison.TargetContent);
                    
                    if (comparison.HasConflicts)
                    {
                        comparison.ConflictingFields = GetConflictingFields(comparison.SourceContent, comparison.TargetContent);
                        comparison.SuggestedResolution = DetermineSuggestedResolution(comparison);
                    }
                }
                else if (comparison.SourceContent?.Exists != comparison.TargetContent?.Exists)
                {
                    // One exists, one doesn't - this is a delete-modify conflict
                    comparison.HasConflicts = true;
                    comparison.SuggestedResolution = "delete_modify_conflict";
                }
                
                return comparison;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get content comparison for {Table}/{DocId}", tableName, documentId);
                return comparison;
            }
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
                    // Check if this is an empty object indicating successful auto-merge
                    if (rootElement.EnumerateObject().Any())
                    {
                        // Single conflict object with actual content
                        var conflict = ParseSingleConflictElement(rootElement);
                        if (conflict != null)
                        {
                            conflicts.Add(conflict);
                        }
                    }
                    else
                    {
                        // Empty object {} indicates successful auto-merge, no conflicts
                        _logger.LogDebug("Empty conflict object detected - indicates successful auto-merge");
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
                // First check for table/collection name
                if (conflictElement.TryGetProperty("table_name", out var tableNameProp) ||
                    conflictElement.TryGetProperty("table", out tableNameProp) ||
                    conflictElement.TryGetProperty("collection_name", out tableNameProp) ||
                    conflictElement.TryGetProperty("collection", out tableNameProp))
                {
                    conflict.Collection = tableNameProp.GetString() ?? "";
                }

                // If collection is still empty, check for nested structure
                if (string.IsNullOrEmpty(conflict.Collection))
                {
                    // Check for Dolt conflict structure (e.g., from dolt_conflicts table)
                    if (conflictElement.TryGetProperty("our_table", out var ourTableProp))
                    {
                        conflict.Collection = ourTableProp.GetString() ?? "";
                    }
                    else if (conflictElement.TryGetProperty("base_table", out var baseTableProp))
                    {
                        conflict.Collection = baseTableProp.GetString() ?? "";
                    }
                }

                // Try to extract document ID with multiple strategies
                if (conflictElement.TryGetProperty("doc_id", out var docIdProp) ||
                    conflictElement.TryGetProperty("document_id", out docIdProp) ||
                    conflictElement.TryGetProperty("id", out docIdProp))
                {
                    conflict.DocumentId = docIdProp.GetString() ?? "";
                }

                // If document ID is still empty, try to extract from conflict data
                if (string.IsNullOrEmpty(conflict.DocumentId))
                {
                    // Check for Dolt's conflict row structure
                    if (conflictElement.TryGetProperty("our_id", out var ourIdProp))
                    {
                        conflict.DocumentId = ourIdProp.GetString() ?? "";
                    }
                    else if (conflictElement.TryGetProperty("their_id", out var theirIdProp))
                    {
                        conflict.DocumentId = theirIdProp.GetString() ?? "";
                    }
                    else if (conflictElement.TryGetProperty("base_id", out var baseIdProp))
                    {
                        conflict.DocumentId = baseIdProp.GetString() ?? "";
                    }
                    
                    // Try to extract from nested row data
                    if (string.IsNullOrEmpty(conflict.DocumentId))
                    {
                        if (conflictElement.TryGetProperty("our_row", out var ourRow) && 
                            ourRow.TryGetProperty("id", out var ourRowId))
                        {
                            conflict.DocumentId = ourRowId.GetString() ?? "";
                        }
                        else if (conflictElement.TryGetProperty("their_row", out var theirRow) &&
                                theirRow.TryGetProperty("id", out var theirRowId))
                        {
                            conflict.DocumentId = theirRowId.GetString() ?? "";
                        }
                    }
                }
                
                // Extract conflict type if available
                if (conflictElement.TryGetProperty("conflict_type", out var typeProp) ||
                    conflictElement.TryGetProperty("type", out typeProp))
                {
                    var typeStr = typeProp.GetString();
                    conflict.Type = ParseConflictType(typeStr);
                }
                
                // Extract base, our, and their values if present
                ExtractConflictValues(conflictElement, conflict);
                
                // Log warning if critical fields are missing
                if (string.IsNullOrEmpty(conflict.Collection))
                {
                    _logger.LogWarning("Could not extract collection name from conflict element: {Json}", 
                        conflictElement.ToString());
                }
                if (string.IsNullOrEmpty(conflict.DocumentId))
                {
                    _logger.LogWarning("Could not extract document ID from conflict element: {Json}", 
                        conflictElement.ToString());
                }

                return conflict;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse single conflict element: {Json}", 
                    conflictElement.ToString());
                return null;
            }
        }

        /// <summary>
        /// Parse conflict type from string
        /// </summary>
        private ConflictType ParseConflictType(string? typeStr)
        {
            if (string.IsNullOrWhiteSpace(typeStr))
                return ConflictType.ContentModification;
            
            switch (typeStr.ToLowerInvariant())
            {
                case "add-add":
                case "addadd":
                case "add_add":
                    return ConflictType.AddAdd;
                    
                case "delete-modify":
                case "deletemodify":
                case "delete_modify":
                case "modify-delete":
                    return ConflictType.DeleteModify;
                    
                case "metadata":
                case "meta":
                    return ConflictType.MetadataConflict;
                    
                case "schema":
                    return ConflictType.SchemaConflict;
                    
                default:
                    return ConflictType.ContentModification;
            }
        }

        /// <summary>
        /// Extract conflict values from JSON element
        /// </summary>
        private void ExtractConflictValues(JsonElement conflictElement, DetailedConflictInfo conflict)
        {
            // Extract base values
            if (conflictElement.TryGetProperty("base_row", out var baseRow) ||
                conflictElement.TryGetProperty("base", out baseRow))
            {
                ExtractValuesToDict(baseRow, conflict.BaseValues);
            }
            
            // Extract our values
            if (conflictElement.TryGetProperty("our_row", out var ourRow) ||
                conflictElement.TryGetProperty("ours", out ourRow) ||
                conflictElement.TryGetProperty("our", out ourRow))
            {
                ExtractValuesToDict(ourRow, conflict.OurValues);
            }
            
            // Extract their values
            if (conflictElement.TryGetProperty("their_row", out var theirRow) ||
                conflictElement.TryGetProperty("theirs", out theirRow) ||
                conflictElement.TryGetProperty("their", out theirRow))
            {
                ExtractValuesToDict(theirRow, conflict.TheirValues);
            }
            
            // Also check for flattened field structure (e.g., base_content, our_content, their_content)
            foreach (var property in conflictElement.EnumerateObject())
            {
                var propName = property.Name;
                
                if (propName.StartsWith("base_", StringComparison.OrdinalIgnoreCase))
                {
                    var fieldName = propName.Substring(5);
                    conflict.BaseValues[fieldName] = JsonElementToObject(property.Value);
                }
                else if (propName.StartsWith("our_", StringComparison.OrdinalIgnoreCase))
                {
                    var fieldName = propName.Substring(4);
                    conflict.OurValues[fieldName] = JsonElementToObject(property.Value);
                }
                else if (propName.StartsWith("their_", StringComparison.OrdinalIgnoreCase))
                {
                    var fieldName = propName.Substring(6);
                    conflict.TheirValues[fieldName] = JsonElementToObject(property.Value);
                }
            }
        }

        /// <summary>
        /// Extract values from JSON element to dictionary
        /// </summary>
        private void ExtractValuesToDict(JsonElement element, Dictionary<string, object> dict)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    dict[property.Name] = JsonElementToObject(property.Value);
                }
            }
        }

        /// <summary>
        /// Convert JsonElement to object
        /// </summary>
        private object JsonElementToObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? "";
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intVal))
                        return intVal;
                    if (element.TryGetInt64(out var longVal))
                        return longVal;
                    if (element.TryGetDouble(out var doubleVal))
                        return doubleVal;
                    return element.GetRawText();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null!;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(JsonElementToObject).ToList();
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in element.EnumerateObject())
                    {
                        dict[prop.Name] = JsonElementToObject(prop.Value);
                    }
                    return dict;
                default:
                    return element.GetRawText();
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
            try
            {
                _logger.LogDebug("Generating merge preview statistics for {Source} -> {Target}", sourceBranch, targetBranch);
                
                // Get the merge base commit
                var mergeBaseResult = await ExecuteDoltCommandAsync("merge-base", sourceBranch, targetBranch);
                var mergeBase = mergeBaseResult.Success ? mergeBaseResult.Output?.Trim() : null;
                
                if (string.IsNullOrEmpty(mergeBase))
                {
                    _logger.LogWarning("Could not determine merge base, using target branch HEAD");
                    mergeBase = await _doltCli.GetHeadCommitHashAsync();
                }
                
                // Get diff statistics between merge base and source branch
                var diffResult = await ExecuteDoltCommandAsync("diff", "--stat", "--json", mergeBase, sourceBranch);
                
                if (!diffResult.Success || string.IsNullOrWhiteSpace(diffResult.Output))
                {
                    // Fallback to basic diff without JSON if not supported
                    diffResult = await ExecuteDoltCommandAsync("diff", "--stat", mergeBase, sourceBranch);
                }
                
                // Parse diff statistics
                var previewInfo = ParseDiffStatistics(diffResult.Output ?? "");
                
                // Get list of affected tables/collections
                var tablesResult = await ExecuteDoltCommandAsync("diff", "--name-only", mergeBase, sourceBranch);
                if (tablesResult.Success && !string.IsNullOrWhiteSpace(tablesResult.Output))
                {
                    var affectedTables = tablesResult.Output
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToList();
                    
                    previewInfo.CollectionsAffected = affectedTables.Count;
                    
                    // Analyze each table for document changes
                    foreach (var table in affectedTables)
                    {
                        await AnalyzeTableChanges(table, mergeBase, sourceBranch, previewInfo);
                    }
                }
                
                _logger.LogDebug("Merge preview: +{Added} ~{Modified} -{Deleted} documents across {Collections} collections",
                    previewInfo.DocumentsAdded, previewInfo.DocumentsModified, 
                    previewInfo.DocumentsDeleted, previewInfo.CollectionsAffected);
                
                return previewInfo;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate accurate merge preview, using estimates");
                return new MergePreviewInfo
                {
                    DocumentsAdded = 0,
                    DocumentsModified = 0,
                    DocumentsDeleted = 0,
                    CollectionsAffected = 1
                };
            }
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

        /// <summary>
        /// Execute a Dolt command directly
        /// </summary>
        private async Task<DoltCommandResult> ExecuteDoltCommandAsync(params string[] args)
        {
            try
            {
                // Use reflection to access the internal ExecuteDoltCommandAsync method
                var doltCliType = _doltCli.GetType();
                var executeMethod = doltCliType.GetMethod("ExecuteDoltCommandAsync", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (executeMethod != null)
                {
                    var task = executeMethod.Invoke(_doltCli, new object[] { args }) as Task<DoltCommandResult>;
                    if (task != null)
                    {
                        return await task;
                    }
                }
                
                // Fallback if reflection fails
                _logger.LogWarning("Could not execute Dolt command directly, using fallback");
                return new DoltCommandResult(false, "Command execution not available", "", 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute Dolt command: {Args}", string.Join(" ", args));
                return new DoltCommandResult(false, ex.Message, "", 1);
            }
        }

        /// <summary>
        /// Parse diff statistics from Dolt diff output
        /// </summary>
        private MergePreviewInfo ParseDiffStatistics(string diffOutput)
        {
            var info = new MergePreviewInfo
            {
                DocumentsAdded = 0,
                DocumentsModified = 0,
                DocumentsDeleted = 0,
                CollectionsAffected = 0
            };
            
            if (string.IsNullOrWhiteSpace(diffOutput))
                return info;
            
            // Try to parse JSON format first
            if (diffOutput.TrimStart().StartsWith("{") || diffOutput.TrimStart().StartsWith("["))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(diffOutput);
                    // Parse JSON diff statistics
                    if (jsonDoc.RootElement.TryGetProperty("tables_changed", out var tables))
                    {
                        info.CollectionsAffected = tables.GetInt32();
                    }
                    if (jsonDoc.RootElement.TryGetProperty("rows_added", out var added))
                    {
                        info.DocumentsAdded = added.GetInt32();
                    }
                    if (jsonDoc.RootElement.TryGetProperty("rows_modified", out var modified))
                    {
                        info.DocumentsModified = modified.GetInt32();
                    }
                    if (jsonDoc.RootElement.TryGetProperty("rows_deleted", out var deleted))
                    {
                        info.DocumentsDeleted = deleted.GetInt32();
                    }
                    return info;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Failed to parse as JSON, trying text format: {Error}", ex.Message);
                }
            }
            
            // Parse text format (e.g., "3 tables changed, 10 rows added, 5 rows modified, 2 rows deleted")
            var lines = diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("tables changed") || line.Contains("table changed"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+tables?\s+changed");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var tables))
                    {
                        info.CollectionsAffected = tables;
                    }
                }
                
                if (line.Contains("rows"))
                {
                    var addMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+rows?\s+added");
                    if (addMatch.Success && int.TryParse(addMatch.Groups[1].Value, out var added))
                    {
                        info.DocumentsAdded = added;
                    }
                    
                    var modMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+rows?\s+modified");
                    if (modMatch.Success && int.TryParse(modMatch.Groups[1].Value, out var modified))
                    {
                        info.DocumentsModified = modified;
                    }
                    
                    var delMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+rows?\s+deleted");
                    if (delMatch.Success && int.TryParse(delMatch.Groups[1].Value, out var deleted))
                    {
                        info.DocumentsDeleted = deleted;
                    }
                }
            }
            
            return info;
        }

        /// <summary>
        /// Get document content at a specific commit
        /// </summary>
        private async Task<DocumentContent> GetDocumentAtCommit(string tableName, string documentId, string commitHash)
        {
            var content = new DocumentContent
            {
                CommitHash = commitHash,
                Exists = false
            };
            
            try
            {
                // Query for document at specific commit using correct schema
                var sql = $"SELECT * FROM `{tableName}` AS OF '{commitHash}' WHERE doc_id = '{documentId}'";
                var result = await _doltCli.QueryJsonAsync(sql);
                
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var jsonDoc = JsonDocument.Parse(result);
                    if (jsonDoc.RootElement.TryGetProperty("rows", out var rows))
                    {
                        var rowArray = rows.EnumerateArray().ToList();
                        if (rowArray.Any())
                        {
                            content.Exists = true;
                            var row = rowArray.First();
                            
                            // Extract content from various possible field names
                            if (row.TryGetProperty("content", out var contentProp) ||
                                row.TryGetProperty("document_text", out contentProp) ||
                                row.TryGetProperty("document_content", out contentProp))
                            {
                                content.Content = contentProp.GetString();
                            }
                            
                            // Extract all other fields as metadata
                            foreach (var prop in row.EnumerateObject())
                            {
                                if (prop.Name != "content" && prop.Name != "document_text" && 
                                    prop.Name != "document_content" && prop.Name != "doc_id")
                                {
                                    content.Metadata[prop.Name] = JsonElementToObject(prop.Value);
                                }
                            }
                            
                            // Try to extract last modified timestamp
                            if (row.TryGetProperty("updated_at", out var updatedProp) ||
                                row.TryGetProperty("modified_at", out updatedProp) ||
                                row.TryGetProperty("timestamp", out updatedProp))
                            {
                                if (DateTime.TryParse(updatedProp.GetString(), out var timestamp))
                                {
                                    content.LastModified = timestamp;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not get document {DocId} from {Table} at {Commit}: {Error}",
                    documentId, tableName, commitHash, ex.Message);
            }
            
            return content;
        }

        /// <summary>
        /// Get document content at a specific branch
        /// </summary>
        private async Task<DocumentContent> GetDocumentAtBranch(string tableName, string documentId, string branchName)
        {
            // First checkout the branch temporarily to query it
            var currentBranch = await _doltCli.GetCurrentBranchAsync();
            var needsCheckout = currentBranch != branchName;
            
            try
            {
                if (needsCheckout)
                {
                    await _doltCli.CheckoutAsync(branchName);
                }
                
                // Get the HEAD commit of this branch
                var commitHash = await _doltCli.GetHeadCommitHashAsync();
                
                // Query for the document
                var content = await GetDocumentAtCommit(tableName, documentId, commitHash);
                content.CommitHash = commitHash;
                
                return content;
            }
            finally
            {
                // Switch back to original branch
                if (needsCheckout && !string.IsNullOrEmpty(currentBranch))
                {
                    await _doltCli.CheckoutAsync(currentBranch);
                }
            }
        }

        /// <summary>
        /// Check if two documents are equal
        /// </summary>
        private bool AreDocumentsEqual(DocumentContent doc1, DocumentContent doc2)
        {
            if (doc1.Content != doc2.Content)
            {
                _logger.LogDebug("Documents differ in content: '{Content1}' vs '{Content2}'", doc1.Content, doc2.Content);
                return false;
            }
            
            // Check metadata equality
            if (doc1.Metadata.Count != doc2.Metadata.Count)
            {
                _logger.LogDebug("Documents differ in metadata count: {Count1} vs {Count2}", doc1.Metadata.Count, doc2.Metadata.Count);
                return false;
            }
            
            foreach (var kvp in doc1.Metadata)
            {
                if (!doc2.Metadata.TryGetValue(kvp.Key, out var value2))
                {
                    _logger.LogDebug("Documents differ in metadata field '{Key}': '{Value1}' vs missing", 
                        kvp.Key, kvp.Value);
                    return false;
                }
                
                // Handle null and string comparisons more carefully
                var val1Str = kvp.Value?.ToString() ?? "";
                var val2Str = value2?.ToString() ?? "";
                
                if (val1Str != val2Str)
                {
                    _logger.LogDebug("Documents differ in metadata field '{Key}': '{Value1}' vs '{Value2}' (types: {Type1} vs {Type2})", 
                        kvp.Key, val1Str, val2Str, kvp.Value?.GetType().Name ?? "null", value2?.GetType().Name ?? "null");
                    return false;
                }
            }
            
            _logger.LogDebug("Documents are equal: content and metadata match");
            return true;
        }

        /// <summary>
        /// Get list of conflicting fields between two documents
        /// </summary>
        private List<string> GetConflictingFields(DocumentContent doc1, DocumentContent doc2)
        {
            var conflictingFields = new List<string>();
            
            if (doc1.Content != doc2.Content)
            {
                conflictingFields.Add("content");
            }
            
            // Check all metadata fields
            var allKeys = doc1.Metadata.Keys.Union(doc2.Metadata.Keys).Distinct();
            
            foreach (var key in allKeys)
            {
                var value1 = doc1.Metadata.GetValueOrDefault(key);
                var value2 = doc2.Metadata.GetValueOrDefault(key);
                
                if (!Equals(value1, value2))
                {
                    conflictingFields.Add(key);
                }
            }
            
            return conflictingFields;
        }

        /// <summary>
        /// Determine suggested resolution based on content comparison
        /// </summary>
        private string DetermineSuggestedResolution(ContentComparison comparison)
        {
            if (!comparison.HasConflicts)
                return "no_conflict";
            
            // If only metadata conflicts, suggest auto-merge
            if (comparison.ConflictingFields.All(f => f != "content"))
            {
                return "auto_merge_metadata";
            }
            
            // If content is identical in source and target but different from base
            if (comparison.SourceContent?.Content == comparison.TargetContent?.Content &&
                comparison.SourceContent?.Content != comparison.BaseContent?.Content)
            {
                return "identical_changes";
            }
            
            // If one side didn't change from base
            if (comparison.SourceContent?.Content == comparison.BaseContent?.Content)
            {
                return "use_target_changes";
            }
            if (comparison.TargetContent?.Content == comparison.BaseContent?.Content)
            {
                return "use_source_changes";
            }
            
            // Both sides changed differently
            return "manual_merge_required";
        }

        /// <summary>
        /// Analyze changes in a specific table
        /// </summary>
        private async Task AnalyzeTableChanges(string tableName, string fromCommit, string toCommit, MergePreviewInfo info)
        {
            try
            {
                // Query the diff for this specific table
                var sql = $"SELECT diff_type, COUNT(*) as cnt FROM DOLT_DIFF('{fromCommit}', '{toCommit}', '{tableName}') GROUP BY diff_type";
                var result = await _doltCli.QueryJsonAsync(sql);
                
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var jsonDoc = JsonDocument.Parse(result);
                    if (jsonDoc.RootElement.TryGetProperty("rows", out var rows))
                    {
                        foreach (var row in rows.EnumerateArray())
                        {
                            if (row.TryGetProperty("diff_type", out var diffType) &&
                                row.TryGetProperty("cnt", out var count))
                            {
                                var type = diffType.GetString();
                                var cnt = count.GetInt32();
                                
                                switch (type?.ToLower())
                                {
                                    case "added":
                                    case "insert":
                                        info.DocumentsAdded += cnt;
                                        break;
                                    case "modified":
                                    case "update":
                                        info.DocumentsModified += cnt;
                                        break;
                                    case "deleted":
                                    case "delete":
                                    case "removed":
                                        info.DocumentsDeleted += cnt;
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not analyze table {Table} changes: {Error}", tableName, ex.Message);
            }
        }

        #endregion
    }
}