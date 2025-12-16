using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that initializes a new Dolt repository
/// </summary>
[McpServerToolType]
public class DoltInitTool
{
    private readonly ILogger<DoltInitTool> _logger;
    private readonly IDoltCli _doltCli;
    private readonly ISyncManagerV2 _syncManager;

    /// <summary>
    /// Initializes a new instance of the DoltInitTool class
    /// </summary>
    public DoltInitTool(ILogger<DoltInitTool> logger, IDoltCli doltCli, ISyncManagerV2 syncManager)
    {
        _logger = logger;
        _doltCli = doltCli;
        _syncManager = syncManager;
    }

    /// <summary>
    /// Initialize a new Dolt repository for version control. Use this when starting a new knowledge base or adding version control to an existing ChromaDB collection
    /// </summary>
    [McpServerTool]
    [Description("Initialize a new Dolt repository for version control. Use this when starting a new knowledge base or adding version control to an existing ChromaDB collection. For cloning an existing repository, use dolt_clone instead.")]
    public virtual async Task<object> DoltInit(
        string? remote_url = null,
        string initial_branch = "main",
        bool import_existing = true,
        string commit_message = "Initial import from ChromaDB")
    {
        _logger.LogInformation("[DoltInitTool.DoltInit] *** METHOD INVOKED *** - Parameters: remote_url={RemoteUrl}, initial_branch={InitialBranch}, import_existing={ImportExisting}, commit_message={CommitMessage}", 
            remote_url ?? "<null>", initial_branch, import_existing, commit_message);
        try
        {
            _logger.LogInformation($"[DoltInitTool.DoltInit] STARTING - Initializing repository with branch={initial_branch}, import={import_existing}");

            // First check if Dolt is available
            _logger.LogDebug("[DoltInitTool.DoltInit] Checking if Dolt executable is available...");
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            _logger.LogDebug($"[DoltInitTool.DoltInit] Dolt availability check result: Success={doltCheck.Success}, Error='{doltCheck.Error}'");
            
            if (!doltCheck.Success)
            {
                _logger.LogError($"[DoltInitTool.DoltInit] Dolt executable not available: {doltCheck.Error}");
                return new
                {
                    success = false,
                    error = "DOLT_EXECUTABLE_NOT_FOUND",
                    message = doltCheck.Error
                };
            }
            
            _logger.LogDebug("[DoltInitTool.DoltInit] Dolt executable is available");

            // Check if already initialized
            _logger.LogDebug("[DoltInitTool.DoltInit] Checking if repository is already initialized...");
            var isInitialized = await _doltCli.IsInitializedAsync();
            _logger.LogDebug($"[DoltInitTool.DoltInit] Repository initialization check result: {isInitialized}");
            
            if (isInitialized)
            {
                _logger.LogWarning("[DoltInitTool.DoltInit] Repository already initialized, returning error");
                return new
                {
                    success = false,
                    error = "ALREADY_INITIALIZED",
                    message = "Repository already exists. Use dolt_status to check state."
                };
            }

            // Initialize repository
            _logger.LogInformation("[DoltInitTool.DoltInit] Initializing new Dolt repository...");
            var initResult = await _doltCli.InitAsync();
            _logger.LogDebug($"[DoltInitTool.DoltInit] Init result: Success={initResult.Success}, Error='{initResult.Error}', Output='{initResult.Output}'");
            
            if (!initResult.Success)
            {
                _logger.LogError($"[DoltInitTool.DoltInit] Failed to initialize repository: {initResult.Error}");
                return new
                {
                    success = false,
                    error = "INIT_FAILED",
                    message = $"Failed to initialize repository: {initResult.Error}"
                };
            }

            // Configure remote if provided
            bool remoteConfigured = false;
            if (!string.IsNullOrEmpty(remote_url))
            {
                try
                {
                    // Format the URL properly if it's just org/repo format
                    if (!remote_url.StartsWith("http"))
                    {
                        remote_url = $"https://doltremoteapi.dolthub.com/{remote_url}";
                    }
                    
                    await _doltCli.AddRemoteAsync("origin", remote_url);
                    remoteConfigured = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to configure remote: {remote_url}");
                }
            }

            // Import existing ChromaDB data if requested
            string? initialCommitHash = null;
            int documentsImported = 0;
            List<string> collections = new();

            if (import_existing)
            {
                try
                {
                    // Use sync manager to import existing data - need collection name
                    // For now, use a default collection name or get it from somewhere
                    string defaultCollectionName = "default";
                    await _syncManager.InitializeVersionControlAsync(defaultCollectionName);
                    
                    // Stage and commit if there are changes
                    var changes = await _syncManager.GetLocalChangesAsync();
                    if (changes != null && changes.HasChanges)
                    {
                        await _syncManager.StageLocalChangesAsync(defaultCollectionName);
                        var commitResult = await _syncManager.ProcessCommitAsync(commit_message, true, false);
                        initialCommitHash = await _doltCli.GetHeadCommitHashAsync();
                        
                        documentsImported = (changes.NewDocuments?.Count ?? 0) + 
                                          (changes.ModifiedDocuments?.Count ?? 0);
                        // TODO: Get actual collection names
                        collections.Add("imported_collections");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to import existing ChromaDB data");
                }
            }

            return new
            {
                success = true,
                repository = new
                {
                    path = "./data/dolt-repo",
                    branch = initial_branch,
                    commit = initialCommitHash != null ? new
                    {
                        hash = initialCommitHash,
                        message = commit_message
                    } : null
                },
                remote = new
                {
                    configured = remoteConfigured,
                    name = remoteConfigured ? "origin" : null,
                    url = remoteConfigured ? remote_url : null
                },
                import_summary = new
                {
                    documents_imported = documentsImported,
                    collections = collections.ToArray()
                },
                message = remoteConfigured 
                    ? $"Repository initialized with {documentsImported} documents. Remote 'origin' configured. Use dolt_push to upload to DoltHub."
                    : $"Repository initialized with {documentsImported} documents."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DoltInitTool.DoltInit] EXCEPTION - Error initializing repository. Type: {ExceptionType}, Message: {Message}", ex.GetType().Name, ex.Message);
            _logger.LogError("[DoltInitTool.DoltInit] Stack trace: {StackTrace}", ex.StackTrace);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to initialize repository: {ex.Message}"
            };
        }
    }
}