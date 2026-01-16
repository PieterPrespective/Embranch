# PP13-76-C2: ExternalChromaDbReaderTests Deadlock Fix

## Date: 2026-01-15
## Type: Bug Fix - Test Infrastructure
## Priority: High
## Depends On: PP13-75 (Import Toolset)
## Related Investigation: Examples/260115_ChromaMultipleRepoInvestigation.md

---

## Problem Statement

Integration tests in `ExternalChromaDbReaderTests.cs` consistently deadlock during execution. The root cause is **dual-client SQLite file locking** when two different ChromaDB PersistentClient instances access the same database path simultaneously.

**Current Behavior:**
- Tests that create external databases via helper methods deadlock
- Tests that don't require database creation pass normally
- SQLite file locking between test setup client and ExternalChromaDbReader client causes infinite hang

**Desired Behavior:**
- All ExternalChromaDbReaderTests pass without deadlock
- Single client access pattern for each database path
- Test infrastructure uses same code path as production service

---

## Root Cause Analysis

The deadlock occurs because:

1. **Test Setup** creates a ChromaDB client with ID `TestExternalDb_{Guid}` to populate test data
2. **ExternalChromaDbReader** creates a different client with ID `ExternalChromaDb_{pathHash}_{timestamp}` for the same database path
3. **Both clients** hold connections to the same `chroma.sqlite3` file
4. **SQLite WAL mode** combined with Python GIL causes deadlock

See `Examples/260115_ChromaMultipleRepoInvestigation.md` for detailed investigation.

---

## Solution Overview

Implement **unified database access** through ExternalChromaDbReader using internal-only write methods:

1. **Add internal write methods** to ExternalChromaDbReader for test setup
2. **Expose internals** to test assembly via `InternalsVisibleTo`
3. **Update tests** to use ExternalChromaDbReader's internal methods
4. **Single client** per database path eliminates file locking conflicts

### Key Design Principle: Single Point of Access
All access to an external database path must go through ExternalChromaDbReader, ensuring ChromaClientPool returns the same client instance for all operations.

---

## Architecture

### Current Architecture (Problematic)

```
Test Setup                          ExternalChromaDbReader
    |                                       |
    v                                       v
ChromaClientPool.GetOrCreateClient    ChromaClientPool.GetOrCreateClient
    |                                       |
    v                                       v
Client A (TestExternalDb_xxx)         Client B (ExternalChromaDb_xxx)
    |                                       |
    +-----------> Same SQLite <-------------+
                     |
                  DEADLOCK
```

### New Architecture (Unified)

```
Test Setup
    |
    v
ExternalChromaDbReader.AddDocumentsAsync() [internal]
    |
    v
GetOrCreateExternalClientId() → ChromaClientPool.GetOrCreateClient
    |
    v
Single Client (ExternalChromaDb_xxx)
    |
    v
SQLite (no conflict)
```

---

## Implementation Phases

### Phase 1: Add InternalsVisibleTo

**File to Modify:**
- `multidolt-mcp/DMMS.csproj`

**Changes:**
```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>DMMSTesting</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

### Phase 2: Add Internal Write Methods to ExternalChromaDbReader

**File to Modify:**
- `multidolt-mcp/Services/ExternalChromaDbReader.cs`

**New Internal Methods:**

```csharp
#region Internal Test Support Methods

/// <summary>
/// Creates or gets a collection in an external database.
/// Internal use for testing only - enables single-client access pattern.
/// </summary>
internal async Task CreateCollectionAsync(string dbPath, string collectionName)
{
    _logger.LogDebug("Creating collection '{Collection}' in external database {Path}", collectionName, dbPath);

    await PythonContext.ExecuteAsync(() =>
    {
        var clientId = GetOrCreateExternalClientId(dbPath);
        dynamic client = ChromaClientPool.GetOrCreateClient(clientId, $"persistent:{dbPath}");
        client.get_or_create_collection(name: collectionName);
        return true;
    }, timeoutMs: 30000, operationName: $"CreateCollection_{collectionName}");
}

/// <summary>
/// Adds documents to a collection in an external database.
/// Internal use for testing only - enables single-client access pattern.
/// </summary>
internal async Task AddDocumentsAsync(string dbPath, string collectionName,
    (string docId, string content)[] documents)
{
    _logger.LogDebug("Adding {Count} documents to collection '{Collection}' in external database {Path}",
        documents.Length, collectionName, dbPath);

    await PythonContext.ExecuteAsync(() =>
    {
        var clientId = GetOrCreateExternalClientId(dbPath);
        dynamic client = ChromaClientPool.GetOrCreateClient(clientId, $"persistent:{dbPath}");
        dynamic collection = client.get_or_create_collection(name: collectionName);

        foreach (var (docId, content) in documents)
        {
            collection.add(
                ids: new[] { docId },
                documents: new[] { content }
            );
        }
        return true;
    }, timeoutMs: 60000, operationName: $"AddDocuments_{collectionName}");
}

/// <summary>
/// Initializes an empty external database by creating a client connection.
/// Internal use for testing only - enables single-client access pattern.
/// </summary>
internal async Task InitializeDatabaseAsync(string dbPath)
{
    _logger.LogDebug("Initializing external database at {Path}", dbPath);

    await PythonContext.ExecuteAsync(() =>
    {
        var clientId = GetOrCreateExternalClientId(dbPath);
        dynamic client = ChromaClientPool.GetOrCreateClient(clientId, $"persistent:{dbPath}");
        return true;
    }, timeoutMs: 30000, operationName: "InitializeDatabase");
}

