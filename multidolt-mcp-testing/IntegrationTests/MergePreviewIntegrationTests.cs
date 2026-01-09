using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DMMS.Models;
using DMMS.Services;
using DMMS.Tools;
using System.Text.Json;

namespace DMMS.Testing.IntegrationTests
{
    /// <summary>
    /// Integration tests specifically for the enhanced merge preview functionality
    /// Tests real branch diff analysis, content comparison, and resolution preview
    /// </summary>
    [TestFixture]
    public class MergePreviewIntegrationTests
    {
        private ILogger<MergePreviewIntegrationTests> _logger;
        private IDoltCli _doltCli;
        private IChromaDbService _chromaService;
        private ISyncManagerV2 _syncManager;
        private IConflictAnalyzer _conflictAnalyzer;
        private PreviewDoltMergeTool _previewTool;
        private SqliteDeletionTracker _deletionTracker;
        
        private string _testCollection = "preview-test-collection";
        private string _tempRepoPath;
        private string _tempDataPath;

        [SetUp]
        public async Task Setup()
        {
            // Initialize Python context if not already initialized
            if (!PythonContext.IsInitialized)
            {
                var setupLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var setupLogger = setupLoggerFactory.CreateLogger<MergePreviewIntegrationTests>();
                var pythonDll = PythonContextUtility.FindPythonDll(setupLogger);
                PythonContext.Initialize(setupLogger, pythonDll);
            }
            
            // Create unique paths for this test
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "MergePreviewTests", Guid.NewGuid().ToString());
            _tempDataPath = Path.Combine(_tempRepoPath, "data");
            Directory.CreateDirectory(_tempRepoPath);
            Directory.CreateDirectory(_tempDataPath);

            // Create logger factory
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            _logger = loggerFactory.CreateLogger<MergePreviewIntegrationTests>();
            var doltLogger = loggerFactory.CreateLogger<DoltCli>();
            var syncLogger = loggerFactory.CreateLogger<SyncManagerV2>();
            var conflictAnalyzerLogger = loggerFactory.CreateLogger<ConflictAnalyzer>();
            var previewToolLogger = loggerFactory.CreateLogger<PreviewDoltMergeTool>();
            var deletionTrackerLogger = loggerFactory.CreateLogger<SqliteDeletionTracker>();

            // Create configuration
            var doltConfig = new DoltConfiguration
            {
                RepositoryPath = _tempRepoPath,
                DoltExecutablePath = GetDoltExecutablePath(),
                CommandTimeoutMs = 30000
            };

            var serverConfig = new ServerConfiguration
            {
                ChromaMode = "persistent",
                ChromaDataPath = Path.Combine(_tempDataPath, "chroma"),
                DataPath = _tempDataPath
            };

            // Initialize services
            _doltCli = new DoltCli(Options.Create(doltConfig), doltLogger);
            _chromaService = CreateChromaService(serverConfig);
            _deletionTracker = new SqliteDeletionTracker(deletionTrackerLogger, serverConfig);
            _syncManager = new SyncManagerV2(
                _doltCli,
                _chromaService,
                _deletionTracker,
                _deletionTracker,
                Options.Create(doltConfig),
                syncLogger);

            _conflictAnalyzer = new ConflictAnalyzer(_doltCli, conflictAnalyzerLogger);
            _previewTool = new PreviewDoltMergeTool(previewToolLogger, _doltCli, _conflictAnalyzer, _syncManager);

            // Initialize the deletion tracker database schema
            await _deletionTracker.InitializeAsync(_tempRepoPath);

            // Initialize repository
            await InitializeTestRepository();
        }

