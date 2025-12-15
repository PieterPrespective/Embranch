# Dolt Interface Implementation Plan - Changelog

**Document**: Dolt_Interface_Implementation_Plan.md  
**Changes Made**: December 13, 2025  
**Purpose**: Summary of changes for updating existing implementation

---

## Executive Summary of Changes

Two major updates were made:

1. **Bidirectional Sync Support** - Added Chroma → Dolt synchronization (previously only Dolt → Chroma)
2. **Generalized Schema** - Replaced rigid domain-specific tables with flexible JSON-based storage

---

## Section-by-Section Changes

### Section 1: Architecture Overview

**Status**: 🔄 MODIFIED

**Changes**:
- Updated architecture diagram to show bidirectional sync arrows
- Added new components: `ChromaToDoltSyncer`, `ChromaToDoltDetector`
- Added explanation of working copy model (ChromaDB = working copy, Dolt = version control)

**Key Concept Added**:
```
ChromaDB = Working directory (where you edit files)
Dolt = Git repository (where versions are stored)
Sync = `git add` (Chroma→Dolt) and `git checkout` (Dolt→Chroma)
```

**Impact on Implementation**:
- No code changes required, conceptual update only

---

### Section 4.1: Core Tables

**Status**: 🔴 MAJOR REWRITE

**Before (Rigid Schema)**:
```sql
CREATE TABLE issue_logs (
    log_id VARCHAR(36) PRIMARY KEY,
    project_id VARCHAR(36) NOT NULL,
    issue_number INT NOT NULL,
    title VARCHAR(500),
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    log_type ENUM('investigation', 'implementation', 'resolution', 'postmortem'),
    ...
);

CREATE TABLE knowledge_docs (
    doc_id VARCHAR(36) PRIMARY KEY,
    category VARCHAR(100) NOT NULL,
    tool_name VARCHAR(255) NOT NULL,
    tool_version VARCHAR(50),
    title VARCHAR(500) NOT NULL,
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    ...
);
```

**After (Generalized Schema)**:
```sql
CREATE TABLE collections (
    collection_name VARCHAR(255) PRIMARY KEY,
    display_name VARCHAR(255),
    description TEXT,
    embedding_model VARCHAR(100),
    chunk_size INT DEFAULT 512,
    chunk_overlap INT DEFAULT 50,
    created_at DATETIME,
    updated_at DATETIME,
    document_count INT DEFAULT 0,
    metadata JSON
);

CREATE TABLE documents (
    doc_id VARCHAR(64) NOT NULL,
    collection_name VARCHAR(255) NOT NULL,
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    title VARCHAR(500),                    -- Extracted, nullable
    doc_type VARCHAR(100),                 -- Extracted, nullable
    metadata JSON NOT NULL,                -- ALL user fields preserved
    created_at DATETIME,
    updated_at DATETIME,
    PRIMARY KEY (doc_id, collection_name),
    FOREIGN KEY (collection_name) REFERENCES collections(collection_name)
);
```

**Impact on Implementation**:
- ❌ Remove `issue_logs` table
- ❌ Remove `knowledge_docs` table  
- ❌ Remove `projects` table
- ✅ Add `collections` table
- ✅ Add `documents` table (generalized)
- 🔄 Update `document_sync_log` - remove `source_table` column, change key structure

**Migration Required**: Yes - see Appendix D.7 for migration SQL

---

### Section 4.2: SQL Queries for Delta Detection

**Status**: 🔄 MODIFIED

**Changes**:
- Removed UNION queries across `issue_logs` and `knowledge_docs`
- Simplified to single table queries on `documents`
- Updated `DocumentDelta` record type
- Updated `DeletedDocument` record type

**Before**:
```csharp
public record DocumentDelta(
    string SourceTable,    // "issue_logs" or "knowledge_docs"
    string SourceId,
    string Content,
    string ContentHash,
    string Identifier,
    string Metadata,
    string ChangeType
);
```

**After**:
```csharp
public record DocumentDelta(
    string DocId,
    string CollectionName,
    string Content,
    string ContentHash,
    string Title,
    string DocType,
    string Metadata,       // JSON string - preserved exactly
    string ChangeType
);
```

**Impact on Implementation**:
- Update `DeltaDetector.GetPendingSyncDocumentsAsync()` SQL
- Update `DeltaDetector.GetDeletedDocumentsAsync()` SQL
- Update record types

---

### Section 4.3: Schema Mapping

**Status**: 🔄 MODIFIED

**Changes**:
- Updated diagram to show generalized `documents` table
- Updated field mapping table for bidirectional sync
- Added examples showing different metadata structures (recipes, tickets, research)

**Key Change - Field Mapping**:

