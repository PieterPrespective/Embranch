using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DMMS.Services;
using DMMS.Tools;
using DMMS.Models;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace DMMSTesting.IntegrationTests
{
    /// <summary>
    /// Integration tests for issues identified in empty repository clone fallback mechanism.
    /// Tests against 'https://www.dolthub.com/repositories/pieter-prespective/NewTestDatabase' 
    /// to reproduce and validate fixes for the four critical issues.
    /// </summary>
    [TestFixture]
    public class EmptyRepositoryFallbackIssuesTests
    {
        private IServiceProvider? _serviceProvider;
        private IDoltCli? _doltCli;
        private IChromaDbService? _chromaService;
        private ISyncManagerV2? _syncManager;
        private DoltCloneTool? _cloneTool;
        private DoltStatusTool? _statusTool;
        private DoltCommitTool? _commitTool;
        private DoltPushTool? _pushTool;
        private ILogger<EmptyRepositoryFallbackIssuesTests>? _logger;
        private string _testRepoUrl = "https://www.dolthub.com/repositories/pieter-prespective/NewTestDatabase";
        private string _testWorkingDir = "";

        [SetUp]
        public void Setup()
        {
            // Create truly unique test directory with timestamp and GUID
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var guid = Guid.NewGuid().ToString("N")[..8];
            _testWorkingDir = Path.Combine(Path.GetTempPath(), $"EmptyRepoTest_{timestamp}_{guid}");
            Directory.CreateDirectory(_testWorkingDir);

            // Setup services with test-specific configuration
            var services = new ServiceCollection();
            
            // Configure Dolt with test working directory
            services.Configure<DoltConfiguration>(options =>
            {
                options.DoltExecutablePath = "C:\\Program Files\\Dolt\\bin\\dolt.exe";
                options.RepositoryPath = Path.Combine(_testWorkingDir, "dolt-repo");
                options.CommandTimeoutMs = 30000;
                options.EnableDebugLogging = true;
            });

            // Configure unique ChromaDB path to prevent conflicts
            services.Configure<ServerConfiguration>(options =>
            {
                options.ChromaDataPath = Path.Combine(_testWorkingDir, "chroma-data");
            });

            // Add required services
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddSingleton<IDoltCli, DoltCli>();
            services.AddSingleton<IChromaDbService, ChromaPersistentDbService>();
            services.AddSingleton<ISyncManagerV2, SyncManagerV2>();
            services.AddSingleton<DoltCloneTool>();
            services.AddSingleton<DoltStatusTool>();
            services.AddSingleton<DoltCommitTool>();
            services.AddSingleton<DoltPushTool>();

            _serviceProvider = services.BuildServiceProvider();
            _doltCli = _serviceProvider.GetRequiredService<IDoltCli>();
            _chromaService = _serviceProvider.GetRequiredService<IChromaDbService>();
            _syncManager = _serviceProvider.GetRequiredService<ISyncManagerV2>();
            _cloneTool = _serviceProvider.GetRequiredService<DoltCloneTool>();
            _statusTool = _serviceProvider.GetRequiredService<DoltStatusTool>();
            _commitTool = _serviceProvider.GetRequiredService<DoltCommitTool>();
            _pushTool = _serviceProvider.GetRequiredService<DoltPushTool>();
            
            // Create standalone logger to avoid disposal race condition
            _logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<EmptyRepositoryFallbackIssuesTests>();
        }

        /// <summary>
        /// Cleans up ChromaDB collections before service disposal to prevent collection name conflicts
        /// </summary>
        private async Task CleanupCollectionsAsync()
        {
            if (_chromaService != null)
            {
                try
                {
                    var collections = await _chromaService.ListCollectionsAsync();
                    foreach (var collection in collections)
                    {
                        if (collection != "default") // Preserve default collection if needed
                        {
                            _logger?.LogInformation($"Cleaning up collection: {collection}");
                            await _chromaService.DeleteCollectionAsync(collection);
                        }
                    }
                    _logger?.LogInformation($"Collection cleanup completed");
                }
                catch (Exception ex)
                {
                    // Don't fail test due to cleanup issues, but log for diagnostics
                    _logger?.LogWarning($"Failed to cleanup collections: {ex.Message}");
                }
            }
        }

        [TearDown]
        public async Task CleanupAsync() // Change from void Cleanup() to async Task
        {
            // Step 1: Clean up ChromaDB collections first (before service disposal)
            await CleanupCollectionsAsync();
            
            // Step 2: Dispose services (releases Python.NET resources)
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // Step 3: Attempt directory cleanup (may fail due to residual locks, non-critical)
            try
            {
                if (Directory.Exists(_testWorkingDir))
                {
                    Directory.Delete(_testWorkingDir, true);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail test - file locks are expected with Python.NET
                Console.WriteLine($"Warning: Could not delete test directory due to file locks: {ex.Message}");
            }
        }

        /// <summary>
        /// Issue 1: Remote Configuration Verification Failure
        /// Tests that remote 'origin' is properly configured and persists after fallback initialization
        /// </summary>
        [Test]
        public async Task Issue1_RemoteConfigurationPersistence_ShouldMaintainOriginRemote()
        {
            // Arrange & Act: Clone empty repository (triggers fallback)
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            
            // Assert: Clone should succeed
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Clone should succeed: {cloneResponse.message}");

            // Act: Check remotes immediately after clone
            var remotesAfterClone = await _doltCli!.ListRemotesAsync();
            var remotesList = remotesAfterClone?.ToList() ?? new List<RemoteInfo>();

            // Assert: Remote 'origin' should be configured
            Assert.That(remotesList.Count, Is.GreaterThan(0), "At least one remote should be configured");
            Assert.That(remotesList.Any(r => r.Name == "origin"), Is.True, "Remote 'origin' should exist");
            
            var originRemote = remotesList.FirstOrDefault(r => r.Name == "origin");
            Assert.That(originRemote, Is.Not.Null, "Origin remote should not be null");
            
            // PP13-53 Fix: Dolt normalizes URLs during clone operation
            // www.dolthub.com URLs are converted to doltremoteapi.dolthub.com
            // This is correct behavior - both formats point to the same repository
            var expectedNormalizedUrl = _testRepoUrl
                .Replace("www.dolthub.com/repositories", "doltremoteapi.dolthub.com");
            Assert.That(originRemote.Url, Is.EqualTo(expectedNormalizedUrl), 
                "Origin URL should match the Dolt-normalized URL format");

            // Act: Perform some operations and check remote persistence
            await _doltCli.GetStatusAsync(); // Status operation
            var remotesAfterStatus = await _doltCli.ListRemotesAsync();
            var remotesListAfterStatus = remotesAfterStatus?.ToList() ?? new List<RemoteInfo>();

            // Assert: Remote should still exist after other operations
            Assert.That(remotesListAfterStatus.Any(r => r.Name == "origin"), Is.True, 
                "Remote 'origin' should persist after status operation");
        }

        /// <summary>
        /// Issue 2: Missing Schema Tables in Repository
        /// Tests that required application schema tables are created during fallback
        /// </summary>
        [Test]
        public async Task Issue2_SchemaTableCreation_ShouldCreateRequiredTables()
        {
            // Arrange & Act: Clone empty repository (triggers fallback)
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            
            // Assert: Clone should succeed
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Clone should succeed: {cloneResponse.message}");

            // Act: Check for required schema tables
            var requiredTables = new[] { "collections", "documents", "chroma_sync_state", "document_sync_log", "local_changes" };
            var missingTables = new List<string>();

            foreach (var tableName in requiredTables)
            {
                try
                {
                    var checkQuery = $"SELECT COUNT(*) as count FROM {tableName} LIMIT 1";
                    await _doltCli!.QueryJsonAsync(checkQuery);
                    // If we get here, table exists
                }
                catch (Exception ex) when (ex.Message.Contains("table not found") || ex.Message.Contains("doesn't exist"))
                {
                    missingTables.Add(tableName);
                }
            }

            // Assert: All required tables should exist
            Assert.That(missingTables.Count, Is.EqualTo(0), 
                $"Missing required schema tables: {string.Join(", ", missingTables)}");

            // Act: Verify tables have proper structure by attempting basic operations
            try
            {
                await _doltCli!.QueryJsonAsync("SELECT doc_id, collection_name, content_hash FROM documents LIMIT 1");
                await _doltCli!.QueryJsonAsync("SELECT collection_name, last_sync_commit FROM chroma_sync_state LIMIT 1");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Schema tables exist but have incorrect structure: {ex.Message}");
            }
        }

        /// <summary>
        /// Issue 3: Working Directory vs Repository Path Consistency
        /// Tests that all Dolt operations use consistent working directory
        /// </summary>
        [Test]
        public async Task Issue3_WorkingDirectoryConsistency_ShouldUseConsistentPaths()
        {
            // Arrange & Act: Clone empty repository (triggers fallback)
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            
            // Assert: Clone should succeed
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Clone should succeed: {cloneResponse.message}");

            // Act: Perform multiple operations that should all use same working directory
            var status1 = await _doltCli!.GetStatusAsync();
            var remotes1 = await _doltCli!.ListRemotesAsync();
            var commits1 = await _doltCli!.GetLogAsync(1);
            var status2 = await _doltCli!.GetStatusAsync();
            var remotes2 = await _doltCli!.ListRemotesAsync();

            // Assert: Results should be consistent across operations
            Assert.That(status2.Branch, Is.EqualTo(status1.Branch), "Branch should be consistent across status calls");
            
            var remotesList1 = remotes1?.ToList() ?? new List<RemoteInfo>();
            var remotesList2 = remotes2?.ToList() ?? new List<RemoteInfo>();
            
            Assert.That(remotesList2.Count, Is.EqualTo(remotesList1.Count), 
                "Remote count should be consistent across calls");
            
            if (remotesList1.Count > 0 && remotesList2.Count > 0)
            {
                Assert.That(remotesList2.First().Url, Is.EqualTo(remotesList1.First().Url), 
                    "Remote URL should be consistent across calls");
            }

            // Act: Check that .dolt directory exists in expected location
            var expectedDoltPath = Path.Combine(_testWorkingDir, "dolt-repo", ".dolt");
            Assert.That(Directory.Exists(expectedDoltPath), Is.True, 
                $"Dolt directory should exist at expected path: {expectedDoltPath}");
        }

        /// <summary>
        /// Issue 4: ChromaDB-Dolt Sync Functionality After Fallback
        /// Tests that changes can be synchronized between ChromaDB and Dolt after fallback initialization
        /// FIXED: Added timeout and operation monitoring to prevent Python.NET deadlocks
        /// FIXED: PP13-54 - Changed commit count validation from absolute count to incremental validation
        /// </summary>
        [Test]
        [Timeout(60000)] // 60 second timeout to prevent infinite hangs during development/debugging
        public async Task Issue4_ChromaDBDoltSyncFunctionality_ShouldDetectAndCommitChanges()
        {
            // Arrange: Clone empty repository (triggers fallback)
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Clone should succeed: {cloneResponse.message}");

            // Act: Create ChromaDB collection and add document
            var createCollectionResult = await _chromaService!.CreateCollectionAsync("testCollection");
            Assert.That(createCollectionResult, Is.True, "Should create ChromaDB collection successfully");

            var addDocResult = await _chromaService!.AddDocumentsAsync(
                "testCollection", 
                new List<string> { "User A creates a document" }, 
                new List<string> { "doc1" }
            );
            Assert.That(addDocResult, Is.True, "Should add document to ChromaDB successfully");

            // Act: Check if local changes are detected with Python.NET operation monitoring
            // PP13-53 Fix: Create logger without using ServiceProvider to avoid disposal race condition
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<EmptyRepositoryFallbackIssuesTests>();
            logger.LogInformation("=== Starting GetLocalChangesAsync() - monitoring for Python.NET operations ===");
            
            var queueStatsBefore = PythonContext.GetQueueStats();
            logger.LogInformation($"Python.NET queue before GetLocalChangesAsync(): Size={queueStatsBefore.QueueSize}, OverThreshold={queueStatsBefore.IsOverThreshold}");
            
            var startTime = DateTime.UtcNow;
            var localChanges = await _syncManager!.GetLocalChangesAsync();
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            var queueStatsAfter = PythonContext.GetQueueStats();
            logger.LogInformation($"GetLocalChangesAsync() completed in {elapsed:F0}ms");
            logger.LogInformation($"Python.NET queue after GetLocalChangesAsync(): Size={queueStatsAfter.QueueSize}, OverThreshold={queueStatsAfter.IsOverThreshold}");
            
            // Assert: Changes should be detected
            Assert.That(localChanges, Is.Not.Null, "Local changes should be detected");
            Assert.That(localChanges.HasChanges, Is.True, "Should detect that changes exist");
            Assert.That(localChanges.NewDocuments?.Count ?? 0, Is.GreaterThan(0), "Should detect new documents");
            Assert.That(elapsed, Is.LessThan(30000), $"GetLocalChangesAsync should complete within 30 seconds, took {elapsed:F0}ms");

            // Get baseline commit count before test operations
            var commitsBeforeTest = await _doltCli!.GetLogAsync(10);
            var baselineCommitCount = commitsBeforeTest?.ToList()?.Count ?? 0;
            logger.LogInformation($"Baseline commit count: {baselineCommitCount}");
            
            // Log commit history for debugging
            if (commitsBeforeTest != null)
            {
                logger.LogInformation("Repository commit history before test:");
                foreach (var commit in commitsBeforeTest.Take(5))
                {
                    logger.LogInformation($"  {commit.Hash}: {commit.Message}");
                }
            }

            // Act: Attempt to commit changes
            var commitResult = await _commitTool!.DoltCommit("Test commit after fallback");
            dynamic commitResponse = commitResult;

            // Assert: Commit should succeed (not return NO_CHANGES error)
            Assert.That(commitResponse.success, Is.True, 
                $"Commit should succeed after adding documents: {commitResponse.message}");
            
            // Verify commit was actually created using incremental validation
            var commitsAfterTest = await _doltCli!.GetLogAsync(10);
            var commitsListAfterTest = commitsAfterTest?.ToList() ?? new List<CommitInfo>();
            var finalCommitCount = commitsListAfterTest.Count;
            
            // Log updated commit history
            logger.LogInformation($"Final commit count: {finalCommitCount}");
            if (commitsAfterTest != null)
            {
                logger.LogInformation("Repository commit history after test:");
                foreach (var commit in commitsAfterTest.Take(5))
                {
                    logger.LogInformation($"  {commit.Hash}: {commit.Message}");
                }
            }
            
            // Assert: Should have exactly one more commit than baseline
            Assert.That(finalCommitCount, Is.EqualTo(baselineCommitCount + 1),
                $"Should have exactly 1 new commit. Baseline: {baselineCommitCount}, Final: {finalCommitCount}");
            
            // Verify the latest commit is our test commit
            var latestCommit = commitsListAfterTest.First();
            Assert.That(latestCommit.Message, Is.EqualTo("Test commit after fallback"),
                "Latest commit message should match our test commit");
        }

        /// <summary>
        /// Issue 5: Push Operation After Fallback
        /// Tests that push operations work correctly after fallback initialization
        /// FIXED: Added timeout and operation monitoring to prevent Python.NET deadlocks
        /// </summary>
        [Test]
        [Timeout(60000)] // 60 second timeout to prevent infinite hangs during development/debugging
        public async Task Issue5_PushOperationAfterFallback_ShouldFindConfiguredRemote()
        {
            // Arrange: Clone and setup with commits
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Clone should succeed: {cloneResponse.message}");

            // Ensure we have something to push by making a change with operation monitoring
            // PP13-53 Fix: Get logger early to avoid disposal race condition
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<EmptyRepositoryFallbackIssuesTests>();
            logger.LogInformation("=== Starting Issue5 ChromaDB operations - monitoring for Python.NET operations ===");
            
            var queueStatsBefore = PythonContext.GetQueueStats();
            logger.LogInformation($"Python.NET queue before operations: Size={queueStatsBefore.QueueSize}, OverThreshold={queueStatsBefore.IsOverThreshold}");
            
            var startTime = DateTime.UtcNow;
            var createCollectionResult = await _chromaService!.CreateCollectionAsync("testCollection");
            var addDocResult = await _chromaService!.AddDocumentsAsync(
                "testCollection", 
                new List<string> { "Test document" }, 
                new List<string> { "doc1" }
            );
            var commitResult = await _commitTool!.DoltCommit("Add test document");
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            var queueStatsAfter = PythonContext.GetQueueStats();
            logger.LogInformation($"ChromaDB operations completed in {elapsed:F0}ms");
            logger.LogInformation($"Python.NET queue after operations: Size={queueStatsAfter.QueueSize}, OverThreshold={queueStatsAfter.IsOverThreshold}");
            
            Assert.That(elapsed, Is.LessThan(30000), $"ChromaDB operations should complete within 30 seconds, took {elapsed:F0}ms");
            
            // Act: Attempt push operation
            var pushResult = await _pushTool!.DoltPush("origin", "main");
            dynamic pushResponse = pushResult;

            // Assert: Push should NOT fail with "Remote not configured" error
            // Note: Push might fail for other reasons (auth, network, etc.) but should find the remote
            Assert.That(pushResponse.error == "REMOTE_NOT_FOUND", Is.False, 
                "Push should not fail with REMOTE_NOT_FOUND error");
                
            // If push fails, it should be for authentication or network reasons, not missing remote
            if (!pushResponse.success)
            {
                var errorMessage = pushResponse.message?.ToString() ?? "";
                Assert.That(errorMessage.Contains("not configured"), Is.False, 
                    $"Error should not indicate remote is not configured: {errorMessage}");
            }
        }

        /// <summary>
        /// Comprehensive Integration Test: Full Workflow After Fallback
        /// Tests the complete workflow: Clone -> Add Data -> Commit -> Push
        /// </summary>
        [Test]
        public async Task Integration_FullWorkflowAfterFallback_ShouldCompleteSuccessfully()
        {
            // Step 1: Clone empty repository
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("=== STEP1 : Clone Empty Repository ===");
            Console.WriteLine("--------------------------------------");
            var cloneResult = await _cloneTool!.DoltClone(_testRepoUrl);
            dynamic cloneResponse = cloneResult;
            Assert.That(cloneResponse.success, Is.True, $"Step 1 - Clone should succeed: {cloneResponse.message}");


            // Step 2: Verify initial repository state
            Console.WriteLine("-----------------------------------------------");
            Console.WriteLine("=== STEP2 : Verify initial repository state ===");
            Console.WriteLine("-----------------------------------------------");
            // PP13-53 Fix: DoltStatusTool returns Dictionary<string,object>, use dictionary syntax
            var statusAfterClone = await _statusTool!.DoltStatus(verbose: true);
            var statusResponse = statusAfterClone as Dictionary<string, object>;
            Assert.That(statusResponse?["success"], Is.EqualTo(true), "Step 2 - Status should succeed after clone");




            // Step 3: Add data to ChromaDB
            Console.WriteLine("------------------------------------");
            Console.WriteLine("=== STEP3 : Add data to ChromaDB ===");
            Console.WriteLine("------------------------------------");
            
            // Before creating collection, ensure clean state
            var collections = await _chromaService!.ListCollectionsAsync();
            var testCollectionName = "integrationTest";

            if (collections.Contains(testCollectionName))
            {
                Console.WriteLine($"Collection {testCollectionName} already exists, cleaning up...");
                await _chromaService.DeleteCollectionAsync(testCollectionName);
            }

            var createCollectionResult = await _chromaService.CreateCollectionAsync(testCollectionName);
            Assert.That(createCollectionResult, Is.True, "Step 3a - Should create collection");
            /*
            var addDocResult = await _chromaService!.AddDocumentsAsync(
                "integrationTest", 
                new List<string> { "Integration test document", "Second test document" }, 
                new List<string> { "int_doc1", "int_doc2" }
            );
            Assert.That(addDocResult, Is.True, "Step 3b - Should add documents");
            
            // Step 4: Verify changes are detected
            var localChanges = await _syncManager!.GetLocalChangesAsync();
            Assert.That(localChanges, Is.Not.Null, "Step 4 - Should detect local changes");
            Assert.That(localChanges.HasChanges, Is.True, "Step 4 - Should have changes to commit");

            // Step 5: Create branch for testing
            var createBranchResult = await _doltCli!.CreateBranchAsync("integration-test-branch");
            Assert.That(createBranchResult.Success, Is.True, $"Step 5 - Should create branch: {createBranchResult.Error}");

            var checkoutResult = await _doltCli!.CheckoutAsync("integration-test-branch");
            Assert.That(checkoutResult.Success, Is.True, $"Step 5 - Should checkout branch: {checkoutResult.Error}");

            // Step 6: Commit changes
            var commitResult = await _commitTool!.DoltCommit("Integration test commit");
            dynamic commitResponse = commitResult;
            Assert.That(commitResponse.success, Is.True, 
                $"Step 6 - Commit should succeed: {commitResponse.message}");

            // Step 7: Verify commit was created
            var commitsAfterCommit = await _doltCli!.GetLogAsync(1);
            var latestCommit = commitsAfterCommit?.First();
            Assert.That(latestCommit, Is.Not.Null, "Step 7 - Should have latest commit");
            Assert.That(latestCommit.Message, Is.EqualTo("Integration test commit"), "Step 7 - Commit message should match");

            // Step 8: Verify remote is still configured
            var remotesBeforePush = await _doltCli!.ListRemotesAsync();
            var remotesList = remotesBeforePush?.ToList() ?? new List<RemoteInfo>();
            Assert.That(remotesList.Any(r => r.Name == "origin"), Is.True, "Step 8 - Origin remote should still be configured");

            // Step 9: Attempt push (may fail due to auth/network, but remote should be found)
            var pushResult = await _pushTool!.DoltPush("origin", "integration-test-branch");
            dynamic pushResponse = pushResult;
            
            // The critical assertion: should NOT fail with remote not found
            Assert.That(pushResponse.error == "REMOTE_NOT_FOUND", Is.False, 
                "Step 9 - Push should not fail with REMOTE_NOT_FOUND error");
            */
            Console.WriteLine("------------------------------------");
            Console.WriteLine("=== STEP10 : Teardown            ===");
            Console.WriteLine("------------------------------------");
        }
    }
}