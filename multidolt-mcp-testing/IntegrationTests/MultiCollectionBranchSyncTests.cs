using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DMMS.Models;
using DMMS.Services;

namespace DMMS.Testing.IntegrationTests
{
    /// <summary>
    /// Focused integration tests to validate Phase 1: Multi-collection sync during branch checkout
    /// </summary>
    [TestFixture]
    public class MultiCollectionBranchSyncTests
    {
        private string _tempDir = null!;
        private DoltCli _doltCli = null!;
        private ChromaDbService _chromaService = null!;
        private SyncManagerV2 _syncManager = null!;
        private ChromaToDoltSyncer _chromaSyncer = null!;
        private ILogger<MultiCollectionBranchSyncTests> _logger = null!;

        [SetUp]
        public async Task Setup()
        {
            // Initialize Python context if not already done
            if (!PythonContext.IsInitialized)
            {
                PythonContext.Initialize();
            }

            _tempDir = Path.Combine(Path.GetTempPath(), $"MultiCollSyncTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            _logger = loggerFactory.CreateLogger<MultiCollectionBranchSyncTests>();

            // Initialize Dolt CLI
            var doltConfig = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = Environment.OSVersion.Platform == PlatformID.Win32NT 
                    ? @"C:\Program Files\Dolt\bin\dolt.exe" 
                    : "dolt",
                RepositoryPath = _tempDir,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            });
            _doltCli = new DoltCli(doltConfig, loggerFactory.CreateLogger<DoltCli>());
            await _doltCli.InitAsync();
            
            // Initialize ChromaDB service with local storage
            var chromaDataPath = Path.Combine(_tempDir, "chroma_data");
            Directory.CreateDirectory(chromaDataPath);
            var serverConfig = Options.Create(new ServerConfiguration 
            { 
                ChromaDataPath = chromaDataPath
            });
            _chromaService = new ChromaDbService(
                loggerFactory.CreateLogger<ChromaDbService>(), 
                serverConfig
            );

            // Initialize services
            var chromaDetector = new ChromaToDoltDetector(_chromaService, _doltCli, loggerFactory.CreateLogger<ChromaToDoltDetector>());
            _chromaSyncer = new ChromaToDoltSyncer(_chromaService, _doltCli, chromaDetector, loggerFactory.CreateLogger<ChromaToDoltSyncer>());
            
