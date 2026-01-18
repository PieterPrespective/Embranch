# PP13-77 SQL JSON Escaping Design Document

## Issue Overview

| Field | Value |
|-------|-------|
| Issue ID | PP13-77 |
| Type | Bug Fix |
| Status | Design Phase |
| Created | 2026-01-16 |
| Severity | High |
| Component | DoltCommit / SQL Document Insertion |
| Related Issues | PP13-75 (Import Toolset), PP13-76 (Legacy Import) |

## Executive Summary

This document describes the design for fixing a critical bug where Windows file paths in document metadata cause Dolt commit failures. The issue occurs because JSON strings containing backslashes are not properly escaped when embedded in SQL string literals, causing the SQL JSON parser to interpret sequences like `\U` as invalid Unicode escapes.

---

## 1. Problem Analysis

### 1.1 Symptom

When attempting to commit documents with metadata containing Windows file paths, users see:
```
Failed to insert planned-approach-001: SQL execution failed: error on line 2 for query INSERT INTO documents ... Invalid JSON text: invalid character 'U' in string escape code
```

### 1.2 Root Cause Deep Dive

#### The Escaping Chain

When metadata is stored in Dolt, it goes through multiple parsing stages:

```
Original Metadata (C# Dictionary)
         ↓ JsonSerializer.Serialize()
JSON String: {"import_source":"C:\\Users\\..."}
         ↓ String Interpolation into SQL
SQL: INSERT INTO documents (..., metadata, ...) VALUES (..., '{"import_source":"C:\\Users\\..."}', ...)
         ↓ SQL Parser (interprets string literal)
Value passed to JSON column: {"import_source":"C:\Users\..."}
         ↓ JSON Parser (parses the JSON value)
ERROR: \U is not a valid JSON escape sequence!
```

#### The Fix

We need to escape backslashes an additional time so they survive SQL parsing:

```
Original Metadata (C# Dictionary)
         ↓ JsonSerializer.Serialize()
JSON String: {"import_source":"C:\\Users\\..."}
         ↓ SqlEscapeUtility.EscapeJsonForSql()
Escaped JSON: {"import_source":"C:\\\\Users\\\\..."}
         ↓ String Interpolation into SQL
SQL: INSERT INTO documents (..., metadata, ...) VALUES (..., '{"import_source":"C:\\\\Users\\\\..."}', ...)
         ↓ SQL Parser (interprets string literal)
Value passed to JSON column: {"import_source":"C:\\Users\\..."}
         ↓ JSON Parser (parses the JSON value)
SUCCESS: \\ is properly parsed as a single backslash
```

### 1.3 Affected Code Locations

| File | Method | Line | Operation |
|------|--------|------|-----------|
| ChromaToDoltSyncer.cs | InsertDocumentToDoltAsync | 255, 269 | INSERT with metadata |
| ChromaToDoltSyncer.cs | UpdateDocumentInDoltAsync | 290, 303 | UPDATE with metadata |
| SyncManagerV2.cs | ProcessCollectionMetadataUpdateAsync | 2352, 2355 | UPDATE collections |

### 1.4 Characters Requiring Special Handling

| Character | JSON Representation | After SQL Escaping | Notes |
|-----------|--------------------|--------------------|-------|
| `\` (backslash) | `\\` | `\\\\` | Most common issue (file paths) |
| `'` (single quote) | N/A (not JSON) | `''` | SQL string delimiter |
| `"` (double quote) | `\"` | `\\"` | Inside JSON strings |
| `\n` (newline) | `\n` | `\\n` | Common in content |
| `\t` (tab) | `\t` | `\\t` | Common in content |
| `\r` (carriage return) | `\r` | `\\r` | Windows line endings |

---

## 2. Solution Design

### 2.1 Design Principles