| Dolt Field | ChromaDB Field | Notes |
|------------|----------------|-------|
| `doc_id` | `metadatas[].source_id` | Was `log_id`/`doc_id` |
| `collection_name` | `metadatas[].collection_name` | **NEW** |
| `metadata` (JSON) | `metadatas[].*` | **ALL user fields preserved** |

**Impact on Implementation**:
- Update metadata field names in sync code

---

### Section 4.3 (continued): DocumentConverter

**Status**: 🔴 MAJOR REWRITE

**Changes to `DoltDocument` Record**:

**Before**:
```csharp
public record DoltDocument(
    string SourceTable,
    string SourceId,
    string Content,
    string ContentHash,
    string ProjectId = null,
    int IssueNumber = 0,
    string LogType = null,
    string Title = null,
    string Category = null,
    string ToolName = null
);
```

**After**:
```csharp
public record DoltDocument(
    string DocId,
    string CollectionName,
    string Content,
    string ContentHash,
    string Title = null,
    string DocType = null,
    Dictionary<string, object> Metadata = null  // ALL other fields
);
```

**Changes to `ConvertDoltToChroma()`**:
- Now merges user metadata with system fields
- Preserves ALL metadata from JSON column

**Changes to `ConvertChromaToDolt()`**:
- Separates system fields from user metadata
- Preserves user metadata in `Metadata` dictionary

**New Helper Method**:
```csharp
private string ExtractAndRemove(Dictionary<string, object> dict, string key)
```

**Impact on Implementation**:
- Rewrite `DocumentConverter` class
- Update all code that creates/uses `DoltDocument`

---

### Section 5: Delta Detection & Sync Processing

**Status**: 🟢 MAJOR ADDITIONS

**New Subsections Added**:
- 5.1 Bidirectional Sync Model (conceptual)
- 5.2 Operation Processing Matrix (updated)
- 5.3 Use Cases for Chroma → Dolt Sync
- 5.4 Chroma → Dolt Delta Detection (`ChromaToDoltDetector` class)
- 5.5 Chroma → Dolt Sync Implementation (`ChromaToDoltSyncer` class)
- 5.6 Updated SyncManager with Bidirectional Support

---

### Section 5.2: Operation Processing Matrix

**Status**: 🔄 MODIFIED

**Added Operations**:
| Operation | Sync Direction |
|-----------|----------------|
| Add Document | User → Chroma |
| Edit Document | User → Chroma |
| Delete Document | User → Chroma |
| Stage Changes | Chroma → Dolt |
| Initialize DB | Chroma → Dolt |

**Impact on Implementation**:
- Update operation routing logic

---

### Section 5.4: NEW - ChromaToDoltDetector Class

**Status**: 🟢 NEW

**New Class**: `ChromaToDoltDetector`

**Methods**:
```csharp
Task<LocalChanges> DetectLocalChangesAsync(string collectionName)
Task<List<ChromaDocument>> GetFlaggedLocalChangesAsync(string collectionName)
Task<List<ChromaDocument>> CompareContentHashesAsync(string collectionName)
Task<List<ChromaDocument>> FindChromaOnlyDocumentsAsync(string collectionName)
Task<List<DeletedDocument>> FindDeletedDocumentsAsync(string collectionName)
```

**New Types**:
```csharp
public record LocalChanges {
    List<ChromaDocument> NewDocuments;
    List<ChromaDocument> ModifiedDocuments;
    List<DeletedDocument> DeletedDocuments;
    bool HasChanges;
    int TotalChanges;
}

public record ChromaDocument(
    string SourceTable,  // Now just "documents"
    string SourceId,
    string Content,
    string ContentHash,
    Dictionary<string, object> Metadata,
    IEnumerable<dynamic> Chunks
);
```

**Impact on Implementation**:
- Add new `ChromaToDoltDetector` class
- Add new types

---

### Section 5.5: NEW - ChromaToDoltSyncer Class

**Status**: 🟢 NEW

**New Class**: `ChromaToDoltSyncer`

**Methods**:
```csharp
Task<StageResult> StageLocalChangesAsync(string collectionName)
Task<InitResult> InitializeFromChromaAsync(string collectionName, string repositoryPath, string initialCommitMessage)
Task InsertDocumentToDoltAsync(ChromaDocument doc, string collectionName)
Task UpdateDocumentInDoltAsync(ChromaDocument doc)
Task DeleteDocumentFromDoltAsync(DeletedDocument doc)
Task ClearLocalChangeFlagAsync(string collectionName, string sourceId)
Task CreateSchemaTablesAsync()
```

**New Types**:
```csharp
public record StageResult {
    StageStatus Status;
    int Added;
    int Modified;
    int Deleted;
    string ErrorMessage;
}

public enum StageStatus { Completed, NoChanges, Failed }

public record InitResult {
    InitStatus Status;
    int DocumentsImported;
    string CommitHash;
    string ErrorMessage;
}

public enum InitStatus { Completed, Failed }
```

