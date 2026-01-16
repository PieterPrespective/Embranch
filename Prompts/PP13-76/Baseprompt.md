# PP13-76 Base Prompt

## Project Context

You are implementing **PP13-76: Legacy ChromaDB Import Compatibility Layer** for the DMMS (Dolt-Managed Multimedia System) project.

## Background

DMMS uses ChromaDB for vector storage with Dolt for version control. The PP13-75 Import Toolset was recently implemented to enable importing content from external ChromaDB databases. However, a critical issue was discovered: **external databases created with older ChromaDB versions fail to import** due to missing `_type` configuration fields.

## The Problem

When ChromaDB evolved, collection configuration schema changed. Older databases lack the `_type` field that newer ChromaDB versions require. When the import tools try to access these databases, they fail with errors like:

```
could not find _type
KeyError: '_type'
```

## The Solution

Implement a **transparent compatibility layer** that:
1. Detects legacy database version issues
2. Copies the database to a temporary location (preserving original)
3. Migrates the temporary copy using existing `ChromaCompatibilityHelper`
4. Redirects import operations to use the migrated copy
5. Cleans up the temporary database after operations complete

## Key Design Constraints

- **NEVER** modify the original external database
- Operations must be **transparent** to end users
- Must handle **file locking** issues (ChromaDB holds file locks)
- Must be **idempotent** (safe to run multiple times)
- Must provide **informative feedback** when migration occurs

## Existing Resources

### ChromaCompatibilityHelper (Already Exists)
Location: `multidolt-mcp/Services/ChromaCompatibilityHelper.cs`

Key methods:
- `MigrateDatabaseAsync(logger, dataPath)` - Adds missing `_type` fields
- `ValidateClientConnectionAsync(logger, dataPath)` - Tests if DB is accessible
- `EnsureCompatibilityAsync(logger, dataPath)` - Full check + migrate flow

### OutOfDateDatabaseMigrationTests (Reference)
Location: `multidolt-mcp-testing/IntegrationTests/OutOfDateDatabaseMigrationTests.cs`

Shows patterns for:
- Extracting test databases from zip
- Running migrations
- Handling ChromaDB file locking in cleanup
- Validating migrated databases work

### PP13-75 Import Infrastructure
- `IExternalChromaDbReader` / `ExternalChromaDbReader` - Reads external DBs
- `IImportAnalyzer` / `ImportAnalyzer` - Analyzes imports, detects conflicts
- `IImportExecutor` / `ImportExecutor` - Executes imports with conflict resolution
- `PreviewImportTool` / `ExecuteImportTool` - MCP tool interfaces

## Implementation Tracking

Use the **Chroma MCP server** collection `PP13-76` to track development progress:
- Log planned approach at start
- Log phase completion after each phase
- Include test counts, build status, key decisions

## Namespace Convention

Use `DMMSTesting.IntegrationTests` namespace for integration tests to ensure `GlobalTestSetup` initializes `PythonContext` correctly. This was a lesson learned in PP13-75 Phase 3.

## Test Data

Existing test database available at:
- `TestData/out-of-date-chroma-database.zip`
- Contains collections: `learning_database`, `DSplineKnowledge`
- Known to trigger `_type` errors with current ChromaDB version

## Success Metrics

- Import from legacy databases works transparently
- Original database is never modified
- Temporary databases are cleaned up
- 21+ tests pass (8 unit, 8 integration, 5 E2E)
- Build succeeds with 0 errors

## Reference Assignment

See `Prompts/PP13-76/Assignment.md` for full design document including:
- Detailed architecture
- Implementation phases
- File lists
- Code patterns
- Test specifications