1. **Centralized Escaping**: All SQL escaping logic in one utility class
2. **Defense in Depth**: Escape both backslashes and single quotes
3. **Clear Semantics**: Separate methods for JSON-in-SQL vs plain string escaping
4. **Testability**: Pure functions with no side effects
5. **Backward Compatibility**: Existing data and queries continue to work

### 2.2 Component Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Service Layer                                │
│                                                                  │
│  ┌─────────────────────┐     ┌─────────────────────────────┐    │
│  │  ChromaToDoltSyncer │     │      SyncManagerV2          │    │
│  │                     │     │                             │    │
│  │  InsertDocument...  │     │  ProcessCollection...       │    │
│  │  UpdateDocument...  │     │                             │    │
│  └──────────┬──────────┘     └────────────┬────────────────┘    │
│             │                             │                      │
│             └─────────────┬───────────────┘                      │
│                           │                                      │
│                           ▼                                      │
│             ┌─────────────────────────────┐                      │
│             │     SqlEscapeUtility        │                      │
│             │                             │                      │
│             │  EscapeJsonForSql()         │                      │
│             │  EscapeStringForSql()       │                      │
│             └─────────────────────────────┘                      │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.3 SqlEscapeUtility API Design

```csharp
namespace DMMS.Utilities
{
    /// <summary>
    /// Provides methods for safely escaping strings for SQL embedding.
    /// </summary>
    /// <remarks>
    /// This utility is essential when building SQL queries with string interpolation.
    /// While parameterized queries would be ideal, Dolt CLI has limited support for them.
    ///
    /// JSON Escaping Context:
    /// When JSON is embedded in a SQL string literal, backslashes need double-escaping.
    /// The SQL parser consumes one level of escaping, then the JSON parser needs the rest.
    ///
    /// Example:
    ///   Original: {"path":"C:\Users"}
    ///   After EscapeJsonForSql: {"path":"C:\\Users"}  (looks like C:\\\\Users in C# string)
    ///   In SQL: '{"path":"C:\\Users"}'
    ///   After SQL parsing: {"path":"C:\Users"}  -- This is what JSON parser sees
    ///   ERROR: \U is invalid!
    ///
    ///   With proper escaping:
    ///   After EscapeJsonForSql: {"path":"C:\\\\Users"}
    ///   In SQL: '{"path":"C:\\\\Users"}'
    ///   After SQL parsing: {"path":"C:\\Users"}  -- JSON parser sees valid escape
    ///   SUCCESS: \\ becomes single backslash
    /// </remarks>
    public static class SqlEscapeUtility
    {
        /// <summary>
        /// Escapes a JSON string for embedding in a SQL string literal.
        /// </summary>
        /// <param name="json">The JSON string from JsonSerializer.Serialize()</param>
        /// <returns>SQL-safe JSON string suitable for embedding in '...'</returns>
        /// <example>
        /// var metadata = new Dictionary<string, object> { ["path"] = @"C:\Users" };
        /// var json = JsonSerializer.Serialize(metadata);  // {"path":"C:\\Users"}
        /// var sqlSafe = SqlEscapeUtility.EscapeJsonForSql(json);  // {"path":"C:\\\\Users"}
        /// var sql = $"INSERT INTO tbl (meta) VALUES ('{sqlSafe}')";
        /// </example>
        public static string EscapeJsonForSql(string json);

        /// <summary>
        /// Escapes a plain string value for use in SQL string literals.
        /// Only escapes single quotes (not backslashes).
        /// Use this for non-JSON string values like content, titles, etc.
        /// </summary>
        /// <param name="value">The string value to escape</param>
        /// <returns>SQL-safe string with single quotes doubled</returns>
        public static string EscapeStringForSql(string value);
    }
}
```

---

## 3. Implementation Details

### 3.1 SqlEscapeUtility Implementation

