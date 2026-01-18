# PP13-79 Base Context

## Project Overview

DMMS (Dolt Multi-Database MCP Server) is an MCP server that provides version-controlled document management by synchronizing ChromaDB vector collections with Dolt (a Git-like database). This enables:

- **Version Control:** Documents stored in ChromaDB are version-controlled via Dolt
- **Branch Operations:** Create branches, merge changes, resolve conflicts
- **Remote Sync:** Push/pull to DoltHub for collaboration

## Current Architecture

### Data Storage

```
project/
├── .mcp.json                    # MCP server configuration (other servers)
├── chroma_data/                 # ChromaDB persistent storage
│   └── [collection folders]
├── data/
│   ├── dolt-repo/               # Dolt version control repository
│   │   ├── .dolt/
│   │   └── [schema tables]
│   └── dev/
│       └── deletion_tracking.db # SQLite state tracking
└── [project files]
```

### State Tracking

DMMS uses SQLite (`deletion_tracking.db`) to track:
- **sync_state:** Per-collection sync status, last Dolt commit hash, branch context
- **local_deletions:** Document deletions awaiting sync
- **local_collection_deletions:** Collection-level changes

### Synchronization Flow

1. **ChromaDB → Dolt:** Document changes in ChromaDB are staged to Dolt tables
2. **Dolt Commit:** User commits changes with message
3. **Dolt → ChromaDB:** On branch checkout/pull, ChromaDB is updated to match Dolt

## The Problem

When a Git repository containing DMMS is cloned:
1. Git tracks source code but not DMMS state
2. `chroma_data/` and `data/` are typically gitignored
3. New user gets empty DMMS state despite code being at specific commit
4. Development history (issue logs, knowledge base) is lost

## Configuration

### Environment Variables

```bash
# Server
CHROMA_MODE=persistent
CHROMA_DATA_PATH=./chroma_data
DMMS_DATA_PATH=./data

# Dolt
DOLT_REPOSITORY_PATH=./data/dolt-repo
DOLT_REMOTE_URL=dolthub.com/org/repo
DOLT_EXECUTABLE_PATH=/path/to/dolt

# Logging
ENABLE_LOGGING=true
LOG_FILE_NAME=dmms.log
LOG_LEVEL=Debug
```

### Key Services

| Service | Purpose |
|---------|---------|
| `IChromaDbService` | ChromaDB operations |
| `IDoltCli` | Dolt command-line wrapper |
| `ISyncManagerV2` | ChromaDB ↔ Dolt synchronization |
| `IDeletionTracker` | Document deletion tracking |
| `ISyncStateTracker` | Per-collection sync state |

## Code Conventions

### Python/ChromaDB Operations

All ChromaDB operations must use `PythonContext.ExecuteAsync`:

```csharp
await PythonContext.ExecuteAsync(() =>
{
    // Python operations here
    return result;
}, timeoutMs: 30000, operationName: "OperationName");
```

### Test Patterns

Integration tests must properly initialize Python context and convert lists:

```csharp
private static PyObject ConvertToPyList(List<string> items)
{
    dynamic pyList = PythonEngine.Eval("[]");
    foreach (var item in items)
    {
        pyList.append(item);
    }
    return pyList;
}
```

### File Logging

DMMS uses structured logging with `ILogger<T>`:

```csharp
_logger.LogInformation("[ClassName.MethodName] Message: {Param}", param);
_logger.LogDebug("[ClassName] Debug info: {Detail}", detail);
_logger.LogWarning("[ClassName] Warning: {Issue}", issue);
_logger.LogError(ex, "[ClassName] Error occurred: {Message}", ex.Message);
```

## Reference Files

For implementation patterns, refer to:
- `multidolt-mcp/Services/SyncManagerV2.cs` - Synchronization logic
- `multidolt-mcp/Services/DoltCli.cs` - Dolt CLI wrapper
- `multidolt-mcp/Services/SqliteDeletionTracker.cs` - State tracking
- `multidolt-mcp/Program.cs` - Service registration and startup

## Testing Infrastructure

Tests use NUnit with categories:
- `[Category("Unit")]` - Fast, isolated tests
- `[Category("Integration")]` - Service integration tests
- `[Category("E2E")]` - End-to-end workflow tests

Test helper pattern for external databases:
```csharp
private async Task CreateExternalDatabaseWithDocuments(
    (string collection, string docId, string content)[] documents)
{
    await PythonContext.ExecuteAsync(() =>
    {
        // ... create ChromaDB client and add documents
        return true;
    }, timeoutMs: 60000, operationName: "CreateExternalDbWithDocs");
}
```
