using System.Text.Json;
using DMMS.Models;
using Microsoft.Extensions.Logging;

namespace DMMS.Services
{
    /// <summary>
    /// Service implementation for resolving merge conflicts using various resolution strategies
    /// Supports field-level merging, custom values, and automatic resolution
    /// </summary>
    public class MergeConflictResolver : IMergeConflictResolver
    {
        private readonly IDoltCli _doltCli;
        private readonly ILogger<MergeConflictResolver> _logger;

        /// <summary>
        /// Initializes a new instance of the MergeConflictResolver class
        /// </summary>
        /// <param name="doltCli">Dolt CLI service for executing Dolt operations</param>
        /// <param name="logger">Logger for diagnostic information</param>
        public MergeConflictResolver(IDoltCli doltCli, ILogger<MergeConflictResolver> logger)
        {
            _doltCli = doltCli;
            _logger = logger;
        }

        /// <summary>
        /// Resolve a specific conflict using the provided resolution strategy
        /// </summary>
        public async Task<bool> ResolveConflictAsync(
            DetailedConflictInfo conflict,
            ConflictResolutionRequest resolution)
        {
            _logger.LogInformation("Resolving conflict {ConflictId} using strategy {Strategy}", 
                conflict.ConflictId, resolution.ResolutionType);

            try
            {
                switch (resolution.ResolutionType)
                {
                    case ResolutionType.KeepOurs:
                        return await ResolveKeepOurs(conflict);
                    
                    case ResolutionType.KeepTheirs:
                        return await ResolveKeepTheirs(conflict);
                    
                    case ResolutionType.FieldMerge:
                        return await ApplyFieldMergeAsync(
                            conflict.Collection,
                            conflict.DocumentId,
                            resolution.FieldResolutions);
                    
                    case ResolutionType.Custom:
                        return await ApplyCustomResolutionAsync(
                            conflict.Collection,
                            conflict.DocumentId,
                            resolution.CustomValues);
                    
                    case ResolutionType.AutoResolve:
                        return await AutoResolveConflictAsync(conflict);
                    
                    default:
                        _logger.LogWarning("Unknown resolution type: {ResolutionType}", resolution.ResolutionType);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve conflict {ConflictId}", conflict.ConflictId);
                return false;
            }
        }

        /// <summary>
        /// Automatically resolve all conflicts that can be safely auto-resolved
        /// </summary>
        public async Task<int> AutoResolveConflictsAsync(List<DetailedConflictInfo> conflicts)
        {
            _logger.LogInformation("Attempting auto-resolution for {ConflictCount} conflicts", conflicts.Count);
            
            int resolved = 0;
            
            foreach (var conflict in conflicts.Where(c => c.AutoResolvable))
            {
                try
                {
                    if (await AutoResolveConflictAsync(conflict))
                    {
                        resolved++;
                        _logger.LogDebug("Auto-resolved conflict {ConflictId}", conflict.ConflictId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-resolve conflict {ConflictId}", conflict.ConflictId);
                }
            }

            _logger.LogInformation("Auto-resolved {Resolved} of {Total} auto-resolvable conflicts", 
                resolved, conflicts.Count(c => c.AutoResolvable));
            
            return resolved;
        }

        /// <summary>
        /// Apply field-level merge resolution where different fields are kept from different branches
        /// </summary>
        public async Task<bool> ApplyFieldMergeAsync(
            string tableName,
            string documentId,
            Dictionary<string, string> fieldResolutions)
        {
            _logger.LogDebug("Applying field merge for document {DocumentId} in table {Table}", 
                documentId, tableName);

            try
            {
                // Build UPDATE statement for conflict table
                var updates = new List<string>();
                foreach (var field in fieldResolutions)
                {
                    var sourceColumn = field.Value.ToLower() == "ours" ? $"our_{field.Key}" : $"their_{field.Key}";
                    var targetColumn = $"our_{field.Key}"; // We update the "our" columns to reflect resolution
                    updates.Add($"{targetColumn} = {sourceColumn}");
                }

                if (!updates.Any())
                {
                    _logger.LogWarning("No field resolutions provided for document {DocumentId}", documentId);
                    return false;
                }

                var updateSql = $@"
                    UPDATE dolt_conflicts_{tableName}
                    SET {string.Join(", ", updates)}
                    WHERE our_doc_id = '{documentId}'";

                _logger.LogDebug("Executing field merge SQL: {Sql}", updateSql);
                var updateResult = await _doltCli.ExecuteConflictResolutionAsync(updateSql);
                
                if (updateResult > 0)
                {
                    // Delete the conflict marker after successful resolution
                    var deleteSql = $@"
                        DELETE FROM dolt_conflicts_{tableName}
                        WHERE our_doc_id = '{documentId}'";
                    
                    await _doltCli.ExecuteConflictResolutionAsync(deleteSql);
                    _logger.LogDebug("Field merge applied successfully for document {DocumentId}", documentId);
                    return true;
                }

                _logger.LogWarning("Field merge update affected 0 rows for document {DocumentId}", documentId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply field merge for document {DocumentId}", documentId);
                return false;
            }
        }

        /// <summary>
        /// Apply custom user-provided values to resolve a conflict
        /// </summary>
        public async Task<bool> ApplyCustomResolutionAsync(
            string tableName,
            string documentId,
            Dictionary<string, object> customValues)
        {
            _logger.LogDebug("Applying custom resolution for document {DocumentId} in table {Table}", 
                documentId, tableName);

            try
            {
                if (!customValues.Any())
                {
                    _logger.LogWarning("No custom values provided for document {DocumentId}", documentId);
                    return false;
                }

                // Update the conflict table with custom values
                var sets = customValues.Select(kvp => 
                {
                    var jsonValue = JsonSerializer.Serialize(kvp.Value);
                    // Escape single quotes in JSON for SQL safety
                    var escapedValue = jsonValue.Replace("'", "''");
                    return $"our_{kvp.Key} = '{escapedValue}'";
                });
                
                var updateSql = $@"
                    UPDATE dolt_conflicts_{tableName}
                    SET {string.Join(", ", sets)}
                    WHERE our_doc_id = '{documentId}'";

                _logger.LogDebug("Executing custom resolution SQL: {Sql}", updateSql);
                var result = await _doltCli.ExecuteConflictResolutionAsync(updateSql);
                
                if (result > 0)
                {
                    // Remove conflict marker after successful resolution
                    var deleteSql = $@"
                        DELETE FROM dolt_conflicts_{tableName}
                        WHERE our_doc_id = '{documentId}'";
                    
                    await _doltCli.ExecuteConflictResolutionAsync(deleteSql);
                    _logger.LogDebug("Custom resolution applied successfully for document {DocumentId}", documentId);
                    return true;
                }

                _logger.LogWarning("Custom resolution update affected 0 rows for document {DocumentId}", documentId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply custom resolution for document {DocumentId}", documentId);
                return false;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Resolve conflict by keeping our version (target branch)
        /// </summary>
        private async Task<bool> ResolveKeepOurs(DetailedConflictInfo conflict)
        {
            _logger.LogDebug("Resolving conflict {ConflictId} by keeping ours", conflict.ConflictId);
            
            try
            {
                var result = await _doltCli.ResolveConflictsAsync(conflict.Collection, ConflictResolution.Ours);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve conflict {ConflictId} with ours strategy", conflict.ConflictId);
                return false;
            }
        }

        /// <summary>
        /// Resolve conflict by keeping their version (source branch)
        /// </summary>
        private async Task<bool> ResolveKeepTheirs(DetailedConflictInfo conflict)
        {
            _logger.LogDebug("Resolving conflict {ConflictId} by keeping theirs", conflict.ConflictId);
            
            try
            {
                var result = await _doltCli.ResolveConflictsAsync(conflict.Collection, ConflictResolution.Theirs);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve conflict {ConflictId} with theirs strategy", conflict.ConflictId);
                return false;
            }
        }

        /// <summary>
        /// Automatically resolve a single conflict based on its characteristics
        /// </summary>
        private async Task<bool> AutoResolveConflictAsync(DetailedConflictInfo conflict)
        {
            _logger.LogDebug("Auto-resolving conflict {ConflictId} of type {Type}", 
                conflict.ConflictId, conflict.Type);

            try
            {
                // Handle different conflict types with appropriate auto-resolution strategies
                switch (conflict.Type)
                {
                    case ConflictType.ContentModification:
                        return await AutoResolveContentModification(conflict);
                    
                    case ConflictType.AddAdd:
                        return await AutoResolveAddAdd(conflict);
                    
                    case ConflictType.MetadataConflict:
                        return await AutoResolveMetadataConflict(conflict);
                    
                    default:
                        _logger.LogWarning("Cannot auto-resolve conflict type {Type}", conflict.Type);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-resolve conflict {ConflictId}", conflict.ConflictId);
                return false;
            }
        }

        /// <summary>
        /// Auto-resolve content modification conflicts with non-overlapping changes
        /// </summary>
        private async Task<bool> AutoResolveContentModification(DetailedConflictInfo conflict)
        {
            // Implement field-level merge for non-overlapping changes
            var baseToOurs = GetModifiedFields(conflict.BaseValues, conflict.OurValues);
            var baseToTheirs = GetModifiedFields(conflict.BaseValues, conflict.TheirValues);
            
            var overlap = baseToOurs.Intersect(baseToTheirs);
            if (overlap.Any())
            {
                _logger.LogDebug("Cannot auto-resolve - overlapping changes in fields: {Fields}", 
                    string.Join(", ", overlap));
                return false;
            }
            
            // No overlapping changes - merge both
            var fieldResolutions = new Dictionary<string, string>();
            
            foreach (var field in baseToOurs)
            {
                fieldResolutions[field] = "ours";
            }
            
            foreach (var field in baseToTheirs)
            {
                fieldResolutions[field] = "theirs";
            }
            
            _logger.LogDebug("Auto-resolving with field merge: ours={OursFields}, theirs={TheirsFields}", 
                string.Join(",", baseToOurs), string.Join(",", baseToTheirs));
            
            return await ApplyFieldMergeAsync(
                conflict.Collection,
                conflict.DocumentId,
                fieldResolutions);
        }

        /// <summary>
        /// Auto-resolve add-add conflicts with identical content
        /// </summary>
        private async Task<bool> AutoResolveAddAdd(DetailedConflictInfo conflict)
        {
            // For identical content, we can safely keep either version (keep ours)
            var ourContent = conflict.OurValues.GetValueOrDefault("content")?.ToString();
            var theirContent = conflict.TheirValues.GetValueOrDefault("content")?.ToString();
            
            if (string.Equals(ourContent, theirContent, StringComparison.Ordinal))
            {
                _logger.LogDebug("Auto-resolving identical add-add conflict by keeping ours");
                return await ResolveKeepOurs(conflict);
            }
            
            _logger.LogDebug("Cannot auto-resolve add-add conflict - content differs");
            return false;
        }

        /// <summary>
        /// Auto-resolve metadata conflicts by preferring newer timestamps
        /// </summary>
        private async Task<bool> AutoResolveMetadataConflict(DetailedConflictInfo conflict)
        {
            // For metadata conflicts, prefer the version with the newer timestamp
            var ourTimestamp = ExtractTimestamp(conflict.OurValues);
            var theirTimestamp = ExtractTimestamp(conflict.TheirValues);
            
            if (ourTimestamp.HasValue && theirTimestamp.HasValue)
            {
                if (ourTimestamp > theirTimestamp)
                {
                    _logger.LogDebug("Auto-resolving metadata conflict by keeping ours (newer timestamp)");
                    return await ResolveKeepOurs(conflict);
                }
                else
                {
                    _logger.LogDebug("Auto-resolving metadata conflict by keeping theirs (newer timestamp)");
                    return await ResolveKeepTheirs(conflict);
                }
            }
            
            // Fallback to keeping ours if timestamps can't be compared
            _logger.LogDebug("Auto-resolving metadata conflict by keeping ours (timestamp comparison failed)");
            return await ResolveKeepOurs(conflict);
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
        /// Extract timestamp from document values for metadata conflict resolution
        /// </summary>
        private DateTime? ExtractTimestamp(Dictionary<string, object> values)
        {
            // Try common timestamp field names
            var timestampFields = new[] { "timestamp", "updated_at", "modified_at", "last_modified" };
            
            foreach (var field in timestampFields)
            {
                if (values.TryGetValue(field, out var value))
                {
                    if (DateTime.TryParse(value?.ToString(), out var timestamp))
                    {
                        return timestamp;
                    }
                }
            }
            
            return null;
        }

        #endregion
    }
}