```csharp
using System;

namespace DMMS.Utilities
{
    public static class SqlEscapeUtility
    {
        /// <summary>
        /// Escapes a JSON string for embedding in a SQL string literal.
        /// This is necessary because JSON strings are first parsed by SQL,
        /// then by the JSON parser. Backslashes need to be double-escaped.
        /// </summary>
        public static string EscapeJsonForSql(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            // Order matters: escape backslashes first, then single quotes
            //
            // JSON serialization produces: {"path":"C:\\Users"}  (one backslash as \\)
            // We need SQL to receive: {"path":"C:\\Users"}  (for JSON parser)
            // So we must send: {"path":"C:\\\\Users"}  (double backslash)
            // After SQL parsing, JSON sees: {"path":"C:\\Users"}  (correct!)

            return json
                .Replace("\\", "\\\\")   // Escape backslashes for SQL string embedding
                .Replace("'", "''");      // Escape single quotes for SQL string literals
        }

        /// <summary>
        /// Escapes a plain string value for use in SQL string literals.
        /// Only escapes single quotes (not backslashes).
        /// </summary>
        public static string EscapeStringForSql(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            // For non-JSON strings, we only need to escape single quotes
            // Backslashes in content are literal and should remain as-is
            return value.Replace("'", "''");
        }
    }
}
```

### 3.2 ChromaToDoltSyncer Changes

#### InsertDocumentToDoltAsync (around line 255)

```csharp
// CURRENT CODE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());

// Escape single quotes in content for SQL
var escapedContent = doltDoc.Content.Replace("'", "''");
var escapedTitle = doltDoc.Title?.Replace("'", "''");
var escapedDocType = doltDoc.DocType?.Replace("'", "''");

var sql = $@"
    INSERT INTO documents
        (doc_id, collection_name, content, content_hash, title, doc_type, metadata, created_at, updated_at)
    VALUES
        ('{doltDoc.DocId}', '{collectionName}', '{escapedContent}', '{doltDoc.ContentHash}',
         {(escapedTitle != null ? $"'{escapedTitle}'" : "NULL")},
         {(escapedDocType != null ? $"'{escapedDocType}'" : "NULL")},
         '{metadataJson}', NOW(), NOW())";

// FIXED CODE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);

// Use utility for consistent escaping
var escapedContent = SqlEscapeUtility.EscapeStringForSql(doltDoc.Content);
var escapedTitle = doltDoc.Title != null ? SqlEscapeUtility.EscapeStringForSql(doltDoc.Title) : null;
var escapedDocType = doltDoc.DocType != null ? SqlEscapeUtility.EscapeStringForSql(doltDoc.DocType) : null;

var sql = $@"
    INSERT INTO documents
        (doc_id, collection_name, content, content_hash, title, doc_type, metadata, created_at, updated_at)
    VALUES
        ('{doltDoc.DocId}', '{collectionName}', '{escapedContent}', '{doltDoc.ContentHash}',
         {(escapedTitle != null ? $"'{escapedTitle}'" : "NULL")},
         {(escapedDocType != null ? $"'{escapedDocType}'" : "NULL")},
         '{escapedMetadataJson}', NOW(), NOW())";
```

#### UpdateDocumentInDoltAsync (around line 290)

```csharp
// CURRENT CODE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
// ...
metadata = '{metadataJson}',

// FIXED CODE:
var metadataJson = JsonSerializer.Serialize(doltDoc.Metadata ?? new Dictionary<string, object>());
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);
// ...
metadata = '{escapedMetadataJson}',
```

### 3.3 SyncManagerV2 Changes

#### ProcessCollectionMetadataUpdateAsync (around line 2352)

```csharp
// CURRENT CODE:
var metadataJson = JsonSerializer.Serialize(newMetadata);
await _dolt.QueryAsync<object>($"UPDATE collections SET metadata = '{metadataJson}' WHERE collection_name = '{update.CollectionName}'");

// FIXED CODE:
var metadataJson = JsonSerializer.Serialize(newMetadata);
var escapedMetadataJson = SqlEscapeUtility.EscapeJsonForSql(metadataJson);
await _dolt.QueryAsync<object>($"UPDATE collections SET metadata = '{escapedMetadataJson}' WHERE collection_name = '{update.CollectionName}'");
```

