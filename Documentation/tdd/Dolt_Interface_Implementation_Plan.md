# Dolt Interface Implementation Plan for VM RAG MCP Server

## Executive Summary

This document provides a step-by-step implementation plan for integrating Dolt CLI commands into your C# MCP server. The approach uses **Dolt CLI exclusively** (via subprocess execution) for all operations—both version control and data queries. This ensures a single, consistent interface with no port conflicts or server management overhead.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Package Recommendations](#2-package-recommendations)
3. [Dolt Functions to Interface](#3-dolt-functions-to-interface)
4. [Database Schema Design](#4-database-schema-design)
   - 4.1 Core Tables
   - 4.2 SQL Queries for Delta Detection
   - 4.3 Schema Mapping: Dolt ↔ ChromaDB
   - 4.4 Ensuring Consistency Across Clones
   - 4.5 The document_sync_log Table
5. [Delta Detection & Sync Processing](#5-delta-detection--sync-processing)
6. [Implementation Steps](#6-implementation-steps)
7. [Acceptance Tests (Gherkin BDD)](#7-acceptance-tests-gherkin-bdd)
8. [Additional Test Scenarios](#8-additional-test-scenarios)

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         C# MCP Server (.NET 9.0)                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌───────────────────────────────────────────┐       ┌─────────────────┐   │
│  │           DoltCliWrapper                  │       │  ChromaManager  │   │
│  │  (All operations via dolt.exe process)    │       │  (Python.NET)   │   │
│  │                                           │       │                 │   │
│  │  • Version Control: commit, push, pull    │       │                 │   │
│  │  • Branching: checkout, merge, branch     │       │                 │   │
│  │  • Data: dolt sql -q "SELECT/INSERT..."   │       │                 │   │
│  │  • Diff: dolt sql -q "DOLT_DIFF(...)"     │       │                 │   │
│  └──────────────────┬────────────────────────┘       └────────┬────────┘   │
│                     │                                         │            │
│                     ▼                                         │            │
│  ┌────────────────────────────────────────────────────────────┴──────────┐ │
│  │                        SyncManager Service                            │ │
│  │  - Delta detection (content_hash comparison via CLI queries)          │ │
│  │  - Operation routing (which DB to update)                             │ │
│  │  - Transaction coordination                                           │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
              │                                               │
              ▼                                               ▼
      ┌──────────────┐                                ┌──────────────┐
      │   Dolt CLI   │                                │   ChromaDB   │
      │  (dolt.exe)  │                                │  (Python)    │
      │              │                                │              │
      │  Repository  │                                │  Collections │
      │  (filesystem)│                                │  (persisted) │
      └──────────────┘                                └──────────────┘
```

### CLI-Only Approach Benefits

1. **Single interface** - All Dolt operations go through `dolt.exe`, no SQL server to manage
2. **No port conflicts** - No risk of conflicting with MySQL, MariaDB, or other Dolt instances
3. **Simpler deployment** - Just need `dolt.exe` in PATH
4. **Atomic operations** - Each CLI call is self-contained
5. **Environment agnostic** - Works identically in CI/CD, containers, and local dev

### How Data Operations Work via CLI

```bash
# Queries via CLI (returns results to stdout)
dolt sql -q "SELECT * FROM issue_logs WHERE project_id = 'proj-001'" -r json

# Inserts/Updates via CLI
dolt sql -q "INSERT INTO issue_logs (log_id, content, ...) VALUES (...)"

# Dolt-specific functions via CLI
dolt sql -q "SELECT active_branch()"
dolt sql -q "SELECT DOLT_HASHOF('HEAD')"
dolt sql -q "SELECT * FROM DOLT_DIFF('abc123', 'HEAD', 'issue_logs')" -r json
```

---

## 2. Package Recommendations

### C# Packages

```xml
<!-- Add to your .csproj file -->
<ItemGroup>
    <!-- For clean process management (Dolt CLI) -->
    <PackageReference Include="CliWrap" Version="3.6.6" />
    
    <!-- JSON handling for parsing dolt sql -r json output -->
    <PackageReference Include="System.Text.Json" Version="8.0.4" />
    
    <!-- SHA-256 hashing (built into .NET) -->
    <!-- System.Security.Cryptography - included in SDK -->
</ItemGroup>
```

### Why CliWrap?

CliWrap provides a clean, async-friendly API for process execution:

```csharp
// Clean fluent API
var result = await Cli.Wrap("dolt")
    .WithArguments(new[] { "sql", "-q", query, "-r", "json" })
    .WithWorkingDirectory(_repositoryPath)
    .ExecuteBufferedAsync();

// vs raw Process.Start (more verbose, harder to handle async)
```

### Alternative: Python via Python.NET

If you prefer consistency with your ChromaDB approach, `doltpy` also uses CLI under the hood:

```python
# doltpy is essentially a CLI wrapper too
from doltpy.cli import Dolt

repo = Dolt("./my_db")
repo.sql("SELECT * FROM documents", result_format="json")
repo.commit("message")
repo.push("origin", "main")
```

**Recommendation**: Use C# with `CliWrap` for type safety and better integration with your existing codebase.

### Dolt Installation

```bash
# Windows (PowerShell as Admin)
choco install dolt

# Or manual download from https://github.com/dolthub/dolt/releases
# Add to PATH: C:\Program Files\dolt\bin

# Verify installation
dolt version
```

---

## 3. Dolt Functions to Interface

### 3.1 Complete CLI Command Reference

| Category | Operation | CLI Command | Output Format |
|----------|-----------|-------------|---------------|
| **Repository** | Initialize | `dolt init` | text |
| | Clone | `dolt clone <remote>` | text |
| | Status | `dolt status` | text |
| **Branching** | List branches | `dolt branch` | text |
| | Create branch | `dolt branch <name>` | text |
| | Delete branch | `dolt branch -d <name>` | text |
| | Checkout | `dolt checkout <branch>` | text |
| | Checkout new | `dolt checkout -b <branch>` | text |
| | Current branch | `dolt sql -q "SELECT active_branch()"` | json |
| **Commits** | Stage all | `dolt add -A` | text |
| | Commit | `dolt commit -m "<msg>"` | text |
| | HEAD hash | `dolt sql -q "SELECT DOLT_HASHOF('HEAD')"` | json |
| | Log | `dolt log --oneline -n <N>` | text |
| **Remote** | Add remote | `dolt remote add <name> <url>` | text |
| | Push | `dolt push <remote> <branch>` | text |
| | Pull | `dolt pull <remote> <branch>` | text |
| | Fetch | `dolt fetch <remote>` | text |
| **Merge** | Merge | `dolt merge <branch>` | text |
| | List conflicts | `dolt conflicts cat <table>` | text |
| | Resolve ours | `dolt conflicts resolve --ours <table>` | text |
| | Resolve theirs | `dolt conflicts resolve --theirs <table>` | text |
| **Diff** | Working changes | `dolt diff` | text |
| | Between commits | `dolt diff <from> <to>` | text |
| | Table diff (SQL) | `dolt sql -q "SELECT * FROM DOLT_DIFF(...)" -r json` | json |
| **Reset** | Hard reset | `dolt reset --hard <commit>` | text |
| | Soft reset | `dolt reset --soft HEAD~1` | text |
| **Data Queries** | Select | `dolt sql -q "SELECT ..." -r json` | json |
| | Insert | `dolt sql -q "INSERT ..."` | text |
| | Update | `dolt sql -q "UPDATE ..."` | text |
| | Delete | `dolt sql -q "DELETE ..."` | text |

### 3.2 C# Interface Definition

```csharp
/// <summary>
/// Complete Dolt CLI wrapper - all operations via subprocess
/// </summary>
public interface IDoltCli
{
    // ==================== Repository Management ====================
    
    /// <summary>Initialize a new Dolt repository</summary>
    Task<CommandResult> InitAsync();
    
    /// <summary>Clone a repository from DoltHub</summary>
    Task<CommandResult> CloneAsync(string remoteUrl, string localPath = null);
    
    /// <summary>Get repository status (staged, unstaged changes)</summary>
    Task<RepositoryStatus> GetStatusAsync();
    
    // ==================== Branch Operations ====================
    
    /// <summary>Get the current active branch name</summary>
    Task<string> GetCurrentBranchAsync();
    
    /// <summary>List all branches</summary>
    Task<IEnumerable<BranchInfo>> ListBranchesAsync();
    
    /// <summary>Create a new branch (does not switch to it)</summary>
    Task<CommandResult> CreateBranchAsync(string branchName);
    
    /// <summary>Delete a branch</summary>
    Task<CommandResult> DeleteBranchAsync(string branchName, bool force = false);
    
    /// <summary>Switch to a branch, optionally creating it</summary>
    Task<CommandResult> CheckoutAsync(string branchName, bool createNew = false);
    
    // ==================== Commit Operations ====================
    
    /// <summary>Stage all changes</summary>
    Task<CommandResult> AddAllAsync();
    
    /// <summary>Stage specific tables</summary>
    Task<CommandResult> AddAsync(params string[] tables);
    
    /// <summary>Commit staged changes</summary>
    Task<CommitResult> CommitAsync(string message);
    
    /// <summary>Get the current HEAD commit hash</summary>
    Task<string> GetHeadCommitHashAsync();
    
    /// <summary>Get commit history</summary>
    Task<IEnumerable<CommitInfo>> GetLogAsync(int limit = 10);
    
    // ==================== Remote Operations ====================
    
    /// <summary>Add a remote</summary>
    Task<CommandResult> AddRemoteAsync(string name, string url);
    
    /// <summary>Remove a remote</summary>
    Task<CommandResult> RemoveRemoteAsync(string name);
    
    /// <summary>List remotes</summary>
    Task<IEnumerable<RemoteInfo>> ListRemotesAsync();
    
    /// <summary>Push to remote</summary>
    Task<CommandResult> PushAsync(string remote = "origin", string branch = null);
    
    /// <summary>Pull from remote</summary>
    Task<PullResult> PullAsync(string remote = "origin", string branch = null);
    
    /// <summary>Fetch from remote (no merge)</summary>
    Task<CommandResult> FetchAsync(string remote = "origin");
    
    // ==================== Merge Operations ====================
    
    /// <summary>Merge a branch into current branch</summary>
    Task<MergeResult> MergeAsync(string sourceBranch);
    
    /// <summary>Check if there are unresolved conflicts</summary>
    Task<bool> HasConflictsAsync();
    
    /// <summary>Get conflict details for a table</summary>
    Task<IEnumerable<ConflictInfo>> GetConflictsAsync(string tableName);
    
    /// <summary>Resolve conflicts using a strategy</summary>
    Task<CommandResult> ResolveConflictsAsync(string tableName, ConflictResolution resolution);
    
    // ==================== Diff Operations ====================
    
    /// <summary>Get uncommitted changes</summary>
    Task<DiffSummary> GetWorkingDiffAsync();
    
    /// <summary>Get diff between two commits for a specific table</summary>
    Task<IEnumerable<DiffRow>> GetTableDiffAsync(string fromCommit, string toCommit, string tableName);
    
    // ==================== Reset Operations ====================
    
    /// <summary>Hard reset to a specific commit</summary>
    Task<CommandResult> ResetHardAsync(string commitHash);
    
    /// <summary>Soft reset (keep changes staged)</summary>
    Task<CommandResult> ResetSoftAsync(string commitRef = "HEAD~1");
    
    // ==================== SQL Operations ====================
    
    /// <summary>Execute a SQL query and return results as JSON</summary>
    Task<string> QueryJsonAsync(string sql);
    
    /// <summary>Execute a SQL query and return typed results</summary>
    Task<IEnumerable<T>> QueryAsync<T>(string sql) where T : new();
    
    /// <summary>Execute a SQL statement (INSERT/UPDATE/DELETE)</summary>
    Task<int> ExecuteAsync(string sql);
    
    /// <summary>Execute a SQL query and return a single scalar value</summary>
    Task<T> ExecuteScalarAsync<T>(string sql);
}

// ==================== Supporting Types ====================

public record CommandResult(bool Success, string Output, string Error, int ExitCode);

public record CommitResult(bool Success, string CommitHash, string Message);

public record PullResult(bool Success, bool WasFastForward, bool HasConflicts, string Message);

public record MergeResult(bool Success, bool HasConflicts, string MergeCommitHash, string Message);

public record BranchInfo(string Name, bool IsCurrent, string LastCommitHash);

public record CommitInfo(string Hash, string Message, string Author, DateTime Date);

public record RemoteInfo(string Name, string Url);

public record DiffRow(
    string DiffType,      // "added", "modified", "removed"
    string SourceId,
    string FromContentHash,
    string ToContentHash,
    string ToContent,
    Dictionary<string, object> Metadata
);

public record ConflictInfo(
    string TableName,
    string RowId,
    Dictionary<string, object> OurValues,
    Dictionary<string, object> TheirValues,
    Dictionary<string, object> BaseValues
);

public enum ConflictResolution { Ours, Theirs }

public record RepositoryStatus(
    string Branch,
    bool HasStagedChanges,
    bool HasUnstagedChanges,
    IEnumerable<string> StagedTables,
    IEnumerable<string> ModifiedTables
);

public record DiffSummary(
    int TablesChanged,
    int RowsAdded,
    int RowsModified,
    int RowsDeleted
);
```

### 3.3 Implementation of Core CLI Wrapper

```csharp
// File: Services/DoltCli.cs
using CliWrap;
using CliWrap.Buffered;
using System.Text.Json;
using System.Text.RegularExpressions;

public class DoltCli : IDoltCli
{
    private readonly string _doltPath;
    private readonly string _repositoryPath;
    private readonly ILogger<DoltCli> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DoltCli(DoltConfiguration config, ILogger<DoltCli> logger)
    {
        _doltPath = config.DoltExecutablePath ?? "dolt";
        _repositoryPath = config.RepositoryPath;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    // ==================== Core Execution Methods ====================

    private async Task<CommandResult> ExecuteAsync(params string[] args)
    {
        _logger.LogDebug("Executing: dolt {Args}", string.Join(" ", args));
        
        try
        {
            var result = await Cli.Wrap(_doltPath)
                .WithArguments(args)
                .WithWorkingDirectory(_repositoryPath)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            var cmdResult = new CommandResult(
                Success: result.ExitCode == 0,
                Output: result.StandardOutput,
                Error: result.StandardError,
                ExitCode: result.ExitCode
            );

            if (!cmdResult.Success)
            {
                _logger.LogWarning("Dolt command failed: {Error}", cmdResult.Error);
            }

            return cmdResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute dolt command");
            return new CommandResult(false, "", ex.Message, -1);
        }
    }

    private async Task<string> ExecuteSqlJsonAsync(string sql)
    {
        var result = await ExecuteAsync("sql", "-q", sql, "-r", "json");
        if (!result.Success)
        {
            throw new DoltException($"SQL query failed: {result.Error}");
        }
        return result.Output;
    }

    // ==================== Branch Operations ====================

    public async Task<string> GetCurrentBranchAsync()
    {
        var json = await ExecuteSqlJsonAsync("SELECT active_branch() as branch");
        var rows = JsonSerializer.Deserialize<JsonElement>(json);
        return rows.GetProperty("rows")[0].GetProperty("branch").GetString();
    }

    public async Task<IEnumerable<BranchInfo>> ListBranchesAsync()
    {
        var result = await ExecuteAsync("branch", "-v");
        if (!result.Success) return Enumerable.Empty<BranchInfo>();

        var branches = new List<BranchInfo>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var isCurrent = line.StartsWith("*");
            var parts = line.TrimStart('*', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                branches.Add(new BranchInfo(parts[0], isCurrent, parts[1]));
            }
        }
        return branches;
    }

    public async Task<CommandResult> CheckoutAsync(string branchName, bool createNew = false)
    {
        return createNew
            ? await ExecuteAsync("checkout", "-b", branchName)
            : await ExecuteAsync("checkout", branchName);
    }

    public async Task<CommandResult> CreateBranchAsync(string branchName)
    {
        return await ExecuteAsync("branch", branchName);
    }

    public async Task<CommandResult> DeleteBranchAsync(string branchName, bool force = false)
    {
        return force
            ? await ExecuteAsync("branch", "-D", branchName)
            : await ExecuteAsync("branch", "-d", branchName);
    }

    // ==================== Commit Operations ====================

    public async Task<CommandResult> AddAllAsync()
    {
        return await ExecuteAsync("add", "-A");
    }

    public async Task<CommandResult> AddAsync(params string[] tables)
    {
        var args = new[] { "add" }.Concat(tables).ToArray();
        return await ExecuteAsync(args);
    }

    public async Task<CommitResult> CommitAsync(string message)
    {
        var result = await ExecuteAsync("commit", "-m", message);
        
        string commitHash = null;
        if (result.Success)
        {
            // Parse commit hash from output like "commit abc123def456"
            var match = Regex.Match(result.Output, @"commit\s+([a-f0-9]+)");
            commitHash = match.Success ? match.Groups[1].Value : await GetHeadCommitHashAsync();
        }

        return new CommitResult(result.Success, commitHash, result.Success ? message : result.Error);
    }

    public async Task<string> GetHeadCommitHashAsync()
    {
        var json = await ExecuteSqlJsonAsync("SELECT DOLT_HASHOF('HEAD') as hash");
        var rows = JsonSerializer.Deserialize<JsonElement>(json);
        return rows.GetProperty("rows")[0].GetProperty("hash").GetString();
    }

    public async Task<IEnumerable<CommitInfo>> GetLogAsync(int limit = 10)
    {
        var json = await ExecuteSqlJsonAsync(
            $"SELECT commit_hash, message, committer, date FROM dolt_log LIMIT {limit}");
        
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var commits = new List<CommitInfo>();
        
        foreach (var row in result.GetProperty("rows").EnumerateArray())
        {
            commits.Add(new CommitInfo(
                row.GetProperty("commit_hash").GetString(),
                row.GetProperty("message").GetString(),
                row.GetProperty("committer").GetString(),
                DateTime.Parse(row.GetProperty("date").GetString())
            ));
        }
        
        return commits;
    }

    // ==================== Remote Operations ====================

    public async Task<CommandResult> AddRemoteAsync(string name, string url)
    {
        return await ExecuteAsync("remote", "add", name, url);
    }

    public async Task<CommandResult> PushAsync(string remote = "origin", string branch = null)
    {
        branch ??= await GetCurrentBranchAsync();
        return await ExecuteAsync("push", remote, branch);
    }

    public async Task<PullResult> PullAsync(string remote = "origin", string branch = null)
    {
        branch ??= await GetCurrentBranchAsync();
        var result = await ExecuteAsync("pull", remote, branch);

        return new PullResult(
            Success: result.Success,
            WasFastForward: result.Output.Contains("Fast-forward"),
            HasConflicts: result.Output.Contains("CONFLICT") || result.Error.Contains("CONFLICT"),
            Message: result.Success ? result.Output : result.Error
        );
    }

    public async Task<CommandResult> FetchAsync(string remote = "origin")
    {
        return await ExecuteAsync("fetch", remote);
    }

    // ==================== Merge Operations ====================

    public async Task<MergeResult> MergeAsync(string sourceBranch)
    {
        var result = await ExecuteAsync("merge", sourceBranch);
        
        string mergeCommitHash = null;
        if (result.Success && !result.Output.Contains("CONFLICT"))
        {
            mergeCommitHash = await GetHeadCommitHashAsync();
        }

        return new MergeResult(
            Success: result.Success && !result.Output.Contains("CONFLICT"),
            HasConflicts: result.Output.Contains("CONFLICT") || result.Error.Contains("CONFLICT"),
            MergeCommitHash: mergeCommitHash,
            Message: result.Output + result.Error
        );
    }

    public async Task<bool> HasConflictsAsync()
    {
        var json = await ExecuteSqlJsonAsync(
            "SELECT COUNT(*) as cnt FROM dolt_conflicts WHERE table_name IS NOT NULL");
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var count = result.GetProperty("rows")[0].GetProperty("cnt").GetInt32();
        return count > 0;
    }

    public async Task<IEnumerable<ConflictInfo>> GetConflictsAsync(string tableName)
    {
        var result = await ExecuteAsync("conflicts", "cat", tableName);
        // Parse conflict output - format varies by table structure
        // Implementation depends on your table schema
        return ParseConflicts(result.Output, tableName);
    }

    public async Task<CommandResult> ResolveConflictsAsync(string tableName, ConflictResolution resolution)
    {
        var strategy = resolution == ConflictResolution.Ours ? "--ours" : "--theirs";
        return await ExecuteAsync("conflicts", "resolve", strategy, tableName);
    }

    // ==================== Diff Operations ====================

    public async Task<IEnumerable<DiffRow>> GetTableDiffAsync(
        string fromCommit, 
        string toCommit, 
        string tableName)
    {
        // Use DOLT_DIFF table function for structured diff data
        var sql = $@"
            SELECT 
                diff_type,
                to_{GetIdColumn(tableName)} as source_id,
                from_content_hash,
                to_content_hash,
                to_content
            FROM DOLT_DIFF('{fromCommit}', '{toCommit}', '{tableName}')";
        
        var json = await ExecuteSqlJsonAsync(sql);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        
        var diffs = new List<DiffRow>();
        foreach (var row in result.GetProperty("rows").EnumerateArray())
        {
            diffs.Add(new DiffRow(
                DiffType: row.GetProperty("diff_type").GetString(),
                SourceId: row.GetProperty("source_id").GetString(),
                FromContentHash: row.TryGetProperty("from_content_hash", out var fch) ? fch.GetString() : null,
                ToContentHash: row.TryGetProperty("to_content_hash", out var tch) ? tch.GetString() : null,
                ToContent: row.TryGetProperty("to_content", out var tc) ? tc.GetString() : null,
                Metadata: new Dictionary<string, object>()
            ));
        }
        
        return diffs;
    }

    // ==================== Reset Operations ====================

    public async Task<CommandResult> ResetHardAsync(string commitHash)
    {
        return await ExecuteAsync("reset", "--hard", commitHash);
    }

    public async Task<CommandResult> ResetSoftAsync(string commitRef = "HEAD~1")
    {
        return await ExecuteAsync("reset", "--soft", commitRef);
    }

    // ==================== SQL Operations ====================

    public async Task<string> QueryJsonAsync(string sql)
    {
        return await ExecuteSqlJsonAsync(sql);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql) where T : new()
    {
        var json = await ExecuteSqlJsonAsync(sql);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        
        var items = new List<T>();
        foreach (var row in result.GetProperty("rows").EnumerateArray())
        {
            items.Add(JsonSerializer.Deserialize<T>(row.GetRawText(), _jsonOptions));
        }
        return items;
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        var result = await ExecuteAsync("sql", "-q", sql);
        if (!result.Success)
        {
            throw new DoltException($"SQL execution failed: {result.Error}");
        }
        
        // Parse affected rows from output if available
        var match = Regex.Match(result.Output, @"(\d+)\s+row");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql)
    {
        var json = await ExecuteSqlJsonAsync(sql);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var firstRow = result.GetProperty("rows")[0];
        var firstProperty = firstRow.EnumerateObject().First();
        return JsonSerializer.Deserialize<T>(firstProperty.Value.GetRawText());
    }

    // ==================== Helper Methods ====================

    private string GetIdColumn(string tableName) => tableName switch
    {
        "issue_logs" => "log_id",
        "knowledge_docs" => "doc_id",
        "projects" => "project_id",
        _ => "id"
    };

    private IEnumerable<ConflictInfo> ParseConflicts(string output, string tableName)
    {
        // Implementation depends on conflict output format
        // This is a placeholder - actual implementation needed
        return Enumerable.Empty<ConflictInfo>();
    }
}

public class DoltException : Exception
{
    public DoltException(string message) : base(message) { }
    public DoltException(string message, Exception inner) : base(message, inner) { }
}
```

---

## 4. Database Schema Design

### 4.1 Core Tables (Source of Truth in Dolt)

```sql
-- ============================================
-- CORE DOCUMENT TABLES (Source of Truth)
-- ============================================

CREATE TABLE projects (
    project_id VARCHAR(36) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    repository_url VARCHAR(500),
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    metadata JSON
);

CREATE TABLE issue_logs (
    log_id VARCHAR(36) PRIMARY KEY,
    project_id VARCHAR(36) NOT NULL,
    issue_number INT NOT NULL,
    title VARCHAR(500),
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,  -- SHA-256
    log_type ENUM('investigation', 'implementation', 'resolution', 'postmortem') DEFAULT 'implementation',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    metadata JSON,
    
    FOREIGN KEY (project_id) REFERENCES projects(project_id),
    UNIQUE KEY uk_project_issue_type (project_id, issue_number, log_type),
    INDEX idx_content_hash (content_hash),
    INDEX idx_project_issue (project_id, issue_number)
);

CREATE TABLE knowledge_docs (
    doc_id VARCHAR(36) PRIMARY KEY,
    category VARCHAR(100) NOT NULL,
    tool_name VARCHAR(255) NOT NULL,
    tool_version VARCHAR(50),
    title VARCHAR(500) NOT NULL,
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    metadata JSON,
    
    INDEX idx_content_hash (content_hash),
    INDEX idx_tool (tool_name, tool_version),
    INDEX idx_category (category)
);

-- ============================================
-- SYNC STATE TRACKING
-- ============================================

CREATE TABLE chroma_sync_state (
    collection_name VARCHAR(255) PRIMARY KEY,
    last_sync_commit VARCHAR(40),
    last_sync_at DATETIME,
    document_count INT DEFAULT 0,
    chunk_count INT DEFAULT 0,
    embedding_model VARCHAR(100),
    sync_status ENUM('synced', 'pending', 'error', 'in_progress') DEFAULT 'pending',
    error_message TEXT,
    metadata JSON
);

CREATE TABLE document_sync_log (
    id INT AUTO_INCREMENT PRIMARY KEY,
    source_table ENUM('issue_logs', 'knowledge_docs') NOT NULL,
    source_id VARCHAR(36) NOT NULL,
    content_hash CHAR(64) NOT NULL,
    chroma_collection VARCHAR(255) NOT NULL,
    chunk_ids JSON,  -- Array of ChromaDB chunk IDs
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    embedding_model VARCHAR(100),
    sync_action ENUM('added', 'modified', 'deleted') NOT NULL,
    
    UNIQUE KEY uk_source_collection (source_table, source_id, chroma_collection),
    INDEX idx_content_hash (content_hash),
    INDEX idx_collection (chroma_collection)
);

-- ============================================
-- OPERATION AUDIT LOG (for debugging/rollback)
-- ============================================

CREATE TABLE sync_operations (
    operation_id INT AUTO_INCREMENT PRIMARY KEY,
    operation_type ENUM('commit', 'push', 'pull', 'merge', 'checkout', 'reset') NOT NULL,
    dolt_branch VARCHAR(255) NOT NULL,
    dolt_commit_before VARCHAR(40),
    dolt_commit_after VARCHAR(40),
    chroma_collections_affected JSON,  -- ["collection1", "collection2"]
    documents_added INT DEFAULT 0,
    documents_modified INT DEFAULT 0,
    documents_deleted INT DEFAULT 0,
    chunks_processed INT DEFAULT 0,
    operation_status ENUM('started', 'completed', 'failed', 'rolled_back') NOT NULL,
    error_message TEXT,
    started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME,
    metadata JSON
);
```

### 4.2 SQL Queries for Delta Detection (via CLI)

```csharp
public class DeltaDetector
{
    private readonly IDoltCli _dolt;

    public DeltaDetector(IDoltCli dolt)
    {
        _dolt = dolt;
    }

    /// <summary>
    /// Find documents that need syncing (new or modified) using content hash comparison
    /// </summary>
    public async Task<IEnumerable<DocumentDelta>> GetPendingSyncDocumentsAsync(string collectionName)
    {
        var sql = $@"
            SELECT 
                'issue_logs' as source_table,
                il.log_id as source_id,
                il.content,
                il.content_hash,
                il.project_id as identifier,
                JSON_OBJECT('issue_number', il.issue_number, 'log_type', il.log_type, 'title', il.title) as metadata,
                CASE 
                    WHEN dsl.content_hash IS NULL THEN 'new'
                    WHEN dsl.content_hash != il.content_hash THEN 'modified'
                END as change_type
            FROM issue_logs il
            LEFT JOIN document_sync_log dsl 
                ON dsl.source_table = 'issue_logs' 
                AND dsl.source_id = il.log_id
                AND dsl.chroma_collection = '{collectionName}'
            WHERE dsl.content_hash IS NULL 
               OR dsl.content_hash != il.content_hash

            UNION ALL

            SELECT 
                'knowledge_docs' as source_table,
                kd.doc_id as source_id,
                kd.content,
                kd.content_hash,
                kd.tool_name as identifier,
                JSON_OBJECT('category', kd.category, 'tool_version', kd.tool_version, 'title', kd.title) as metadata,
                CASE 
                    WHEN dsl.content_hash IS NULL THEN 'new'
                    WHEN dsl.content_hash != kd.content_hash THEN 'modified'
                END as change_type
            FROM knowledge_docs kd
            LEFT JOIN document_sync_log dsl 
                ON dsl.source_table = 'knowledge_docs' 
                AND dsl.source_id = kd.doc_id
                AND dsl.chroma_collection = '{collectionName}'
            WHERE dsl.content_hash IS NULL 
               OR dsl.content_hash != kd.content_hash";

        return await _dolt.QueryAsync<DocumentDelta>(sql);
    }

    /// <summary>
    /// Find documents deleted from Dolt but still tracked in sync log
    /// </summary>
    public async Task<IEnumerable<DeletedDocument>> GetDeletedDocumentsAsync(string collectionName)
    {
        var sql = $@"
            SELECT 
                dsl.source_table,
                dsl.source_id,
                dsl.chroma_collection,
                dsl.chunk_ids
            FROM document_sync_log dsl
            WHERE dsl.chroma_collection = '{collectionName}'
              AND (
                  (dsl.source_table = 'issue_logs' 
                   AND dsl.source_id NOT IN (SELECT log_id FROM issue_logs))
                  OR
                  (dsl.source_table = 'knowledge_docs' 
                   AND dsl.source_id NOT IN (SELECT doc_id FROM knowledge_docs))
              )";

        return await _dolt.QueryAsync<DeletedDocument>(sql);
    }

    /// <summary>
    /// Use Dolt's native diff for efficient commit-to-commit comparison
    /// </summary>
    public async Task<IEnumerable<DiffRow>> GetCommitDiffAsync(
        string fromCommit, 
        string toCommit, 
        string tableName)
    {
        return await _dolt.GetTableDiffAsync(fromCommit, toCommit, tableName);
    }
}

public record DocumentDelta(
    string SourceTable,
    string SourceId,
    string Content,
    string ContentHash,
    string Identifier,
    string Metadata,
    string ChangeType  // "new" or "modified"
);

public record DeletedDocument(
    string SourceTable,
    string SourceId,
    string ChromaCollection,
    string ChunkIds  // JSON array
);
```

### 4.3 Schema Mapping: Dolt ↔ ChromaDB

Understanding how data flows between Dolt and ChromaDB is critical for maintaining consistency across clones.

#### Key Principle: Dolt = Source of Truth, ChromaDB = Computed Cache

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        DATA FLOW DIRECTION                                   │
│                                                                             │
│   Dolt (Source of Truth)              ChromaDB (Derived/Computed)           │
│   ─────────────────────               ───────────────────────────           │
│                                                                             │
│   issue_logs                          Collection: vmrag_main                │
│   ┌─────────────────────┐             ┌─────────────────────────┐           │
│   │ log_id (PK)         │────────────▶│ Ids: ["log-001_chunk_0",│           │
│   │ content             │   chunk +   │       "log-001_chunk_1"]│           │
│   │ content_hash        │   embed     ├─────────────────────────┤           │
│   │ project_id          │────────────▶│ Documents: [chunk text] │           │
│   │ issue_number        │             ├─────────────────────────┤           │
│   │ title               │             │ Embeddings: [vectors]   │ ◀─ Generated
│   │ log_type            │────────────▶│ Metadatas: [            │           │
│   │ metadata (JSON)     │             │   {source_id: "log-001",│           │
│   └─────────────────────┘             │    content_hash: "...", │           │
│                                       │    chunk_index: 0,      │           │
│   document_sync_log                   │    project_id: "...",   │           │
│   ┌─────────────────────┐             │    ...}                 │           │
│   │ source_id ──────────┼─────────────┤ ]                       │           │
│   │ content_hash        │             ├─────────────────────────┤           │
│   │ chunk_ids (JSON) ───┼─────────────┤ Distances: [0.23, 0.41] │ ◀─ Query-time only
│   └─────────────────────┘             └─────────────────────────┘           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### Field Mapping Reference

| Dolt Field | ChromaDB Field | Transformation |
|------------|----------------|----------------|
| `log_id` / `doc_id` | `ids[]` | `{source_id}_chunk_{N}` |
| `content` | `documents[]` | Chunked (512 chars, 50 overlap) |
| `content_hash` | `metadatas[].content_hash` | Direct copy |
| `project_id` | `metadatas[].project_id` | Direct copy |
| `issue_number` | `metadatas[].issue_number` | Direct copy |
| `title` | `metadatas[].title` | Direct copy |
| `log_type` | `metadatas[].log_type` | Direct copy |
| N/A | `embeddings[]` | Generated by embedding model |
| N/A | `distances[]` | Computed at query time |

#### ChromaDB Collection Structure

```python
# What gets stored in ChromaDB for each synced document
collection.add(
    ids=["log-001_chunk_0", "log-001_chunk_1"],      # Derived from log_id + chunk index
    documents=["First 512 chars...", "Next 512..."], # Chunked from content
    embeddings=[[0.1, 0.2, ...], [0.3, 0.4, ...]],   # Computed by embedding model
    metadatas=[
        {
            # Back-references to Dolt (for sync tracking)
            "source_table": "issue_logs",
            "source_id": "log-001",
            "content_hash": "abc123...",
            "dolt_commit": "def456...",
            
            # Chunk positioning
            "chunk_index": 0,
            "total_chunks": 2,
            
            # Searchable metadata (copied from Dolt for filtering)
            "project_id": "proj-001",
            "issue_number": 101,
            "log_type": "implementation",
            "title": "Auth Bug Fix"
        },
        {
            # Second chunk has same metadata except chunk_index
            "source_table": "issue_logs",
            "source_id": "log-001",
            "content_hash": "abc123...",
            "dolt_commit": "def456...",
            "chunk_index": 1,
            "total_chunks": 2,
            "project_id": "proj-001",
            "issue_number": 101,
            "log_type": "implementation",
            "title": "Auth Bug Fix"
        }
    ]
)
```

#### Conversion Implementation

```csharp
// File: Services/DocumentConverter.cs
public class DocumentConverter
{
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    
    public DocumentConverter(int chunkSize = 512, int chunkOverlap = 50)
    {
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }
    
    /// <summary>
    /// Convert a Dolt document to ChromaDB entries
    /// </summary>
    public ChromaEntries ConvertDoltToChroma(
        DoltDocument doc, 
        string currentCommit)
    {
        // 1. Chunk the content
        var chunks = ChunkContent(doc.Content);
        
        // 2. Generate deterministic IDs
        var ids = chunks.Select((_, i) => $"{doc.SourceId}_chunk_{i}").ToList();
        
        // 3. Build metadata for each chunk
        var metadatas = chunks.Select((_, i) => new Dictionary<string, object>
        {
            // Back-references (for sync tracking and validation)
            ["source_table"] = doc.SourceTable,
            ["source_id"] = doc.SourceId,
            ["content_hash"] = doc.ContentHash,
            ["dolt_commit"] = currentCommit,
            
            // Chunk positioning
            ["chunk_index"] = i,
            ["total_chunks"] = chunks.Count,
            
            // Searchable metadata (copied from Dolt)
            ["project_id"] = doc.ProjectId ?? "",
            ["issue_number"] = doc.IssueNumber,
            ["log_type"] = doc.LogType ?? "",
            ["title"] = doc.Title ?? "",
            ["category"] = doc.Category ?? "",
            ["tool_name"] = doc.ToolName ?? ""
        }).ToList();
        
        return new ChromaEntries(ids, chunks, metadatas);
    }
    
    /// <summary>
    /// Chunk content with overlap for context preservation
    /// </summary>
    public List<string> ChunkContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new List<string> { "" };
            
        var chunks = new List<string>();
        var start = 0;
        
        while (start < content.Length)
        {
            var length = Math.Min(_chunkSize, content.Length - start);
            chunks.Add(content.Substring(start, length));
            
            // Move forward by (chunkSize - overlap)
            start += _chunkSize - _chunkOverlap;
            
            // Prevent infinite loop on small content
            if (start <= 0 && chunks.Count > 0) break;
        }
        
        return chunks;
    }
    
    /// <summary>
    /// Reconstruct chunk IDs for a document (for deletion)
    /// </summary>
    public List<string> GetChunkIds(string sourceId, int totalChunks)
    {
        return Enumerable.Range(0, totalChunks)
            .Select(i => $"{sourceId}_chunk_{i}")
            .ToList();
    }
}

// Supporting types
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

public record ChromaEntries(
    List<string> Ids,
    List<string> Documents,
    List<Dictionary<string, object>> Metadatas
);
```

#### Reverse Lookup: ChromaDB → Dolt

When query results need full document context:

```csharp
/// <summary>
/// Fetch full document from Dolt based on ChromaDB query result
/// </summary>
public async Task<FullDocumentResult> GetFullDocumentAsync(
    ChromaQueryResult chromaResult)
{
    var sourceId = chromaResult.Metadata["source_id"].ToString();
    var sourceTable = chromaResult.Metadata["source_table"].ToString();
    
    // Query Dolt for full document
    var sql = sourceTable == "issue_logs"
        ? $"SELECT * FROM issue_logs WHERE log_id = '{sourceId}'"
        : $"SELECT * FROM knowledge_docs WHERE doc_id = '{sourceId}'";
    
    var fullDoc = (await _dolt.QueryAsync<dynamic>(sql)).FirstOrDefault();
    
    return new FullDocumentResult
    {
        // From ChromaDB (the matching chunk)
        MatchedChunkText = chromaResult.Document,
        MatchedChunkIndex = (int)chromaResult.Metadata["chunk_index"],
        Distance = chromaResult.Distance,
        
        // From Dolt (full context)
        FullContent = fullDoc?.content,
        SourceId = sourceId,
        SourceTable = sourceTable,
        Title = fullDoc?.title,
        Metadata = fullDoc
    };
}

public record FullDocumentResult
{
    public string MatchedChunkText { get; init; }
    public int MatchedChunkIndex { get; init; }
    public float Distance { get; init; }
    public string FullContent { get; init; }
    public string SourceId { get; init; }
    public string SourceTable { get; init; }
    public string Title { get; init; }
    public dynamic Metadata { get; init; }
}
```

### 4.4 Ensuring Consistency Across Clones

#### The Consistency Challenge

ChromaDB embeddings are **not directly portable** because:
1. They're derived data (can be regenerated from source)
2. They're model-specific (different embedding models = incompatible vectors)
3. ChromaDB doesn't have built-in version control

#### Solution: Regenerate from Dolt State

When cloning or pulling, ChromaDB is regenerated from Dolt's versioned state:

```
Clone A (Developer 1)                    Clone B (Developer 2)
─────────────────────                    ─────────────────────
1. dolt clone org/repo                   1. dolt clone org/repo
   ↓                                        ↓
2. Dolt contains:                        2. Dolt contains: (IDENTICAL)
   • issue_logs (3 rows)                    • issue_logs (3 rows)
   • knowledge_docs (2 rows)                • knowledge_docs (2 rows)
   • document_sync_log (empty*)             • document_sync_log (empty*)
   • chroma_sync_state (empty*)             • chroma_sync_state (empty*)
   ↓                                        ↓
3. MCP Server starts                     3. MCP Server starts
   ↓                                        ↓
4. Detects: No sync state for            4. Detects: No sync state for
   current branch collection                current branch collection
   ↓                                        ↓
5. Full sync triggered:                  5. Full sync triggered:
   • Read all docs from Dolt                • Read all docs from Dolt
   • Chunk each document                    • Chunk each document
   • Generate embeddings                    • Generate embeddings
   • Store in ChromaDB                      • Store in ChromaDB
   • Update sync state in Dolt              • Update sync state in Dolt
   ↓                                        ↓
6. Result: ChromaDB matches              6. Result: ChromaDB matches
   Dolt state exactly                       Dolt state exactly

* sync tables may have data if synced before push, but will be validated
```

#### Consistency Guarantee Matrix

| Property | Clone A | Clone B | Guaranteed Match? |
|----------|---------|---------|-------------------|
| Dolt document content | ✓ | ✓ | Yes (versioned) |
| Dolt content_hash | ✓ | ✓ | Yes (versioned) |
| ChromaDB chunk text | ✓ | ✓ | Yes (deterministic chunking) |
| ChromaDB chunk IDs | ✓ | ✓ | Yes (deterministic: `{id}_chunk_{N}`) |
| ChromaDB embeddings | ✓ | ✓ | Yes* (same model = same vectors) |
| Query result ranking | ✓ | ✓ | Yes (same embeddings = same distances) |

*Embeddings match only if both clones use identical embedding model and version.

#### Sync Validation Implementation

```csharp
// File: Services/SyncValidator.cs
public class SyncValidator
{
    private readonly IDoltCli _dolt;
    private readonly IChromaManager _chromaManager;
    private readonly ILogger<SyncValidator> _logger;
    
    public SyncValidator(
        IDoltCli dolt, 
        IChromaManager chromaManager,
        ILogger<SyncValidator> logger)
    {
        _dolt = dolt;
        _chromaManager = chromaManager;
        _logger = logger;
    }
    
    /// <summary>
    /// Validate that ChromaDB collection matches Dolt state
    /// </summary>
    public async Task<ValidationResult> ValidateCollectionAsync(string collectionName)
    {
        var issues = new List<string>();
        
        // 1. Check if sync state exists
        var syncState = await GetSyncStateAsync(collectionName);
        if (syncState == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                NeedsFullSync = true,
                Issues = new[] { "No sync state found - full sync required" }
            };
        }
        
        // 2. Check if commits match
        var currentCommit = await _dolt.GetHeadCommitHashAsync();
        if (syncState.LastSyncCommit != currentCommit)
        {
            return new ValidationResult
            {
                IsValid = false,
                NeedsIncrementalSync = true,
                Issues = new[] { $"Sync commit {syncState.LastSyncCommit} != HEAD {currentCommit}" }
            };
        }
        
        // 3. Validate embedding model matches
        var configuredModel = _configuration.EmbeddingModel;
        if (syncState.EmbeddingModel != configuredModel)
        {
            issues.Add($"Model mismatch: collection uses '{syncState.EmbeddingModel}', " +
                      $"system configured for '{configuredModel}'");
        }
        
        // 4. Spot-check document count
        var doltDocCount = await GetDoltDocumentCountAsync();
        if (Math.Abs(syncState.DocumentCount - doltDocCount) > 0)
        {
            issues.Add($"Document count mismatch: Dolt has {doltDocCount}, " +
                      $"sync state shows {syncState.DocumentCount}");
        }
        
        // 5. Validate content hashes match (sample check)
        var hashMismatches = await ValidateContentHashesAsync(collectionName);
        issues.AddRange(hashMismatches);
        
        return new ValidationResult
        {
            IsValid = !issues.Any(),
            NeedsFullSync = issues.Any(i => i.Contains("Model mismatch")),
            Issues = issues
        };
    }
    
    /// <summary>
    /// Validate that content hashes in ChromaDB metadata match Dolt
    /// </summary>
    private async Task<List<string>> ValidateContentHashesAsync(string collectionName)
    {
        var issues = new List<string>();
        
        // Get sync log entries
        var syncLogs = await _dolt.QueryAsync<SyncLogEntry>(
            $"SELECT source_id, content_hash, chunk_ids FROM document_sync_log " +
            $"WHERE chroma_collection = '{collectionName}'");
        
        foreach (var log in syncLogs.Take(10)) // Sample check first 10
        {
            var chunkIds = JsonSerializer.Deserialize<List<string>>(log.ChunkIds);
            if (chunkIds == null || !chunkIds.Any()) continue;
            
            // Get first chunk from ChromaDB
            var chromaDoc = await _chromaManager.GetAsync(collectionName, chunkIds.First());
            if (chromaDoc == null)
            {
                issues.Add($"Document {log.SourceId}: chunk not found in ChromaDB");
                continue;
            }
            
            var chromaHash = chromaDoc.Metadata["content_hash"]?.ToString();
            if (chromaHash != log.ContentHash)
            {
                issues.Add($"Document {log.SourceId}: hash mismatch " +
                          $"(Dolt: {log.ContentHash}, Chroma: {chromaHash})");
            }
        }
        
        return issues;
    }
    
    /// <summary>
    /// Ensure embedding model consistency before sync
    /// </summary>
    public async Task<ModelValidationResult> ValidateEmbeddingModelAsync(string collectionName)
    {
        var syncState = await GetSyncStateAsync(collectionName);
        if (syncState == null)
        {
            return new ModelValidationResult { IsCompatible = true, IsNewCollection = true };
        }
        
        var configuredModel = _configuration.EmbeddingModel;
        var storedModel = syncState.EmbeddingModel;
        
        if (storedModel == configuredModel)
        {
            return new ModelValidationResult { IsCompatible = true };
        }
        
        return new ModelValidationResult
        {
            IsCompatible = false,
            StoredModel = storedModel,
            ConfiguredModel = configuredModel,
            Message = $"Collection was created with '{storedModel}' but system is configured " +
                     $"for '{configuredModel}'. Options:\n" +
                     $"  1. Regenerate collection with new model (recommended)\n" +
                     $"  2. Change configuration to use '{storedModel}'"
        };
    }
    
    private async Task<SyncState> GetSyncStateAsync(string collectionName)
    {
        try
        {
            var results = await _dolt.QueryAsync<SyncState>(
                $"SELECT * FROM chroma_sync_state WHERE collection_name = '{collectionName}'");
            return results.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
    
    private async Task<int> GetDoltDocumentCountAsync()
    {
        var issueCount = await _dolt.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM issue_logs");
        var knowledgeCount = await _dolt.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM knowledge_docs");
        return issueCount + knowledgeCount;
    }
}

public record SyncState(
    string CollectionName,
    string LastSyncCommit,
    DateTime? LastSyncAt,
    int DocumentCount,
    int ChunkCount,
    string EmbeddingModel,
    string SyncStatus
);

public record SyncLogEntry(
    string SourceId,
    string ContentHash,
    string ChunkIds
);

public record ValidationResult
{
    public bool IsValid { get; init; }
    public bool NeedsFullSync { get; init; }
    public bool NeedsIncrementalSync { get; init; }
    public IEnumerable<string> Issues { get; init; } = Array.Empty<string>();
}

public record ModelValidationResult
{
    public bool IsCompatible { get; init; }
    public bool IsNewCollection { get; init; }
    public string StoredModel { get; init; }
    public string ConfiguredModel { get; init; }
    public string Message { get; init; }
}
```

### 4.5 The document_sync_log Table: Critical for Bidirectional Mapping

The `document_sync_log` table is the **key bridge** between Dolt and ChromaDB:

```sql
-- Example state after syncing 3 documents:
SELECT * FROM document_sync_log;

| id | source_table  | source_id | content_hash     | chroma_collection | chunk_ids                                    | synced_at           |
|----|---------------|-----------|------------------|-------------------|----------------------------------------------|---------------------|
| 1  | issue_logs    | log-001   | abc123...        | vmrag_main        | ["log-001_chunk_0","log-001_chunk_1"]        | 2025-01-15 10:30:00 |
| 2  | issue_logs    | log-002   | def456...        | vmrag_main        | ["log-002_chunk_0"]                          | 2025-01-15 10:30:01 |
| 3  | knowledge_docs| doc-001   | ghi789...        | vmrag_main        | ["doc-001_chunk_0","doc-001_chunk_1","..."]  | 2025-01-15 10:30:02 |
```

**This table enables:**

1. **Forward lookup** (Dolt → ChromaDB): "What chunks exist for this document?"
2. **Reverse lookup** (ChromaDB → Dolt): "What document does this chunk belong to?"
3. **Change detection**: Compare `content_hash` to detect modifications
4. **Deletion tracking**: Find orphaned sync entries when documents are deleted
5. **Chunk cleanup**: Know exactly which ChromaDB IDs to delete when updating

---

## 5. Delta Detection & Sync Processing

### 5.1 Operation Processing Matrix

| Operation | Dolt Action | Chroma Action | Sync Direction |
|-----------|-------------|---------------|----------------|
| **Commit** | `dolt add -A` + `dolt commit` | Sync pending docs | Dolt → Chroma |
| **Push** | `dolt push origin <branch>` | No change | Dolt → Remote |
| **Pull** | `dolt pull origin <branch>` | Sync all changes | Remote → Dolt → Chroma |
| **Checkout** | `dolt checkout <branch>` | Load/create branch collection | Dolt → Chroma |
| **Merge** | `dolt merge <branch>` | Sync merged documents | Dolt → Chroma |
| **Reset** | `dolt reset --hard <commit>` | Regenerate from reset state | Dolt → Chroma |
| **Fast-Forward** | `dolt pull` (FF) | Incremental sync | Remote → Dolt → Chroma |

### 5.2 Sync Manager Implementation

```csharp
// File: Services/SyncManager.cs
public class SyncManager : ISyncManager
{
    private readonly IDoltCli _dolt;
    private readonly IChromaManager _chromaManager;
    private readonly IEmbeddingService _embeddingService;
    private readonly DeltaDetector _deltaDetector;
    private readonly ILogger<SyncManager> _logger;

    public SyncManager(
        IDoltCli dolt,
        IChromaManager chromaManager,
        IEmbeddingService embeddingService,
        ILogger<SyncManager> logger)
    {
        _dolt = dolt;
        _chromaManager = chromaManager;
        _embeddingService = embeddingService;
        _deltaDetector = new DeltaDetector(dolt);
        _logger = logger;
    }

    // ==================== Commit Processing ====================

    public async Task<SyncResult> ProcessCommitAsync(string message, bool syncAfter = true)
    {
        var result = new SyncResult();
        var branch = await _dolt.GetCurrentBranchAsync();
        var beforeCommit = await _dolt.GetHeadCommitHashAsync();

        // Log operation start
        var operationId = await LogOperationStartAsync("commit", branch, beforeCommit);

        try
        {
            // Stage and commit in Dolt
            await _dolt.AddAllAsync();
            var commitResult = await _dolt.CommitAsync(message);
            
            if (!commitResult.Success)
            {
                await LogOperationFailedAsync(operationId, commitResult.Message);
                result.Status = SyncStatus.Failed;
                result.ErrorMessage = commitResult.Message;
                return result;
            }

            var afterCommit = commitResult.CommitHash;
            
            if (syncAfter)
            {
                // Find changes using DOLT_DIFF
                var issueChanges = await _dolt.GetTableDiffAsync(beforeCommit, afterCommit, "issue_logs");
                var knowledgeChanges = await _dolt.GetTableDiffAsync(beforeCommit, afterCommit, "knowledge_docs");
                var allChanges = issueChanges.Concat(knowledgeChanges).ToList();

                // Process each change
                foreach (var change in allChanges)
                {
                    await ProcessDiffRowAsync(change, branch, result);
                }

                // Update sync state
                await UpdateSyncStateAsync(branch, afterCommit, result);
            }

            await LogOperationCompletedAsync(operationId, afterCommit, result);
            result.Status = SyncStatus.Completed;
            result.CommitHash = afterCommit;
        }
        catch (Exception ex)
        {
            await LogOperationFailedAsync(operationId, ex.Message);
            throw;
        }

        return result;
    }

    // ==================== Pull Processing ====================

    public async Task<SyncResult> ProcessPullAsync(string remote = "origin")
    {
        var result = new SyncResult();
        var branch = await _dolt.GetCurrentBranchAsync();
        var beforeCommit = await _dolt.GetHeadCommitHashAsync();

        var operationId = await LogOperationStartAsync("pull", branch, beforeCommit);

        try
        {
            // Pull from remote
            var pullResult = await _dolt.PullAsync(remote, branch);
            
            if (!pullResult.Success)
            {
                if (pullResult.HasConflicts)
                {
                    result.Status = SyncStatus.Conflicts;
                    result.ErrorMessage = "Pull resulted in conflicts. Resolve before syncing.";
                }
                else
                {
                    result.Status = SyncStatus.Failed;
                    result.ErrorMessage = pullResult.Message;
                }
                await LogOperationFailedAsync(operationId, result.ErrorMessage);
                return result;
            }

            var afterCommit = await _dolt.GetHeadCommitHashAsync();

            // If no changes (same commit), return early
            if (beforeCommit == afterCommit)
            {
                result.Status = SyncStatus.NoChanges;
                await LogOperationCompletedAsync(operationId, afterCommit, result);
                return result;
            }

            // Sync all changes between commits
            await SyncCommitRangeAsync(beforeCommit, afterCommit, branch, result);

            await LogOperationCompletedAsync(operationId, afterCommit, result);
            result.Status = SyncStatus.Completed;
            result.WasFastForward = pullResult.WasFastForward;
        }
        catch (Exception ex)
        {
            await LogOperationFailedAsync(operationId, ex.Message);
            throw;
        }

        return result;
    }

    // ==================== Checkout Processing ====================

    public async Task<SyncResult> ProcessCheckoutAsync(string targetBranch, bool createNew = false)
    {
        var result = new SyncResult();
        var previousBranch = await _dolt.GetCurrentBranchAsync();

        var operationId = await LogOperationStartAsync("checkout", targetBranch, null);

        try
        {
            // Checkout in Dolt
            var checkoutResult = await _dolt.CheckoutAsync(targetBranch, createNew);
            
            if (!checkoutResult.Success)
            {
                result.Status = SyncStatus.Failed;
                result.ErrorMessage = checkoutResult.Error;
                await LogOperationFailedAsync(operationId, result.ErrorMessage);
                return result;
            }

            // Get collection name for target branch
            var collectionName = GetCollectionName(targetBranch);
            var collectionExists = await _chromaManager.CollectionExistsAsync(collectionName);

            if (createNew)
            {
                // New branch: clone collection from parent branch
                var parentCollection = GetCollectionName(previousBranch);
                await _chromaManager.CloneCollectionAsync(parentCollection, collectionName);
                _logger.LogInformation("Created collection {Collection} from {Parent}", 
                    collectionName, parentCollection);
            }
            else if (!collectionExists)
            {
                // Existing branch but no collection: full sync
                await FullSyncToChromaAsync(targetBranch, collectionName, result);
            }
            else
            {
                // Existing collection: check if incremental sync needed
                var lastSyncCommit = await GetLastSyncCommitAsync(collectionName);
                var currentCommit = await _dolt.GetHeadCommitHashAsync();

                if (lastSyncCommit != currentCommit)
                {
                    await SyncCommitRangeAsync(lastSyncCommit, currentCommit, targetBranch, result);
                }
            }

            var afterCommit = await _dolt.GetHeadCommitHashAsync();
            await LogOperationCompletedAsync(operationId, afterCommit, result);
            result.Status = SyncStatus.Completed;
        }
        catch (Exception ex)
        {
            await LogOperationFailedAsync(operationId, ex.Message);
            throw;
        }

        return result;
    }

    // ==================== Merge Processing ====================

    public async Task<MergeSyncResult> ProcessMergeAsync(string sourceBranch)
    {
        var result = new MergeSyncResult();
        var targetBranch = await _dolt.GetCurrentBranchAsync();
        var beforeCommit = await _dolt.GetHeadCommitHashAsync();

        var operationId = await LogOperationStartAsync("merge", targetBranch, beforeCommit);

        try
        {
            // Attempt merge
            var mergeResult = await _dolt.MergeAsync(sourceBranch);

            if (mergeResult.HasConflicts)
            {
                result.HasConflicts = true;
                result.Conflicts = (await _dolt.GetConflictsAsync("issue_logs"))
                    .Concat(await _dolt.GetConflictsAsync("knowledge_docs"))
                    .ToList();
                result.Status = MergeSyncStatus.ConflictsDetected;
                await LogOperationFailedAsync(operationId, "Merge conflicts detected");
                return result;
            }

            if (!mergeResult.Success)
            {
                result.Status = MergeSyncStatus.Failed;
                result.ErrorMessage = mergeResult.Message;
                await LogOperationFailedAsync(operationId, result.ErrorMessage);
                return result;
            }

            var afterCommit = mergeResult.MergeCommitHash ?? await _dolt.GetHeadCommitHashAsync();

            // Sync merged changes
            await SyncCommitRangeAsync(beforeCommit, afterCommit, targetBranch, result);

            await LogOperationCompletedAsync(operationId, afterCommit, result);
            result.Status = MergeSyncStatus.Completed;
        }
        catch (Exception ex)
        {
            await LogOperationFailedAsync(operationId, ex.Message);
            throw;
        }

        return result;
    }

    // ==================== Reset Processing ====================

    public async Task<SyncResult> ProcessResetAsync(string targetCommit)
    {
        var result = new SyncResult();
        var branch = await _dolt.GetCurrentBranchAsync();
        var beforeCommit = await _dolt.GetHeadCommitHashAsync();

        var operationId = await LogOperationStartAsync("reset", branch, beforeCommit);

        try
        {
            // Reset Dolt
            var resetResult = await _dolt.ResetHardAsync(targetCommit);
            
            if (!resetResult.Success)
            {
                result.Status = SyncStatus.Failed;
                result.ErrorMessage = resetResult.Error;
                await LogOperationFailedAsync(operationId, result.ErrorMessage);
                return result;
            }

            // Full regeneration (reset can go forward or backward)
            var collectionName = GetCollectionName(branch);
            
            // Delete existing collection
            await _chromaManager.DeleteCollectionAsync(collectionName);
            
            // Full sync from reset state
            await FullSyncToChromaAsync(branch, collectionName, result);

            await LogOperationCompletedAsync(operationId, targetCommit, result);
            result.Status = SyncStatus.Completed;
        }
        catch (Exception ex)
        {
            await LogOperationFailedAsync(operationId, ex.Message);
            throw;
        }

        return result;
    }

    // ==================== Change Detection ====================

    public async Task<bool> HasPendingChangesAsync()
    {
        var branch = await _dolt.GetCurrentBranchAsync();
        var collectionName = GetCollectionName(branch);
        var lastSyncCommit = await GetLastSyncCommitAsync(collectionName);
        var currentCommit = await _dolt.GetHeadCommitHashAsync();

        return lastSyncCommit != currentCommit;
    }

    public async Task<PendingChanges> GetPendingChangesAsync()
    {
        var branch = await _dolt.GetCurrentBranchAsync();
        var collectionName = GetCollectionName(branch);

        var pendingDocs = await _deltaDetector.GetPendingSyncDocumentsAsync(collectionName);
        var deletedDocs = await _deltaDetector.GetDeletedDocumentsAsync(collectionName);

        return new PendingChanges
        {
            NewDocuments = pendingDocs.Where(d => d.ChangeType == "new").ToList(),
            ModifiedDocuments = pendingDocs.Where(d => d.ChangeType == "modified").ToList(),
            DeletedDocuments = deletedDocs.ToList()
        };
    }

    // ==================== Helper Methods ====================

    private async Task SyncCommitRangeAsync(
        string fromCommit, 
        string toCommit, 
        string branch,
        SyncResult result)
    {
        var issueChanges = await _dolt.GetTableDiffAsync(fromCommit, toCommit, "issue_logs");
        var knowledgeChanges = await _dolt.GetTableDiffAsync(fromCommit, toCommit, "knowledge_docs");

        foreach (var change in issueChanges.Concat(knowledgeChanges))
        {
            await ProcessDiffRowAsync(change, branch, result);
        }

        await UpdateSyncStateAsync(branch, toCommit, result);
    }

    private async Task ProcessDiffRowAsync(DiffRow diff, string branch, SyncResult result)
    {
        var collectionName = GetCollectionName(branch);

        switch (diff.DiffType)
        {
            case "added":
                await AddDocumentToChromaAsync(diff, collectionName);
                result.Added++;
                break;

            case "modified":
                await UpdateDocumentInChromaAsync(diff, collectionName);
                result.Modified++;
                break;

            case "removed":
                await RemoveDocumentFromChromaAsync(diff.SourceId, collectionName);
                result.Deleted++;
                break;
        }
    }

    private async Task AddDocumentToChromaAsync(DiffRow diff, string collectionName)
    {
        // Chunk the content
        var chunks = ChunkContent(diff.ToContent);
        var chunkIds = chunks.Select((_, i) => $"{diff.SourceId}_chunk_{i}").ToList();

        // Generate embeddings
        var embeddings = await _embeddingService.EmbedAsync(chunks);

        // Build metadata for each chunk
        var metadatas = chunks.Select((_, i) => new Dictionary<string, object>
        {
            ["source_id"] = diff.SourceId,
            ["content_hash"] = diff.ToContentHash,
            ["chunk_index"] = i,
            ["total_chunks"] = chunks.Count
        }).ToList();

        // Add to ChromaDB
        await _chromaManager.AddDocumentsAsync(collectionName, chunkIds, chunks, embeddings, metadatas);

        // Update sync log
        await UpdateDocumentSyncLogAsync(diff, collectionName, chunkIds, "added");
    }

    private async Task UpdateDocumentInChromaAsync(DiffRow diff, string collectionName)
    {
        // Remove old chunks first
        await RemoveDocumentFromChromaAsync(diff.SourceId, collectionName);
        
        // Add new chunks
        await AddDocumentToChromaAsync(diff, collectionName);
    }

    private async Task RemoveDocumentFromChromaAsync(string sourceId, string collectionName)
    {
        // Get existing chunk IDs from sync log
        var sql = $@"SELECT chunk_ids FROM document_sync_log 
                     WHERE source_id = '{sourceId}' AND chroma_collection = '{collectionName}'";
        var chunkIdsJson = await _dolt.ExecuteScalarAsync<string>(sql);
        
        if (!string.IsNullOrEmpty(chunkIdsJson))
        {
            var chunkIds = JsonSerializer.Deserialize<List<string>>(chunkIdsJson);
            await _chromaManager.DeleteDocumentsAsync(collectionName, chunkIds);
        }

        // Remove from sync log
        await _dolt.ExecuteAsync(
            $"DELETE FROM document_sync_log WHERE source_id = '{sourceId}' AND chroma_collection = '{collectionName}'");
    }

    private async Task FullSyncToChromaAsync(string branch, string collectionName, SyncResult result)
    {
        _logger.LogInformation("Starting full sync for branch {Branch}", branch);

        // Create collection
        await _chromaManager.CreateCollectionAsync(collectionName, new Dictionary<string, object>
        {
            ["dolt_branch"] = branch,
            ["source"] = "dolt"
        });

        // Get all documents from both tables
        var issueLogs = await _dolt.QueryAsync<DocumentRecord>(
            "SELECT log_id as source_id, content, content_hash, project_id as identifier FROM issue_logs");
        var knowledgeDocs = await _dolt.QueryAsync<DocumentRecord>(
            "SELECT doc_id as source_id, content, content_hash, tool_name as identifier FROM knowledge_docs");

        foreach (var doc in issueLogs.Concat(knowledgeDocs))
        {
            var diff = new DiffRow("added", doc.SourceId, null, doc.ContentHash, doc.Content, new());
            await AddDocumentToChromaAsync(diff, collectionName);
            result.Added++;
        }

        var currentCommit = await _dolt.GetHeadCommitHashAsync();
        await UpdateSyncStateAsync(branch, currentCommit, result);
    }

    private string GetCollectionName(string branch)
    {
        var safeBranch = branch.Replace("/", "-").Replace("_", "-");
        if (safeBranch.Length > 20) safeBranch = safeBranch.Substring(0, 20);
        return $"vmrag_{safeBranch}";
    }

    private List<string> ChunkContent(string content, int chunkSize = 512, int overlap = 50)
    {
        var chunks = new List<string>();
        var start = 0;
        
        while (start < content.Length)
        {
            var end = Math.Min(start + chunkSize, content.Length);
            chunks.Add(content.Substring(start, end - start));
            start = end - overlap;
            if (start < 0) break;
        }
        
        return chunks;
    }

    // Sync state and logging methods...
    private async Task<string> GetLastSyncCommitAsync(string collectionName)
    {
        try
        {
            return await _dolt.ExecuteScalarAsync<string>(
                $"SELECT last_sync_commit FROM chroma_sync_state WHERE collection_name = '{collectionName}'");
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateSyncStateAsync(string branch, string commit, SyncResult result)
    {
        var collectionName = GetCollectionName(branch);
        var sql = $@"
            INSERT INTO chroma_sync_state 
                (collection_name, last_sync_commit, last_sync_at, document_count, sync_status)
            VALUES 
                ('{collectionName}', '{commit}', NOW(), {result.Added}, 'synced')
            ON DUPLICATE KEY UPDATE
                last_sync_commit = '{commit}',
                last_sync_at = NOW(),
                document_count = document_count + {result.Added} - {result.Deleted},
                sync_status = 'synced'";
        
        await _dolt.ExecuteAsync(sql);
    }

    private async Task UpdateDocumentSyncLogAsync(
        DiffRow diff, 
        string collectionName, 
        List<string> chunkIds,
        string action)
    {
        var chunkIdsJson = JsonSerializer.Serialize(chunkIds);
        var sql = $@"
            INSERT INTO document_sync_log 
                (source_table, source_id, content_hash, chroma_collection, chunk_ids, sync_action)
            VALUES 
                ('issue_logs', '{diff.SourceId}', '{diff.ToContentHash}', '{collectionName}', '{chunkIdsJson}', '{action}')
            ON DUPLICATE KEY UPDATE
                content_hash = '{diff.ToContentHash}',
                chunk_ids = '{chunkIdsJson}',
                synced_at = NOW(),
                sync_action = '{action}'";
        
        await _dolt.ExecuteAsync(sql);
    }

    private async Task<int> LogOperationStartAsync(string opType, string branch, string beforeCommit)
    {
        var sql = $@"
            INSERT INTO sync_operations 
                (operation_type, dolt_branch, dolt_commit_before, operation_status)
            VALUES 
                ('{opType}', '{branch}', '{beforeCommit ?? ""}', 'started')";
        
        await _dolt.ExecuteAsync(sql);
        return await _dolt.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
    }

    private async Task LogOperationCompletedAsync(int operationId, string afterCommit, SyncResult result)
    {
        var sql = $@"
            UPDATE sync_operations SET
                dolt_commit_after = '{afterCommit}',
                documents_added = {result.Added},
                documents_modified = {result.Modified},
                documents_deleted = {result.Deleted},
                operation_status = 'completed',
                completed_at = NOW()
            WHERE operation_id = {operationId}";
        
        await _dolt.ExecuteAsync(sql);
    }

    private async Task LogOperationFailedAsync(int operationId, string error)
    {
        var escapedError = error.Replace("'", "''");
        var sql = $@"
            UPDATE sync_operations SET
                operation_status = 'failed',
                error_message = '{escapedError}',
                completed_at = NOW()
            WHERE operation_id = {operationId}";
        
        await _dolt.ExecuteAsync(sql);
    }
}

// Supporting types
public record DocumentRecord(string SourceId, string Content, string ContentHash, string Identifier);

public class SyncResult
{
    public SyncStatus Status { get; set; }
    public string CommitHash { get; set; }
    public int Added { get; set; }
    public int Modified { get; set; }
    public int Deleted { get; set; }
    public bool WasFastForward { get; set; }
    public string ErrorMessage { get; set; }
}

public class MergeSyncResult : SyncResult
{
    public new MergeSyncStatus Status { get; set; }
    public bool HasConflicts { get; set; }
    public List<ConflictInfo> Conflicts { get; set; } = new();
}

public class PendingChanges
{
    public List<DocumentDelta> NewDocuments { get; set; } = new();
    public List<DocumentDelta> ModifiedDocuments { get; set; } = new();
    public List<DeletedDocument> DeletedDocuments { get; set; } = new();
    
    public bool HasChanges => NewDocuments.Any() || ModifiedDocuments.Any() || DeletedDocuments.Any();
    public int TotalChanges => NewDocuments.Count + ModifiedDocuments.Count + DeletedDocuments.Count;
}

public enum SyncStatus { Completed, NoChanges, Failed, Conflicts }
public enum MergeSyncStatus { Completed, Failed, ConflictsDetected }
```

---

## 6. Implementation Steps

### Phase 1: Core Infrastructure (Week 1)

#### Step 1.1: Project Setup

```bash
# Create project structure
mkdir -p VmRagMcp/Services
mkdir -p VmRagMcp/Models
mkdir -p VmRagMcp/Configuration
mkdir -p VmRagMcp/McpTools
```

```xml
<!-- VmRagMcp.csproj additions -->
<ItemGroup>
    <PackageReference Include="CliWrap" Version="3.6.6" />
    <PackageReference Include="System.Text.Json" Version="8.0.4" />
</ItemGroup>
```

#### Step 1.2: Implement DoltCli class

1. Create `Services/DoltCli.cs` with the implementation from Section 3.3
2. Create `Configuration/DoltConfiguration.cs`:

```csharp
public class DoltConfiguration
{
    public string DoltExecutablePath { get; set; } = "dolt";
    public string RepositoryPath { get; set; } = "./data/dolt-repo";
    public string RemoteName { get; set; } = "origin";
    public string RemoteUrl { get; set; }
}
```

#### Step 1.3: Implement DeltaDetector

Create `Services/DeltaDetector.cs` with the implementation from Section 4.2

#### Step 1.4: Implement SyncManager

Create `Services/SyncManager.cs` with the implementation from Section 5.2

### Phase 2: MCP Tool Integration (Week 2)

#### Step 2.1: Add Dolt Tools to MCP Server

```csharp
// File: McpTools/DoltTools.cs
public class DoltTools
{
    private readonly IDoltCli _dolt;
    private readonly ISyncManager _syncManager;

    public DoltTools(IDoltCli dolt, ISyncManager syncManager)
    {
        _dolt = dolt;
        _syncManager = syncManager;
    }

    [McpTool("dolt_status", "Get repository status and pending sync changes")]
    public async Task<ToolResult> StatusAsync()
    {
        var branch = await _dolt.GetCurrentBranchAsync();
        var commit = await _dolt.GetHeadCommitHashAsync();
        var status = await _dolt.GetStatusAsync();
        var pending = await _syncManager.GetPendingChangesAsync();

        return new ToolResult
        {
            Content = new
            {
                branch,
                commit,
                hasUncommittedChanges = status.HasStagedChanges || status.HasUnstagedChanges,
                pendingSync = new
                {
                    newDocuments = pending.NewDocuments.Count,
                    modifiedDocuments = pending.ModifiedDocuments.Count,
                    deletedDocuments = pending.DeletedDocuments.Count
                }
            }
        };
    }

    [McpTool("dolt_commit", "Commit changes and sync to ChromaDB")]
    public async Task<ToolResult> CommitAsync(
        [McpParameter("message", "Commit message", required: true)] string message,
        [McpParameter("sync", "Sync to ChromaDB after commit", required: false)] bool sync = true)
    {
        var result = await _syncManager.ProcessCommitAsync(message, sync);

        return new ToolResult
        {
            Content = new
            {
                success = result.Status == SyncStatus.Completed,
                commitHash = result.CommitHash,
                synced = new
                {
                    added = result.Added,
                    modified = result.Modified,
                    deleted = result.Deleted
                }
            }
        };
    }

    [McpTool("dolt_push", "Push current branch to DoltHub")]
    public async Task<ToolResult> PushAsync(
        [McpParameter("remote", "Remote name", required: false)] string remote = "origin")
    {
        var branch = await _dolt.GetCurrentBranchAsync();
        var result = await _dolt.PushAsync(remote, branch);

        return new ToolResult
        {
            Content = new
            {
                success = result.Success,
                branch,
                remote,
                message = result.Success ? "Push successful" : result.Error
            }
        };
    }

    [McpTool("dolt_pull", "Pull from DoltHub and sync to ChromaDB")]
    public async Task<ToolResult> PullAsync(
        [McpParameter("remote", "Remote name", required: false)] string remote = "origin")
    {
        var result = await _syncManager.ProcessPullAsync(remote);

        return new ToolResult
        {
            Content = new
            {
                success = result.Status == SyncStatus.Completed,
                status = result.Status.ToString(),
                wasFastForward = result.WasFastForward,
                synced = new
                {
                    added = result.Added,
                    modified = result.Modified,
                    deleted = result.Deleted
                }
            }
        };
    }

    [McpTool("dolt_checkout", "Switch branch and load ChromaDB collection")]
    public async Task<ToolResult> CheckoutAsync(
        [McpParameter("branch", "Branch name", required: true)] string branch,
        [McpParameter("create", "Create new branch", required: false)] bool create = false)
    {
        var result = await _syncManager.ProcessCheckoutAsync(branch, create);

        return new ToolResult
        {
            Content = new
            {
                success = result.Status == SyncStatus.Completed,
                branch,
                created = create,
                synced = new
                {
                    added = result.Added,
                    modified = result.Modified,
                    deleted = result.Deleted
                }
            }
        };
    }

    [McpTool("dolt_merge", "Merge branch into current branch")]
    public async Task<ToolResult> MergeAsync(
        [McpParameter("source_branch", "Branch to merge from", required: true)] string sourceBranch)
    {
        var result = await _syncManager.ProcessMergeAsync(sourceBranch);

        if (result.HasConflicts)
        {
            return new ToolResult
            {
                Content = new
                {
                    success = false,
                    hasConflicts = true,
                    conflicts = result.Conflicts.Select(c => new
                    {
                        table = c.TableName,
                        rowId = c.RowId
                    })
                }
            };
        }

        return new ToolResult
        {
            Content = new
            {
                success = result.Status == MergeSyncStatus.Completed,
                sourceBranch,
                synced = new
                {
                    added = result.Added,
                    modified = result.Modified,
                    deleted = result.Deleted
                }
            }
        };
    }

    [McpTool("dolt_log", "Get commit history")]
    public async Task<ToolResult> LogAsync(
        [McpParameter("limit", "Number of commits", required: false)] int limit = 10)
    {
        var commits = await _dolt.GetLogAsync(limit);

        return new ToolResult
        {
            Content = commits.Select(c => new
            {
                hash = c.Hash,
                message = c.Message,
                author = c.Author,
                date = c.Date
            })
        };
    }

    [McpTool("dolt_branches", "List all branches")]
    public async Task<ToolResult> BranchesAsync()
    {
        var branches = await _dolt.ListBranchesAsync();

        return new ToolResult
        {
            Content = branches.Select(b => new
            {
                name = b.Name,
                current = b.IsCurrent,
                lastCommit = b.LastCommitHash
            })
        };
    }

    [McpTool("dolt_diff", "Show changes between commits")]
    public async Task<ToolResult> DiffAsync(
        [McpParameter("from_commit", "Starting commit", required: true)] string fromCommit,
        [McpParameter("to_commit", "Ending commit (default: HEAD)", required: false)] string toCommit = "HEAD",
        [McpParameter("table", "Specific table to diff", required: false)] string table = null)
    {
        var tables = table != null 
            ? new[] { table } 
            : new[] { "issue_logs", "knowledge_docs" };

        var allDiffs = new List<object>();
        foreach (var t in tables)
        {
            var diffs = await _dolt.GetTableDiffAsync(fromCommit, toCommit, t);
            allDiffs.AddRange(diffs.Select(d => new
            {
                table = t,
                type = d.DiffType,
                sourceId = d.SourceId
            }));
        }

        return new ToolResult { Content = allDiffs };
    }

    [McpTool("dolt_reset", "Reset to a specific commit (warning: destructive)")]
    public async Task<ToolResult> ResetAsync(
        [McpParameter("commit", "Commit hash to reset to", required: true)] string commit)
    {
        var result = await _syncManager.ProcessResetAsync(commit);

        return new ToolResult
        {
            Content = new
            {
                success = result.Status == SyncStatus.Completed,
                resetTo = commit,
                chromaRegenerated = true,
                documentsInCollection = result.Added
            }
        };
    }
}
```

### Phase 3: Testing (Week 3)

Implement acceptance tests as defined in Section 7.

### Phase 4: DoltHub Integration (Week 4)

1. Create DoltHub account and repository
2. Configure remote in your local Dolt repo
3. Test push/pull workflows
4. Document team onboarding process

---

## 7. Acceptance Tests (Gherkin BDD)

### T1: Copy RAG Data Across DoltHub Test

```gherkin
Feature: Copy RAG Data Across DoltHub
  As a developer
  I want to copy RAG data from one project to another via DoltHub
  So that I can share knowledge bases between team members

  Background:
    Given DoltHub remote "testorg/vmrag-test" exists
    And the remote database has the standard VM RAG schema

  @T1 @copy @dolthub
  Scenario: Full database copy via DoltHub
    # Step 1: Create and populate source ChromaDB
    Given a new VM RAG MCP server instance "source-project"
    And the Dolt database is initialized at "./source-db"
    And the ChromaDB is initialized at "./source-chroma"
    
    # Step 2: Fill with test data
    When I add the following issue logs via MCP:
      | project_id | issue_number | title              | content                                    | log_type       |
      | proj-001   | 101          | Auth Bug Fix       | Fixed JWT validation timeout issue...      | implementation |
      | proj-001   | 102          | Performance Tuning | Optimized database queries by adding...    | resolution     |
      | proj-001   | 103          | API Refactor       | Restructured the REST endpoints to...      | investigation  |
    And I add the following knowledge docs via MCP:
      | category | tool_name     | title                 | content                              |
      | api      | EntityFramework| EF Core Migrations   | Database migrations allow you to...   |
      | tooling  | Docker        | Container Best Practices | When containerizing .NET apps...   |
    And I call "dolt_commit" with message "Initial test data"
    Then the commit should succeed
    And ChromaDB should contain 5 documents total
    
    # Step 3: Push to DoltHub
    When I configure remote "origin" as "testorg/vmrag-test"
    And I call "dolt_push" with remote "origin"
    Then the push should succeed
    And DoltHub should contain 3 issue_logs records
    And DoltHub should contain 2 knowledge_docs records

    # Step 4: Pull into empty project
    Given a new VM RAG MCP server instance "target-project"
    And the Dolt database is empty at "./target-db"
    And the ChromaDB is empty at "./target-chroma"
    When I run "dolt clone testorg/vmrag-test ./target-db"
    And I initialize VM RAG MCP server for "./target-db"
    And I call "dolt_checkout" with branch "main"
    Then ChromaDB sync should complete
    
    # Step 5: Verify data integrity
    Then the target Dolt database should contain 3 issue_logs records
    And the target Dolt database should contain 2 knowledge_docs records
    And the target ChromaDB should have 5 documents

    # Step 6: Validate query equivalence
    When I search "JWT authentication timeout" in source-project
    And I search "JWT authentication timeout" in target-project
    Then the search results should return the same document IDs
    And the relevance scores should differ by less than 0.01

  @T1 @copy @content-hash
  Scenario: Verify content hash integrity after copy
    Given source-project has document with:
      | field        | value                          |
      | log_id       | log-test-001                   |
      | content      | This is test content for hash  |
      | content_hash | <calculated SHA-256>           |
    When the document is copied to target-project via DoltHub
    Then target-project should have document "log-test-001"
    And the content_hash should match exactly
    And the content should be byte-for-byte identical
```

### T2: Fast-Forward RAG Data Across DoltHub Test

```gherkin
Feature: Fast-Forward RAG Data Sync
  As a developer
  I want incremental updates to sync efficiently via DoltHub
  So that only changed documents are re-embedded

  Background:
    Given DoltHub remote "testorg/vmrag-test" exists with initial data
    And source-project has VM RAG MCP server connected to the remote
    And target-project has VM RAG MCP server connected to same remote
    And both projects are synced to the same commit "initial-commit"

  @T2 @fastforward @change-detection
  Scenario: Detect and sync incremental changes
    # Step 1: Update data in source project
    Given source-project ChromaDB has 5 documents in collection "vmrag_main"
    When I update issue log "log-001" in source-project:
      | field   | new_value                        |
      | content | Updated investigation notes...   |
    And I add new issue log in source-project:
      | log_id   | project_id | issue_number | content                    |
      | log-006  | proj-001   | 106          | New feature implementation |
    And I delete issue log "log-003" in source-project
    
    # Step 2: Verify change detection
    When I call "dolt_status" in source-project
    Then the response should show pending changes:
      | change_type | count |
      | new         | 1     |
      | modified    | 1     |
      | deleted     | 1     |
    And sync_status should be "pending"

    # Step 3: Commit and push changes
    When I call "dolt_commit" with message "Sprint 5 updates"
    Then the commit should succeed
    And sync result should show:
      | metric   | value |
      | added    | 1     |
      | modified | 1     |
      | deleted  | 1     |
    When I call "dolt_push"
    Then the push should succeed

    # Step 4: Pull in secondary project
    Given target-project last sync commit is "initial-commit"
    When I call "dolt_pull" in target-project
    Then the pull should succeed
    And the response should indicate "wasFastForward": true
    And sync result should show:
      | metric   | value |
      | added    | 1     |
      | modified | 1     |
      | deleted  | 1     |
    
    # Step 5: Validate new data
    Then target-project should have 5 documents (5 - 1 + 1)
    When I search "Updated investigation" in target-project
    Then results should include document "log-001"
    When I search content from "log-003" in target-project
    Then results should NOT include document "log-003"
    When I search "New feature implementation" in target-project
    Then results should include document "log-006"

  @T2 @fastforward @no-changes
  Scenario: No sync when already up-to-date
    Given source-project and target-project are on same commit
    When I call "dolt_pull" in target-project
    Then the response should show:
      | field  | value      |
      | status | NoChanges  |
      | added  | 0          |
    And no ChromaDB operations should be performed
```

### T3: Merge RAG Data Across DoltHub Test

```gherkin
Feature: Merge RAG Data Between Branches
  As a team lead
  I want to merge branch changes into main
  So that feature work is integrated into the shared knowledge base

  Background:
    Given DoltHub remote "testorg/vmrag-test" exists
    And project-A has VM RAG MCP server on branch "main"
    And project-B has VM RAG MCP server on branch "main"
    And both are synced to initial commit

  @T3 @merge @parallel-changes
  Scenario: Merge parallel branch changes without conflicts
    # Step 1A: Project A creates branch and adds document A
    When I call "dolt_checkout" in project-A with:
      | branch | create |
      | feature/auth | true |
    And I add issue log in project-A:
      | log_id   | project_id | issue_number | title           | content                        |
      | log-A01  | proj-001   | 201          | Auth Enhancement| Implemented OAuth2 flow for... |
    And I call "dolt_commit" in project-A with message "Added OAuth2 notes"
    And I call "dolt_push" in project-A

    # Step 1B: Project B creates branch and adds document B  
    When I call "dolt_checkout" in project-B with:
      | branch | create |
      | feature/db-opt | true |
    And I add issue log in project-B:
      | log_id   | project_id | issue_number | title           | content                        |
      | log-B01  | proj-001   | 202          | DB Optimization | Added indexes for query perf...|
    And I call "dolt_commit" in project-B with message "Added DB optimization notes"
    And I call "dolt_push" in project-B

    # Step 2: Merge branch B into branch A (in project A)
    When I run "dolt fetch origin" in project-A
    And I run "dolt checkout feature/auth" in project-A
    And I call "dolt_merge" in project-A with source_branch "origin/feature/db-opt"
    Then the merge should succeed without conflicts
    And the response should show:
      | field      | value |
      | success    | true  |
      | hasConflicts | false |
    
    # Step 3: Validate merged content
    Then project-A Dolt should have both documents:
      | log_id   | exists |
      | log-A01  | true   |
      | log-B01  | true   |
    And project-A ChromaDB should have both documents embedded
    
    When I search "OAuth2 authentication" in project-A
    Then results should include document "log-A01"
    When I search "database index optimization" in project-A
    Then results should include document "log-B01"

  @T3 @merge @conflict-resolution
  Scenario: Merge with conflict resolution
    # Setup: Both projects modify the same document
    Given document "shared-doc-001" exists on branch "main"
    
    When project-A creates branch "feature/update-a" from main
    And project-A updates "shared-doc-001" content to "Version A: Auth updated..."
    And project-A commits with message "Update A"
    
    And project-B creates branch "feature/update-b" from main
    And project-B updates "shared-doc-001" content to "Version B: Auth refactored..."
    And project-B commits with message "Update B"
    
    # Attempt merge
    When project-A checks out "feature/update-a"
    And project-A fetches "feature/update-b" from origin
    And I call "dolt_merge" in project-A with source_branch "origin/feature/update-b"
    
    Then the response should show:
      | field        | value |
      | success      | false |
      | hasConflicts | true  |
    And conflicts should list:
      | table      | row_id          |
      | issue_logs | shared-doc-001  |
    
    # Resolve conflict
    When I run "dolt conflicts resolve --ours issue_logs" in project-A
    And I call "dolt_commit" with message "Resolved: kept our version"
    
    Then the document "shared-doc-001" should contain "Version A: Auth updated"
    And ChromaDB should have updated embedding for "shared-doc-001"

  @T3 @merge @query-validation
  Scenario: Query merged content from multiple sources
    Given project-A has merged both feature branches
    And project-A ChromaDB is synced
    
    When I search "How did we handle authentication?" in project-A
    Then results should be ranked by semantic similarity
    And results should include contributions from both:
      | source_branch     | document |
      | feature/auth      | log-A01  |
      | feature/db-opt    | log-B01  |
```

---

## 8. Additional Test Scenarios

### Scenario: Branch Isolation

```gherkin
Feature: Branch Isolation in ChromaDB
  Branches should have isolated ChromaDB collections

  @isolation @branch
  Scenario: Changes on feature branch don't affect main
    Given I am on branch "main" with 5 documents
    And ChromaDB collection "vmrag_main" has 5 documents
    
    When I call "dolt_checkout" with branch "feature/test" and create true
    Then ChromaDB should have new collection "vmrag_feature-test"
    And collection "vmrag_feature-test" should have 5 documents (copied from main)
    
    When I add 3 new documents on branch "feature/test"
    And I call "dolt_commit" with message "Feature changes"
    Then collection "vmrag_feature-test" should have 8 documents
    And collection "vmrag_main" should still have 5 documents
    
    When I call "dolt_checkout" with branch "main"
    And I search for content from new documents
    Then results should NOT include the new documents

  @isolation @checkout-switch
  Scenario: Checkout switches active collection
    Given branch "main" collection has document with content "Main branch content"
    And branch "feature" collection has document with content "Feature branch content"
    
    When I am on branch "main"
    And I search "branch content"
    Then top result should contain "Main branch content"
    
    When I call "dolt_checkout" with branch "feature"
    And I search "branch content"  
    Then top result should contain "Feature branch content"
```

### Scenario: Reset and Recovery

```gherkin
Feature: Reset and Recovery
  Support for reverting to previous states

  @reset @hard
  Scenario: Hard reset regenerates ChromaDB
    Given commit history: A -> B -> C (HEAD)
    And commit A had 3 documents
    And commit C has 7 documents
    And ChromaDB has 7 documents
    
    When I call "dolt_reset" with commit "A"
    Then Dolt HEAD should be at commit A
    And ChromaDB should be regenerated
    And ChromaDB should have 3 documents
    And documents from commits B and C should NOT exist in ChromaDB

  @reset @recovery  
  Scenario: Recover from failed sync
    Given a sync operation failed midway
    And sync_operations shows status "failed"
    And ChromaDB is out of sync with Dolt
    
    When I call "dolt_checkout" with branch "main"
    Then system should detect sync mismatch
    And system should perform full resync
    And ChromaDB should match Dolt state
    And sync_status should become "synced"
```

### Scenario: Embedding Model Consistency

```gherkin
Feature: Embedding Model Consistency
  Ensure embedding model changes are handled properly

  @embedding @model-mismatch
  Scenario: Detect embedding model mismatch
    Given ChromaDB collection was created with model "text-embedding-ada-002"
    And chroma_sync_state shows embedding_model "text-embedding-ada-002"
    When system is configured to use model "text-embedding-3-small"
    And I attempt to sync new documents
    Then a warning should be logged about model mismatch
    And new documents should NOT be embedded
    And user should be notified to:
      | option     | description                    |
      | regenerate | Full re-embed with new model   |
      | revert     | Use original model             |

  @embedding @regeneration
  Scenario: Full regeneration on model change
    Given I confirm regeneration with new model
    When regeneration starts
    Then all existing chunks should be deleted
    And all documents should be re-chunked  
    And all chunks should be re-embedded with "text-embedding-3-small"
    And chroma_sync_state should show new embedding_model
```

### Scenario: Cross-Clone Consistency

```gherkin
Feature: Cross-Clone Data Consistency
  Multiple clones should have identical query results when synced to same commit

  Background:
    Given DoltHub remote "testorg/vmrag-shared" exists
    And the remote has 5 issue_logs and 3 knowledge_docs
    And all documents were committed at commit "abc123"

  @consistency @clone-sync
  Scenario: Two fresh clones produce identical ChromaDB state
    # Clone A setup
    Given developer A clones the repository to "./clone-a"
    And developer A initializes VM RAG MCP server
    When developer A's server syncs ChromaDB
    Then clone-a ChromaDB should have collection "vmrag_main"
    And clone-a should have 8 documents synced
    
    # Clone B setup (completely independent)
    Given developer B clones the repository to "./clone-b"
    And developer B initializes VM RAG MCP server
    When developer B's server syncs ChromaDB
    Then clone-b ChromaDB should have collection "vmrag_main"
    And clone-b should have 8 documents synced
    
    # Validation
    Then clone-a and clone-b should have identical:
      | property                  | match_type        |
      | document_sync_log entries | exact             |
      | ChromaDB chunk IDs        | exact             |
      | ChromaDB chunk content    | exact             |
      | chroma_sync_state         | exact             |
    
    When developer A searches "authentication bug fix"
    And developer B searches "authentication bug fix"
    Then both should return the same documents in same order
    And distance scores should differ by less than 0.001

  @consistency @content-hash-validation
  Scenario: Content hash validates document integrity
    Given clone-a has document "log-001" with content_hash "sha256_abc..."
    When clone-b syncs from DoltHub
    Then clone-b document "log-001" should have content_hash "sha256_abc..."
    And clone-b ChromaDB metadata for "log-001" chunks should have:
      | field        | value           |
      | content_hash | sha256_abc...   |
      | source_id    | log-001         |

  @consistency @chunk-determinism
  Scenario: Chunking produces identical results across clones
    Given document "log-002" has content of 1500 characters
    And chunk_size is 512 with overlap 50
    
    When clone-a chunks and syncs the document
    Then clone-a should create chunks:
      | chunk_id         | char_range  |
      | log-002_chunk_0  | 0-512       |
      | log-002_chunk_1  | 462-974     |
      | log-002_chunk_2  | 924-1436    |
      | log-002_chunk_3  | 1386-1500   |
    
    When clone-b chunks and syncs the same document
    Then clone-b should create identical chunk IDs and ranges
    And chunk text content should be byte-for-byte identical

  @consistency @sync-state-tracking
  Scenario: Sync state enables consistency verification
    Given clone-a synced at commit "abc123" 
    And clone-a chroma_sync_state shows:
      | field            | value          |
      | last_sync_commit | abc123         |
      | document_count   | 8              |
      | chunk_count      | 24             |
      | embedding_model  | text-embedding-3-small |
    
    When clone-b syncs from the same commit
    Then clone-b chroma_sync_state should show identical values
    And validation check should pass with no issues

  @consistency @model-mismatch-prevention
  Scenario: Prevent mixing embeddings from different models
    Given clone-a used embedding model "text-embedding-ada-002"
    And clone-a chroma_sync_state shows embedding_model "text-embedding-ada-002"
    
    When clone-b is configured with model "text-embedding-3-small"
    And clone-b attempts to sync
    Then clone-b should detect model mismatch
    And clone-b should NOT add new embeddings
    And clone-b should prompt for resolution:
      | option              | result                          |
      | full_regeneration   | Delete all, re-embed with new   |
      | use_existing_model  | Switch config to ada-002        |
      | abort               | Cancel sync operation           |

  @consistency @after-pull
  Scenario: Consistency maintained after pull with changes
    Given clone-a and clone-b are both synced to commit "abc123"
    
    When clone-a adds a new document and commits "def456"
    And clone-a pushes to DoltHub
    
    When clone-b pulls from DoltHub
    And clone-b syncs ChromaDB
    Then clone-b should be at commit "def456"
    And clone-b should have the new document
    And clone-b query results should match clone-a for:
      | query                    | expected_top_result |
      | "authentication bug"     | same document ID    |
      | "new document content"   | the new document    |
```

### Scenario: Error Handling

```gherkin
Feature: Error Handling and Audit Logging
  Comprehensive error handling and operation logging

  @error @network
  Scenario: DoltHub network failure during push
    Given I have committed local changes
    When I call "dolt_push"
    And the network connection fails
    Then the response should show:
      | field   | value                   |
      | success | false                   |
      | message | Network connection lost |
    And local Dolt state should remain unchanged
    And ChromaDB should remain unchanged
    And sync_operations should log:
      | field          | value  |
      | operation_type | push   |
      | status         | failed |

  @audit @complete-trail
  Scenario: All operations create audit trail
    When I perform operations:
      | operation | parameters                  |
      | commit    | message: "Test"             |
      | push      | remote: origin              |
      | pull      | remote: origin              |
    Then sync_operations should have 3 entries
    And each entry should have all required fields:
      | field               | required |
      | operation_id        | yes      |
      | operation_type      | yes      |
      | dolt_branch         | yes      |
      | dolt_commit_before  | yes      |
      | dolt_commit_after   | yes      |
      | started_at          | yes      |
      | completed_at        | yes      |
      | operation_status    | yes      |
```

### Scenario: Large Dataset Performance

```gherkin
Feature: Large Dataset Performance
  Handle larger datasets efficiently

  @performance @batch-sync
  Scenario: Batch processing for large changesets
    Given I have 500 new documents to sync
    When I call "dolt_commit" with sync enabled
    Then documents should be processed in batches
    And each batch should be logged
    And total sync time should be under 5 minutes
    And memory usage should remain stable

  @performance @incremental
  Scenario: Incremental sync is efficient
    Given ChromaDB has 10,000 existing documents
    And I add 5 new documents to Dolt
    When I call "dolt_commit"
    Then only 5 documents should be embedded
    And DOLT_DIFF should be used (not full table scan)
    And sync should complete in under 10 seconds
```

---

## Appendix A: Configuration Schema

```json
{
  "Dolt": {
    "ExecutablePath": "dolt",
    "RepositoryPath": "./data/dolt-repo",
    "Remote": {
      "Name": "origin",
      "Url": "https://doltremoteapi.dolthub.com/yourorg/yourrepo"
    }
  },
  "ChromaDb": {
    "PersistDirectory": "./data/chroma-db",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "Sync": {
    "AutoSyncOnCommit": true,
    "BatchSize": 50,
    "ChunkSize": 512,
    "ChunkOverlap": 50
  }
}
```

---

## Appendix B: Quick Reference - Dolt CLI Commands

```bash
# ==================== Repository Setup ====================
dolt init                              # Initialize new repo
dolt clone <remote-url>                # Clone from DoltHub
dolt remote add origin <url>           # Add remote

# ==================== Branch Operations ====================
dolt branch                            # List branches
dolt branch <name>                     # Create branch
dolt branch -d <name>                  # Delete branch
dolt checkout <branch>                 # Switch branch
dolt checkout -b <branch>              # Create and switch

# ==================== Commit Operations ====================
dolt add -A                            # Stage all changes
dolt commit -m "<message>"             # Commit staged changes
dolt log --oneline -n 10               # Show history

# ==================== Remote Operations ====================
dolt push origin <branch>              # Push to remote
dolt pull origin <branch>              # Pull from remote
dolt fetch origin                      # Fetch without merge

# ==================== Merge Operations ====================
dolt merge <branch>                    # Merge branch
dolt conflicts cat <table>             # Show conflicts
dolt conflicts resolve --ours <table>  # Resolve with ours
dolt conflicts resolve --theirs <table># Resolve with theirs

# ==================== Diff and Status ====================
dolt status                            # Show working status
dolt diff                              # Show uncommitted changes
dolt diff <from> <to>                  # Diff between commits

# ==================== Reset ====================
dolt reset --hard <commit>             # Hard reset
dolt reset --soft HEAD~1               # Soft reset

# ==================== SQL Queries via CLI ====================
dolt sql -q "SELECT * FROM table" -r json          # Query as JSON
dolt sql -q "SELECT active_branch()"               # Get current branch
dolt sql -q "SELECT DOLT_HASHOF('HEAD')"          # Get HEAD hash
dolt sql -q "SELECT * FROM DOLT_DIFF(...)" -r json # Get structured diff
dolt sql -q "INSERT INTO table (...) VALUES (...)" # Insert data
dolt sql -q "UPDATE table SET ... WHERE ..."       # Update data
dolt sql -q "DELETE FROM table WHERE ..."          # Delete data
```

---

## Appendix C: JSON Output Format

When using `-r json` flag, Dolt returns results in this format:

```json
{
  "rows": [
    {
      "column1": "value1",
      "column2": "value2"
    },
    {
      "column1": "value3",
      "column2": "value4"
    }
  ]
}
```

Example parsing in C#:

```csharp
var json = await ExecuteSqlJsonAsync("SELECT log_id, content FROM issue_logs LIMIT 2");
// json = {"rows":[{"log_id":"abc","content":"..."},{"log_id":"def","content":"..."}]}

var result = JsonSerializer.Deserialize<JsonElement>(json);
foreach (var row in result.GetProperty("rows").EnumerateArray())
{
    var logId = row.GetProperty("log_id").GetString();
    var content = row.GetProperty("content").GetString();
}
```