**Impact on Implementation**:
- Add new `ChromaToDoltSyncer` class
- Add new types

---

### Section 5.6: SyncManager Updates

**Status**: 🔄 MODIFIED

**New Dependencies**:
```csharp
private readonly ChromaToDoltSyncer _chromaToDoltSyncer;
private readonly ChromaToDoltDetector _chromaToDoltDetector;
```

**Modified Methods**:

1. **`ProcessCommitAsync()`** - Now auto-stages from ChromaDB:
```csharp
// Before
Task<SyncResult> ProcessCommitAsync(string message, bool syncAfter = true)

// After
Task<SyncResult> ProcessCommitAsync(string message, bool autoStageFromChroma = true, bool syncBackToChroma = false)
```

2. **`ProcessPullAsync()`** - Now checks for local changes:
```csharp
// Before
Task<SyncResult> ProcessPullAsync(string remote = "origin")

// After
Task<SyncResult> ProcessPullAsync(string remote = "origin", bool force = false)
```

3. **`ProcessCheckoutAsync()`** - Now checks for local changes:
```csharp
// Before
Task<SyncResult> ProcessCheckoutAsync(string targetBranch, bool createNew = false)

// After
Task<SyncResult> ProcessCheckoutAsync(string targetBranch, bool createNew = false, bool force = false)
```

4. **`ProcessMergeAsync()`** - Now checks for local changes:
```csharp
// Before
Task<MergeSyncResult> ProcessMergeAsync(string sourceBranch)

// After
Task<MergeSyncResult> ProcessMergeAsync(string sourceBranch, bool force = false)
```

**New Methods**:
```csharp
Task<LocalChanges> GetLocalChangesAsync()
Task<StatusSummary> GetStatusAsync()
Task<InitResult> InitializeVersionControlAsync(string collectionName, string initialCommitMessage)
```

**Impact on Implementation**:
- Update `SyncManager` constructor
- Add local change detection before pull/checkout/merge
- Add auto-staging in commit flow

---

### Section 5.6 (continued): Updated Types

**Status**: 🔄 MODIFIED

**`SyncResult` - New Properties**:
```csharp
public class SyncResult {
    // Existing properties...
    
    // NEW
    public int StagedFromChroma { get; set; }
    public LocalChanges LocalChanges { get; set; }
    public SyncDirection Direction { get; set; }
}
```

**`SyncStatus` - New Value**:
```csharp
public enum SyncStatus { 
    Completed, 
    NoChanges, 
    Failed, 
    Conflicts,
    LocalChangesExist  // NEW
}
```

**`MergeSyncStatus` - New Value**:
```csharp
public enum MergeSyncStatus { 
    Completed, 
    Failed, 
    ConflictsDetected,
    LocalChangesExist  // NEW
}
```

**New Enum**:
```csharp
public enum SyncDirection {
    DoltToChroma,
    ChromaToDolt,
    Bidirectional
}
```

**New Type**:
```csharp
public class StatusSummary {
    public string Branch { get; set; }
    public string CurrentCommit { get; set; }
    public string CollectionName { get; set; }
    public LocalChanges LocalChanges { get; set; }
    public bool HasUncommittedDoltChanges { get; set; }
    public bool HasUncommittedChromaChanges { get; set; }
    public bool IsClean => !HasUncommittedDoltChanges && !HasUncommittedChromaChanges;
}
```

---

### Section 6: MCP Tool Updates (DoltTools)

**Status**: 🔄 MODIFIED

**New Tool**:
```csharp
[McpTool("dolt_init", "Initialize version control for an existing ChromaDB collection")]
Task<ToolResult> InitAsync(string collection, string message = "Initial import from ChromaDB")
```

**Modified Tools**:

1. **`dolt_status`** - Now shows local changes:
```csharp
// Returns localChanges.hasChanges, localChanges.newDocuments, etc.
```

2. **`dolt_commit`** - New parameter:
```csharp
// Before
Task<ToolResult> CommitAsync(string message, bool sync = true)

// After  
Task<ToolResult> CommitAsync(string message, bool autoStage = true)
// Returns stagedFromChroma count
```

3. **`dolt_push`** - Now checks for uncommitted changes first

4. **`dolt_pull`** - New parameter:
```csharp
// Before
Task<ToolResult> PullAsync(string remote = "origin")

// After
Task<ToolResult> PullAsync(string remote = "origin", bool force = false)
// Returns local_changes_exist status if blocked
```

5. **`dolt_checkout`** - New parameter:
```csharp
// Before
Task<ToolResult> CheckoutAsync(string branch, bool create = false)

// After
Task<ToolResult> CheckoutAsync(string branch, bool create = false, bool force = false)
// Returns local_changes_exist status if blocked
```

