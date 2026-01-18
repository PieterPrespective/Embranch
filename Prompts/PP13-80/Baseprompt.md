# PP13-80 Base Prompt

## Project Context

You are implementing **PP13-80: Robust JSON Column Parsing for Dolt Query Results** for the DMMS (Dolt-Managed Multimedia System) project.

## Background

DMMS uses Dolt (a version-controlled SQL database) alongside ChromaDB. The Dolt schema includes several `JSON` type columns for storing metadata. When querying these columns via `QueryAsync<dynamic>`, the results are parsed as `System.Text.Json.JsonElement` objects.

## The Problem

When DMMS starts with an existing Dolt repository that has collections with metadata, the application crashes:

```
System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Object'.
   at CollectionChangeDetector.GetDoltCollectionsAsync() line 349
```

**Root Cause**: The code calls `GetString()` on a `JsonElement` that contains a JSON object (not a string). Dolt returns `JSON` type columns as nested JSON structures, not as escaped strings.

## The Solution

1. Create a reusable `JsonUtility` class with safe extraction methods that handle all `JsonValueKind` types
2. Fix the immediate bug in `CollectionChangeDetector.GetDoltCollectionsAsync()`
3. Audit the codebase for similar issues
4. Add comprehensive tests

## Key Design Constraints

- **Backward Compatible**: Fix must not break existing functionality
- **Reusable**: Create utility methods for consistent pattern across codebase
- **Defensive**: Handle all `JsonValueKind` types (String, Object, Array, Null, etc.)
- **Testable**: Comprehensive unit tests for utility, integration tests for affected services

## Existing Correct Patterns

The codebase already has the correct pattern in several files. Use these as reference:

### DoltCli.cs (lines 1646-1653)
```csharp
object? value = prop.Value.ValueKind switch
{
    JsonValueKind.String => prop.Value.GetString(),
    JsonValueKind.Number => prop.Value.GetDecimal(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Null => null,
    _ => prop.Value.GetRawText()  // Handles Object/Array correctly
};
```

### ImportAnalyzer.cs (lines 705-714)
```csharp
return element.ValueKind switch
{
    JsonValueKind.String => element.GetString() ?? string.Empty,
    JsonValueKind.Number when element.TryGetInt32(out var i) => i,
    // ... other cases ...
    _ => element.GetRawText()
};
```

## JSON Columns in Schema

From `SyncDatabaseSchemaV2.sql`, these columns are `JSON` type and need careful handling:

| Table | Column | Current Risk |
|-------|--------|--------------|
| `collections` | `metadata` | **HIGH** - Causes current bug |
| `documents` | `metadata` | Medium |
| `document_sync_log` | `chroma_chunk_ids` | Low |
| `local_changes` | `metadata` | Medium |
| `sync_operations` | `chroma_collections_affected` | Low |
| `sync_operations` | `metadata` | Low |

## Implementation Tracking

Use the **Chroma MCP server** collection `PP13-80` to track development progress:
- Log planned approach at start
- Log phase completion after each phase
- Include test counts, build status, key decisions

## Namespace Convention

Use `DMMSTesting.UnitTests` namespace for unit tests and `DMMSTesting.IntegrationTests` namespace for integration tests to ensure `GlobalTestSetup` initializes correctly.

## Success Metrics

- Application starts successfully with existing Dolt data containing collection metadata
- Reusable `JsonUtility` class created
- 16+ tests pass (10 unit, 6 integration)
- Build succeeds with 0 errors
- All existing tests continue to pass

## Reference Assignment

See `Prompts/PP13-80/Assignment.md` for full design document including:
- Detailed root cause analysis
- Implementation phases
- File lists
- Code patterns
- Test specifications
- Audit checklist
