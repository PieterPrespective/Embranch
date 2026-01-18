# PP13-78 Assignment: Cross-Collection ID Collision Detection for Import Consolidation

- IssueID = PP13-78
- Please read 'Prompts/BasePrompt.md' first for general context
- Please read 'Prompts/PP13-78/Design.md' for the complete design specification
- For implementation patterns, refer to:
  - `multidolt-mcp/Services/ImportAnalyzer.cs` - Current conflict detection implementation
  - `multidolt-mcp/Services/ImportExecutor.cs` - Current import execution implementation
  - `multidolt-mcp/Models/ImportModels.cs` - Conflict types and resolution models

## Problem Statement

When importing from an external ChromaDB database and consolidating multiple source collections into a single target collection using wildcard filters, the import fails due to duplicate document IDs across the source collections.

**Critical Bug:** `PreviewImportTool` does not detect these cross-collection ID collisions and incorrectly reports `can_auto_import: true` with no conflicts, leading to unexpected execution failures.

### Root Cause

The current `ImportAnalyzer.AnalyzeImportAsync` method:
1. Groups collection mappings by target collection
2. Analyzes each source collection individually against the local target
3. **Never checks for ID collisions between multiple source collections being merged into the same target**

When `ExecuteImport` runs, it batches all documents by target collection and calls `AddDocumentsAsync`. ChromaDB then fails with "Expected IDs to be unique" because the batch contains duplicate IDs from different source collections.

### Example Scenario (from production incident)

```json
// Filter consolidating 9 source collections into 2 targets
{
  "collections": [
    {"name": "SE-*", "import_into": "issueLogs"},      // 6 collections
    {"name": "PP02-*", "import_into": "issueLogs"},    // 3 collections
    {"name": "ProjectDevelopmentLog", "import_into": "ProjectDevelopmentLog"}
  ]
}
```

Multiple source collections (SE-405, SE-406, SE-407, etc.) contain documents with the same ID:
- `planned_approach` (exists in PP02-186 and PP02-193)
- `e2e_test_location_update` (exists in SE-405 through SE-410)

When chunked, these produce duplicate chunk IDs like `planned_approach_chunk_0`, causing ChromaDB to reject the batch.

## Assignment Objectives

### Primary Goals

1. **Fix ImportAnalyzer** - Detect cross-collection ID collisions when multiple sources map to one target
2. **Enhance ImportExecutor** - Add optional document ID namespacing resolution strategy
3. **Add New Resolution Type** - Support `namespace` resolution that prefixes document IDs with source collection name
4. **Create comprehensive test suite** - Unit and integration tests for collision detection and resolution

### Success Criteria

```
Build Status: 0 errors
Unit Tests: 8+ tests passing
Integration Tests: 10+ tests passing
Total Tests: 18+ tests passing
```

**Critical Validation Points:**
- `PreviewImport` correctly detects cross-collection ID collisions
- `PreviewImport` returns `can_auto_import: false` when collisions exist
- `ExecuteImport` with `namespace` resolution successfully imports with prefixed IDs
- Original document IDs preserved in metadata (`original_doc_id`)
- Collision detection works with wildcards and multiple filter entries

## Implementation Requirements

### Phase 1: ImportAnalyzer Enhancement

**File to Modify:** `multidolt-mcp/Services/ImportAnalyzer.cs`

**Key Changes:**

1. **Add Cross-Collection ID Collision Detection**

   After grouping mappings by target collection (line ~77-79), before analyzing individual collection pairs:

   ```csharp
   // NEW: Detect cross-collection ID collisions within each target group
   foreach (var (targetCollection, mappings) in mappingsByTarget)
   {
       if (mappings.Count > 1)
       {
           // Multiple sources -> same target: check for ID collisions
           var collisionConflicts = await DetectCrossCollectionIdCollisionsAsync(
               sourcePath, mappings, targetCollection, includeContentPreview);
           allConflicts.AddRange(collisionConflicts);
       }

       // ... existing analysis code ...
   }
   ```

