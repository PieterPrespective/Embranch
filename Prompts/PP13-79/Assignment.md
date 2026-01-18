# PP13-79: Git-Synchronized DMMS Initialization

## Date: 2026-01-18
## Type: Feature Enhancement - Repository/Branch/Commit Targeting
## Priority: High
## Depends On: Core DMMS functionality (PP13-69)

---

## Problem Statement

When a Git repository containing DMMS is cloned, the DMMS state (ChromaDB collections, Dolt version control) does not automatically match the state at the Git commit being checked out. This means:

1. **Issue logs are lost:** A project's development history (stored in ChromaDB/Dolt) doesn't transfer with Git clone
2. **State mismatch:** The code state (Git) doesn't match the knowledge base state (DMMS)
3. **Manual recovery required:** Users must manually import/sync to recover DMMS state
4. **No branch/commit targeting:** DMMS cannot be instructed to initialize at a specific Dolt branch or commit

**Current Behavior:**
- DMMS initializes with empty or local-only state on startup
- No mechanism to associate Git commits with Dolt commits
- The `repository` field in MCP settings is unused
- Cloning a project loses all DMMS-managed knowledge

**Desired Behavior:**
- Git-tracked manifest file (`.dmms/state.json`) stores Dolt remote URL and commit reference
- On clone/startup, DMMS can sync to the manifest-specified Dolt state
- Optional Git-Dolt commit mapping for precise state reconstruction
- Configurable initialization modes (auto, prompt, manual, disabled)

---

## Solution Overview

Implement a **Git-tracked state manifest** system that:

1. **Records** the Dolt repository URL, branch, and commit in `.dmms/state.json`
2. **Detects** project root (Git repository root) on startup
3. **Compares** manifest state against current local DMMS state
4. **Initializes** DMMS by fetching and syncing to the manifest-specified Dolt commit
5. **Updates** the manifest after Dolt commits (optional Git hook integration)

### Key Design Principle: Backward Compatible

- Projects without manifest continue working unchanged
- Opt-in adoption via `dmms init-manifest` or automatic on first tracked commit
- Graceful degradation when network/Dolt unavailable

---

## Architecture

### New Files to Create

| File | Purpose |
|------|---------|
| `multidolt-mcp/Models/ManifestModels.cs` | Data models for manifest |
| `multidolt-mcp/Services/IDmmsStateManifest.cs` | Interface for manifest operations |
| `multidolt-mcp/Services/DmmsStateManifest.cs` | Implementation of manifest service |
| `multidolt-mcp/Services/IDmmsInitializer.cs` | Interface for initialization logic |
| `multidolt-mcp/Services/DmmsInitializer.cs` | Implementation of initializer |
| `multidolt-mcp/Services/IGitIntegration.cs` | Interface for Git operations |
| `multidolt-mcp/Services/GitIntegration.cs` | Implementation of Git helper |
| `multidolt-mcp/Tools/UpdateManifestTool.cs` | Tool to update manifest |
| `multidolt-mcp/Tools/SyncToManifestTool.cs` | Tool to sync to manifest state |
| `multidolt-mcp/Tools/InitManifestTool.cs` | Tool to create initial manifest |

### Files to Modify

| File | Changes |
|------|---------|
| `multidolt-mcp/Models/DoltConfiguration.cs` | Add `TargetBranch`, `TargetCommit`, `UseManifest` |
| `multidolt-mcp/Models/ServerConfiguration.cs` | Add `ProjectRoot`, `AutoDetectProjectRoot` |
| `multidolt-mcp/Program.cs` | Add manifest check and initialization at startup |
| `multidolt-mcp/Tools/DoltCommitTool.cs` | Optionally update manifest after commit |

---

## Implementation Phases

### Phase 1: Core Models and Manifest Service

**Files to Create:**
- `multidolt-mcp/Models/ManifestModels.cs`
- `multidolt-mcp/Services/IDmmsStateManifest.cs`
- `multidolt-mcp/Services/DmmsStateManifest.cs`

**Key Features:**
1. `DmmsManifest` record with all configuration fields
2. Read/write manifest from `.dmms/state.json`
3. Validation of manifest structure
4. Schema versioning support