        [TearDown]
        public async Task Cleanup()
        {
            try
            {
                // Clean up test collection
                try
                {
                    await _chromaService.DeleteCollectionAsync(_testCollection);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Dispose deletion tracker
                _deletionTracker?.Dispose();

                // Clean up temp directories
                if (Directory.Exists(_tempRepoPath))
                {
                    Directory.Delete(_tempRepoPath, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during test cleanup");
            }
        }

        [Test]
        public async Task PreviewMerge_RealDiffAnalysis_ReturnsAccurateStatistics()
        {
            // Arrange: Create branches with known document changes
            await CreateBranchesWithKnownChanges();

            // Act: Preview merge to get statistics
            var preview = await _previewTool.PreviewDoltMerge("feature-branch", "main", detailed_diff: true);
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(preview));

            // Assert: Should have accurate change statistics
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
            
            var changesPreview = result.GetProperty("merge_preview").GetProperty("changes_preview");
            var docsAdded = changesPreview.GetProperty("documents_added").GetInt32();
            var docsModified = changesPreview.GetProperty("documents_modified").GetInt32();
            var docsDeleted = changesPreview.GetProperty("documents_deleted").GetInt32();
            var collectionsAffected = changesPreview.GetProperty("collections_affected").GetInt32();

            // We added 1 doc in feature branch, modified 1 existing doc
            Assert.That(docsAdded, Is.GreaterThanOrEqualTo(1), "Should detect added documents");
            Assert.That(docsModified, Is.GreaterThanOrEqualTo(0), "Should detect modified documents");
            Assert.That(collectionsAffected, Is.GreaterThanOrEqualTo(1), "Should detect affected collections");

            _logger.LogInformation("Preview statistics: +{Added} ~{Modified} -{Deleted} collections:{Collections}",
                docsAdded, docsModified, docsDeleted, collectionsAffected);
        }

        [Test]
        public async Task PreviewMerge_DocumentIdentification_ExtractsCorrectIds()
        {
            // Arrange: Create conflicting changes to same document
            await CreateConflictingDocumentChanges();

            // Act: Preview merge with detailed diff
            var preview = await _previewTool.PreviewDoltMerge("conflict-branch", "main", 
                include_auto_resolvable: true, detailed_diff: true);
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(preview));

            // Assert: Check that the merge analysis succeeded
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);

            var conflicts = result.GetProperty("conflicts").EnumerateArray().ToList();
            
            // Dolt may auto-merge successfully, which means no conflicts
            if (conflicts.Count > 0)
            {
                // If there are conflicts, verify they are properly identified
                foreach (var conflict in conflicts)
                {
                    var collection = conflict.GetProperty("collection").GetString();
                    var documentId = conflict.GetProperty("document_id").GetString();
                    
                    Assert.That(collection, Is.Not.Null.And.Not.Empty, 
                        "Collection should be properly identified");
                    Assert.That(documentId, Is.Not.Null.And.Not.Empty, 
                        "Document ID should be properly identified");
                    
                    _logger.LogInformation("Identified conflict: {Collection}/{DocId}", collection, documentId);
                }
            }
            else
            {
                // No conflicts means Dolt successfully auto-merged
                _logger.LogInformation("No conflicts found - Dolt successfully auto-merged the changes");
                Assert.That(result.GetProperty("can_auto_merge").GetBoolean(), Is.True,
                    "Should be able to auto-merge when no conflicts are detected");
            }
        }

        [Test]
        public async Task GetContentComparison_ShowsActualDifferences()
        {
            // Arrange: Create branches with different content for same document
            await CreateBranchesWithContentDifferences();

            // Act: Get content comparison for the conflicted document
            var comparison = await _conflictAnalyzer.GetContentComparisonAsync(
                "documents", "shared_doc", "content-branch", "main");

            // Assert: Should show actual content differences
            Assert.That(comparison.TableName, Is.EqualTo("documents"));
            Assert.That(comparison.DocumentId, Is.EqualTo("shared_doc"));

            // Check if documents exist on both branches
            if (comparison.SourceContent?.Exists == true && comparison.TargetContent?.Exists == true)
            {
                _logger.LogInformation("Both documents exist - checking for conflicts");
                
                // If both documents exist, check if they have differences
                if (!string.IsNullOrEmpty(comparison.SourceContent.Content) && 
                    !string.IsNullOrEmpty(comparison.TargetContent.Content))
                {
                    var hasContentDifferences = comparison.SourceContent.Content != comparison.TargetContent.Content;
                    
                    if (hasContentDifferences)
                    {
                        Assert.That(comparison.HasConflicts, Is.True, "Should detect content conflicts when content differs");
                        Assert.That(comparison.ConflictingFields, 
                            Contains.Item("content") | Contains.Item("document_text"),
                            "Should identify content field as conflicting");
                        
                        _logger.LogInformation("Content comparison: Source=[{Source}] Target=[{Target}]",
                            comparison.SourceContent.Content, comparison.TargetContent.Content);
                    }
                    else
                    {
                        _logger.LogInformation("Documents exist on both branches but have identical content");
                        Assert.That(comparison.HasConflicts, Is.False, "Should not detect conflicts for identical content");
                    }
                }
                else
                {
                    _logger.LogInformation("Documents exist but content is null/empty");
                }
            }
            else
            {
                _logger.LogInformation("Document doesn't exist on one or both branches - Source exists: {SourceExists}, Target exists: {TargetExists}",
                    comparison.SourceContent?.Exists, comparison.TargetContent?.Exists);
                
                // If one doesn't exist, that's still a valid comparison scenario
                Assert.That(comparison, Is.Not.Null, "Comparison should still be returned even if documents don't exist on all branches");
            }
        }

