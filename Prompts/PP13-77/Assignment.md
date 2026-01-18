# PP13-77 Assignment: SQL JSON Escaping for Metadata with Special Characters

- IssueID = PP13-77
- Please read 'Prompts/BasePrompt.md' first for general context
- Please read 'Prompts/PP13-77/Design.md' for the complete design specification
- For implementation patterns, refer to:
  - `multidolt-mcp/Services/ChromaToDoltSyncer.cs` - Primary location of the bug
  - `multidolt-mcp/Services/SyncManagerV2.cs` - Secondary location with same bug pattern
  - `multidolt-mcp/Utilities/SqlEscapeUtility.cs` - Proposed utility location (to be created)

## Problem Statement

When committing documents to Dolt that contain Windows file paths in metadata (e.g., `C:\Users\...`), the commit fails with a SQL JSON parsing error. The backslash sequences like `\U` are interpreted as invalid Unicode escape sequences by the SQL JSON parser.

### Root Cause

The metadata JSON is serialized using `JsonSerializer.Serialize()` which produces valid JSON:
```json
{"import_source":"C:\\Users\\piete\\AppData\\Local\\Temp\\..."}
```

However, when embedded directly into a SQL string literal, the `\\` becomes `\` which the SQL JSON parser interprets as the start of an escape sequence. `\U` is not a valid JSON escape (only `\u` with 4 hex digits is valid), causing the error:
```
Invalid JSON text: invalid character 'U' in string escape code
```

### Affected Code Locations

| File | Lines | Issue |
|------|-------|-------|
| `ChromaToDoltSyncer.cs` | 255, 269 | INSERT INTO documents with unescaped metadataJson |
| `ChromaToDoltSyncer.cs` | 290, 303 | UPDATE documents with unescaped metadataJson |
| `SyncManagerV2.cs` | 2352, 2355 | UPDATE collections with unescaped metadataJson |

## Assignment Objectives

### Primary Goals

1. **Create SqlEscapeUtility** - Centralized utility for SQL string escaping
2. **Fix ChromaToDoltSyncer** - Apply proper escaping to INSERT and UPDATE statements
3. **Fix SyncManagerV2** - Apply proper escaping to UPDATE statements
4. **Create comprehensive test suite** - Unit tests for escaping, integration tests for round-trip

### Success Criteria

```
Build Status: 0 errors
Unit Tests: 8+ tests passing
Integration Tests: 4+ tests passing
Total Tests: 12+ tests passing
```

**Critical Validation Points:**
- Windows paths with backslashes are properly stored and retrieved
- Unicode characters are preserved through the round-trip
- Common escape sequences (\n, \t, \r) work correctly
- Single quotes in metadata values are handled
- Empty and null metadata work correctly

## Implementation Requirements

### Phase 1: SqlEscapeUtility Creation

**Files to Create:**
- `multidolt-mcp/Utilities/SqlEscapeUtility.cs` - Centralized escaping utility

**Key Implementation:**

```csharp
/// <summary>
/// Utility for safely escaping strings for SQL embedding.
/// Handles JSON escaping for Dolt/MySQL JSON columns.
/// </summary>
public static class SqlEscapeUtility
{
    /// <summary>
    /// Escapes a JSON string for embedding in a SQL string literal.
    /// This is necessary because JSON strings are first parsed by SQL,
    /// then by the JSON parser. Backslashes need to be double-escaped.
    /// </summary>
    /// <param name="json">The JSON string to escape</param>
    /// <returns>SQL-safe JSON string</returns>
    public static string EscapeJsonForSql(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        // Step 1: Escape single quotes for SQL string literals
        // Step 2: Escape backslashes so they survive SQL parsing to reach JSON parser
        //         JSON has: \\  (representing one backslash)
        //         SQL needs: \\\\ (so after SQL parsing, JSON sees \\)
        return json
            .Replace("\\", "\\\\")  // Double backslashes for SQL embedding
            .Replace("'", "''");     // Escape single quotes for SQL
    }

    /// <summary>
    /// Escapes a plain string for use in SQL string literals.
    /// Use this for non-JSON string values.
    /// </summary>
    public static string EscapeStringForSql(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("'", "''");
    }
}
```

### Phase 2: Fix ChromaToDoltSyncer

**File to Modify:**
- `multidolt-mcp/Services/ChromaToDoltSyncer.cs`

**Changes Required:**

1. **InsertDocumentToDoltAsync** (around line 255):
```csharp
// BEFORE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
// ... later ...
'{metadataJson}', NOW(), NOW())";