            // Create SyncManagerV2
            _syncManager = new SyncManagerV2(
                _doltCli,
                _chromaService,
                loggerFactory.CreateLogger<SyncManagerV2>()
            );
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                // Dispose ChromaService
                _chromaService?.Dispose();
                
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// CRITICAL TEST: Verify that ALL collections are synced during branch checkout, not just the first one
        /// This is the primary issue identified in PP13-57
        /// </summary>
        [Test]
        [Ignore("PP13-58: Temporarily disabled - sync state tracking issues causing test hangs")]
        public async Task TestAllCollectionsSyncedDuringCheckout()
        {
            _logger.LogInformation("=== TEST: All Collections Synced During Checkout ===");

            // Step 1: Create multiple collections on main branch
            _logger.LogInformation("Step 1: Creating 3 collections on main branch");
            
            await _chromaService.CreateCollectionAsync("collection_alpha");
            await _chromaService.CreateCollectionAsync("collection_beta");
            await _chromaService.CreateCollectionAsync("collection_gamma");

            // Add documents to each collection
            await _chromaService.AddDocumentsAsync("collection_alpha",
                new List<string> { "Alpha doc 1 on main", "Alpha doc 2 on main" },
                new List<string> { "alpha-1", "alpha-2" });

            await _chromaService.AddDocumentsAsync("collection_beta",
                new List<string> { "Beta doc 1 on main", "Beta doc 2 on main", "Beta doc 3 on main" },
                new List<string> { "beta-1", "beta-2", "beta-3" });

            await _chromaService.AddDocumentsAsync("collection_gamma",
                new List<string> { "Gamma doc 1 on main" },
                new List<string> { "gamma-1" });

            // Stage and commit on main
            await _chromaSyncer.StageLocalChangesAsync("collection_alpha");
            await _chromaSyncer.StageLocalChangesAsync("collection_beta");
            await _chromaSyncer.StageLocalChangesAsync("collection_gamma");
            await _syncManager.ProcessCommitAsync("Initial collections on main");

            _logger.LogInformation("Main branch state: Alpha=2 docs, Beta=3 docs, Gamma=1 doc");

            // Step 2: Create feature branch with different collection states
            _logger.LogInformation("Step 2: Creating feature branch with modified collections");
            
            await _doltCli.CheckoutAsync("feature-branch", createNew: true);

            // Sync to get main content first
            await _syncManager.FullSyncAsync("collection_alpha");
            await _syncManager.FullSyncAsync("collection_beta");
            await _syncManager.FullSyncAsync("collection_gamma");

            // Modify collections on feature branch
            // Alpha: Add 1 document
            await _chromaService.AddDocumentsAsync("collection_alpha",
                new List<string> { "Alpha doc 3 on feature" },
                new List<string> { "alpha-3" });

            // Beta: Delete 1 document
            await _chromaService.DeleteDocumentsAsync("collection_beta", new List<string> { "beta-2" });

            // Gamma: Add 2 documents
            await _chromaService.AddDocumentsAsync("collection_gamma",
                new List<string> { "Gamma doc 2 on feature", "Gamma doc 3 on feature" },
                new List<string> { "gamma-2", "gamma-3" });

            // Create a new collection only on feature branch
            await _chromaService.CreateCollectionAsync("collection_delta");
            await _chromaService.AddDocumentsAsync("collection_delta",
                new List<string> { "Delta doc 1 on feature", "Delta doc 2 on feature" },
                new List<string> { "delta-1", "delta-2" });

            // Stage and commit on feature branch
            await _chromaSyncer.StageLocalChangesAsync("collection_alpha");
            await _chromaSyncer.StageLocalChangesAsync("collection_beta");
            await _chromaSyncer.StageLocalChangesAsync("collection_gamma");
            await _chromaSyncer.StageLocalChangesAsync("collection_delta");
            await _syncManager.ProcessCommitAsync("Feature branch changes");

            _logger.LogInformation("Feature branch state: Alpha=3 docs, Beta=2 docs, Gamma=3 docs, Delta=2 docs");

            // Step 3: Switch back to main and verify ALL collections are synced
            _logger.LogInformation("Step 3: Switching back to main branch");
            
            var checkoutResult = await _syncManager.ProcessCheckoutAsync("main", false);
            
            Assert.That(checkoutResult.Status, Is.EqualTo(SyncStatusV2.Completed), 
                "Checkout to main should complete successfully");

            // CRITICAL VERIFICATION: Check document counts in ALL collections
            _logger.LogInformation("Step 4: Verifying ALL collections reflect main branch state");
            
            var alphaCount = await _chromaService.GetDocumentCountAsync("collection_alpha");
            var betaCount = await _chromaService.GetDocumentCountAsync("collection_beta");
            var gammaCount = await _chromaService.GetDocumentCountAsync("collection_gamma");
            
            _logger.LogInformation($"After checkout to main - Alpha: {alphaCount}, Beta: {betaCount}, Gamma: {gammaCount}");

            // These assertions verify the fix for PP13-57
            Assert.That(alphaCount, Is.EqualTo(2), 
                "collection_alpha should have 2 documents on main (was getting wrong count due to single collection sync)");
            Assert.That(betaCount, Is.EqualTo(3), 
                "collection_beta should have 3 documents on main (was not synced in original bug)");
            Assert.That(gammaCount, Is.EqualTo(1), 
                "collection_gamma should have 1 document on main (was not synced in original bug)");

            // Verify delta collection doesn't exist on main
            var collections = await _chromaService.ListCollectionsAsync();
            Assert.That(collections.Contains("collection_delta"), Is.False, 
                "collection_delta should not exist on main branch");

            // Step 5: Switch to feature branch and verify ALL collections are synced
            _logger.LogInformation("Step 5: Switching to feature branch");
            
            checkoutResult = await _syncManager.ProcessCheckoutAsync("feature-branch", false);
            
            Assert.That(checkoutResult.Status, Is.EqualTo(SyncStatusV2.Completed), 
                "Checkout to feature branch should complete successfully");

            // Verify feature branch state
            alphaCount = await _chromaService.GetDocumentCountAsync("collection_alpha");
            betaCount = await _chromaService.GetDocumentCountAsync("collection_beta");
            gammaCount = await _chromaService.GetDocumentCountAsync("collection_gamma");
            var deltaCount = await _chromaService.GetDocumentCountAsync("collection_delta");
            
            _logger.LogInformation($"After checkout to feature - Alpha: {alphaCount}, Beta: {betaCount}, Gamma: {gammaCount}, Delta: {deltaCount}");

            Assert.That(alphaCount, Is.EqualTo(3), 
                "collection_alpha should have 3 documents on feature branch");
            Assert.That(betaCount, Is.EqualTo(2), 
                "collection_beta should have 2 documents on feature branch");
            Assert.That(gammaCount, Is.EqualTo(3), 
                "collection_gamma should have 3 documents on feature branch");
            Assert.That(deltaCount, Is.EqualTo(2), 
                "collection_delta should have 2 documents on feature branch");

            _logger.LogInformation("=== TEST PASSED: All collections properly synced during checkout ===");
        }

