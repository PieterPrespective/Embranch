# PP13-78 Design: Cross-Collection ID Collision Detection for Import Consolidation

## Date: 2026-01-16
## Type: Bug Fix - Import Toolset Collection Consolidation
## Priority: High
## Depends On: PP13-75 (Import Toolset), PP13-76 (Legacy DB Migration)

---

## Problem Statement

When importing documents from an external ChromaDB database and consolidating multiple source collections into a single target collection, the import process fails due to duplicate document IDs across the source collections.

### Current Behavior (Bug)
- `PreviewImportTool` reports `can_auto_import: true` with no conflicts
- `ExecuteImportTool` fails with ChromaDB error: "Expected IDs to be unique"
- User has no warning that the operation will fail

### Desired Behavior
- `PreviewImportTool` detects cross-collection ID collisions
- `PreviewImportTool` reports `can_auto_import: false` with collision conflicts listed
- User can choose resolution strategy: `namespace`, `keep_first`, `keep_last`, `skip`
- `ExecuteImportTool` applies the chosen resolution and imports successfully

---

## Root Cause Analysis

### Code Path Analysis

**ImportAnalyzer.AnalyzeImportAsync** (lines 36-161):
1. Validates external database (line 46)
2. Resolves collection mappings via `ResolveCollectionMappingsAsync` (line 61)
3. Groups mappings by target collection (lines 77-79):
   ```csharp
   var mappingsByTarget = collectionMappings
       .GroupBy(m => m.TargetCollection)
       .ToDictionary(g => g.Key, g => g.ToList());
   ```
4. For each target, analyzes each source collection **separately** (lines 101-115):
   ```csharp
   foreach (var mapping in mappings)
   {
       var (conflicts, added, updated, skipped) = await AnalyzeCollectionPairAsync(
           sourcePath, mapping.SourceCollection, targetCollection, ...);
       // Only checks source vs LOCAL target, never source vs OTHER sources
   }
   ```

**The Bug:** Step 4 only compares each source collection against the local target collection. It never compares source collections against each other when multiple sources map to the same target.

### Failure Scenario

Given filter:
```json
{"collections": [
  {"name": "SE-*", "import_into": "issueLogs"},
  {"name": "PP02-*", "import_into": "issueLogs"}
]}
```

And source collections containing:
- `PP02-186`: `planned_approach`, `actions_taken`
- `PP02-193`: `planned_approach`, `actions_taken`
- `SE-405`: `e2e_test_location_update`
- `SE-406`: `e2e_test_location_update`

**Current Flow:**
1. Preview analyzes PP02-186 vs local `issueLogs` → no conflicts
2. Preview analyzes PP02-193 vs local `issueLogs` → no conflicts
3. Preview analyzes SE-405 vs local `issueLogs` → no conflicts
4. etc.
5. **Result:** `can_auto_import: true` (WRONG!)

**Execute Flow:**
1. Collect all documents: `planned_approach` (x2), `actions_taken` (x2), `e2e_test_location_update` (x6)
2. Batch add to `issueLogs`
3. ChromaDB rejects: "Expected IDs to be unique" (duplicate `planned_approach`, etc.)

---

## Solution Architecture

### Overview

Add a **cross-collection collision detection phase** that runs before individual collection pair analysis. This phase:

1. Groups all source documents by target collection
2. Within each target group, detects document IDs that appear in multiple source collections
3. Reports these as `ImportConflictType.IdCollision` conflicts
4. Sets `can_auto_import: false` when collisions exist

### Conflict Detection Flow (Updated)

```
AnalyzeImportAsync
├── ValidateExternalDb
├── ResolveCollectionMappings
├── Group mappings by target
├── For each target group:
│   ├── [NEW] DetectCrossCollectionIdCollisions  ← ADDED
│   │   ├── Collect all docs from all source collections
│   │   ├── Group by document ID
│   │   ├── Report collisions where count > 1
│   │   └── Add to allConflicts
│   └── For each source mapping:
│       └── AnalyzeCollectionPairAsync (existing - checks source vs local)
└── Build result with all conflicts
```

### New Conflict Type: Cross-Collection ID Collision

**Conflict ID Format:** `xc_[12-char-hex]`
- Prefix `xc_` distinguishes from regular import conflicts (`imp_`)
- Hash includes both source collection names (sorted for determinism)

