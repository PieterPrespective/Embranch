# PP13-79: Git-Synchronized DMMS Initialization - Technical Design

## Date: 2026-01-18
## Type: Feature Enhancement - Repository/Branch/Commit Targeting
## Priority: High

---

## Problem Analysis

### Current State

1. **DMMS State Tracking:**
   - SQLite `deletion_tracking.db` tracks sync state per collection/branch
   - `sync_state.LastSyncCommit` links ChromaDB collections to Dolt commits
   - Branch-aware tracking exists within Dolt context only

2. **Missing Functionality:**
   - No stored reference to what **Git commit** the DMMS state corresponds to
   - No mechanism to initialize DMMS to a specific Dolt branch/commit at startup
   - The `repository` field in MCP settings (`.mcp.json`) is unused
   - When cloning a Git repo with DMMS, ChromaDB/Dolt state doesn't match Git history

3. **User Scenario - The Problem:**
   ```
   User A: Commits DMMS project at Git commit abc123
           - DMMS has issue logs PP13-50 through PP13-78
           - Dolt is at commit xyz789 on branch "main"
           - ChromaDB has matching collections

   User B: Clones the Git repo
           - Gets Git commit abc123
           - But DMMS initializes fresh (empty state)
           - Issue logs don't match the code state
           - Must manually import/sync to recover state
   ```

### Root Cause