**Impact on Implementation**:
- Add `dolt_init` tool
- Update `dolt_status` response
- Add `autoStage` parameter to `dolt_commit`
- Add `force` parameter to `dolt_pull`, `dolt_checkout`
- Add local change checking logic

---

### Section 7: Acceptance Tests

**Status**: 🟢 MAJOR ADDITIONS

**New Test Scenarios**:

1. **T0: Bidirectional Sync - Chroma to Dolt**
   - Initialize version control for existing ChromaDB
   - Detect and commit local changes
   - Pull blocked when local changes exist
   - Checkout blocked when local changes exist
   - Chunk reassembly verification
   - Batch commit after offline work

2. **T0.5: Workflow Integration Tests**
   - New user creates and shares knowledge base
   - Two developers collaborating
   - Feature branch workflow with local changes

**Impact on Implementation**:
- Add new test scenarios to test suite

---

### Appendices

**Status**: 🟢 NEW APPENDIX

**Added**: Appendix D: Schema Design Rationale

Contents:
- D.1 The Problem: ChromaDB is Schema-less
- D.2 Alternative Approaches Considered
- D.3 Why Generalized Schema Wins (comparison table)
- D.4 Hybrid Approach: Extracted Fields
- D.5 JSON Query Examples
- D.6 Performance Considerations
- D.7 Migration Path (SQL scripts)
- D.8 Conclusion

---

## Implementation Checklist

### Database Changes
- [ ] Create migration to drop `issue_logs`, `knowledge_docs`, `projects` tables
- [ ] Create `collections` table
- [ ] Create `documents` table with JSON metadata column
- [ ] Update `document_sync_log` structure
- [ ] Update `chroma_sync_state` foreign key

### New Classes to Add
- [ ] `ChromaToDoltDetector` class
- [ ] `ChromaToDoltSyncer` class

### Classes to Modify
- [ ] `DocumentConverter` - new `DoltDocument` structure, metadata handling
- [ ] `DeltaDetector` - simplified queries for single `documents` table
- [ ] `SyncManager` - add bidirectional sync, local change detection
- [ ] `DoltTools` - add `dolt_init`, update parameters

### New Types to Add
- [ ] `LocalChanges` record
- [ ] `ChromaDocument` record
- [ ] `StageResult` record
- [ ] `InitResult` record
- [ ] `StatusSummary` class
- [ ] `SyncDirection` enum
- [ ] `StageStatus` enum
- [ ] `InitStatus` enum

### Types to Modify
- [ ] `DoltDocument` - generalized with JSON metadata
- [ ] `DocumentDelta` - updated fields
- [ ] `DeletedDocument` - updated fields
- [ ] `SyncResult` - new properties
- [ ] `SyncStatus` - new `LocalChangesExist` value
- [ ] `MergeSyncStatus` - new `LocalChangesExist` value

### MCP Tools to Update
- [ ] Add `dolt_init`
- [ ] Update `dolt_status` response
- [ ] Update `dolt_commit` with `autoStage`
- [ ] Update `dolt_pull` with `force`
- [ ] Update `dolt_checkout` with `force`

### Tests to Add
- [ ] T0 scenarios (Chroma → Dolt sync)
- [ ] T0.5 scenarios (workflow integration)

---

## Quick Reference: Key API Changes

### Before → After

```csharp
// DoltDocument
DoltDocument(SourceTable, SourceId, Content, ContentHash, ProjectId, IssueNumber, LogType, Title, Category, ToolName)
→
DoltDocument(DocId, CollectionName, Content, ContentHash, Title, DocType, Metadata)

// ProcessCommitAsync
ProcessCommitAsync(message, syncAfter)
→
ProcessCommitAsync(message, autoStageFromChroma, syncBackToChroma)

// ProcessPullAsync
ProcessPullAsync(remote)
→
ProcessPullAsync(remote, force)

// ProcessCheckoutAsync
ProcessCheckoutAsync(targetBranch, createNew)
→
ProcessCheckoutAsync(targetBranch, createNew, force)

// ProcessMergeAsync
ProcessMergeAsync(sourceBranch)
→
ProcessMergeAsync(sourceBranch, force)
```

---

## Files Affected

| File | Change Type |
|------|-------------|
| `Services/DocumentConverter.cs` | Major rewrite |
| `Services/DeltaDetector.cs` | Modify |
| `Services/SyncManager.cs` | Major modify |
| `Services/ChromaToDoltDetector.cs` | **NEW** |
| `Services/ChromaToDoltSyncer.cs` | **NEW** |
| `McpTools/DoltTools.cs` | Modify |
| `Models/DoltDocument.cs` | Rewrite |
| `Models/DocumentDelta.cs` | Modify |
| `Models/SyncResult.cs` | Modify |
| `Models/LocalChanges.cs` | **NEW** |
| `Models/StatusSummary.cs` | **NEW** |
| Database schema | Migration required |