**Conflict Info Structure:**
```csharp
{
  ConflictId: "xc_abc123def456",
  SourceCollection: "PP02-186+PP02-193",  // Both source collections
  TargetCollection: "issueLogs",
  DocumentId: "planned_approach",
  Type: ImportConflictType.IdCollision,
  AutoResolvable: false,
  SuggestedResolution: "namespace",
  ResolutionOptions: ["namespace", "keep_first", "keep_last", "skip"],
  SourceContent: "Content from PP02-186...",   // First occurrence
  TargetContent: "Content from PP02-193...",   // Second occurrence
  SourceContentHash: "hash1...",
  TargetContentHash: "hash2..."
}
```

### Resolution Strategies

| Strategy | Behavior | Result Document ID |
|----------|----------|-------------------|
| `namespace` | Prefix each document with source collection name | `PP02-186__planned_approach`, `PP02-193__planned_approach` |
| `keep_first` | Keep document from alphabetically first source collection | `planned_approach` (from PP02-186) |
| `keep_last` | Keep document from alphabetically last source collection | `planned_approach` (from PP02-193) |
| `skip` | Skip all colliding documents | No document imported |

### Namespace Resolution Details

When `namespace` resolution is applied:
- Original ID: `planned_approach`
- New ID: `PP02-186__planned_approach`
- Metadata additions:
  - `original_doc_id`: `planned_approach`
  - `namespaced_from`: `PP02-186`

This allows users to query by original ID via metadata if needed.

---

## Implementation Details

### Phase 1: ImportAnalyzer Changes

**Location:** `multidolt-mcp/Services/ImportAnalyzer.cs`

**Change 1: Add collision detection call in AnalyzeImportAsync**

After line 79 (grouping by target), before the foreach loop:

```csharp
foreach (var (targetCollection, mappings) in mappingsByTarget)
{
    // NEW: Check for cross-collection ID collisions when multiple sources
    if (mappings.Count > 1)
    {
        var collisionConflicts = await DetectCrossCollectionIdCollisionsAsync(
            sourcePath, mappings, targetCollection, includeContentPreview);

        if (collisionConflicts.Count > 0)
        {
            _logger.LogWarning(
                "Detected {Count} cross-collection ID collisions for target {Target}",
                collisionConflicts.Count, targetCollection);
            allConflicts.AddRange(collisionConflicts);
        }
    }

    // ... rest of existing code ...
}
```

**Change 2: New method DetectCrossCollectionIdCollisionsAsync**

```csharp
/// <summary>
/// Detects document ID collisions across multiple source collections
/// being merged into the same target collection.
/// </summary>
/// <param name="sourcePath">Path to external database</param>
/// <param name="mappings">Collection mappings all targeting the same collection</param>
/// <param name="targetCollection">Target collection name</param>
/// <param name="includeContentPreview">Whether to include content in conflict info</param>
/// <returns>List of collision conflicts</returns>
private async Task<List<ImportConflictInfo>> DetectCrossCollectionIdCollisionsAsync(
    string sourcePath,
    List<CollectionMapping> mappings,
    string targetCollection,
    bool includeContentPreview)
{
    var conflicts = new List<ImportConflictInfo>();

    // Collect all documents from all source collections for this target
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
    foreach (var (docId, sources) in docIdToSources.Where(kvp => kvp.Value.Count > 1))
    {
        _logger.LogDebug(
            "Document ID '{DocId}' found in {Count} source collections: {Collections}",
            docId, sources.Count, string.Join(", ", sources.Select(s => s.sourceCollection)));

        // Sort sources by collection name for deterministic conflict generation
        var sortedSources = sources.OrderBy(s => s.sourceCollection).ToList();

        // Create conflict between first and each subsequent occurrence
        for (int i = 1; i < sortedSources.Count; i++)
        {
            var first = sortedSources[0];
            var other = sortedSources[i];

            var conflict = CreateCrossCollectionConflict(
                first, other, targetCollection, includeContentPreview);
            conflicts.Add(conflict);
        }
    }

    return conflicts;
}

/// <summary>
/// Creates an ImportConflictInfo for a cross-collection ID collision
/// </summary>
private ImportConflictInfo CreateCrossCollectionConflict(
    (string sourceCollection, ExternalDocument doc) first,
    (string sourceCollection, ExternalDocument doc) second,
    string targetCollection,
    bool includeContentPreview)
{
    var conflictId = ImportUtility.GenerateCrossCollectionConflictId(
        first.sourceCollection, second.sourceCollection,
        targetCollection, first.doc.DocId);

    _logger.LogDebug(
        "Generated cross-collection conflict ID {Id} for doc '{DocId}' ({Col1} vs {Col2})",
        conflictId, first.doc.DocId, first.sourceCollection, second.sourceCollection);

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

### Phase 2: ImportModels Changes

**Location:** `multidolt-mcp/Models/ImportModels.cs`

**Change 1: Add new resolution types (after line 311)**

```csharp
public enum ImportResolutionType
{
    // ... existing types ...

