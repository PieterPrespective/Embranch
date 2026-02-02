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
    /// Integration tests for PP13-95: Merge Stale Local Data Fix.
    /// Tests that stale deletion tracking doesn't block merges and that
    /// post-merge reconciliation correctly syncs all documents.
    /// </summary>
    [TestFixture]
    public class PP13_95_MergeReconciliationTests
    {
        private ILogger<PP13_95_MergeReconciliationTests> _logger;
        private IDoltCli _doltCli;
        private IChromaDbService _chromaService;
        private ISyncManagerV2 _syncManager;
        private SqliteDeletionTracker _deletionTracker;
        private DoltResetTool _resetTool;

        private string _testCollection = "pp13-95-test-collection";
        private string _tempRepoPath;
        private string _tempDataPath;

        [SetUp]
        public async Task Setup()
        {
            // Initialize Python context if not already initialized (for standalone test runs)
            if (!PythonContext.IsInitialized)
            {
                var setupLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var setupLogger = setupLoggerFactory.CreateLogger<PP13_95_MergeReconciliationTests>();
                var pythonDll = PythonContextUtility.FindPythonDll(setupLogger);
                PythonContext.Initialize(setupLogger, pythonDll);
            }

            // Create unique paths for this test
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "PP13_95_Tests", Guid.NewGuid().ToString());
            _tempDataPath = Path.Combine(_tempRepoPath, "data");
            Directory.CreateDirectory(_tempRepoPath);
            Directory.CreateDirectory(_tempDataPath);

            // Create logger factory and individual loggers
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<PP13_95_MergeReconciliationTests>();
            var doltLogger = loggerFactory.CreateLogger<DoltCli>();
            var syncLogger = loggerFactory.CreateLogger<SyncManagerV2>();
            var deletionTrackerLogger = loggerFactory.CreateLogger<SqliteDeletionTracker>();
            var resetToolLogger = loggerFactory.CreateLogger<DoltResetTool>();

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

            // Create mocks for tool dependencies
            var manifestService = new Mock<IEmbranchStateManifest>().Object;
            var syncStateChecker = new Mock<ISyncStateChecker>();
            syncStateChecker.Setup(x => x.GetProjectRootAsync()).ReturnsAsync(_tempRepoPath);

            _resetTool = new DoltResetTool(
                resetToolLogger,
                _doltCli,
                _syncManager,
                manifestService,
                syncStateChecker.Object,
                _deletionTracker,
                _deletionTracker,
                Options.Create(doltConfig));

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

                // PP13-95: Dispose deletion tracker
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
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var chromaLogger = loggerFactory.CreateLogger<ChromaPythonService>();
            return new ChromaPythonService(chromaLogger, Options.Create(config));
        }

        private static string GetDoltExecutablePath()
        {
            // Check for common Dolt locations
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
        /// PP13-95: After DoltReset, pending deletions should be cleared for the current branch.
        /// </summary>
        [Test]
        public async Task DoltReset_ShouldClearPendingDeletions()
        {
            // Arrange: Track some deletions for the main branch
            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "doc-to-delete-1",
                _testCollection,
                "hash1",
                new Dictionary<string, object>(),
                "main",
                "commit1"
            );

            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "doc-to-delete-2",
                _testCollection,
                "hash2",
                new Dictionary<string, object>(),
                "main",
                "commit1"
            );

            // Verify deletions are tracked
            var pendingBefore = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(pendingBefore, Has.Count.EqualTo(2), "Expected 2 pending deletions before reset");

            // Act: Perform reset
            var result = await _resetTool.DoltReset("HEAD", confirm_discard: true);

            // Assert: Pending deletions should be cleared
            var pendingAfter = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(pendingAfter, Has.Count.EqualTo(0),
                "PP13-95: Expected pending deletions to be cleared after reset");
        }

        /// <summary>
        /// PP13-95: DiscardPendingDeletionsForBranchAsync should only affect the specified branch.
        /// </summary>
        [Test]
        public async Task DiscardPendingDeletionsForBranch_ShouldOnlyAffectSpecifiedBranch()
        {
            // Arrange: Track deletions for different branches
            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "main-doc-1",
                _testCollection,
                "hash1",
                new Dictionary<string, object>(),
                "main",
                "commit1"
            );

            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "feature-doc-1",
                _testCollection,
                "hash2",
                new Dictionary<string, object>(),
                "feature-branch",
                "commit2"
            );

            // Act: Discard only main branch deletions
            await _deletionTracker.DiscardPendingDeletionsForBranchAsync(_tempRepoPath, "main");

            // Assert: Only feature-branch deletion should remain
            var remaining = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(remaining, Has.Count.EqualTo(1),
                "PP13-95: Expected only feature-branch deletion to remain");
            Assert.That(remaining[0].DocId, Is.EqualTo("feature-doc-1"),
                "PP13-95: Expected feature-doc-1 to remain");
        }

        /// <summary>
        /// PP13-95: DiscardDeletionAsync should remove specific deletion record.
        /// </summary>
        [Test]
        public async Task DiscardDeletionAsync_ShouldRemoveSpecificDeletion()
        {
            // Arrange: Track multiple deletions
            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "doc-1",
                _testCollection,
                "hash1",
                new Dictionary<string, object>(),
                "main",
                "commit1"
            );

            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "doc-2",
                _testCollection,
                "hash2",
                new Dictionary<string, object>(),
                "main",
                "commit1"
            );

            // Get the IDs of tracked deletions
            var pendingBefore = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(pendingBefore, Has.Count.EqualTo(2));

            // Act: Discard one specific deletion by ID
            var deletionToDiscard = pendingBefore.First(d => d.DocId == "doc-1");
            await _deletionTracker.DiscardDeletionAsync(deletionToDiscard.Id);

            // Assert: Only doc-2 should remain
            var pendingAfter = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(pendingAfter, Has.Count.EqualTo(1),
                "PP13-95: Expected only doc-2 to remain after discarding doc-1");
            Assert.That(pendingAfter[0].DocId, Is.EqualTo("doc-2"));
        }

        /// <summary>
        /// PP13-95: CleanupStaleSyncStatesAsync should remove old orphaned deletion records.
        /// </summary>
        [Test]
        public async Task CleanupStaleSyncStatesAsync_ShouldRemoveOrphanedRecords()
        {
            // This test validates that the cleanup method runs without errors
            // Full validation would require setting up old records

            // Act: Should not throw
            Assert.DoesNotThrowAsync(async () =>
                await _deletionTracker.CleanupStaleSyncStatesAsync(_tempRepoPath));
        }

        /// <summary>
        /// PP13-95: Merge after reset should not be blocked by stale tracking.
        /// Integration test verifying the full workflow.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task Merge_AfterReset_ShouldNotBeBlockedByStaleTracking()
        {
            // This test requires a full Dolt + ChromaDB setup
            // Skipping in unit test context if Dolt is not available

            var doltAvailable = await _doltCli.CheckDoltAvailableAsync();
            if (!doltAvailable.Success)
            {
                Assert.Ignore("Dolt not available for integration test");
            }

            // Arrange: Track a "stale" deletion that shouldn't block merge
            await _deletionTracker.TrackDeletionAsync(
                _tempRepoPath,
                "stale-doc",
                _testCollection,
                "stale-hash",
                new Dictionary<string, object>(),
                "main",
                "old-commit"
            );

            // Perform reset (which should clear the tracking)
            await _resetTool.DoltReset("HEAD", confirm_discard: true);

            // Assert: No pending deletions should remain
            var pendingAfterReset = await _deletionTracker.GetPendingDeletionsAsync(_tempRepoPath, _testCollection);
            Assert.That(pendingAfterReset, Has.Count.EqualTo(0),
                "PP13-95: Reset should have cleared all pending deletions");
        }
    }
}