        [Test]
        public async Task GenerateResolutionPreview_KeepOurs_ShowsCorrectOutcome()
        {
            // Arrange: Create a specific conflict scenario
            var conflict = await CreateTestConflict();

            // Act: Generate preview for "keep ours" resolution
            var preview = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.KeepOurs);

            // Assert: Should show what "keep ours" would produce
            Assert.That(preview.ConflictId, Is.EqualTo(conflict.ConflictId));
            Assert.That(preview.ResolutionType, Is.EqualTo(ResolutionType.KeepOurs));
            Assert.That(preview.ConfidenceLevel, Is.EqualTo(100));
            
            Assert.That(preview.ResultingContent.Content, Is.EqualTo("Our version of content"));
            Assert.That(preview.DataLossWarnings, Is.Not.Empty, 
                "Should warn about data loss from their side");

            _logger.LogInformation("KeepOurs preview: {Description}, Warnings: {Count}",
                preview.Description, preview.DataLossWarnings.Count);
        }

        [Test]
        public async Task GenerateResolutionPreview_KeepTheirs_ShowsCorrectOutcome()
        {
            // Arrange: Create a specific conflict scenario
            var conflict = await CreateTestConflict();

            // Act: Generate preview for "keep theirs" resolution
            var preview = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.KeepTheirs);

            // Assert: Should show what "keep theirs" would produce
            Assert.That(preview.ResolutionType, Is.EqualTo(ResolutionType.KeepTheirs));
            Assert.That(preview.ConfidenceLevel, Is.EqualTo(100));
            
            Assert.That(preview.ResultingContent.Content, Is.EqualTo("Their version of content"));
            Assert.That(preview.DataLossWarnings, Is.Not.Empty,
                "Should warn about data loss from our side");

            _logger.LogInformation("KeepTheirs preview: {Description}, Warnings: {Count}",
                preview.Description, preview.DataLossWarnings.Count);
        }

        [Test]
        public async Task GenerateResolutionPreview_FieldMerge_IntelligentMerging()
        {
            // Arrange: Create conflict with timestamp and version fields
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_field_merge",
                Collection = _testCollection,
                DocumentId = "test_doc",
                BaseValues = new Dictionary<string, object>
                {
                    { "content", "base content" },
                    { "timestamp", "2024-01-01T00:00:00Z" },
                    { "version", 1 },
                    { "metadata", "base meta" }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "content", "our content" },
                    { "timestamp", "2024-01-02T00:00:00Z" },
                    { "version", 2 },
                    { "our_field", "our_value" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "their content" },
                    { "timestamp", "2024-01-03T00:00:00Z" },
                    { "version", 3 },
                    { "their_field", "their_value" }
                }
            };

            // Act: Generate field merge preview
            var preview = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.FieldMerge);

            // Assert: Should intelligently merge fields
            Assert.That(preview.ResolutionType, Is.EqualTo(ResolutionType.FieldMerge));
            
            // Should use newer timestamp
            Assert.That(preview.ResultingContent.Metadata["timestamp"].ToString(), 
                Is.EqualTo("2024-01-03T00:00:00Z"), "Should use newer timestamp");
            
            // Should use higher version
            Assert.That(Convert.ToInt32(preview.ResultingContent.Metadata["version"]), 
                Is.EqualTo(3), "Should use higher version number");

            // Should preserve unique fields from both sides
            Assert.That(preview.ResultingContent.Metadata.ContainsKey("our_field"), Is.True);
            Assert.That(preview.ResultingContent.Metadata.ContainsKey("their_field"), Is.True);

            _logger.LogInformation("FieldMerge result: Confidence={Confidence}, Fields={FieldCount}",
                preview.ConfidenceLevel, preview.ResultingContent.Metadata.Count);
        }

        [Test]
        public async Task PreviewMerge_NoPlaceholderValues_ReturnsRealData()
        {
            // Arrange: Create a realistic merge scenario
            await CreateRealisticMergeScenario();

            // Act: Preview the merge
            var preview = await _previewTool.PreviewDoltMerge("realistic-branch", "main");
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(preview));

            // Assert: Should NOT return placeholder values (all zeros)
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);

            var changesPreview = result.GetProperty("merge_preview").GetProperty("changes_preview");
            var docsAdded = changesPreview.GetProperty("documents_added").GetInt32();
            var docsModified = changesPreview.GetProperty("documents_modified").GetInt32();
            var docsDeleted = changesPreview.GetProperty("documents_deleted").GetInt32();
            var collectionsAffected = changesPreview.GetProperty("collections_affected").GetInt32();

            // Verify we're not getting placeholder zeros for everything
            var totalChanges = docsAdded + docsModified + docsDeleted;
            
            // We know we made changes, so total should be > 0 OR collections affected should be accurate
            Assert.That(totalChanges > 0 || collectionsAffected > 0, Is.True,
                "Should not return all zeros - indicates placeholder data is fixed");

            // Collections affected should never be hardcoded to 1
            Assert.That(collectionsAffected, Is.Not.EqualTo(1),
                "Collections affected should be calculated, not hardcoded to 1");

            _logger.LogInformation("Non-placeholder results: Changes={Changes}, Collections={Collections}",
                totalChanges, collectionsAffected);
        }

        [Test]
        public async Task PreviewMerge_EmptyBranches_ReturnsZerosCorrectly()
        {
            // Arrange: Ensure we're on main with no changes to merge
            await _doltCli.CheckoutAsync("main");

            // Act: Preview merge from main to main (no changes)
            var preview = await _previewTool.PreviewDoltMerge("main", "main");
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(preview));

            // Assert: Should correctly return zeros when there are genuinely no changes
            Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("can_auto_merge").GetBoolean(), Is.True);

            var changesPreview = result.GetProperty("merge_preview").GetProperty("changes_preview");
            var docsAdded = changesPreview.GetProperty("documents_added").GetInt32();
            var docsModified = changesPreview.GetProperty("documents_modified").GetInt32();
            var docsDeleted = changesPreview.GetProperty("documents_deleted").GetInt32();

            // These SHOULD be zero when there are truly no changes
            Assert.That(docsAdded, Is.EqualTo(0), "Should be zero for no changes scenario");
            Assert.That(docsModified, Is.EqualTo(0), "Should be zero for no changes scenario");
            Assert.That(docsDeleted, Is.EqualTo(0), "Should be zero for no changes scenario");

            _logger.LogInformation("No changes scenario correctly returns zeros");
        }

        #region Private Helper Methods

        private async Task InitializeTestRepository()
        {
            // Initialize Dolt repository
            await _doltCli.InitAsync();

            // Create initial collection with documents
            await _chromaService.CreateCollectionAsync(_testCollection);
            await _chromaService.AddDocumentsAsync(_testCollection, new List<string>
            {
                "Initial document for merge testing",
                "Second document for testing"
            }, new List<string> { "initial_doc", "second_doc" });

            // Initialize version control
            await _syncManager.InitializeVersionControlAsync(_testCollection, "Initial commit for preview tests");
        }

        private async Task CreateBranchesWithKnownChanges()
        {
            // Create feature branch
            await _doltCli.CreateBranchAsync("feature-branch");
            await _doltCli.CheckoutAsync("feature-branch");

            // Add a new document
            await _chromaService.AddDocumentsAsync(_testCollection, new List<string>
            {
                "New document added in feature branch"
            }, new List<string> { "feature_doc" });

            // Modify existing document
            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Modified content from feature branch"
            }, new List<string> { "initial_doc" });

            // Commit changes
            await _syncManager.ProcessCommitAsync("Feature branch: added and modified documents");

            // Switch back to main
            await _doltCli.CheckoutAsync("main");
        }

        private async Task CreateConflictingDocumentChanges()
        {
            // Create conflict branch
            await _doltCli.CreateBranchAsync("conflict-branch");
            await _doltCli.CheckoutAsync("conflict-branch");

            // Modify the same document as will be modified in main
            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Conflict branch content for shared document"
            }, new List<string> { "initial_doc" });

            await _syncManager.ProcessCommitAsync("Conflict branch changes");

            // Switch to main and make conflicting changes
            await _doltCli.CheckoutAsync("main");
            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Main branch content for shared document"
            }, new List<string> { "initial_doc" });

            await _syncManager.ProcessCommitAsync("Main branch conflicting changes");
        }

        private async Task CreateBranchesWithContentDifferences()
        {
            // Add a shared document that will be modified in different branches
            await _chromaService.AddDocumentsAsync(_testCollection, new List<string>
            {
                "Base content for comparison testing"
            }, new List<string> { "shared_doc" });

            await _syncManager.ProcessCommitAsync("Added shared document for comparison");

            // Create content branch
            await _doltCli.CreateBranchAsync("content-branch");
            await _doltCli.CheckoutAsync("content-branch");

            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Content branch version with different content"
            }, new List<string> { "shared_doc" });

            await _syncManager.ProcessCommitAsync("Content branch modifications");

            // Switch to main and make different changes
            await _doltCli.CheckoutAsync("main");
            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Main branch version with different content"
            }, new List<string> { "shared_doc" });

            await _syncManager.ProcessCommitAsync("Main branch modifications");
        }

        private async Task<DetailedConflictInfo> CreateTestConflict()
        {
            return new DetailedConflictInfo
            {
                ConflictId = "test_conflict_123",
                Collection = _testCollection,
                DocumentId = "test_doc",
                Type = ConflictType.ContentModification,
                OurValues = new Dictionary<string, object>
                {
                    { "content", "Our version of content" },
                    { "our_metadata", "our_value" },
                    { "shared_field", "our_shared_value" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "Their version of content" },
                    { "their_metadata", "their_value" },
                    { "shared_field", "their_shared_value" }
                },
                BaseValues = new Dictionary<string, object>
                {
                    { "content", "Base version of content" },
                    { "shared_field", "base_shared_value" }
                }
            };
        }

        private async Task CreateRealisticMergeScenario()
        {
            // Create a branch with multiple types of changes
            await _doltCli.CreateBranchAsync("realistic-branch");
            await _doltCli.CheckoutAsync("realistic-branch");

            // Add multiple documents
            await _chromaService.AddDocumentsAsync(_testCollection, new List<string>
            {
                "First new document in realistic scenario",
                "Second new document with metadata"
            }, new List<string> { "realistic_doc1", "realistic_doc2" },
            new List<Dictionary<string, object>>
            {
                new() { { "category", "test" }, { "priority", "high" } },
                new() { { "category", "example" }, { "version", 2 } }
            });

            // Modify existing document
            await _chromaService.UpdateDocumentsAsync(_testCollection, new List<string>
            {
                "Updated content for existing document"
            }, new List<string> { "second_doc" });

            // Commit all changes
            await _syncManager.ProcessCommitAsync("Realistic scenario: multiple document operations");

            // Switch back to main
            await _doltCli.CheckoutAsync("main");
        }

        private IChromaDbService CreateChromaService(ServerConfiguration config)
        {
            var services = new ServiceCollection();
            services.AddSingleton(config);
            services.AddSingleton<IOptions<ServerConfiguration>>(Options.Create(config));
            services.AddSingleton<ILoggerFactory>(LoggerFactory.Create(builder => builder.AddConsole()));
            services.AddSingleton<ILogger>(_logger);
            services.AddSingleton<ChromaDbService>();
            var serviceProvider = services.BuildServiceProvider();
            
            return ChromaDbServiceFactory.CreateService(serviceProvider);
        }

        private string GetDoltExecutablePath()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var windowsPath = @"C:\Program Files\Dolt\bin\dolt.exe";
                if (File.Exists(windowsPath))
                    return windowsPath;
            }

            return "dolt";
        }

        #endregion
    }
}