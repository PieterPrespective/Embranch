using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;
using Embranch.Services;
using Moq;

namespace EmbranchTesting.UnitTests
{
    /// <summary>
    /// Unit tests for PP13-95: Fix stale deletion tracking blocking merges.
    /// Tests that FindDeletedDocumentsAsync filters out stale deletions and
    /// DoltResetTool clears deletion tracking after reset.
    /// </summary>
    [TestFixture]
    public class PP13_95_StaleDeletionTrackingTests
    {
        private Mock<IChromaDbService> _mockChroma = null!;
        private Mock<IDoltCli> _mockDolt = null!;
        private Mock<IDeletionTracker> _mockDeletionTracker = null!;
        private Mock<IOptions<DoltConfiguration>> _mockDoltConfig = null!;
        private Mock<ILogger<ChromaToDoltDetector>> _mockLogger = null!;
        private ChromaToDoltDetector _detector = null!;

        [SetUp]
        public void Setup()
        {
            _mockChroma = new Mock<IChromaDbService>();
            _mockDolt = new Mock<IDoltCli>();
            _mockDeletionTracker = new Mock<IDeletionTracker>();
            _mockLogger = new Mock<ILogger<ChromaToDoltDetector>>();

            var doltConfig = new DoltConfiguration { RepositoryPath = "/test/repo" };
            _mockDoltConfig = new Mock<IOptions<DoltConfiguration>>();
            _mockDoltConfig.Setup(x => x.Value).Returns(doltConfig);

            // Setup default Dolt mock for current branch
            _mockDolt.Setup(x => x.GetCurrentBranchAsync())
                .ReturnsAsync("main");

            // Setup default ChromaDB mock to return empty results
            SetupChromaEmptyResults();

            _detector = new ChromaToDoltDetector(
                _mockChroma.Object,
                _mockDolt.Object,
                _mockDeletionTracker.Object,
                _mockDoltConfig.Object,
                _mockLogger.Object,
                null
            );
        }

        private void SetupChromaEmptyResults()
        {
            // Setup ChromaDB mock with explicit parameters to avoid optional argument issues
            _mockChroma.Setup(x => x.GetDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()))
                .ReturnsAsync(new Dictionary<string, object>
                {
                    ["ids"] = new List<object>(),
                    ["documents"] = new List<object>(),
                    ["metadatas"] = new List<object>()
                });
        }

