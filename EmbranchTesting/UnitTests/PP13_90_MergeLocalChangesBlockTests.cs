using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;
using Moq;

namespace EmbranchTesting.UnitTests
{
    /// <summary>
    /// Unit tests for PP13-90: Fix Merge Sync Failure on Linux.
    /// Tests that ExecuteDoltMergeTool properly handles LocalChangesExist status from ProcessMergeAsync.
    /// </summary>
    [TestFixture]
    public class PP13_90_MergeLocalChangesBlockTests
    {
        private Mock<ILogger<ExecuteDoltMergeTool>> _mockLogger;
        private Mock<IDoltCli> _mockDoltCli;
        private Mock<IMergeConflictResolver> _mockConflictResolver;
        private Mock<ISyncManagerV2> _mockSyncManager;
        private Mock<IConflictAnalyzer> _mockConflictAnalyzer;
        private Mock<IEmbranchStateManifest> _mockManifestService;
        private Mock<ISyncStateChecker> _mockSyncStateChecker;
        private ExecuteDoltMergeTool _mergeTool;

        [SetUp]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<ExecuteDoltMergeTool>>();
            _mockDoltCli = new Mock<IDoltCli>();
            _mockConflictResolver = new Mock<IMergeConflictResolver>();
            _mockSyncManager = new Mock<ISyncManagerV2>();
            _mockConflictAnalyzer = new Mock<IConflictAnalyzer>();
            _mockManifestService = new Mock<IEmbranchStateManifest>();
            _mockSyncStateChecker = new Mock<ISyncStateChecker>();

            _mergeTool = new ExecuteDoltMergeTool(
                _mockLogger.Object,
                _mockDoltCli.Object,
                _mockConflictResolver.Object,
                _mockSyncManager.Object,
                _mockConflictAnalyzer.Object,
                _mockManifestService.Object,
                _mockSyncStateChecker.Object
            );

            // Common setup for all tests
            SetupCommonMocks();
        }

        private void SetupCommonMocks()
        {
            // Dolt is available
            _mockDoltCli.Setup(x => x.CheckDoltAvailableAsync())
                .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

            // Repository is initialized
            _mockDoltCli.Setup(x => x.IsInitializedAsync())
                .ReturnsAsync(true);

            // Current branch
            _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
                .ReturnsAsync("main");

            // No merge in progress
            _mockDoltCli.Setup(x => x.IsMergeInProgressAsync())
                .ReturnsAsync(false);

            // HEAD commit hash
            _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
                .ReturnsAsync("abc123def");
        }

        /// <summary>
        /// PP13-90: When ProcessMergeAsync returns LocalChangesExist status,
        /// ExecuteDoltMerge should return an error response with success=false
        /// and error="LOCAL_CHANGES_EXIST".
        /// </summary>
        [Test]
        public async Task ExecuteDoltMerge_WithLocalChanges_ShouldReturnLocalChangesExistError()
        {
            // Arrange: Mock ProcessMergeAsync to return LocalChangesExist status
            var localChanges = new LocalChanges(
                NewDocuments: new List<ChromaDocument>
                {
                    new ChromaDocument("doc1", "test", "test content", "hash1", new Dictionary<string, object>())
                },
                ModifiedDocuments: new List<ChromaDocument>(),
                DeletedDocuments: new List<DeletedDocumentV2>()
            );

            var mergeResult = new MergeSyncResultV2
            {
                Status = SyncStatusV2.LocalChangesExist,
                LocalChanges = localChanges
            };

            _mockSyncManager.Setup(x => x.ProcessMergeAsync("feature-branch", false, null))
                .ReturnsAsync(mergeResult);

            // Act
            var result = await _mergeTool.ExecuteDoltMerge("feature-branch");

            // Assert
            Assert.That(result, Is.Not.Null);

            // Use reflection to access anonymous type properties
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var errorProp = resultType.GetProperty("error");
            var messageProp = resultType.GetProperty("message");
            var localChangesProp = resultType.GetProperty("local_changes");
            var hintProp = resultType.GetProperty("hint");

            Assert.That(successProp?.GetValue(result), Is.False, "Expected success to be false");
            Assert.That(errorProp?.GetValue(result), Is.EqualTo("LOCAL_CHANGES_EXIST"), "Expected error code to be LOCAL_CHANGES_EXIST");
            Assert.That(messageProp?.GetValue(result)?.ToString(), Does.Contain("1 uncommitted local changes"), "Expected message to mention local changes count");
            Assert.That(hintProp?.GetValue(result)?.ToString(), Does.Contain("dolt_commit"), "Expected hint to mention dolt_commit");

            // Verify local_changes breakdown
            var localChangesValue = localChangesProp?.GetValue(result);
            Assert.That(localChangesValue, Is.Not.Null, "Expected local_changes to be present");

            var localChangesType = localChangesValue!.GetType();
            var newDocsProp = localChangesType.GetProperty("new_documents");
            Assert.That(newDocsProp?.GetValue(localChangesValue), Is.EqualTo(1), "Expected 1 new document in local_changes");
        }

