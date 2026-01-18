# PP13-80: Robust JSON Column Parsing for Dolt Query Results

## Date: 2026-01-18
## Type: Bug Fix - JSON Type Handling in Dolt Query Parsing
## Priority: Critical
## Depends On: None (standalone fix)

---

## Problem Statement

When DMMS starts with an existing Dolt repository containing collections with metadata, the application fails during initialization with:

```
System.InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Object'.
   at DMMS.Services.CollectionChangeDetector.GetDoltCollectionsAsync() in CollectionChangeDetector.cs:line 349
```

**Current Behavior:**
- Application crashes on startup when `collections` table contains rows with non-null `metadata` JSON values
- Error occurs in `CollectionChangeDetector.ValidateInitializationAsync()` during startup validation
- User cannot start DMMS after data has been committed to Dolt with collection metadata

**Desired Behavior:**
- Application starts successfully regardless of metadata content
- JSON columns (which can return as nested JSON objects) are handled correctly
- Consistent JSON extraction pattern used throughout the codebase

---

## Root Cause Analysis

### The Bug

In `CollectionChangeDetector.cs:349`:

```csharp
metadataJson = jsonElement.TryGetProperty("metadata", out var metadataProp)
    ? metadataProp.GetString() ?? "{}"
    : "{}";
```

The code assumes `metadata` will be a JSON string value, but Dolt returns JSON-type columns as **nested JSON objects** when the content is non-null.

### Why This Happens

1. **Schema**: The `collections` table defines `metadata` as `JSON` type:
   ```sql
   CREATE TABLE collections (
       collection_name VARCHAR(255) PRIMARY KEY,
       ...
       metadata JSON,
       ...
   );
   ```

2. **Dolt JSON Output**: When `QueryAsync<dynamic>` executes `SELECT ... metadata FROM collections`, Dolt returns JSON columns as embedded JSON structures, not escaped strings.

3. **JsonElement Type Mismatch**: The `metadataProp` JsonElement has `ValueKind = Object`, but `GetString()` requires `ValueKind = String`, causing `InvalidOperationException`.

### Why It Wasn't Caught Earlier

- Test databases likely had NULL or empty metadata
- Fresh repositories skip validation (early return at line 329)
- The code was added in PP13-61 and not tested with populated metadata

---

## Solution Architecture

### Overview

1. **Create a reusable utility method** for safe JSON element extraction
2. **Fix the immediate bug** in `CollectionChangeDetector.GetDoltCollectionsAsync()`
3. **Audit the codebase** for similar issues and apply the fix systematically
4. **Add comprehensive tests** to prevent regression

### Utility Pattern

The codebase already has the correct pattern in several places. We'll standardize on a utility method:

```csharp
/// <summary>
/// Safely extracts a string value from a JsonElement, handling both string values
/// and JSON object/array values (which are serialized via GetRawText()).
/// </summary>
public static string GetJsonElementAsString(JsonElement element, string defaultValue = "")
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? defaultValue,
        JsonValueKind.Null => defaultValue,
        JsonValueKind.Undefined => defaultValue,
        JsonValueKind.Object => element.GetRawText(),
        JsonValueKind.Array => element.GetRawText(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => defaultValue
    };
}
```

---

## Implementation Phases

### Phase 1: Create Utility Method