    /// <summary>
    /// Namespace document IDs with source collection prefix to avoid collision.
    /// Results in IDs like "SourceCollection__original_doc_id"
    /// </summary>
    Namespace,

    /// <summary>
    /// Keep only the first occurrence (from alphabetically first source collection)
    /// </summary>
    KeepFirst,

    /// <summary>
    /// Keep only the last occurrence (from alphabetically last source collection)
    /// </summary>
    KeepLast
}
```

**Change 2: Add cross-collection conflict ID generator (after line 558)**

```csharp
/// <summary>
/// Generates a deterministic conflict ID for cross-collection ID collisions.
/// Uses sorted collection names to ensure same ID regardless of detection order.
/// Format: xc_[12-char-hex]
/// </summary>
public static string GenerateCrossCollectionConflictId(
    string sourceCollection1,
    string sourceCollection2,
    string targetCollection,
    string documentId)
{
    // Sort collection names for determinism (same result regardless of order detected)
    var sorted = new[] { sourceCollection1, sourceCollection2 }.OrderBy(x => x).ToArray();
    var input = $"CROSS_{sorted[0]}_{sorted[1]}_{targetCollection}_{documentId}";

    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
    var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    return $"xc_{hashHex[..12]}";
}
```

**Change 3: Update GetResolutionOptions (around line 616)**

```csharp
ImportConflictType.IdCollision => new List<string>
    { "namespace", "keep_first", "keep_last", "skip" },
```

**Change 4: Update ParseResolutionType (around line 590)**

Add cases:
```csharp
"namespace" => ImportResolutionType.Namespace,
"keepfirst" => ImportResolutionType.KeepFirst,
"first" => ImportResolutionType.KeepFirst,
"keeplast" => ImportResolutionType.KeepLast,
"last" => ImportResolutionType.KeepLast,
```

**Change 5: Update GetSuggestedResolution (around line 649)**

```csharp
ImportConflictType.IdCollision => "namespace",
```

### Phase 3: ImportExecutor Changes

**Location:** `multidolt-mcp/Services/ImportExecutor.cs`

**Change 1: Track processed document IDs and handle new resolutions**

Add tracking dictionary at the start of ExecuteImportAsync (after line 74):

```csharp
// Track which document IDs have been processed per target (for KeepFirst/KeepLast)
var processedDocIdsByTarget = new Dictionary<string, Dictionary<string, string>>();
// processedDocIdsByTarget[targetCollection][docId] = sourceCollection that "owns" it
```

**Change 2: Handle cross-collection collisions in the document processing loop**

Before processing each external document, check if it's involved in a cross-collection collision:

```csharp
// Check for cross-collection ID collision (xc_ prefix conflicts)
var crossCollisionConflict = preview.Conflicts.FirstOrDefault(c =>
    c.ConflictId.StartsWith("xc_") &&
    c.DocumentId == extDoc.DocId &&
    c.TargetCollection == mapping.TargetCollection &&
    c.SourceCollection.Contains(mapping.SourceCollection));