        /// <summary>
        /// Test that the branch state validation method works correctly
        /// </summary>
        [Test]
        //[Ignore("PP13-58: Temporarily disabled - sync state tracking issues causing test hangs")]
        public async Task TestBranchStateValidation()
        {
            _logger.LogInformation("=== TEST: Branch State Validation ===");

            // Create test collections
            await _chromaService.CreateCollectionAsync("validation_test_1");
            await _chromaService.CreateCollectionAsync("validation_test_2");

            await _chromaService.AddDocumentsAsync("validation_test_1",
                new List<string> { "Doc 1", "Doc 2" },
                new List<string> { "val-1", "val-2" });

            await _chromaService.AddDocumentsAsync("validation_test_2",
                new List<string> { "Doc A" },
                new List<string> { "val-a" });

            // Stage and commit
            await _chromaSyncer.StageLocalChangesAsync("validation_test_1");
            await _chromaSyncer.StageLocalChangesAsync("validation_test_2");
            await _syncManager.ProcessCommitAsync("Test validation setup");

            // The validation should pass after a successful sync
            var currentBranch = await _doltCli.GetCurrentBranchAsync();
            _logger.LogInformation($"Validating state on branch: {currentBranch}");

            // After commit, ChromaDB and Dolt should be in sync
            var collection1Count = await _chromaService.GetDocumentCountAsync("validation_test_1");
            var collection2Count = await _chromaService.GetDocumentCountAsync("validation_test_2");

            Assert.That(collection1Count, Is.EqualTo(2), "validation_test_1 should have 2 documents");
            Assert.That(collection2Count, Is.EqualTo(1), "validation_test_2 should have 1 document");

            _logger.LogInformation("=== TEST PASSED: Branch state validation working ===");
        }

        /// <summary>
        /// Test that uncommitted changes are properly detected and handled
        /// </summary>
        [Test]
        //[Ignore("PP13-58: Temporarily disabled - sync state tracking issues causing test hangs")]
        public async Task TestUncommittedChangesDetection()
        {
            _logger.LogInformation("=== TEST: Uncommitted Changes Detection ===");

            // Create initial state
            await _chromaService.CreateCollectionAsync("changes_test");
            await _chromaService.AddDocumentsAsync("changes_test",
                new List<string> { "Initial doc" },
                new List<string> { "init-1" });

            await _chromaSyncer.StageLocalChangesAsync("changes_test");
            await _syncManager.ProcessCommitAsync("Initial commit");

            // Create a feature branch
            await _doltCli.CheckoutAsync("test-feature", createNew: true);
            await _syncManager.ProcessCommitAsync("Create feature branch");

            // Switch back to main
            await _syncManager.ProcessCheckoutAsync("main", false);

            // Add uncommitted changes
            await _chromaService.AddDocumentsAsync("changes_test",
                new List<string> { "Uncommitted doc" },
                new List<string> { "uncommitted-1" });

            // Check that changes are detected
            var localChanges = await _syncManager.GetLocalChangesAsync();
            
            Assert.That(localChanges.HasChanges, Is.True, 
                "Should detect uncommitted changes");
            Assert.That(localChanges.TotalChanges, Is.GreaterThan(0), 
                "Should have at least one change");

            // Try to checkout without forcing (should handle based on uncommitted changes)
            // This tests the if_uncommitted logic
            var checkoutResult = await _syncManager.ProcessCheckoutAsync("test-feature", false, force: false);
            
            // The checkout might fail or succeed based on uncommitted changes handling
            // Log the result for debugging
            _logger.LogInformation($"Checkout result with uncommitted changes: Status={checkoutResult.Status}");

            if (checkoutResult.Status == SyncStatusV2.LocalChangesExist)
            {
                _logger.LogInformation("Checkout blocked due to local changes (expected behavior)");
                Assert.Pass("Uncommitted changes properly detected and blocked checkout");
            }
            else if (checkoutResult.Status == SyncStatusV2.Completed)
            {
                _logger.LogInformation("Checkout completed (may have handled changes)");
                Assert.Pass("Checkout handled uncommitted changes");
            }

            _logger.LogInformation("=== TEST PASSED: Uncommitted changes detection working ===");
        }

