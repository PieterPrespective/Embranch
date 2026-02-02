using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;
using Moq;

namespace EmbranchTesting.IntegrationTests
{
    /// <summary>
    /// Integration tests for PP13-97: Merge ChromaDB Sync Incomplete.
    /// Tests that document deletions from source branches are properly synced to ChromaDB
    /// after merge operations, especially for fast-forward merges and single-chunk documents.
    /// </summary>
    [TestFixture]
    public class PP13_97_MergeDeletionSyncTests
    {
        private ILogger<PP13_97_MergeDeletionSyncTests> _logger;
        private IDoltCli _doltCli;
        private IChromaDbService _chromaService;
        private ISyncManagerV2 _syncManager;
        private SqliteDeletionTracker _deletionTracker;

        private string _testCollection = "pp13-97-test-collection";
        private string _tempRepoPath;
        private string _tempDataPath;

        [SetUp]
        public async Task Setup()
        {
            // Initialize Python context if not already initialized (for standalone test runs)
            if (!PythonContext.IsInitialized)
            {
                var setupLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var setupLogger = setupLoggerFactory.CreateLogger<PP13_97_MergeDeletionSyncTests>();
                var pythonDll = PythonContextUtility.FindPythonDll(setupLogger);
                PythonContext.Initialize(setupLogger, pythonDll);
            }

            // Create unique paths for this test
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "PP13_97_Tests", Guid.NewGuid().ToString());
            _tempDataPath = Path.Combine(_tempRepoPath, "data");
            Directory.CreateDirectory(_tempRepoPath);
            Directory.CreateDirectory(_tempDataPath);