DMMS stores its state in:
- `./chroma_data/` - ChromaDB persistent data (not Git-tracked)
- `./data/dolt-repo/` - Dolt repository (could be Git-tracked but isn't by default)
- `./data/dev/deletion_tracking.db` - SQLite tracking (not Git-tracked)

**None of these are Git-versioned**, so the DMMS state is lost when cloning.

---

## Proposed Solution: DMMS State Manifest File

### Core Concept

Create a **Git-tracked manifest file** (`.dmms/state.json`) that records:
1. The Dolt remote URL and branch/commit for the current state
2. The mapping between Git commits and Dolt commits
3. Configuration for automatic initialization

When DMMS starts, it reads this manifest and:
1. Clones/fetches the Dolt repository if needed
2. Checks out the specified Dolt branch/commit
3. Syncs ChromaDB to match the Dolt state

### Manifest File Structure

**Location:** `.dmms/state.json` (Git-tracked)

```json
{
  "$schema": "https://dmms.dev/schemas/state.v1.json",
  "version": "1.0",
  "dolt": {
    "remote_url": "dolthub.com/organization/repo-name",
    "default_branch": "main",
    "current_commit": "abc123def456...",
    "current_branch": "main"
  },
  "git_mapping": {
    "enabled": true,
    "last_git_commit": "fedcba987654...",
    "dolt_commit_at_git_commit": "abc123def456..."
  },
  "initialization": {
    "mode": "auto",
    "on_clone": "sync_to_manifest",
    "on_branch_change": "preserve_local"
  },
  "collections": {
    "tracked": ["ProjectDevelopmentLog", "PP13-*"],
    "excluded": ["test-*", "temp-*"]
  },
  "updated_at": "2026-01-18T10:30:00Z",
  "updated_by": "user@example.com"
}
```

### Initialization Modes

| Mode | Behavior |
|------|----------|
| `auto` | Automatically sync on startup if manifest differs from local state |
| `prompt` | Ask user before syncing on startup |
| `manual` | Only sync when explicitly requested |
| `disabled` | Never auto-sync; use local state only |

### On-Clone Behaviors

| Behavior | Description |
|----------|-------------|
| `sync_to_manifest` | Clone Dolt repo and checkout specified commit |
| `sync_to_latest` | Clone Dolt repo and checkout latest on default branch |
| `empty` | Start with empty DMMS state (current behavior) |
| `prompt` | Ask user what to do |

---

## Architecture

### New Components

#### 1. `IDmmsStateManifest` Interface

```csharp
public interface IDmmsStateManifest
{
    /// <summary>
    /// Reads the state manifest from the project directory
    /// </summary>
    Task<DmmsManifest?> ReadManifestAsync(string projectPath);

    /// <summary>
    /// Writes/updates the state manifest
    /// </summary>
    Task WriteManifestAsync(string projectPath, DmmsManifest manifest);

    /// <summary>
    /// Checks if manifest exists in the project
    /// </summary>
    Task<bool> ManifestExistsAsync(string projectPath);

    /// <summary>
    /// Updates the Dolt commit reference in the manifest
    /// </summary>
    Task UpdateDoltCommitAsync(string projectPath, string commitHash, string branch);

    /// <summary>
    /// Records Git-Dolt commit mapping after a Dolt commit
    /// </summary>
    Task RecordGitMappingAsync(string projectPath, string gitCommit, string doltCommit);
}
```

#### 2. `IDmmsInitializer` Interface

```csharp
public interface IDmmsInitializer
{
    /// <summary>
    /// Initializes DMMS state based on manifest and current state
    /// </summary>
    Task<InitializationResult> InitializeFromManifestAsync(DmmsManifest manifest);

    /// <summary>
    /// Checks if initialization is needed based on manifest vs current state
    /// </summary>
    Task<InitializationCheck> CheckInitializationNeededAsync(DmmsManifest manifest);

    /// <summary>
    /// Syncs local DMMS state to match a specific Dolt commit
    /// </summary>
    Task<SyncResult> SyncToCommitAsync(string doltCommit, string? branch = null);
}
```

#### 3. `IGitIntegration` Interface

```csharp
public interface IGitIntegration
{
    /// <summary>
    /// Gets the current Git commit hash (HEAD)
    /// </summary>
    Task<string?> GetCurrentGitCommitAsync(string repoPath);

    /// <summary>
    /// Checks if the current directory is inside a Git repository
    /// </summary>
    Task<bool> IsGitRepositoryAsync(string path);

    /// <summary>
    /// Gets the Git root directory from a path inside the repo
    /// </summary>
    Task<string?> GetGitRootAsync(string path);
}
```

### New Models

```csharp
public record DmmsManifest
{
    public string Version { get; init; } = "1.0";
    public DoltManifestConfig Dolt { get; init; } = new();
    public GitMappingConfig GitMapping { get; init; } = new();
    public InitializationConfig Initialization { get; init; } = new();
    public CollectionConfig Collections { get; init; } = new();
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

public record DoltManifestConfig
{
    public string? RemoteUrl { get; init; }
    public string DefaultBranch { get; init; } = "main";
    public string? CurrentCommit { get; init; }
    public string? CurrentBranch { get; init; }
}

public record GitMappingConfig
{
    public bool Enabled { get; init; } = true;
    public string? LastGitCommit { get; init; }
    public string? DoltCommitAtGitCommit { get; init; }
}

public record InitializationConfig
{
    public string Mode { get; init; } = "auto";
    public string OnClone { get; init; } = "sync_to_manifest";
    public string OnBranchChange { get; init; } = "preserve_local";
}

public record CollectionConfig
{
    public List<string> Tracked { get; init; } = new();
    public List<string> Excluded { get; init; } = new();
}

public record InitializationResult
{
    public bool Success { get; init; }
    public InitializationAction ActionTaken { get; init; }
    public string? DoltCommit { get; init; }
    public string? DoltBranch { get; init; }
    public int CollectionsSynced { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum InitializationAction
{
    None,
    ClonedAndSynced,
    FetchedAndSynced,
    CheckedOutBranch,
    CheckedOutCommit,
    SyncedExisting,
    Skipped,
    Failed
}

public record InitializationCheck
{
    public bool NeedsInitialization { get; init; }
    public string Reason { get; init; } = "";
    public string? CurrentDoltCommit { get; init; }
    public string? ManifestDoltCommit { get; init; }
    public string? CurrentBranch { get; init; }
    public string? ManifestBranch { get; init; }
}
```

---

## Integration with Existing Configuration

### Enhanced DoltConfiguration

```csharp
public class DoltConfiguration
{
    // Existing fields...
    public string DoltExecutablePath { get; set; } = "dolt";
    public string RepositoryPath { get; set; } = "./data/dolt-repo";
    public string RemoteName { get; set; } = "origin";
    public string? RemoteUrl { get; set; }
    public int CommandTimeoutMs { get; set; } = 30000;
    public bool EnableDebugLogging { get; set; } = false;

    // NEW: Startup targeting
    public string? TargetBranch { get; set; }      // Branch to checkout on startup
    public string? TargetCommit { get; set; }      // Commit to checkout on startup
    public bool UseManifest { get; set; } = true;  // Whether to read .dmms/state.json
}
```

### Enhanced ServerConfiguration

```csharp
public class ServerConfiguration
{
    // Existing fields...

    // NEW: Project root detection
    public string? ProjectRoot { get; set; }       // Git root or explicit path
    public bool AutoDetectProjectRoot { get; set; } = true;
}
```

### Environment Variables

```bash
# Existing
DOLT_REPOSITORY_PATH=./data/dolt-repo
DOLT_REMOTE_URL=dolthub.com/org/repo

# NEW: Startup targeting
DMMS_TARGET_BRANCH=main
DMMS_TARGET_COMMIT=abc123def456
DMMS_USE_MANIFEST=true
DMMS_PROJECT_ROOT=/path/to/git/repo
DMMS_INIT_MODE=auto  # auto, prompt, manual, disabled
```

---

## Startup Initialization Flow

```
Program.cs Startup
        │
        ▼
┌───────────────────────────┐
│ 1. Load Configuration     │
│    (env vars, settings)   │
└───────────────────────────┘
        │
        ▼
┌───────────────────────────┐
│ 2. Detect Project Root    │
│    (Git root or explicit) │
└───────────────────────────┘
        │
        ▼
┌───────────────────────────┐
│ 3. Check for Manifest     │
│    (.dmms/state.json)     │
└───────────────────────────┘
        │
        ├─── No Manifest ───► Continue with existing initialization
        │
        ▼ Has Manifest
┌───────────────────────────┐
│ 4. Compare Manifest vs    │
│    Current State          │
│    - Dolt commit match?   │
│    - Branch match?        │
│    - Git commit match?    │
└───────────────────────────┘
        │
        ├─── Matches ───► Continue with existing initialization
        │
        ▼ Mismatch
┌───────────────────────────┐
│ 5. Determine Action       │
│    Based on init mode:    │
│    - auto: proceed        │
│    - prompt: ask user     │
│    - manual: log warning  │
│    - disabled: skip       │
└───────────────────────────┘
        │
        ▼ Proceed with sync
┌───────────────────────────┐
│ 6. Initialize DMMS State  │
│    a. Clone/fetch Dolt    │
│    b. Checkout commit     │
│    c. Sync ChromaDB       │
└───────────────────────────┘
        │
        ▼
┌───────────────────────────┐
│ 7. Continue startup       │
│    (existing flow)        │
└───────────────────────────┘
```

---

## Workflow Integration

### After DoltCommitTool

When user commits to Dolt, update manifest:

```csharp
// In DoltCommitTool.cs
var result = await _syncManager.ProcessCommitAsync(message);
if (result.Status == SyncStatusV2.Completed)
{
    // Update manifest with new Dolt commit
    await _manifestService.UpdateDoltCommitAsync(
        projectPath,
        result.CommitHash,
        currentBranch);
}
```

### After Git Commit (Hook Integration)

DMMS could provide a Git hook that:
1. Reads current Dolt state
2. Updates `.dmms/state.json`
3. Stages the manifest file

**Optional Git pre-commit hook** (`.git/hooks/pre-commit`):
```bash
#!/bin/bash
# Update DMMS manifest before Git commit
dmms-cli update-manifest
git add .dmms/state.json
```

### New Tool: UpdateManifestTool

```csharp
[McpServerTool]
[McpServerToolType("update_manifest")]
public async Task<object> UpdateManifest(
    bool? include_git_mapping = true,
    string? note = null)
{
    // Updates .dmms/state.json with current Dolt state
    // Optionally records Git commit mapping
}
```

### New Tool: SyncToManifestTool

```csharp
[McpServerTool]
[McpServerToolType("sync_to_manifest")]
public async Task<object> SyncToManifest(
    bool? force = false,
    string? target_commit = null,
    string? target_branch = null)
{
    // Syncs DMMS state to match manifest (or override parameters)
}
```

---

## Storage Location Decision

### Options Considered

| Option | Location | Git-Tracked | Pros | Cons |
|--------|----------|-------------|------|------|
| A | `.dmms/state.json` | Yes | Clean namespace, discoverable | New folder |
| B | `dmms.json` | Yes | Simple, root-level | Could conflict |
| C | `.mcp.json` (extended) | Yes | Reuses existing | Mixed concerns |
| D | `package.json` (field) | Yes | JS ecosystem standard | Not universal |

### Recommendation: Option A - `.dmms/state.json`

**Rationale:**
1. **Clean namespace** - `.dmms/` folder can hold additional files (logs, cache)
2. **Discoverable** - Standard hidden folder pattern (like `.git`, `.vscode`)
3. **Extensible** - Can add more files: `config.json`, `hooks/`, etc.
4. **Non-conflicting** - Unique to DMMS

**Folder structure:**
```
project/
├── .dmms/
│   ├── state.json          # Current state manifest
│   ├── config.json         # Optional: local config overrides
│   └── hooks/              # Optional: custom hooks
├── .git/
├── .mcp.json               # MCP server configuration (unchanged)
├── chroma_data/            # ChromaDB (gitignored)
├── data/
│   ├── dolt-repo/          # Dolt repository (gitignored or submodule)
│   └── dev/                # SQLite tracking (gitignored)
└── ...
```

---

## Git Integration Strategies

### Strategy 1: Manifest-Only (Recommended for v1)

- `.dmms/state.json` is Git-tracked
- Dolt repo at `./data/dolt-repo/` is gitignored
- On clone: DMMS fetches Dolt data based on manifest

**Pros:** Simple, no submodule complexity
**Cons:** Requires Dolt remote access on clone

### Strategy 2: Dolt as Git Submodule

- `./data/dolt-repo/` is a Git submodule pointing to Dolt remote
- Git tracks the submodule commit reference
- On clone: `git submodule update --init`

**Pros:** Works offline after initial clone
**Cons:** Submodule complexity, potential merge conflicts

### Strategy 3: Dolt Data in Git LFS

- Dolt database files stored in Git LFS
- Full offline capability

**Pros:** Complete offline support
**Cons:** Large storage, slow clones, LFS dependency

### Recommendation: Strategy 1 for v1

Start simple with manifest-only. Can add submodule support later as Strategy 2.

---

## Error Handling

### Initialization Failures

| Scenario | Behavior |
|----------|----------|
| No network, Dolt remote unavailable | Log warning, continue with local state |
| Manifest commit doesn't exist | Log error, prompt user or use latest |
| Dolt CLI not installed | Log error, skip Dolt initialization |
| Manifest parse error | Log error, ignore manifest |
| Git not installed (for mapping) | Skip Git mapping, continue |

### Graceful Degradation

1. **No manifest:** Behave as current (backward compatible)
2. **Invalid manifest:** Log warning, ignore
3. **Network failure:** Use cached/local state
4. **Partial sync:** Report which collections synced

---

## Security Considerations

1. **Credentials:** Manifest should NOT store credentials
   - Use environment variables for Dolt auth
   - Use system credential storage

2. **Remote URL Validation:** Validate Dolt remote URLs
   - Prevent command injection
   - Whitelist known hosts (optional)

3. **Manifest Integrity:** Consider signing manifests
   - Optional for enterprise deployments
   - Verify manifest author via Git commit signatures

---

## Migration Path

### For Existing Projects

1. **No action required** - Existing projects continue working
2. **Opt-in** - Run `dmms init-manifest` to create manifest
3. **Gradual adoption** - Start with `mode: manual`

### For New Projects

1. **Clone with DMMS** - Automatically creates manifest on first commit
2. **Init command** - `dmms init --with-manifest`

---

## Performance Considerations

1. **Startup Impact:**
   - Manifest read: <1ms (small JSON file)
   - State comparison: <10ms
   - Full sync (if needed): Variable (network-dependent)

2. **Async Initialization:**
   - Critical path: Load manifest, check state
   - Background: Network operations, sync

3. **Caching:**
   - Cache last known Dolt state in SQLite
   - Avoid unnecessary network calls

---

## Testing Strategy

See `Assignment.md` for detailed test requirements.

**Key Test Categories:**
1. Manifest read/write operations
2. Initialization scenarios (various modes)
3. Git integration (commit detection, mapping)
4. Error handling (network failures, invalid data)
5. Migration (existing projects without manifest)
6. E2E workflow (commit → update manifest → clone → sync)