**File to Modify:** `multidolt-mcp/Utilities/JsonUtility.cs` (create if doesn't exist)

**New Methods:**

```csharp
namespace DMMS.Utilities
{
    /// <summary>
    /// Utility methods for JSON handling, particularly for Dolt query result parsing.
    /// </summary>
    public static class JsonUtility
    {
        /// <summary>
        /// Safely extracts a string representation from a JsonElement.
        /// Handles JSON columns from Dolt which may return as nested objects rather than strings.
        /// </summary>
        /// <param name="element">The JsonElement to extract a value from</param>
        /// <param name="defaultValue">Default value if element is null/undefined</param>
        /// <returns>String representation of the element's value</returns>
        public static string GetElementAsString(JsonElement element, string defaultValue = "")
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? defaultValue,
                JsonValueKind.Null => defaultValue,
                JsonValueKind.Undefined => defaultValue,
                JsonValueKind.Object => element.GetRawText(),
                JsonValueKind.Array => element.GetRawText(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => defaultValue
            };
        }

        /// <summary>
        /// Safely extracts a string from a JsonElement property.
        /// Returns defaultValue if the property doesn't exist or is null/undefined.
        /// </summary>
        public static string GetPropertyAsString(
            JsonElement parent,
            string propertyName,
            string defaultValue = "")
        {
            if (parent.TryGetProperty(propertyName, out var prop))
            {
                return GetElementAsString(prop, defaultValue);
            }
            return defaultValue;
        }

        /// <summary>
        /// Safely extracts a nullable string from a JsonElement property.
        /// Returns null if property doesn't exist, is null, or is undefined.
        /// </summary>
        public static string? GetPropertyAsNullableString(
            JsonElement parent,
            string propertyName)
        {
            if (parent.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined)
                    return null;
                return GetElementAsString(prop, "");
            }
            return null;
        }
    }
}
```

### Phase 2: Fix CollectionChangeDetector

**File to Modify:** `multidolt-mcp/Services/CollectionChangeDetector.cs`

**Change at line 346-355:**

Before:
```csharp
if (row is System.Text.Json.JsonElement jsonElement)
{
    name = jsonElement.TryGetProperty("collection_name", out var nameProp) ? nameProp.GetString() ?? "" : "";
    metadataJson = jsonElement.TryGetProperty("metadata", out var metadataProp) ? metadataProp.GetString() ?? "{}" : "{}";
}
```

After:
```csharp
if (row is System.Text.Json.JsonElement jsonElement)
{
    name = JsonUtility.GetPropertyAsString(jsonElement, "collection_name", "");
    metadataJson = JsonUtility.GetPropertyAsString(jsonElement, "metadata", "{}");
}
```

**Add using statement:**
```csharp
using DMMS.Utilities;
```

### Phase 3: Audit and Fix Similar Issues

**Files to Audit:**

The following files use `GetString()` on JsonElements from Dolt queries and should be reviewed:

1. **`multidolt-mcp/Services/ChromaToDoltDetector.cs`** (lines 571-572)
   - Uses `GetString()` for `doc_id` and `content_hash` columns
   - These are `VARCHAR` columns, so `String` type is expected - **likely safe**
   - Review and add defensive handling if needed

2. **`multidolt-mcp/Services/SyncManager.cs`**
   - Review any `QueryAsync<dynamic>` usage with JSON columns

3. **`multidolt-mcp/Services/SyncManagerV2.cs`**
   - Review any `QueryAsync<dynamic>` usage with JSON columns

4. **`multidolt-mcp/Services/DeltaDetector.cs`**
   - Review any `QueryAsync<dynamic>` usage with JSON columns

5. **`multidolt-mcp/Services/DeltaDetectorV2.cs`** (lines 385, 465)
   - Already has `ValueKind` checks - **already safe**

6. **`multidolt-mcp/Tools/DoltCloneTool.cs`** (lines 1215, 1332)
   - Already has `ValueKind` checks - **already safe**

**Systematic Review Process:**

For each file, search for patterns:
- `QueryAsync<dynamic>`
- `.GetString()` on JsonElement
- JSON column access without `ValueKind` check

Apply `JsonUtility` where appropriate, particularly for columns defined as `JSON` type in the schema:
- `collections.metadata` (JSON)
- `documents.metadata` (JSON)
- `document_sync_log.chroma_chunk_ids` (JSON)
- `local_changes.metadata` (JSON)
- `sync_operations.chroma_collections_affected` (JSON)
- `sync_operations.metadata` (JSON)

### Phase 4: Testing

**Files to Create:**
- `multidolt-mcp-testing/UnitTests/JsonUtilityTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_80_JsonColumnParsingTests.cs`

**Unit Tests (minimum 10):**

```csharp
[TestFixture]
public class JsonUtilityTests
{
    [Test]
    public void GetElementAsString_StringValue_ReturnsString()

    [Test]
    public void GetElementAsString_NullValue_ReturnsDefault()

    [Test]
    public void GetElementAsString_UndefinedValue_ReturnsDefault()

    [Test]
    public void GetElementAsString_ObjectValue_ReturnsRawText()

    [Test]
    public void GetElementAsString_ArrayValue_ReturnsRawText()

    [Test]
    public void GetElementAsString_NumberValue_ReturnsRawText()

    [Test]
    public void GetElementAsString_BooleanTrue_ReturnsTrue()

    [Test]
    public void GetElementAsString_BooleanFalse_ReturnsFalse()

    [Test]
    public void GetPropertyAsString_ExistingProperty_ReturnsValue()

    [Test]
    public void GetPropertyAsString_MissingProperty_ReturnsDefault()

    [Test]
    public void GetPropertyAsNullableString_NullProperty_ReturnsNull()

    [Test]
    public void GetPropertyAsNullableString_MissingProperty_ReturnsNull()
}
```

**Integration Tests (minimum 6):**

```csharp
[TestFixture]
public class PP13_80_JsonColumnParsingTests
{
    [Test]
    public async Task CollectionChangeDetector_WithMetadataObject_ParsesCorrectly()
    // Create collection with JSON metadata, verify GetDoltCollectionsAsync works

    [Test]
    public async Task CollectionChangeDetector_WithNullMetadata_ParsesCorrectly()
    // Create collection with null metadata, verify parsing works

    [Test]
    public async Task CollectionChangeDetector_WithEmptyMetadata_ParsesCorrectly()
    // Create collection with empty {} metadata, verify parsing works

    [Test]
    public async Task CollectionChangeDetector_WithNestedMetadata_ParsesCorrectly()
    // Create collection with nested JSON metadata, verify parsing works

    [Test]
    public async Task CollectionChangeDetector_Validation_SucceedsWithPopulatedMetadata()
    // Full validation flow with populated metadata

    [Test]
    public async Task ApplicationStartup_WithExistingCollections_Succeeds()
    // Simulate the startup validation scenario
}
```

---

## Technical Details

### JSON Columns in Dolt Schema

From `SyncDatabaseSchemaV2.sql`, these columns are JSON type:

| Table | Column | Risk |
|-------|--------|------|
| `collections` | `metadata` | **HIGH** - Directly causes current bug |
| `documents` | `metadata` | Medium - Uses typed queries elsewhere |
| `document_sync_log` | `chroma_chunk_ids` | Low - Array of strings |
| `local_changes` | `metadata` | Medium |
| `sync_operations` | `chroma_collections_affected` | Low |
| `sync_operations` | `metadata` | Low |

### Existing Correct Patterns

The following files already handle JSON ValueKind correctly and can serve as reference:

1. **`DoltCli.cs:1646-1653`** - Switch pattern for all ValueKind types
2. **`ImportAnalyzer.cs:705-714`** - Switch pattern with GetRawText fallback
3. **`ImportExecutor.cs:820-829`** - Switch pattern with GetRawText fallback
4. **`ConflictAnalyzer.cs:1331-1359`** - Comprehensive switch with all types

---

## Success Criteria

1. **Bug Fixed**: Application starts successfully with existing Dolt data containing collection metadata
2. **Utility Created**: Reusable `JsonUtility` class with safe extraction methods
3. **Systematic Fix**: All `QueryAsync<dynamic>` usages with JSON columns reviewed and fixed
4. **Test Coverage**: 16+ tests (10 unit + 6 integration)
5. **Build Status**: 0 errors
6. **No Regression**: Existing tests continue to pass

---

## Files to Create/Modify Summary

**New Files (2):**
- `multidolt-mcp/Utilities/JsonUtility.cs` - Reusable JSON extraction utilities
- `multidolt-mcp-testing/UnitTests/JsonUtilityTests.cs` - Unit tests for utility

**Modified Files (2-5):**
- `multidolt-mcp/Services/CollectionChangeDetector.cs` - Fix immediate bug
- `multidolt-mcp-testing/IntegrationTests/PP13_80_JsonColumnParsingTests.cs` - Integration tests
- (Additional files as identified in Phase 3 audit)

---

## Validation Process

1. **Build**: `dotnet build` - expect 0 errors
2. **Unit Tests**: `dotnet test --filter "FullyQualifiedName~JsonUtilityTests"`
3. **Integration Tests**: `dotnet test --filter "FullyQualifiedName~PP13_80"`
4. **Full Test Suite**: `dotnet test` - all existing tests pass
5. **Manual Validation**: Start DMMS with existing repository containing collection metadata

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Breaking existing code that relies on string-only values | Unit tests verify all ValueKind types are handled; integration tests verify end-to-end |
| Missing audit locations | Systematic grep for `GetString()` + `JsonElement` patterns |
| Performance impact of additional checks | ValueKind switch is O(1); negligible overhead |
| GetRawText() returns different format than expected | Downstream `ParseMetadata` methods already handle JSON strings |

---

## Implementation Order

1. **Phase 1**: Create `JsonUtility` class with safe extraction methods
2. **Phase 2**: Fix `CollectionChangeDetector.GetDoltCollectionsAsync()` using new utility
3. **Phase 3**: Audit codebase and apply fixes to other locations
4. **Phase 4**: Create unit and integration tests
5. **Validation**: Run full test suite and manual startup test

---

## Estimated Complexity

- **Phase 1**: Low - Single utility class with straightforward logic
- **Phase 2**: Trivial - Two-line change using new utility
- **Phase 3**: Medium - Requires careful audit of multiple files
- **Phase 4**: Medium - Comprehensive test coverage

**Total New/Modified Files**: ~4-7
**Total New Tests**: 16+

---

## Related Work

- **Introduced By**: PP13-61 (Collection change and removal tracking)
- **Uses**: `System.Text.Json` patterns already in codebase
- **Similar Patterns**: `ImportAnalyzer.cs`, `ImportExecutor.cs`, `DoltCli.cs`