#endregion
```

### Phase 3: Update ExternalChromaDbReaderTests

**File to Modify:**
- `multidolt-mcp-testing/IntegrationTests/ExternalChromaDbReaderTests.cs`

**Changes:**

1. **Remove unused field** `_externalClientId` (if present from previous fix attempts)

2. **Update helper methods** to use ExternalChromaDbReader's internal methods:

```csharp
#region Helper Methods

/// <summary>
/// Creates an empty external ChromaDB database using the reader's internal method.
/// This ensures single-client access pattern, avoiding SQLite file locking deadlocks.
/// </summary>
private async Task CreateEmptyExternalDatabase()
{
    // Cast to concrete type to access internal methods
    var reader = (ExternalChromaDbReader)_externalReader;
    await reader.InitializeDatabaseAsync(_externalDbPath);
}

/// <summary>
/// Creates external database with specified documents using the reader's internal methods.
/// This ensures single-client access pattern, avoiding SQLite file locking deadlocks.
/// </summary>
private async Task CreateExternalDatabaseWithDocuments(
    (string collection, string docId, string content)[] documents)
{
    // Cast to concrete type to access internal methods
    var reader = (ExternalChromaDbReader)_externalReader;

    // Group by collection
    var byCollection = documents.GroupBy(d => d.collection);

    foreach (var group in byCollection)
    {
        var docsArray = group.Select(d => (d.docId, d.content)).ToArray();
        await reader.AddDocumentsAsync(_externalDbPath, group.Key, docsArray);
    }
}

#endregion
```

3. **Clean up imports** - remove any unused `using` statements from previous fix attempts

### Phase 4: Revert Previous Fix Attempts

**Files to Check/Revert:**
- `multidolt-mcp/Services/ExternalChromaDbReader.cs` - Revert `GetOrCreateExternalClientId()` if modified to remove timestamp (the timestamp removal was part of a failed fix attempt)

**Note:** The original `GetOrCreateExternalClientId()` with timestamp is fine because:
- The test now uses the same ExternalChromaDbReader instance
- `_externalClientIds` dictionary caches by path
- First call (from test setup) creates and caches the ID
- Subsequent calls (from test assertions) return the same cached ID

---

## Testing

### Verify All ExternalChromaDbReaderTests Pass

Run the full test suite for ExternalChromaDbReaderTests:

```bash
dotnet test --filter "FullyQualifiedName~ExternalChromaDbReaderTests" -v n
```

**Expected Results:**
- All 20 tests pass
- No timeouts or deadlocks
- Tests complete within reasonable time (< 2 minutes total)

### Test Categories to Verify

| Test Category | Count | Key Tests |
|---------------|-------|-----------|
| Database Validation | 4 | ValidateExternalDbAsync_* |
| Collection Listing | 2 | ListExternalCollectionsAsync_* |
| Wildcard Matching | 4 | ListMatchingCollectionsAsync_* |
| Document Retrieval | 5 | GetExternalDocumentsAsync_* |
| Collection Metadata | 1 | GetExternalCollectionMetadataAsync_* |
| Collection Count | 2 | GetExternalCollectionCountAsync_* |
| Collection Exists | 2 | CollectionExistsAsync_* |

### Regression Testing

Ensure ImportAnalyzerTests still pass (they use ExternalChromaDbReader):

```bash
dotnet test --filter "FullyQualifiedName~ImportAnalyzerTests" -v n
```

---

## Files Summary

| File | Action | Description |
|------|--------|-------------|
| `DMMS.csproj` | Modify | Add InternalsVisibleTo attribute |
| `ExternalChromaDbReader.cs` | Modify | Add internal write methods |
| `ExternalChromaDbReaderTests.cs` | Modify | Update helpers to use internal methods |

---

## Success Criteria

1. **All Tests Pass**: All 20 ExternalChromaDbReaderTests pass without timeout
2. **No Deadlocks**: Tests complete within expected time
3. **No Regression**: ImportAnalyzerTests continue to pass
4. **Build Success**: Solution builds with no errors
5. **Single Client Pattern**: Each database path uses only one ChromaClientPool client

---

## Technical Notes

### Why Internal Methods Work

The key insight is that `GetOrCreateExternalClientId()` caches client IDs in the `_externalClientIds` dictionary keyed by normalized path:

```csharp
return _externalClientIds.GetOrAdd(normalizedPath, _ => {
    // Generate new ID only if not cached
    return $"ExternalChromaDb_{pathHash}_{timestamp}";
});
```

When both test setup and test assertions use the **same ExternalChromaDbReader instance**:
1. First call (test setup via internal method) creates and caches the client ID
2. Subsequent calls (test assertions via public methods) return the cached ID
3. ChromaClientPool returns the same client for the same ID
4. Only one PersistentClient ever accesses each database path

### Why Previous Fixes Failed

Previous attempts tried to:
1. Dispose the test client before ExternalChromaDbReader accessed it → SQLite WAL mode still held locks
2. Use separate paths and copy files → Python runtime cached references
3. Match client IDs manually → ExternalChromaDbReader caches in its own dictionary

The unified access approach solves this by ensuring there's only ever **one code path** to client creation.

---

## Related Work

- **Investigation**: `Examples/260115_ChromaMultipleRepoInvestigation.md`
- **Builds On**: PP13-75 (Import Toolset)
- **Uses**: `PythonContext`, `ChromaClientPool`, `ExternalChromaDbReader`

---

## Estimated Complexity

- **Phase 1**: Trivial - Single line addition to csproj
- **Phase 2**: Low - Add 3 simple internal methods
- **Phase 3**: Low - Update 2 helper methods
- **Phase 4**: Trivial - Check/revert if needed

**Total Effort**: ~30 minutes implementation + testing