---

## 4. Test Plan

### 4.1 Unit Tests for SqlEscapeUtility

```csharp
[TestFixture]
[Category("Unit")]
public class SqlEscapeUtilityTests
{
    [Test]
    public void EscapeJsonForSql_WindowsPath_EscapesBackslashes()
    {
        // Arrange
        var json = @"{""path"":""C:\\Users\\piete""}";

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert - backslashes should be doubled
        Assert.That(result, Is.EqualTo(@"{""path"":""C:\\\\Users\\\\piete""}"));
    }

    [Test]
    public void EscapeJsonForSql_SingleQuote_Doubled()
    {
        // Arrange
        var json = @"{""name"":""O'Brien""}";

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert - single quotes should be doubled
        Assert.That(result, Is.EqualTo(@"{""name"":""O''Brien""}"));
    }

    [Test]
    public void EscapeJsonForSql_BackslashAndQuote_BothEscaped()
    {
        // Arrange
        var json = @"{""path"":""C:\\Users\\O'Brien""}";

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert
        Assert.That(result, Is.EqualTo(@"{""path"":""C:\\\\Users\\\\O''Brien""}"));
    }

    [Test]
    public void EscapeJsonForSql_UnicodeEscape_PreservedButEscaped()
    {
        // Arrange - JSON already has \u0041 which is the letter A
        var json = @"{""char"":""\u0041""}";

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert - the backslash in \u0041 should be escaped
        Assert.That(result, Is.EqualTo(@"{""char"":""\\u0041""}"));
    }

    [Test]
    public void EscapeJsonForSql_NewlineAndTab_Escaped()
    {
        // Arrange - JSON with escaped newline and tab
        var json = @"{""text"":""line1\nline2\ttab""}";

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert
        Assert.That(result, Is.EqualTo(@"{""text"":""line1\\nline2\\ttab""}"));
    }

    [Test]
    public void EscapeJsonForSql_EmptyString_ReturnsEmpty()
    {
        Assert.That(SqlEscapeUtility.EscapeJsonForSql(""), Is.EqualTo(""));
    }

    [Test]
    public void EscapeJsonForSql_NullString_ReturnsNull()
    {
        Assert.That(SqlEscapeUtility.EscapeJsonForSql(null), Is.Null);
    }

    [Test]
    public void EscapeJsonForSql_ComplexMetadata_FullEscape()
    {
        // Arrange - realistic import metadata
        var metadata = new Dictionary<string, object>
        {
            ["import_source"] = @"C:\Users\piete\AppData\Local\Temp\DMMS",
            ["import_timestamp"] = "2026-01-16T13:05:50.4134547Z",
            ["is_local_change"] = true
        };
        var json = JsonSerializer.Serialize(metadata);

        // Act
        var result = SqlEscapeUtility.EscapeJsonForSql(json);

        // Assert - should contain escaped backslashes
        Assert.That(result, Does.Contain(@"C:\\\\Users\\\\piete"));
        Assert.That(result, Does.Contain(@"\\\\Temp\\\\DMMS"));
    }

    [Test]
    public void EscapeStringForSql_SingleQuote_Doubled()
    {
        Assert.That(SqlEscapeUtility.EscapeStringForSql("It's working"), Is.EqualTo("It''s working"));
    }

    [Test]
    public void EscapeStringForSql_BackslashNotEscaped()
    {
        // For non-JSON content, backslashes should remain as-is
        Assert.That(SqlEscapeUtility.EscapeStringForSql(@"C:\Users"), Is.EqualTo(@"C:\Users"));
    }
}
```

### 4.2 Integration Tests