**Manifest Structure:**
```json
{
  "version": "1.0",
  "dolt": {
    "remote_url": "dolthub.com/org/repo",
    "default_branch": "main",
    "current_commit": "abc123...",
    "current_branch": "main"
  },
  "git_mapping": {
    "enabled": true,
    "last_git_commit": "fedcba...",
    "dolt_commit_at_git_commit": "abc123..."
  },
  "initialization": {
    "mode": "auto",
    "on_clone": "sync_to_manifest",
    "on_branch_change": "preserve_local"
  },
  "collections": {
    "tracked": ["*"],
    "excluded": ["test-*"]
  },
  "updated_at": "2026-01-18T10:30:00Z"
}
```

### Phase 2: Git Integration Service

**Files to Create:**
- `multidolt-mcp/Services/IGitIntegration.cs`
- `multidolt-mcp/Services/GitIntegration.cs`

**Key Features:**
1. Detect if current path is in a Git repository
2. Get Git repository root directory
3. Get current Git commit hash (HEAD)
4. Check if specific file is Git-tracked

**Implementation Notes:**
- Use `git rev-parse` commands via CliWrap
- Handle Git not being installed gracefully
- Cache repository root for performance

### Phase 3: DMMS Initializer Service

**Files to Create:**
- `multidolt-mcp/Services/IDmmsInitializer.cs`
- `multidolt-mcp/Services/DmmsInitializer.cs`

**Key Features:**
1. `CheckInitializationNeededAsync` - Compare manifest vs current state
2. `InitializeFromManifestAsync` - Perform the sync operation
3. `SyncToCommitAsync` - Sync to specific Dolt commit

**Initialization Flow:**
```csharp
public async Task<InitializationResult> InitializeFromManifestAsync(DmmsManifest manifest)
{
    // 1. Check if Dolt repo exists locally
    var repoExists = await _doltCli.IsInitializedAsync();

    if (!repoExists && manifest.Dolt.RemoteUrl != null)
    {
        // Clone from remote
        await _doltCli.CloneAsync(manifest.Dolt.RemoteUrl);
    }

    // 2. Fetch latest from remote (if configured)
    if (manifest.Dolt.RemoteUrl != null)
    {
        await _doltCli.FetchAsync();
    }

    // 3. Checkout specified commit or branch
    if (manifest.Dolt.CurrentCommit != null)
    {
        await _doltCli.CheckoutAsync(manifest.Dolt.CurrentCommit);
    }
    else if (manifest.Dolt.CurrentBranch != null)
    {
        await _doltCli.CheckoutAsync(manifest.Dolt.CurrentBranch);
    }

    // 4. Sync ChromaDB to match Dolt state
    await _syncManager.FullSyncAsync(forceSync: true);

    return new InitializationResult { Success = true, ... };
}
```

### Phase 4: Configuration Enhancement

**Files to Modify:**
- `multidolt-mcp/Models/DoltConfiguration.cs`
- `multidolt-mcp/Models/ServerConfiguration.cs`
- `multidolt-mcp/Program.cs` (ConfigurationUtility)

**New Configuration Options:**

```csharp
// DoltConfiguration additions
public string? TargetBranch { get; set; }      // DMMS_TARGET_BRANCH
public string? TargetCommit { get; set; }      // DMMS_TARGET_COMMIT
public bool UseManifest { get; set; } = true;  // DMMS_USE_MANIFEST

// ServerConfiguration additions
public string? ProjectRoot { get; set; }       // DMMS_PROJECT_ROOT
public bool AutoDetectProjectRoot { get; set; } = true;
public string InitMode { get; set; } = "auto"; // DMMS_INIT_MODE
```

### Phase 5: Startup Integration

**Files to Modify:**
- `multidolt-mcp/Program.cs`

**Key Changes:**
1. Detect project root (Git root or explicit)
2. Check for `.dmms/state.json`
3. Compare manifest vs current state
4. Execute initialization if needed (based on mode)