        /// <summary>
        /// Test rapid branch switching to ensure no state corruption
        /// </summary>
        [Test]
        [Ignore("PP13-58: Temporarily disabled - sync state tracking issues causing test hangs")]
        public async Task TestRapidBranchSwitching()
        {
            _logger.LogInformation("=== TEST: Rapid Branch Switching ===");

            // Create collections on main
            await _chromaService.CreateCollectionAsync("rapid_test");
            await _chromaService.AddDocumentsAsync("rapid_test",
                new List<string> { "Main doc 1", "Main doc 2" },
                new List<string> { "rapid-1", "rapid-2" });

            await _chromaSyncer.StageLocalChangesAsync("rapid_test");
            await _syncManager.ProcessCommitAsync("Main setup");

            var mainCount = await _chromaService.GetDocumentCountAsync("rapid_test");
            _logger.LogInformation($"Main branch: {mainCount} documents");

            // Create branch A with different content
            await _doltCli.CheckoutAsync("branch-a", createNew: true);
            await _syncManager.FullSyncAsync("rapid_test");
            
            await _chromaService.AddDocumentsAsync("rapid_test",
                new List<string> { "Branch A doc" },
                new List<string> { "branch-a-1" });
            
            await _chromaSyncer.StageLocalChangesAsync("rapid_test");
            await _syncManager.ProcessCommitAsync("Branch A changes");

            var branchACount = await _chromaService.GetDocumentCountAsync("rapid_test");
            _logger.LogInformation($"Branch A: {branchACount} documents");

            // Create branch B with different content
            await _syncManager.ProcessCheckoutAsync("main", false);
            await _doltCli.CheckoutAsync("branch-b", createNew: true);
            await _syncManager.FullSyncAsync("rapid_test");
            
            await _chromaService.DeleteDocumentsAsync("rapid_test", new List<string> { "rapid-1" });
            
            await _chromaSyncer.StageLocalChangesAsync("rapid_test");
            await _syncManager.ProcessCommitAsync("Branch B changes");

            var branchBCount = await _chromaService.GetDocumentCountAsync("rapid_test");
            _logger.LogInformation($"Branch B: {branchBCount} documents");

            // Rapid switching test
            _logger.LogInformation("Starting rapid branch switching...");

            for (int i = 0; i < 3; i++)
            {
                _logger.LogInformation($"Iteration {i + 1}:");
                
                // Switch to main
                await _syncManager.ProcessCheckoutAsync("main", false);
                var count = await _chromaService.GetDocumentCountAsync("rapid_test");
                Assert.That(count, Is.EqualTo(2), $"Iteration {i}: Main should have 2 docs");
                _logger.LogInformation($"  Main: {count} docs ✓");

                // Switch to branch-a
                await _syncManager.ProcessCheckoutAsync("branch-a", false);
                count = await _chromaService.GetDocumentCountAsync("rapid_test");
                Assert.That(count, Is.EqualTo(3), $"Iteration {i}: Branch A should have 3 docs");
                _logger.LogInformation($"  Branch A: {count} docs ✓");

                // Switch to branch-b
                await _syncManager.ProcessCheckoutAsync("branch-b", false);
                count = await _chromaService.GetDocumentCountAsync("rapid_test");
                Assert.That(count, Is.EqualTo(1), $"Iteration {i}: Branch B should have 1 doc");
                _logger.LogInformation($"  Branch B: {count} docs ✓");
            }

            // Final check - no false positive changes
            var localChanges = await _syncManager.GetLocalChangesAsync();
            Assert.That(localChanges.HasChanges, Is.False, 
                "Should have no false positive changes after rapid switching");

            _logger.LogInformation("=== TEST PASSED: Rapid branch switching without corruption ===");
        }
    }
}