```csharp
[TestFixture]
[Category("Integration")]
public class PP13_77_MetadataEscapingTests
{
    // Test infrastructure setup...

    [Test]
    public async Task InsertDocument_WindowsPathInMetadata_Succeeds()
    {
        // Arrange - create document with Windows path in metadata
        var doc = new ChromaDocument
        {
            DocId = "test-doc-001",
            CollectionName = "test-collection",
            Content = "Test content",
            Metadata = new Dictionary<string, object>
            {
                ["import_source"] = @"C:\Users\piete\AppData\Local\Temp\DMMS_Migration",
                ["import_timestamp"] = DateTime.UtcNow.ToString("O")
            }
        };

        // Act - should not throw
        await _syncer.InsertDocumentToDoltAsync(doc, "test-collection");

        // Assert - document exists in Dolt
        var result = await _dolt.QueryAsync<object>("SELECT metadata FROM documents WHERE doc_id = 'test-doc-001'");
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task UpdateDocument_WindowsPathInMetadata_Succeeds()
    {
        // Arrange - insert document first
        // ... then update with new metadata containing Windows path

        // Act & Assert - should not throw
    }

    [Test]
    public async Task RoundTrip_MetadataWithSpecialChars_Preserved()
    {
        // Arrange
        var originalMetadata = new Dictionary<string, object>
        {
            ["path"] = @"C:\Users\O'Brien\Documents",
            ["note"] = "Line1\nLine2\tTabbed"
        };

        // Act - insert, then retrieve
        // ...

        // Assert - metadata values match original
        Assert.That(retrievedMetadata["path"], Is.EqualTo(@"C:\Users\O'Brien\Documents"));
    }

    [Test]
    public async Task ImportedDocument_WithLegacyPath_CommitsSuccessfully()
    {
        // This test simulates the exact scenario from the bug report
        // Import document from external ChromaDB with Windows path metadata
        // Then commit to Dolt

        // Arrange - simulate import metadata
        var importMetadata = new Dictionary<string, object>
        {
            ["import_source"] = @"C:\Users\piete\AppData\Local\Temp\DMMS_LegacyMigration\feat-design-planning_27e44c08b6534f57ad9f059c75ec4261",
            ["import_source_collection"] = "SE-409",
            ["import_timestamp"] = "2026-01-16T13:05:50.4134547Z"
        };

        // Act - create document and stage to Dolt
        // ...

        // Assert - commit succeeds (no JSON parsing errors)
    }
}
```

---

## 5. Risk Analysis

| Risk | Mitigation |
|------|------------|
| Double-escaping breaks existing data | Unit tests verify escaping is idempotent when needed |
| Performance impact from string operations | Minimal - only happens during sync operations |
| Missing edge cases | Comprehensive test suite covers common scenarios |
| Breaking changes to sync behavior | Integration tests verify end-to-end flow |

---

## 6. Alternative Solutions Considered

### 6.1 Parameterized Queries (Rejected)

**Approach:** Use `@param` syntax with values passed separately.

**Why Rejected:**
- Dolt CLI has limited support for parameterized queries
- Would require significant refactoring of DoltCli service
- Current string interpolation pattern is used consistently throughout codebase

### 6.2 Path Normalization (Rejected)

**Approach:** Convert all Windows paths to forward slashes before storing.

**Why Rejected:**
- Loses original path information
- May break functionality that depends on exact path matching
- Doesn't solve the general escaping problem for other special characters

### 6.3 Base64 Encoding Metadata (Rejected)

**Approach:** Base64-encode the entire metadata JSON before storing.

**Why Rejected:**
- Makes metadata unreadable in SQL queries
- Complicates debugging and manual data inspection
- Overhead of encoding/decoding on every operation

### 6.4 Using JSON() SQL Function (Considered)

**Approach:** Wrap metadata with `JSON()` function: `JSON('{"path":"..."}')`.

**Why Not Primary:**
- Still requires proper escaping for the string literal inside JSON()
- Adds complexity without solving the core escaping problem
- May have compatibility issues across Dolt versions