**Integration Point:**
```csharp
// After Python initialization, before deletion tracker
// Add manifest check and initialization

if (doltConfig.UseManifest)
{
    var manifestService = host.Services.GetRequiredService<IDmmsStateManifest>();
    var initializer = host.Services.GetRequiredService<IDmmsInitializer>();
    var gitService = host.Services.GetRequiredService<IGitIntegration>();

    var projectRoot = serverConfig.ProjectRoot
        ?? await gitService.GetGitRootAsync(Directory.GetCurrentDirectory())
        ?? Directory.GetCurrentDirectory();

    var manifest = await manifestService.ReadManifestAsync(projectRoot);
    if (manifest != null)
    {
        var check = await initializer.CheckInitializationNeededAsync(manifest);
        if (check.NeedsInitialization)
        {
            await initializer.InitializeFromManifestAsync(manifest);
        }
    }
}
```

### Phase 6: MCP Tools

**Files to Create:**
- `multidolt-mcp/Tools/InitManifestTool.cs`
- `multidolt-mcp/Tools/UpdateManifestTool.cs`
- `multidolt-mcp/Tools/SyncToManifestTool.cs`

#### InitManifestTool
Creates initial `.dmms/state.json` based on current state.

```csharp
[McpServerTool]
[McpServerToolType("init_manifest")]
public async Task<object> InitManifest(
    string? remote_url = null,
    string? default_branch = "main",
    string? init_mode = "auto")
```

#### UpdateManifestTool
Updates manifest with current Dolt state.

```csharp
[McpServerTool]
[McpServerToolType("update_manifest")]
public async Task<object> UpdateManifest(
    bool? include_git_mapping = true,
    string? note = null)
```

#### SyncToManifestTool
Syncs local DMMS state to match manifest.

```csharp
[McpServerTool]
[McpServerToolType("sync_to_manifest")]
public async Task<object> SyncToManifest(
    bool? force = false,
    string? target_commit = null,
    string? target_branch = null)
```

### Phase 7: DI Registration

**Files to Modify:**
- `multidolt-mcp/Program.cs`

**Additions:**
```csharp
builder.Services.AddSingleton<IGitIntegration, GitIntegration>();
builder.Services.AddSingleton<IDmmsStateManifest, DmmsStateManifest>();
builder.Services.AddSingleton<IDmmsInitializer, DmmsInitializer>();

// Tools
.WithTools<InitManifestTool>()
.WithTools<UpdateManifestTool>()
.WithTools<SyncToManifestTool>()
```

### Phase 8: Testing

**Files to Create:**
- `multidolt-mcp-testing/UnitTests/ManifestModelsTests.cs`
- `multidolt-mcp-testing/UnitTests/DmmsStateManifestTests.cs`
- `multidolt-mcp-testing/UnitTests/GitIntegrationTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_79_ManifestIntegrationTests.cs`
- `multidolt-mcp-testing/IntegrationTests/PP13_79_InitializationE2ETests.cs`

---

## Test Requirements

### Unit Tests (15+ tests)

**ManifestModelsTests.cs:**
1. `DmmsManifest_DefaultValues_AreCorrect`
2. `DmmsManifest_Serialization_RoundTrips`
3. `InitializationConfig_AllModes_ParseCorrectly`

**DmmsStateManifestTests.cs:**
4. `ReadManifestAsync_ValidFile_ReturnsManifest`
5. `ReadManifestAsync_MissingFile_ReturnsNull`
6. `ReadManifestAsync_InvalidJson_ReturnsNull`
7. `WriteManifestAsync_CreatesDirectory_AndFile`
8. `WriteManifestAsync_OverwritesExisting`
9. `UpdateDoltCommitAsync_UpdatesCorrectFields`
10. `ManifestExistsAsync_ReturnsCorrectStatus`

**GitIntegrationTests.cs:**
11. `GetGitRootAsync_InsideRepo_ReturnsRoot`
12. `GetGitRootAsync_OutsideRepo_ReturnsNull`
13. `GetCurrentGitCommitAsync_ReturnsHash`
14. `IsGitRepositoryAsync_CorrectlyDetects`
15. `GitNotInstalled_GracefullyFails`

### Integration Tests (12+ tests)

**PP13_79_ManifestIntegrationTests.cs:**
1. `Manifest_CreateAndRead_RoundTrips`
2. `Manifest_UpdateDoltCommit_PersistsCorrectly`
3. `Manifest_WithGitMapping_UpdatesOnCommit`
4. `Manifest_InvalidVersion_HandledGracefully`