2. **New Method: DetectCrossCollectionIdCollisionsAsync**

   ```csharp
   /// <summary>
   /// Detects document ID collisions across multiple source collections
   /// being merged into the same target collection
   /// </summary>
   private async Task<List<ImportConflictInfo>> DetectCrossCollectionIdCollisionsAsync(
       string sourcePath,
       List<CollectionMapping> mappings,
       string targetCollection,
       bool includeContentPreview)
   {
       var conflicts = new List<ImportConflictInfo>();

       // Collect all documents from all source collections
       // Key: docId -> List of (sourceCollection, document)
       var docIdToSources = new Dictionary<string, List<(string sourceCollection, ExternalDocument doc)>>();

       foreach (var mapping in mappings)
       {
           var docs = await _externalReader.GetExternalDocumentsAsync(
               sourcePath, mapping.SourceCollection, mapping.DocumentPatterns);

           foreach (var doc in docs)
           {
               if (!docIdToSources.ContainsKey(doc.DocId))
                   docIdToSources[doc.DocId] = new List<(string, ExternalDocument)>();

               docIdToSources[doc.DocId].Add((mapping.SourceCollection, doc));
           }
       }

       // Identify collisions (same docId in multiple source collections)
       foreach (var (docId, sources) in docIdToSources)
       {
           if (sources.Count > 1)
           {
               // Create collision conflict for each pair
               for (int i = 1; i < sources.Count; i++)
               {
                   var conflict = CreateCrossCollectionConflict(
                       sources[0], sources[i], targetCollection, includeContentPreview);
                   conflicts.Add(conflict);
               }
           }
       }

       return conflicts;
   }
   ```

3. **New Method: CreateCrossCollectionConflict**

   ```csharp
   private ImportConflictInfo CreateCrossCollectionConflict(
       (string sourceCollection, ExternalDocument doc) first,
       (string sourceCollection, ExternalDocument doc) second,
       string targetCollection,
       bool includeContentPreview)
   {
       // Use special conflict ID format for cross-collection collisions
       var conflictId = ImportUtility.GenerateCrossCollectionConflictId(
           first.sourceCollection, second.sourceCollection,
           targetCollection, first.doc.DocId);

       return new ImportConflictInfo
       {
           ConflictId = conflictId,
           SourceCollection = $"{first.sourceCollection}+{second.sourceCollection}",
           TargetCollection = targetCollection,
           DocumentId = first.doc.DocId,
           Type = ImportConflictType.IdCollision,
           AutoResolvable = false,
           SourceContent = includeContentPreview ? TruncateContent(first.doc.Content) : null,
           TargetContent = includeContentPreview ? TruncateContent(second.doc.Content) : null,
           SourceContentHash = first.doc.ContentHash,
           TargetContentHash = second.doc.ContentHash,
           SuggestedResolution = "namespace",
           ResolutionOptions = new List<string> { "namespace", "keep_first", "keep_last", "skip" },
           SourceMetadata = first.doc.Metadata,
           TargetMetadata = second.doc.Metadata
       };
   }
   ```

### Phase 2: ImportModels Enhancement

**File to Modify:** `multidolt-mcp/Models/ImportModels.cs`

**Key Changes:**

1. **Add New Resolution Type**

   ```csharp
   public enum ImportResolutionType
   {
       // ... existing types ...

       /// <summary>
       /// Namespace document IDs with source collection prefix to avoid collision
       /// </summary>
       Namespace,

       /// <summary>
       /// Keep the first occurrence (from first source collection alphabetically)
       /// </summary>
       KeepFirst,

       /// <summary>
       /// Keep the last occurrence (from last source collection alphabetically)
       /// </summary>
       KeepLast
   }
   ```

2. **Add Cross-Collection Conflict ID Generator**

   ```csharp
   /// <summary>
   /// Generates a deterministic conflict ID for cross-collection ID collisions.
   /// Format: xc_[12-char-hex]
   /// </summary>
   public static string GenerateCrossCollectionConflictId(
       string sourceCollection1,
       string sourceCollection2,
       string targetCollection,
       string documentId)
   {
       // Sort collection names for determinism
       var sorted = new[] { sourceCollection1, sourceCollection2 }.OrderBy(x => x).ToArray();
       var input = $"CROSS_{sorted[0]}_{sorted[1]}_{targetCollection}_{documentId}";
       using var sha256 = SHA256.Create();
       var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
       var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
       return $"xc_{hashHex[..12]}";
   }
   ```

3. **Update GetResolutionOptions for IdCollision**

   ```csharp
   ImportConflictType.IdCollision => new List<string>
       { "namespace", "keep_first", "keep_last", "skip" },
   ```

4. **Update ParseResolutionType**

   ```csharp
   "namespace" => ImportResolutionType.Namespace,
   "keepfirst" => ImportResolutionType.KeepFirst,
   "first" => ImportResolutionType.KeepFirst,
   "keeplast" => ImportResolutionType.KeepLast,
   "last" => ImportResolutionType.KeepLast,
   ```

### Phase 3: ImportExecutor Enhancement

**File to Modify:** `multidolt-mcp/Services/ImportExecutor.cs`