// AFTER:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);
// ... later ...
'{escapedMetadataJson}', NOW(), NOW())";
```

2. **UpdateDocumentInDoltAsync** (around line 290):
```csharp
// BEFORE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
// ... later ...
metadata = '{metadataJson}',

// AFTER:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);
// ... later ...
metadata = '{escapedMetadataJson}',
```

### Phase 3: Fix SyncManagerV2

**File to Modify:**
- `multidolt-mcp/Services/SyncManagerV2.cs`

**Changes Required:**

**ProcessCollectionMetadataUpdateAsync** (around line 2352):
```csharp
// BEFORE:
var metadataJson = JsonSerializer.Serialize(newMetadata);
await _dolt.QueryAsync<object>($"UPDATE collections SET metadata = '{metadataJson}' WHERE collection_name = '{update.CollectionName}'");

// AFTER:
var metadataJson = JsonSerializer.Serialize(newMetadata);
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);
await _dolt.QueryAsync<object>($"UPDATE collections SET metadata = '{escapedMetadataJson}' WHERE collection_name = '{update.CollectionName}'");
```

### Phase 4: Testing

**Files to Create:**
- `multidolt-mcp-testing/UnitTests/SqlEscapeUtilityTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_77_MetadataEscapingTests.cs`

**Required Unit Tests (8 minimum):**

| Test Name | Description |
|-----------|-------------|
| `EscapeJsonForSql_WindowsPath_EscapesBackslashes` | Verify `C:\Users` becomes `C:\\\\Users` |
| `EscapeJsonForSql_SingleQuote_Doubled` | Verify `'` becomes `''` |
| `EscapeJsonForSql_UnicodeSequence_Preserved` | Verify `\u0041` becomes `\\u0041` |
| `EscapeJsonForSql_NewlineTab_Escaped` | Verify `\n\t` becomes `\\n\\t` |
| `EscapeJsonForSql_EmptyString_ReturnsEmpty` | Verify empty input returns empty |
| `EscapeJsonForSql_NullString_ReturnsNull` | Verify null input returns null |
| `EscapeJsonForSql_ComplexMetadata_FullEscape` | Verify full metadata object escapes correctly |
| `EscapeStringForSql_SingleQuote_Doubled` | Verify plain string escaping works |

**Required Integration Tests (4 minimum):**

| Test Name | Description |
|-----------|-------------|
| `InsertDocument_WindowsPathInMetadata_Succeeds` | Full INSERT with Windows path metadata |
| `UpdateDocument_WindowsPathInMetadata_Succeeds` | Full UPDATE with Windows path metadata |
| `RoundTrip_MetadataWithSpecialChars_Preserved` | Insert, retrieve, verify metadata integrity |
| `ImportedDocument_WithLegacyPath_CommitsSuccessfully` | Simulate import scenario from issue |

## Technical Constraints

- **DO NOT** use parameterized queries (Dolt CLI does not support them well)
- **DO NOT** modify the SQL schema or column types
- **PRESERVE** existing behavior for normal metadata without special characters
- **ENSURE** backward compatibility - existing data must continue to work
- **USE** the centralized utility - do not duplicate escaping logic

## Validation Process

1. Build the solution: `dotnet build` - expect 0 errors
2. Run unit tests: `dotnet test --filter "Category=Unit&FullyQualifiedName~SqlEscape"`
3. Run integration tests: `dotnet test --filter "FullyQualifiedName~PP13_77"`
4. Manual validation: Import from Windows ChromaDB and commit

## Expected Outcome

Documents with metadata containing Windows file paths (or any backslash sequences) can be successfully committed to Dolt without JSON parsing errors. The fix is:
- Centralized in a utility class for easy maintenance
- Applied consistently across all affected code locations
- Thoroughly tested with unit and integration tests
- Backward compatible with existing data

**Priority**: High - Blocks import functionality for Windows users

## Files to Create/Modify Summary

**New Files (2):**
- `multidolt-mcp/Utilities/SqlEscapeUtility.cs`
- `multidolt-mcp-testing/UnitTests/SqlEscapeUtilityTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_77_MetadataEscapingTests.cs`

**Modified Files (2):**
- `multidolt-mcp/Services/ChromaToDoltSyncer.cs` - Apply escaping to INSERT and UPDATE
- `multidolt-mcp/Services/SyncManagerV2.cs` - Apply escaping to UPDATE

## Error Message Reference

The fix should prevent errors like:
```
Invalid JSON text: invalid character 'U' in string escape code
```

After the fix, metadata like this should work:
```json
{
  "import_source": "C:\\Users\\piete\\AppData\\Local\\Temp\\DMMS_LegacyMigration\\...",
  "import_timestamp": "2026-01-16T13:05:50.4134547Z"
}
```