**PP13_79_InitializationE2ETests.cs:**
5. `Initialization_NoManifest_SkipsManifestLogic`
6. `Initialization_ManifestMatchesState_NoAction`
7. `Initialization_ManifestMismatch_AutoMode_Syncs`
8. `Initialization_ManifestMismatch_ManualMode_SkipsWithWarning`
9. `Initialization_CloneRequired_ClonesAndSyncs`
10. `Initialization_NetworkFailure_GracefulDegradation`
11. `Initialization_InvalidCommit_FallsBackToLatest`
12. `SyncToManifest_WithOverrideCommit_UsesOverride`

### Tool Tests (9+ tests)

**InitManifestToolTests.cs:**
1. `InitManifest_CreatesValidManifest`
2. `InitManifest_WithRemoteUrl_IncludesRemote`
3. `InitManifest_ExistingManifest_ReturnsWarning`

**UpdateManifestToolTests.cs:**
4. `UpdateManifest_UpdatesDoltCommit`
5. `UpdateManifest_WithGitMapping_RecordsMapping`
6. `UpdateManifest_NoManifest_ReturnsError`

**SyncToManifestToolTests.cs:**
7. `SyncToManifest_SyncsToManifestState`
8. `SyncToManifest_WithOverride_UsesOverrideCommit`
9. `SyncToManifest_NoManifest_ReturnsError`

---

## Success Criteria

1. **Manifest Creation:** Users can create `.dmms/state.json` via tool
2. **Automatic Sync:** On startup, DMMS syncs to manifest state (auto mode)
3. **Clone Workflow:** Cloning Git repo + DMMS startup = correct state
4. **Backward Compatible:** Projects without manifest work unchanged
5. **Network Resilience:** Graceful handling of network failures
6. **Mode Support:** All four modes (auto/prompt/manual/disabled) work
7. **Git Mapping:** Optional Git-Dolt commit association
8. **Tool Support:** Three new MCP tools functional
9. **Test Coverage:** 36+ tests passing
10. **Build:** 0 errors, 0 warnings

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Network unavailable on clone | Cache last known state; fallback to latest |
| Dolt remote requires auth | Use env vars for credentials; clear error messages |
| Large Dolt repos slow to clone | Show progress; async initialization option |
| Manifest gets out of sync | Provide `update_manifest` tool; optional Git hook |
| Git not installed | Detect and skip Git features; clear warnings |
| Concurrent modifications | Lock manifest file during writes |

---

## Related Work

- **Builds On:** PP13-69 (SyncManagerV2), DoltCli, ChromaToDoltSyncer
- **Uses:** CliWrap (for Git commands), existing sync infrastructure
- **Enables:** Team collaboration with shared DMMS state, reproducible environments

---

## Future Enhancements (Not in Scope)

1. **Git Hooks:** Automatic manifest update on Git commit
2. **Dolt Submodule:** Option to track Dolt as Git submodule
3. **Manifest Signing:** Cryptographic verification for enterprise
4. **Multi-Remote:** Support for multiple Dolt remotes
5. **Selective Sync:** Sync only specific collections on clone

---

## Implementation Order

1. **Phase 1:** Core Models and Manifest Service (foundation)
2. **Phase 2:** Git Integration Service (detection)
3. **Phase 3:** DMMS Initializer Service (core logic)
4. **Phase 4:** Configuration Enhancement (settings)
5. **Phase 5:** Startup Integration (Program.cs)
6. **Phase 6:** MCP Tools (user interface)
7. **Phase 7:** DI Registration (wiring)
8. **Phase 8:** Testing (validation)

---

## Estimated Complexity

| Phase | Complexity | New Files | Modified Files |
|-------|------------|-----------|----------------|
| 1 | Medium | 3 | 0 |
| 2 | Low | 2 | 0 |
| 3 | High | 2 | 0 |
| 4 | Low | 0 | 3 |
| 5 | Medium | 0 | 1 |
| 6 | Medium | 3 | 0 |
| 7 | Trivial | 0 | 1 |
| 8 | Medium | 5 | 0 |

**Total New Files:** ~15
**Total Modified Files:** ~5
**Total New Tests:** 36+