        /// <summary>
        /// PP13-90: When force_merge=true is passed, the merge should proceed
        /// even if local changes exist (ProcessMergeAsync is called with force=true).
        /// </summary>
        [Test]
        public async Task ExecuteDoltMerge_WithForceFlag_ShouldProceedDespiteLocalChanges()
        {
            // Arrange: Mock ProcessMergeAsync to return Completed status when force=true
            var mergeResult = new MergeSyncResultV2
            {
                Status = SyncStatusV2.Completed,
                MergeCommitHash = "merge123",
                Added = 2,
                Modified = 1,
                Deleted = 0
            };

            _mockSyncManager.Setup(x => x.ProcessMergeAsync("feature-branch", true, null))
                .ReturnsAsync(mergeResult);

            // Mock manifest service for success case
            _mockSyncStateChecker.Setup(x => x.GetProjectRootAsync())
                .ReturnsAsync("C:\\test\\project");
            _mockManifestService.Setup(x => x.ManifestExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);

            // Act
            var result = await _mergeTool.ExecuteDoltMerge("feature-branch", force_merge: true);

            // Assert
            Assert.That(result, Is.Not.Null);

            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");

            Assert.That(successProp?.GetValue(result), Is.True, "Expected success to be true when force_merge=true");

            // Verify ProcessMergeAsync was called with force=true
            _mockSyncManager.Verify(x => x.ProcessMergeAsync("feature-branch", true, null), Times.Once);
        }

        /// <summary>
        /// PP13-90: Verify that the MergeStatus property correctly reflects LocalChangesExist status.
        /// </summary>
        [Test]
        public void MergeSyncResultV2_MergeStatus_ShouldReturnLocalChangesExist_WhenStatusIsLocalChangesExist()
        {
            // Arrange
            var result = new MergeSyncResultV2
            {
                Status = SyncStatusV2.LocalChangesExist
            };

            // Act
            var mergeStatus = result.MergeStatus;

            // Assert
            Assert.That(mergeStatus, Is.EqualTo(MergeSyncStatusV2.LocalChangesExist),
                "MergeStatus should be LocalChangesExist when Status is LocalChangesExist");
        }

        /// <summary>
        /// PP13-90: Verify that when local changes exist with multiple document types,
        /// all change types are reported in the response.
        /// </summary>
        [Test]
        public async Task ExecuteDoltMerge_WithMultipleLocalChangeTypes_ShouldReportAllChanges()
        {
            // Arrange: Mock ProcessMergeAsync with multiple types of local changes
            var localChanges = new LocalChanges(
                NewDocuments: new List<ChromaDocument>
                {
                    new ChromaDocument("new1", "test", "new content", "hash1", new Dictionary<string, object>()),
                    new ChromaDocument("new2", "test", "new content 2", "hash2", new Dictionary<string, object>())
                },
                ModifiedDocuments: new List<ChromaDocument>
                {
                    new ChromaDocument("mod1", "test", "modified content", "hash3", new Dictionary<string, object>())
                },
                DeletedDocuments: new List<DeletedDocumentV2>
                {
                    new DeletedDocumentV2("del1", "test", "[]"),
                    new DeletedDocumentV2("del2", "test", "[]"),
                    new DeletedDocumentV2("del3", "test", "[]")
                }
            );

            var mergeResult = new MergeSyncResultV2
            {
                Status = SyncStatusV2.LocalChangesExist,
                LocalChanges = localChanges
            };

            _mockSyncManager.Setup(x => x.ProcessMergeAsync("feature-branch", false, null))
                .ReturnsAsync(mergeResult);

            // Act
            var result = await _mergeTool.ExecuteDoltMerge("feature-branch");

            // Assert
            var resultType = result.GetType();
            var localChangesProp = resultType.GetProperty("local_changes");
            var localChangesValue = localChangesProp?.GetValue(result);
            Assert.That(localChangesValue, Is.Not.Null);

            var localChangesType = localChangesValue!.GetType();
            var newDocsProp = localChangesType.GetProperty("new_documents");
            var modifiedDocsProp = localChangesType.GetProperty("modified_documents");
            var deletedDocsProp = localChangesType.GetProperty("deleted_documents");

            Assert.That(newDocsProp?.GetValue(localChangesValue), Is.EqualTo(2), "Expected 2 new documents");
            Assert.That(modifiedDocsProp?.GetValue(localChangesValue), Is.EqualTo(1), "Expected 1 modified document");
            Assert.That(deletedDocsProp?.GetValue(localChangesValue), Is.EqualTo(3), "Expected 3 deleted documents");

            // Verify total in message
            var messageProp = resultType.GetProperty("message");
            Assert.That(messageProp?.GetValue(result)?.ToString(), Does.Contain("6 uncommitted local changes"),
                "Expected message to mention total of 6 local changes");
        }

        /// <summary>
        /// PP13-90: Verify that when ProcessMergeAsync returns Completed status,
        /// the merge proceeds normally without LocalChangesExist error.
        /// </summary>
        [Test]
        public async Task ExecuteDoltMerge_NoLocalChanges_ShouldProceedNormally()
        {
            // Arrange: Mock ProcessMergeAsync to return Completed status (no local changes)
            var mergeResult = new MergeSyncResultV2
            {
                Status = SyncStatusV2.Completed,
                MergeCommitHash = "merge456",
                Added = 5,
                Modified = 2,
                Deleted = 1,
                CollectionsSynced = 1
            };

            _mockSyncManager.Setup(x => x.ProcessMergeAsync("feature-branch", false, null))
                .ReturnsAsync(mergeResult);

            // Mock manifest service for success case
            _mockSyncStateChecker.Setup(x => x.GetProjectRootAsync())
                .ReturnsAsync("C:\\test\\project");
            _mockManifestService.Setup(x => x.ManifestExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);

            // Act
            var result = await _mergeTool.ExecuteDoltMerge("feature-branch");

            // Assert
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var errorProp = resultType.GetProperty("error");

            Assert.That(successProp?.GetValue(result), Is.True, "Expected success to be true when no local changes");
            Assert.That(errorProp?.GetValue(result), Is.Null, "Expected no error property when merge succeeds");
        }
    }
}