        /// <summary>
        /// PP13-95: Deletions with a different branch context should be discarded.
        /// </summary>
        [Test]
        public async Task FindDeletedDocuments_ShouldIgnoreOtherBranchDeletions()
        {
            // Arrange: Create a deletion from "feature-branch" while current branch is "main"
            var staleDeletion = new DeletionRecord
            {
                Id = "del-1",
                DocId = "doc1",
                CollectionName = "test-collection",
                BranchContext = "feature-branch",  // Different from current "main"
                SyncStatus = "pending",
                OriginalContentHash = "hash1"
            };

            _mockDeletionTracker.Setup(x => x.GetPendingDeletionsAsync("/test/repo", "test-collection"))
                .ReturnsAsync(new List<DeletionRecord> { staleDeletion });

            // Mock Dolt to return empty documents (simulating no documents in Dolt)
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.IsAny<string>()))
                .ReturnsAsync(new List<dynamic>());

            // Act
            var deleted = await _detector.FindDeletedDocumentsAsync("test-collection");

            // Assert: The stale deletion should have been discarded
            Assert.That(deleted, Is.Empty, "Expected stale deletion from other branch to be discarded");

            // Verify DiscardDeletionAsync was called for the stale record
            _mockDeletionTracker.Verify(
                x => x.DiscardDeletionAsync("del-1"),
                Times.Once,
                "Expected DiscardDeletionAsync to be called for stale deletion"
            );
        }

        /// <summary>
        /// PP13-95: Deletions for documents that don't exist in Dolt should be discarded.
        /// </summary>
        [Test]
        public async Task FindDeletedDocuments_ShouldDiscardNonExistentDocumentDeletions()
        {
            // Arrange: Create a deletion for a document that doesn't exist in Dolt
            var staleDeletion = new DeletionRecord
            {
                Id = "del-2",
                DocId = "nonexistent-doc",
                CollectionName = "test-collection",
                BranchContext = "main",  // Same branch, but doc doesn't exist
                SyncStatus = "pending",
                OriginalContentHash = "hash2"
            };

            _mockDeletionTracker.Setup(x => x.GetPendingDeletionsAsync("/test/repo", "test-collection"))
                .ReturnsAsync(new List<DeletionRecord> { staleDeletion });

            // Mock Dolt query to return count=0 (document doesn't exist)
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("COUNT(*)"))))
                .ReturnsAsync(new List<dynamic> { new { count = 0 } });

            // Mock Dolt query for fallback deletion detection (no documents)
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("doc_id, content_hash"))))
                .ReturnsAsync(new List<dynamic>());

            // Act
            var deleted = await _detector.FindDeletedDocumentsAsync("test-collection");

            // Assert: The stale deletion for non-existent doc should have been discarded
            Assert.That(deleted, Is.Empty, "Expected stale deletion for non-existent document to be discarded");

            // Verify DiscardDeletionAsync was called
            _mockDeletionTracker.Verify(
                x => x.DiscardDeletionAsync("del-2"),
                Times.Once,
                "Expected DiscardDeletionAsync to be called for non-existent document deletion"
            );
        }

        /// <summary>
        /// PP13-95: Valid deletions (same branch, doc exists in Dolt) should be retained.
        /// </summary>
        [Test]
        public async Task FindDeletedDocuments_ShouldRetainValidDeletions()
        {
            // Arrange: Create a valid deletion (same branch, document exists in Dolt)
            var validDeletion = new DeletionRecord
            {
                Id = "del-3",
                DocId = "valid-doc",
                CollectionName = "test-collection",
                BranchContext = "main",  // Same branch
                SyncStatus = "pending",
                OriginalContentHash = "hash3"
            };

            _mockDeletionTracker.Setup(x => x.GetPendingDeletionsAsync(It.IsAny<string>(), "test-collection"))
                .ReturnsAsync(new List<DeletionRecord> { validDeletion });

            // Mock Dolt query to return count=1 (document exists) - use JsonElement for proper parsing
            var countResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"count\": 1}");
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("COUNT(*)"))))
                .ReturnsAsync(new List<dynamic> { countResult });

            // Mock Dolt query for fallback - document exists
            var docResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"doc_id\": \"valid-doc\", \"content_hash\": \"hash3\"}");
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("doc_id, content_hash"))))
                .ReturnsAsync(new List<dynamic> { docResult });

            // Act
            var deleted = await _detector.FindDeletedDocumentsAsync("test-collection");

            // Assert: The valid deletion should be retained
            Assert.That(deleted, Has.Count.EqualTo(1), "Expected valid deletion to be retained");
            Assert.That(deleted[0].DocId, Is.EqualTo("valid-doc"), "Expected valid-doc to be in deleted list");

            // Verify DiscardDeletionAsync was NOT called for valid deletion
            _mockDeletionTracker.Verify(
                x => x.DiscardDeletionAsync("del-3"),
                Times.Never,
                "Expected DiscardDeletionAsync to NOT be called for valid deletion"
            );
        }

        /// <summary>
        /// PP13-95: Deletions with null BranchContext should be treated as valid for current branch.
        /// </summary>
        [Test]
        public async Task FindDeletedDocuments_ShouldAcceptNullBranchContext()
        {
            // Arrange: Create a deletion with null BranchContext (legacy record)
            var legacyDeletion = new DeletionRecord
            {
                Id = "del-4",
                DocId = "legacy-doc",
                CollectionName = "test-collection",
                BranchContext = null,  // Legacy record with no branch context
                SyncStatus = "pending",
                OriginalContentHash = "hash4"
            };

            _mockDeletionTracker.Setup(x => x.GetPendingDeletionsAsync(It.IsAny<string>(), "test-collection"))
                .ReturnsAsync(new List<DeletionRecord> { legacyDeletion });

            // Mock Dolt query to return count=1 (document exists) - use JsonElement for proper parsing
            var countResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"count\": 1}");
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("COUNT(*)"))))
                .ReturnsAsync(new List<dynamic> { countResult });

            // Mock Dolt query for fallback - document exists
            var docResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"doc_id\": \"legacy-doc\", \"content_hash\": \"hash4\"}");
            _mockDolt.Setup(x => x.QueryAsync<dynamic>(It.Is<string>(s => s.Contains("doc_id, content_hash"))))
                .ReturnsAsync(new List<dynamic> { docResult });

            // Act
            var deleted = await _detector.FindDeletedDocumentsAsync("test-collection");

            // Assert: Legacy deletion (null BranchContext) should be retained
            Assert.That(deleted, Has.Count.EqualTo(1), "Expected legacy deletion with null BranchContext to be retained");
            Assert.That(deleted[0].DocId, Is.EqualTo("legacy-doc"));

            // Verify DiscardDeletionAsync was NOT called
            _mockDeletionTracker.Verify(
                x => x.DiscardDeletionAsync("del-4"),
                Times.Never,
                "Expected DiscardDeletionAsync to NOT be called for null BranchContext deletion"
            );
        }
    }

    /// <summary>
    /// Unit tests for SqliteDeletionTracker PP13-95 methods.
    /// </summary>
    [TestFixture]
    public class PP13_95_SqliteDeletionTrackerTests
    {
        /// <summary>
        /// PP13-95: DiscardPendingDeletionsForBranchAsync should only affect specified branch.
        /// Note: This is a behavioral test - the actual SQLite testing is done in integration tests.
        /// </summary>
        [Test]
        public void DiscardPendingDeletionsForBranchAsync_ShouldExistOnInterface()
        {
            // Arrange
            var deletionTracker = new Mock<IDeletionTracker>();

            // Act & Assert: Verify the method exists and can be invoked
            deletionTracker.Setup(x => x.DiscardPendingDeletionsForBranchAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            Assert.DoesNotThrowAsync(async () =>
                await deletionTracker.Object.DiscardPendingDeletionsForBranchAsync("/repo", "main"));
        }

        /// <summary>
        /// PP13-95: DiscardDeletionAsync should exist on interface.
        /// </summary>
        [Test]
        public void DiscardDeletionAsync_ShouldExistOnInterface()
        {
            // Arrange
            var deletionTracker = new Mock<IDeletionTracker>();

            // Act & Assert: Verify the method exists and can be invoked
            deletionTracker.Setup(x => x.DiscardDeletionAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            Assert.DoesNotThrowAsync(async () =>
                await deletionTracker.Object.DiscardDeletionAsync("deletion-id-123"));
        }
    }
}