---

## 7. Implementation Phases

### Phase 1: Create SqlEscapeUtility
1. Create `multidolt-mcp/Utilities/SqlEscapeUtility.cs`
2. Implement `EscapeJsonForSql()` and `EscapeStringForSql()`
3. Add comprehensive XML documentation

### Phase 2: Create Unit Tests
1. Create `multidolt-mcp-testing/UnitTests/SqlEscapeUtilityTests.cs`
2. Implement all 10+ unit tests
3. Verify all tests pass

### Phase 3: Fix ChromaToDoltSyncer
1. Add `using DMMS.Utilities;` import
2. Update `InsertDocumentToDoltAsync()` to use `EscapeJsonForSql()`
3. Update `UpdateDocumentInDoltAsync()` to use `EscapeJsonForSql()`
4. Optionally refactor existing `Replace("'", "''")` to use `EscapeStringForSql()`

### Phase 4: Fix SyncManagerV2
1. Add `using DMMS.Utilities;` import
2. Update `ProcessCollectionMetadataUpdateAsync()` to use `EscapeJsonForSql()`

### Phase 5: Integration Testing
1. Create `multidolt-mcp-testing/IntegrationTests/PP13_77_MetadataEscapingTests.cs`
2. Implement integration tests
3. Run full test suite
4. Manual verification with import scenario

---

## 8. Success Criteria

1. **Build succeeds** with 0 errors
2. **All unit tests pass** (10+ tests)
3. **All integration tests pass** (4+ tests)
4. **Manual verification:**
   - Import from Windows ChromaDB with paths in metadata
   - Execute DoltCommit
   - Verify commit succeeds without JSON parsing errors
   - Verify metadata is correctly stored and retrievable
5. **No regression** in existing functionality

---

## Appendix A: File Structure

```
multidolt-mcp/
├── Utilities/
│   └── SqlEscapeUtility.cs          # NEW - Escaping utility
├── Services/
│   ├── ChromaToDoltSyncer.cs        # MODIFIED - Use escaping utility
│   └── SyncManagerV2.cs             # MODIFIED - Use escaping utility

multidolt-mcp-testing/
├── UnitTests/
│   └── SqlEscapeUtilityTests.cs     # NEW - Unit tests for utility
└── IntegrationTests/
    └── PP13_77_MetadataEscapingTests.cs  # NEW - Integration tests
```

---

## Appendix B: Example SQL Before and After

### Before Fix (Fails)

```sql
INSERT INTO documents
  (doc_id, collection_name, content, content_hash, title, doc_type, metadata, created_at, updated_at)
VALUES
  ('planned-approach-001', 'SE-409', 'content...', 'hash...',
   NULL, NULL,
   '{"import_source":"C:\\Users\\piete\\AppData\\Local\\Temp\\DMMS_LegacyMigration\\feat-design-planning_27e44c08b6534f57ad9f059c75ec4261","import_timestamp":"2026-01-16T13:05:50.4134547Z"}',
   NOW(), NOW())
```

**Error:** `Invalid JSON text: invalid character 'U' in string escape code`

### After Fix (Succeeds)

```sql
INSERT INTO documents
  (doc_id, collection_name, content, content_hash, title, doc_type, metadata, created_at, updated_at)
VALUES
  ('planned-approach-001', 'SE-409', 'content...', 'hash...',
   NULL, NULL,
   '{"import_source":"C:\\\\Users\\\\piete\\\\AppData\\\\Local\\\\Temp\\\\DMMS_LegacyMigration\\\\feat-design-planning_27e44c08b6534f57ad9f059c75ec4261","import_timestamp":"2026-01-16T13:05:50.4134547Z"}',
   NOW(), NOW())
```

**Result:** Success - JSON parser receives valid escaped backslashes.

---

*Document Version: 1.0*
*Last Updated: 2026-01-16*