if (crossCollisionConflict != null)
{
    var resolution = DetermineResolution(crossCollisionConflict, resolutionMap, autoResolveRemaining, defaultStrategy);

    // Track resolution
    var resolutionKey = resolution.ResolutionType.ToString().ToLowerInvariant();
    resolutionBreakdown[resolutionKey] = resolutionBreakdown.GetValueOrDefault(resolutionKey, 0) + 1;
    conflictsResolved++;

    switch (resolution.ResolutionType)
    {
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

        case ImportResolutionType.KeepFirst:
            // Only keep if this is the first source collection alphabetically
            var firstSourceCol = crossCollisionConflict.SourceCollection
                .Split('+')
                .OrderBy(x => x)
                .First();

            if (mapping.SourceCollection == firstSourceCol)
            {
                documentsByTarget[mapping.TargetCollection].Add(CreateImportDocumentData(
                    extDoc, sourcePath, mapping.SourceCollection, isUpdate: false));
            }
            else
            {
                documentsSkipped++;
            }
            break;

        case ImportResolutionType.KeepLast:
            // Only keep if this is the last source collection alphabetically
            var lastSourceCol = crossCollisionConflict.SourceCollection
                .Split('+')
                .OrderBy(x => x)
                .Last();

            if (mapping.SourceCollection == lastSourceCol)
            {
                documentsByTarget[mapping.TargetCollection].Add(CreateImportDocumentData(
                    extDoc, sourcePath, mapping.SourceCollection, isUpdate: false));
            }
            else
            {
                documentsSkipped++;
            }
            break;

        case ImportResolutionType.Skip:
            documentsSkipped++;
            break;

        default:
            // Fall through to regular processing
            break;
    }

    continue; // Skip normal processing
}
```

---

## Test Plan

### Unit Tests (CrossCollectionConflictTests.cs)

| Test | Description |
|------|-------------|
| `GenerateCrossCollectionConflictId_Deterministic` | Same inputs produce same ID |
| `GenerateCrossCollectionConflictId_OrderIndependent` | `(A,B)` and `(B,A)` produce same ID |
| `GenerateCrossCollectionConflictId_DifferentDocIds_DifferentIds` | Different doc IDs produce different conflict IDs |
| `ParseResolutionType_Namespace_Correct` | "namespace" parses to Namespace enum |
| `ParseResolutionType_KeepFirst_Correct` | "keep_first" and "first" parse to KeepFirst |
| `ParseResolutionType_KeepLast_Correct` | "keep_last" and "last" parse to KeepLast |
| `GetResolutionOptions_IdCollision_IncludesNamespace` | IdCollision options include namespace |
| `IsAutoResolvable_IdCollision_ReturnsFalse` | IdCollision is never auto-resolvable |

### Integration Tests (PP13_78_CollisionDetectionTests.cs)

| Test | Description |
|------|-------------|
| `PreviewImport_TwoSourcesSameDocId_DetectsCollision` | Basic collision detection |
| `PreviewImport_CollisionDetected_CanAutoImportFalse` | can_auto_import is false |
| `PreviewImport_ThreeSourcesSameDocId_DetectsMultipleCollisions` | Multiple collisions from 3 sources |
| `PreviewImport_WildcardFilter_DetectsCollisions` | Collisions detected with wildcard patterns |
| `ExecuteImport_NamespaceResolution_CreatesNamespacedIds` | IDs are prefixed correctly |
| `ExecuteImport_NamespaceResolution_PreservesOriginalIdInMetadata` | original_doc_id metadata present |
| `ExecuteImport_KeepFirstResolution_KeepsAlphabeticallyFirst` | First collection's doc kept |
| `ExecuteImport_KeepLastResolution_KeepsAlphabeticallyLast` | Last collection's doc kept |
| `ExecuteImport_SkipResolution_SkipsAllCollisions` | Colliding docs not imported |
| `ExecuteImport_MixedResolutions_HandlesEachCorrectly` | Different resolutions for different collisions |

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| Performance impact from collecting all docs | Lazy evaluation; only check when multiple sources |
| Breaking existing imports | Cross-collection check only runs when `mappings.Count > 1` |
| Conflict ID inconsistency | Sorted collection names ensure determinism |
| Memory usage with large imports | Documents already loaded for analysis; minimal additional overhead |

---

## Success Criteria

1. **PreviewImport Detection:** Returns `can_auto_import: false` when cross-collection ID collisions exist
2. **Conflict Reporting:** Collisions reported with type `IdCollision` and conflict ID format `xc_*`
3. **Resolution Options:** All four options work: `namespace`, `keep_first`, `keep_last`, `skip`
4. **Namespace Metadata:** Namespaced documents have `original_doc_id` and `namespaced_from` metadata
5. **Backward Compatibility:** Single-source imports and non-consolidation imports unchanged
6. **Test Coverage:** 18+ tests passing (8 unit + 10 integration)
7. **Build Status:** 0 errors

---

## Related Work

- **Builds On:** PP13-75 (Import Toolset), PP13-76 (Legacy DB Migration)
- **Uses:** `IExternalChromaDbReader`, `IChromaDbService`, `ImportUtility`
- **Fixes:** Production incident documented in `Examples/260116_1648/`