            // Create logger factory and individual loggers
            var loggerFactory = LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug));
            _logger = loggerFactory.CreateLogger<PP13_97_MergeDeletionSyncTests>();
            var doltLogger = loggerFactory.CreateLogger<DoltCli>();
            var syncLogger = loggerFactory.CreateLogger<SyncManagerV2>();
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

            // Initialize Dolt repository
            await InitializeTestRepository();
        }

        [TearDown]
        public async Task TearDown()
        {
            try
            {
                // Clean up ChromaDB collection
                if (_chromaService != null)
                {
                    try
                    {
                        await _chromaService.DeleteCollectionAsync(_testCollection);
                    }
                    catch
                    {
                        // Collection may not exist
                    }
                }

                // PP13-97: Dispose deletion tracker
                _deletionTracker?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during test teardown");
            }
        }

        private async Task InitializeTestRepository()
        {
            // Change to test directory
            Directory.SetCurrentDirectory(_tempRepoPath);

            // Initialize Dolt
            await _doltCli.InitAsync();

            // Initialize deletion tracker
            await _deletionTracker.InitializeAsync(_tempRepoPath);
        }

        private IChromaDbService CreateChromaService(ServerConfiguration config)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug));
            var chromaLogger = loggerFactory.CreateLogger<ChromaPythonService>();
            return new ChromaPythonService(chromaLogger, Options.Create(config));
        }

        private static string GetDoltExecutablePath()
        {
            var possiblePaths = new[]
            {
                "dolt",
                "/usr/local/bin/dolt",
                Environment.GetEnvironmentVariable("DOLT_PATH") ?? ""
            };

            foreach (var path in possiblePaths.Where(p => !string.IsNullOrEmpty(p)))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "version",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit(5000);
                    if (process?.ExitCode == 0)
                    {
                        return path;
                    }
                }
                catch
                {
                    // Try next path
                }
            }

            return "dolt"; // Default, let it fail if not found
        }

        /// <summary>
        /// Helper method to add a document to both Dolt and ChromaDB.
        /// </summary>
        private async Task AddDocumentToDoltAndChroma(string docId, string content)
        {
            // Add to ChromaDB
            await _chromaService.AddDocumentsAsync(
                _testCollection,
                new List<string> { content },
                new List<string> { docId },
                new List<Dictionary<string, object>> { new() { ["doc_id"] = docId } },
                allowDuplicateIds: true,
                markAsLocalChange: false);

            // Add to Dolt using QueryAsync (compatible with INSERT statements)
            var sql = $@"
                INSERT INTO documents (doc_id, collection_name, title, content, content_hash, doc_type, metadata)
                VALUES ('{docId}', '{_testCollection}', '{docId}', '{content.Replace("'", "''")}',
                        '{Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16)}', 'text', '{{}}')";

            await _doltCli.QueryAsync<dynamic>(sql);
        }

        /// <summary>
        /// Helper method to delete a document from Dolt only.
        /// </summary>
        private async Task DeleteDocumentFromDolt(string docId)
        {
            var sql = $"DELETE FROM documents WHERE doc_id = '{docId}'";
            await _doltCli.QueryAsync<dynamic>(sql);
        }

        /// <summary>
        /// Helper method to get ChromaDB document count.
        /// </summary>
        private async Task<int> GetChromaDocumentCount()
        {
            try
            {
                var results = await _chromaService.GetDocumentsAsync(_testCollection);
                if (results is Dictionary<string, object> dict &&
                    dict.TryGetValue("ids", out var idsObj) &&
                    idsObj is List<object> idList)
                {
                    // Extract unique base document IDs
                    var uniqueDocIds = new HashSet<string>();
                    foreach (var id in idList)
                    {
                        var chunkId = id?.ToString() ?? "";
                        var lastChunkIndex = chunkId.LastIndexOf("_chunk_");
                        var docId = lastChunkIndex > 0 ? chunkId.Substring(0, lastChunkIndex) : chunkId;
                        if (!string.IsNullOrEmpty(docId))
                        {
                            uniqueDocIds.Add(docId);
                        }
                    }
                    return uniqueDocIds.Count;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Helper method to get Dolt document count.
        /// </summary>
        private async Task<int> GetDoltDocumentCount()
        {
            try
            {
                var sql = $"SELECT COUNT(*) as cnt FROM documents WHERE collection_name = '{_testCollection}'";
                var results = await _doltCli.QueryAsync<dynamic>(sql);
                var row = results?.FirstOrDefault();
                if (row != null)
                {
                    if (row is System.Text.Json.JsonElement jsonElement)
                    {
                        if (jsonElement.TryGetProperty("cnt", out var cntProp))
                        {
                            return cntProp.GetInt32();
                        }
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// PP13-97: Single-chunk documents (stored without _chunk_ suffix) should be
        /// properly deleted during reconciliation after merge.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task ReconcileDoltToChroma_ShouldDeleteSingleChunkDocuments()
        {
            var doltAvailable = await _doltCli.CheckDoltAvailableAsync();
            if (!doltAvailable.Success)
            {
                Assert.Ignore("Dolt not available for integration test");
            }

            _logger.LogInformation("PP13-97: Starting single-chunk document deletion test");

            // Arrange: Create documents table with all required columns
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS documents (
                    doc_id VARCHAR(255) PRIMARY KEY,
                    collection_name VARCHAR(255) NOT NULL,
                    title VARCHAR(255),
                    content TEXT,
                    content_hash VARCHAR(64),
                    doc_type VARCHAR(100),
                    metadata JSON,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";
            await _doltCli.ExecuteAsync(createTableSql);

            // Add 3 small documents (single-chunk - stored without _chunk_ suffix)
            await AddDocumentToDoltAndChroma("doc1", "Short content 1");
            await AddDocumentToDoltAndChroma("doc2", "Short content 2");
            await AddDocumentToDoltAndChroma("doc3", "Short content 3");

            // Commit initial state
            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Initial 3 documents");

            var initialChromaCount = await GetChromaDocumentCount();
            var initialDoltCount = await GetDoltDocumentCount();
            _logger.LogInformation("PP13-97: Initial state - ChromaDB: {ChromaCount}, Dolt: {DoltCount}",
                initialChromaCount, initialDoltCount);
            Assert.That(initialChromaCount, Is.EqualTo(3), "Expected 3 documents in ChromaDB initially");
            Assert.That(initialDoltCount, Is.EqualTo(3), "Expected 3 documents in Dolt initially");

            // Create feature branch
            await _doltCli.CheckoutAsync("feature-delete", createNew: true);

            // Delete doc2 from Dolt on feature branch
            await DeleteDocumentFromDolt("doc2");
            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Delete doc2");

            var featureDoltCount = await GetDoltDocumentCount();
            _logger.LogInformation("PP13-97: After deletion on feature branch - Dolt: {DoltCount}", featureDoltCount);
            Assert.That(featureDoltCount, Is.EqualTo(2), "Expected 2 documents in Dolt after deletion");

            // Checkout main and merge feature branch
            await _doltCli.CheckoutAsync("main");
            _logger.LogInformation("PP13-97: Merging feature-delete into main");

            // Act: Merge with sync
            var mergeResult = await _syncManager.ProcessMergeAsync("feature-delete", force: true);

            // Assert
            Assert.That(mergeResult.Status, Is.EqualTo(SyncStatusV2.Completed),
                "PP13-97: Merge should complete successfully");

            var finalChromaCount = await GetChromaDocumentCount();
            var finalDoltCount = await GetDoltDocumentCount();
            _logger.LogInformation("PP13-97: After merge - ChromaDB: {ChromaCount}, Dolt: {DoltCount}",
                finalChromaCount, finalDoltCount);

            Assert.That(finalDoltCount, Is.EqualTo(2),
                "PP13-97: Expected 2 documents in Dolt after merge");
            Assert.That(finalChromaCount, Is.EqualTo(2),
                "PP13-97: Expected 2 documents in ChromaDB after merge (doc2 should be deleted)");
            Assert.That(finalChromaCount, Is.EqualTo(finalDoltCount),
                "PP13-97: ChromaDB count should match Dolt count after merge");
        }

        /// <summary>
        /// PP13-97: After a fast-forward merge, documents deleted on the source branch
        /// should be removed from ChromaDB.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task FastForwardMerge_ShouldRemoveDeletedDocumentsFromChroma()
        {
            var doltAvailable = await _doltCli.CheckDoltAvailableAsync();
            if (!doltAvailable.Success)
            {
                Assert.Ignore("Dolt not available for integration test");
            }

            _logger.LogInformation("PP13-97: Starting fast-forward merge deletion test");

            // Arrange: Create documents table with all required columns
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS documents (
                    doc_id VARCHAR(255) PRIMARY KEY,
                    collection_name VARCHAR(255) NOT NULL,
                    title VARCHAR(255),
                    content TEXT,
                    content_hash VARCHAR(64),
                    doc_type VARCHAR(100),
                    metadata JSON,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";
            await _doltCli.ExecuteAsync(createTableSql);

            // Add 5 documents (matches Linux test scenario)
            for (int i = 1; i <= 5; i++)
            {
                await AddDocumentToDoltAndChroma($"doc{i}", $"Document {i} content");
            }

            // Commit initial state on main
            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Initial 5 documents");

            var initialChromaCount = await GetChromaDocumentCount();
            _logger.LogInformation("PP13-97: Initial ChromaDB count: {Count}", initialChromaCount);
            Assert.That(initialChromaCount, Is.EqualTo(5), "Expected 5 documents initially");

            // Create feature branch
            await _doltCli.CheckoutAsync("feature-updates", createNew: true);

            // On feature branch: add doc6, update doc5, delete doc4
            await AddDocumentToDoltAndChroma("doc6", "New document 6 content");

            // Update doc5 (just in Dolt for now)
            await _doltCli.ExecuteAsync($"UPDATE documents SET content = 'Updated doc5 content' WHERE doc_id = 'doc5'");

            // Delete doc4
            await DeleteDocumentFromDolt("doc4");

            // Commit on feature branch
            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Add doc6, update doc5, delete doc4");

            // Checkout main (main has no changes since branch, so merge will be fast-forward)
            await _doltCli.CheckoutAsync("main");
            _logger.LogInformation("PP13-97: Merging feature-updates into main (should be fast-forward)");

            // Act: Merge with sync
            var mergeResult = await _syncManager.ProcessMergeAsync("feature-updates", force: true);

            // Assert
            Assert.That(mergeResult.Status, Is.EqualTo(SyncStatusV2.Completed),
                "PP13-97: Merge should complete successfully");

            var finalChromaCount = await GetChromaDocumentCount();
            var finalDoltCount = await GetDoltDocumentCount();
            _logger.LogInformation("PP13-97: After fast-forward merge - ChromaDB: {ChromaCount}, Dolt: {DoltCount}",
                finalChromaCount, finalDoltCount);

            // Expected: 5 initial - 1 deleted (doc4) + 1 added (doc6) = 5
            Assert.That(finalDoltCount, Is.EqualTo(5),
                "PP13-97: Expected 5 documents in Dolt after merge (5 - 1 deleted + 1 added)");
            Assert.That(finalChromaCount, Is.EqualTo(5),
                "PP13-97: Expected 5 documents in ChromaDB after merge (doc4 should be deleted, doc6 added)");
            Assert.That(finalChromaCount, Is.EqualTo(finalDoltCount),
                "PP13-97: ChromaDB count should match Dolt count after fast-forward merge");
        }

        /// <summary>
        /// PP13-97: Reconciliation should properly sync documents after merge
        /// even when delta sync reports no deletions.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task Merge_ReconciliationShouldCatchMissedDeletions()
        {
            var doltAvailable = await _doltCli.CheckDoltAvailableAsync();
            if (!doltAvailable.Success)
            {
                Assert.Ignore("Dolt not available for integration test");
            }

            _logger.LogInformation("PP13-97: Starting reconciliation missed deletions test");

            // Arrange: Create documents table with all required columns
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS documents (
                    doc_id VARCHAR(255) PRIMARY KEY,
                    collection_name VARCHAR(255) NOT NULL,
                    title VARCHAR(255),
                    content TEXT,
                    content_hash VARCHAR(64),
                    doc_type VARCHAR(100),
                    metadata JSON,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";
            await _doltCli.ExecuteAsync(createTableSql);

            // Add documents
            await AddDocumentToDoltAndChroma("alpha", "Alpha content");
            await AddDocumentToDoltAndChroma("beta", "Beta content");
            await AddDocumentToDoltAndChroma("gamma", "Gamma content");
            await AddDocumentToDoltAndChroma("delta", "Delta content");

            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Initial 4 documents");

            // Create branch and make changes
            await _doltCli.CheckoutAsync("cleanup-branch", createNew: true);

            // Delete two documents
            await DeleteDocumentFromDolt("beta");
            await DeleteDocumentFromDolt("delta");

            await _doltCli.AddAsync(".");
            await _doltCli.CommitAsync("Delete beta and delta");

            // Return to main and merge
            await _doltCli.CheckoutAsync("main");

            // Act
            var mergeResult = await _syncManager.ProcessMergeAsync("cleanup-branch", force: true);

            // Assert
            Assert.That(mergeResult.Status, Is.EqualTo(SyncStatusV2.Completed));

            var finalChromaCount = await GetChromaDocumentCount();
            var finalDoltCount = await GetDoltDocumentCount();

            _logger.LogInformation("PP13-97: After merge - ChromaDB: {ChromaCount}, Dolt: {DoltCount}",
                finalChromaCount, finalDoltCount);

            Assert.That(finalDoltCount, Is.EqualTo(2), "Expected 2 documents in Dolt (alpha, gamma)");
            Assert.That(finalChromaCount, Is.EqualTo(2), "Expected 2 documents in ChromaDB after reconciliation");
            Assert.That(finalChromaCount, Is.EqualTo(finalDoltCount),
                "PP13-97: ChromaDB and Dolt counts should match after reconciliation catches missed deletions");
        }

        /// <summary>
        /// PP13-97: DocumentIdResolver pattern matching should find single-chunk documents.
        /// Unit-level test for the pattern matching fix.
        /// </summary>
        [Test]
        public async Task DocumentIdResolver_ShouldFindSingleChunkDocuments()
        {
            // Create a collection with a single-chunk document (no _chunk_ suffix)
            await _chromaService.AddDocumentsAsync(
                _testCollection,
                new List<string> { "Short content that fits in one chunk" },
                new List<string> { "single-chunk-doc" },
                new List<Dictionary<string, object>> { new() { ["doc_id"] = "single-chunk-doc" } },
                allowDuplicateIds: true,
                markAsLocalChange: false);

            // Verify the document was added with its base ID (not _chunk_0)
            var results = await _chromaService.GetDocumentsAsync(_testCollection);
            Assert.That(results, Is.Not.Null);

            var resultsDict = results as Dictionary<string, object>;
            Assert.That(resultsDict, Is.Not.Null);

            var ids = resultsDict.GetValueOrDefault("ids") as List<object>;
            Assert.That(ids, Is.Not.Null);
            Assert.That(ids, Has.Count.GreaterThanOrEqualTo(1));

            // Check that the ID is the base ID without _chunk_ suffix
            var firstId = ids[0]?.ToString();
            _logger.LogInformation("PP13-97: Single-chunk document stored with ID: {Id}", firstId);

            // The ID should either be the base ID or have _chunk_ suffix
            // For single-chunk docs, it should be the base ID
            Assert.That(firstId, Is.EqualTo("single-chunk-doc"),
                "PP13-97: Single-chunk document should be stored with base ID, not _chunk_0 suffix");

            // Now test that deletion works
            var deleteResult = await _chromaService.DeleteDocumentsAsync(
                _testCollection,
                new List<string> { "single-chunk-doc" },
                expandChunks: true);

            Assert.That(deleteResult, Is.True, "PP13-97: Deletion should succeed for single-chunk document");

            // Verify deletion
            var afterDelete = await _chromaService.GetDocumentsAsync(_testCollection);
            var afterDeleteDict = afterDelete as Dictionary<string, object>;
            var afterDeleteIds = afterDeleteDict?.GetValueOrDefault("ids") as List<object>;

            Assert.That(afterDeleteIds == null || afterDeleteIds.Count == 0,
                "PP13-97: Document should be deleted from ChromaDB");
        }
    }
}