**Key Changes:**

1. **Handle Namespace Resolution**

   When processing documents, apply namespacing for IdCollision conflicts with Namespace resolution:

   ```csharp
   case ImportResolutionType.Namespace:
       // Prefix document ID with source collection name
       var namespacedId = $"{mapping.SourceCollection}__{extDoc.DocId}";
       var namespacedMetadata = BuildImportMetadata(extDoc, sourcePath, mapping.SourceCollection, null);
       namespacedMetadata["original_doc_id"] = extDoc.DocId;
       namespacedMetadata["namespaced_from"] = mapping.SourceCollection;

       documentsByTarget[mapping.TargetCollection].Add(new ImportDocumentData
       {
           DocId = namespacedId,
           Content = extDoc.Content,
           Metadata = namespacedMetadata,
           IsUpdate = false
       });
       break;
   ```

2. **Handle KeepFirst / KeepLast Resolution**

   Track which documents have been processed for a target:

   ```csharp
   // Track processed doc IDs per target to handle KeepFirst/KeepLast
   var processedDocIds = new Dictionary<string, HashSet<string>>();

   // For KeepFirst: skip if already processed
   // For KeepLast: allow overwrite
   ```

### Phase 4: Testing

**Files to Create:**
- `multidolt-mcp-testing/UnitTests/CrossCollectionConflictTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_78_CollisionDetectionTests.cs`

**Required Test Coverage:**

Unit Tests (minimum 8):
1. `GenerateCrossCollectionConflictId_Deterministic`
2. `GenerateCrossCollectionConflictId_OrderIndependent` (same ID regardless of collection order)
3. `ParseResolutionType_Namespace_Correct`
4. `ParseResolutionType_KeepFirst_Correct`
5. `ParseResolutionType_KeepLast_Correct`
6. `GetResolutionOptions_IdCollision_IncludesNamespace`
7. `IsAutoResolvable_IdCollision_ReturnsFalse`
8. `GetSuggestedResolution_IdCollision_ReturnsNamespace`

Integration Tests (minimum 10):
1. `PreviewImport_MultipleSourcesSameTarget_DetectsCollisions`
2. `PreviewImport_CollisionDetected_CanAutoImportFalse`
3. `PreviewImport_NoCollisions_CanAutoImportTrue`
4. `PreviewImport_CollisionWithWildcard_DetectsCorrectly`
5. `ExecuteImport_NamespaceResolution_PrefixesIds`
6. `ExecuteImport_NamespaceResolution_PreservesOriginalId`
7. `ExecuteImport_KeepFirstResolution_KeepsFirstOccurrence`
8. `ExecuteImport_KeepLastResolution_KeepsLastOccurrence`
9. `ExecuteImport_SkipResolution_SkipsCollisions`
10. `ExecuteImport_MixedResolutions_HandlesCorrectly`

## Technical Constraints

- **DO NOT** break existing import functionality for non-consolidation imports
- **MAINTAIN** backward compatibility with existing conflict resolution formats
- **PRESERVE** deterministic conflict IDs (critical for preview-execute consistency)
- **ENSURE** collision detection runs before individual collection pair analysis

## Validation Process

1. Build the solution: `dotnet build` - expect 0 errors
2. Run unit tests: `dotnet test --filter "FullyQualifiedName~CrossCollectionConflict"`
3. Run integration tests: `dotnet test --filter "FullyQualifiedName~PP13_78"`
4. Manual validation with test database containing duplicate document IDs across collections

## Expected Outcome

After implementation:
- `PreviewImport` correctly reports ID collisions when consolidating multiple collections
- `PreviewImport` returns `can_auto_import: false` when collisions exist
- Users can choose resolution strategies: `namespace`, `keep_first`, `keep_last`, `skip`
- `ExecuteImport` with `namespace` creates documents like `SE-405__planned_approach` with metadata preserving the original ID

**Priority**: High - Blocking production use of collection consolidation feature

## Files to Create/Modify Summary

**Modified Files (3):**
- `multidolt-mcp/Services/ImportAnalyzer.cs` - Add cross-collection collision detection
- `multidolt-mcp/Services/ImportExecutor.cs` - Add namespace/keep_first/keep_last resolution handling
- `multidolt-mcp/Models/ImportModels.cs` - Add new resolution types and conflict ID generator

**New Files (2):**
- `multidolt-mcp-testing/UnitTests/CrossCollectionConflictTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_78_CollisionDetectionTests.cs`

Please log all development actions to the 'chroma-feat-design-planning-mcp' database under collection 'PP13-78' following the established pattern.
