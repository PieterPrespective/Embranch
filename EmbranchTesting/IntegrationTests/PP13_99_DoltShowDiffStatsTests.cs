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
    /// Integration tests for PP13-99: DoltShow Diff Stats Empty Fix.
    /// Tests that DoltShow returns accurate change counts when include_diff=true.
    /// Requires Dolt to be installed and available in PATH.
    /// </summary>
    [TestFixture]
    public class PP13_99_DoltShowDiffStatsTests
    {
        private string _tempDir = null!;
        private DoltCli _doltCli = null!;
        private Mock<ILogger<DoltCli>> _mockDoltLogger = null!;
        private Mock<ILogger<DoltShowTool>> _mockShowToolLogger = null!;
        private Mock<ISyncStateTracker> _mockSyncStateTracker = null!;
        private IOptions<DoltConfiguration> _doltConfigOptions = null!;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PP13_99_Test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);

            _mockDoltLogger = new Mock<ILogger<DoltCli>>();
            _mockShowToolLogger = new Mock<ILogger<DoltShowTool>>();
            _mockSyncStateTracker = new Mock<ISyncStateTracker>();

            var config = new DoltConfiguration
            {
                DoltExecutablePath = "dolt",
                RepositoryPath = _tempDir,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            };
            _doltConfigOptions = Options.Create(config);
            _doltCli = new DoltCli(_doltConfigOptions, _mockDoltLogger.Object);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        /// <summary>
        /// Helper to create DoltShowTool with all required dependencies
        /// </summary>
        private DoltShowTool CreateDoltShowTool()
        {
            return new DoltShowTool(
                _mockShowToolLogger.Object,
                _doltCli,
                _mockSyncStateTracker.Object,
                _doltConfigOptions);
        }

        /// <summary>
        /// Helper to initialize Dolt repo with the documents and collections tables
        /// </summary>
        private async Task InitializeRepoWithDocumentsTableAsync()
        {
            await _doltCli.InitAsync();

            // Create collections table (required by DeltaDetectorV2)
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", @"
                CREATE TABLE collections (
                    collection_name VARCHAR(255) PRIMARY KEY,
                    display_name VARCHAR(255),
                    description TEXT,
                    embedding_model VARCHAR(100),
                    chunk_size INT,
                    chunk_overlap INT,
                    document_count INT,
                    metadata JSON
                )");

            // Create documents table (required by DeltaDetectorV2)
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", @"
                CREATE TABLE documents (
                    doc_id VARCHAR(255) NOT NULL,
                    collection_name VARCHAR(255) NOT NULL,
                    content TEXT,
                    content_hash VARCHAR(64),
                    title VARCHAR(255),
                    doc_type VARCHAR(50),
                    metadata JSON,
                    PRIMARY KEY (doc_id, collection_name)
                )");

            // Create document_sync_log table (required by DeltaDetectorV2)
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", @"
                CREATE TABLE document_sync_log (
                    doc_id VARCHAR(255) NOT NULL,
                    collection_name VARCHAR(255) NOT NULL,
                    content_hash VARCHAR(64),
                    chroma_chunk_ids JSON,
                    sync_direction VARCHAR(50),
                    sync_action VARCHAR(50),
                    synced_at DATETIME,
                    PRIMARY KEY (doc_id, collection_name, sync_direction)
                )");

            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Initialize schema");
        }

        /// <summary>
        /// PP13-99: DoltShow with include_diff=false should return zeros for all counts.
        /// This is the default behavior and should not call DeltaDetectorV2.
        /// </summary>
        [Test]
        public async Task DoltShow_WithIncludeDiffFalse_ShouldReturnZeroCounts()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add a document
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 1, '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc1', 'test-collection', 'Hello World', 'abc123', 'Doc 1', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add document");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: false);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var changesProp = resultType.GetProperty("changes");

            Assert.That(successProp?.GetValue(result), Is.True);

            var changes = changesProp?.GetValue(result);
            Assert.That(changes, Is.Not.Null);

            var changesType = changes!.GetType();
            var summaryProp = changesType.GetProperty("summary");
            var summary = summaryProp?.GetValue(changes);

            var summaryType = summary!.GetType();
            var addedProp = summaryType.GetProperty("added");
            var modifiedProp = summaryType.GetProperty("modified");
            var deletedProp = summaryType.GetProperty("deleted");
            var totalProp = summaryType.GetProperty("total");

            Assert.That((int)(addedProp?.GetValue(summary) ?? -1), Is.EqualTo(0), "Added should be 0 when include_diff=false");
            Assert.That((int)(modifiedProp?.GetValue(summary) ?? -1), Is.EqualTo(0), "Modified should be 0 when include_diff=false");
            Assert.That((int)(deletedProp?.GetValue(summary) ?? -1), Is.EqualTo(0), "Deleted should be 0 when include_diff=false");
            Assert.That((int)(totalProp?.GetValue(summary) ?? -1), Is.EqualTo(0), "Total should be 0 when include_diff=false");
        }

        /// <summary>
        /// PP13-99: DoltShow with include_diff=true should return accurate added count.
        /// </summary>
        [Test]
        public async Task DoltShow_WithIncludeDiffTrue_ShouldReturnAddedCount()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add a collection first
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add collection");

            // Add documents (this creates the commit we'll test)
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc1', 'test-collection', 'Hello World 1', 'hash1', 'Doc 1', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc2', 'test-collection', 'Hello World 2', 'hash2', 'Doc 2', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc3', 'test-collection', 'Hello World 3', 'hash3', 'Doc 3', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add 3 documents");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var changesProp = resultType.GetProperty("changes");

            Assert.That(successProp?.GetValue(result), Is.True);

            var changes = changesProp?.GetValue(result);
            Assert.That(changes, Is.Not.Null);

            var changesType = changes!.GetType();
            var summaryProp = changesType.GetProperty("summary");
            var summary = summaryProp?.GetValue(changes);

            var summaryType = summary!.GetType();
            var addedProp = summaryType.GetProperty("added");
            var totalProp = summaryType.GetProperty("total");

            var addedCount = (int)(addedProp?.GetValue(summary) ?? 0);
            var totalCount = (int)(totalProp?.GetValue(summary) ?? 0);

            Assert.That(addedCount, Is.EqualTo(3), "Should have 3 added documents");
            Assert.That(totalCount, Is.EqualTo(3), "Total should be 3");
        }

        /// <summary>
        /// PP13-99: DoltShow with include_diff=true should return accurate modified count.
        /// </summary>
        [Test]
        public async Task DoltShow_WithIncludeDiffTrue_ShouldReturnModifiedCount()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add collection and initial documents
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc1', 'test-collection', 'Original content', 'original-hash', 'Doc 1', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Initial state");

            // Modify the document
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "UPDATE documents SET content = 'Modified content', content_hash = 'modified-hash' WHERE doc_id = 'doc1'");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Modify document");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var changesProp = resultType.GetProperty("changes");
            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var summaryProp = changesType.GetProperty("summary");
            var summary = summaryProp?.GetValue(changes);

            var summaryType = summary!.GetType();
            var modifiedProp = summaryType.GetProperty("modified");
            var modifiedCount = (int)(modifiedProp?.GetValue(summary) ?? 0);

            Assert.That(modifiedCount, Is.EqualTo(1), "Should have 1 modified document");
        }

        /// <summary>
        /// PP13-99: DoltShow with include_diff=true should return accurate deleted count.
        /// </summary>
        [Test]
        public async Task DoltShow_WithIncludeDiffTrue_ShouldReturnDeletedCount()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add collection and documents
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc1', 'test-collection', 'Content 1', 'hash1', 'Doc 1', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc2', 'test-collection', 'Content 2', 'hash2', 'Doc 2', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Initial state with documents");

            // Delete one document
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "DELETE FROM documents WHERE doc_id = 'doc1'");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Delete document");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var changesProp = resultType.GetProperty("changes");
            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var summaryProp = changesType.GetProperty("summary");
            var summary = summaryProp?.GetValue(changes);

            var summaryType = summary!.GetType();
            var deletedProp = summaryType.GetProperty("deleted");
            var deletedCount = (int)(deletedProp?.GetValue(summary) ?? 0);

            Assert.That(deletedCount, Is.EqualTo(1), "Should have 1 deleted document");
        }

        /// <summary>
        /// PP13-99: DoltShow should respect diff_limit parameter.
        /// </summary>
        [Test]
        public async Task DoltShow_WithDiffLimit_ShouldLimitDocumentList()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add collection
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add collection");

            // Add 5 documents
            for (int i = 1; i <= 5; i++)
            {
                await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                    $"INSERT INTO documents VALUES ('doc{i}', 'test-collection', 'Content {i}', 'hash{i}', 'Doc {i}', 'text', '{{}}')");
            }
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add 5 documents");

            var showTool = CreateDoltShowTool();

            // Act - Limit to 2 documents
            var result = await showTool.DoltShow("HEAD", include_diff: true, diff_limit: 2);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var changesProp = resultType.GetProperty("changes");

            Assert.That(successProp?.GetValue(result), Is.True);

            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var documentsProp = changesType.GetProperty("documents");
            var summaryProp = changesType.GetProperty("summary");

            var documents = documentsProp?.GetValue(changes) as System.Collections.IList;
            var summary = summaryProp?.GetValue(changes);
            var summaryType = summary!.GetType();
            var addedProp = summaryType.GetProperty("added");

            // Summary should show total count (5 added)
            Assert.That((int)(addedProp?.GetValue(summary) ?? 0), Is.EqualTo(5),
                "Summary should show all 5 added documents");

            // But documents list should be limited to 2
            Assert.That(documents?.Count, Is.EqualTo(2),
                "Documents list should be limited to diff_limit (2)");
        }

        /// <summary>
        /// PP13-99: DoltShow should include document details in diff results.
        /// </summary>
        [Test]
        public async Task DoltShow_WithIncludeDiffTrue_ShouldIncludeDocumentDetails()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Add collection and document with specific title
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add collection");

            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('unique-doc-id', 'test-collection', 'Content', 'hash', 'My Test Document', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add document with title");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var changesProp = resultType.GetProperty("changes");
            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var documentsProp = changesType.GetProperty("documents");
            var documents = documentsProp?.GetValue(changes) as System.Collections.IList;

            Assert.That(documents, Is.Not.Null.And.Count.EqualTo(1));

            var doc = documents![0];
            var docType = doc.GetType();

            var idProp = docType.GetProperty("id");
            var collectionProp = docType.GetProperty("collection");
            var changeTypeProp = docType.GetProperty("change_type");
            var titleProp = docType.GetProperty("title");

            Assert.That(idProp?.GetValue(doc), Is.EqualTo("unique-doc-id"), "Document id should match");
            Assert.That(collectionProp?.GetValue(doc), Is.EqualTo("test-collection"), "Collection name should match");
            Assert.That(changeTypeProp?.GetValue(doc), Is.EqualTo("added"), "Change type should be 'added'");
            Assert.That(titleProp?.GetValue(doc), Is.EqualTo("My Test Document"), "Title should match");
        }

        /// <summary>
        /// PP13-99: DoltShow should handle mixed changes (adds + modifies + deletes).
        /// </summary>
        [Test]
        public async Task DoltShow_WithMixedChanges_ShouldReturnAllCounts()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            // Initial state: add collection and 2 documents
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc-keep', 'test-collection', 'Keep content', 'hash-keep', 'Keep Doc', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc-modify', 'test-collection', 'Original content', 'hash-orig', 'Modify Doc', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc-delete', 'test-collection', 'Delete content', 'hash-del', 'Delete Doc', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Initial state");

            // Mixed changes: add 1, modify 1, delete 1
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc-new', 'test-collection', 'New content', 'hash-new', 'New Doc', 'text', '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "UPDATE documents SET content = 'Modified content', content_hash = 'hash-mod' WHERE doc_id = 'doc-modify'");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "DELETE FROM documents WHERE doc_id = 'doc-delete'");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Mixed changes");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var changesProp = resultType.GetProperty("changes");
            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var summaryProp = changesType.GetProperty("summary");
            var summary = summaryProp?.GetValue(changes);

            var summaryType = summary!.GetType();
            var addedProp = summaryType.GetProperty("added");
            var modifiedProp = summaryType.GetProperty("modified");
            var deletedProp = summaryType.GetProperty("deleted");
            var totalProp = summaryType.GetProperty("total");

            Assert.That((int)(addedProp?.GetValue(summary) ?? 0), Is.EqualTo(1), "Should have 1 added document");
            Assert.That((int)(modifiedProp?.GetValue(summary) ?? 0), Is.EqualTo(1), "Should have 1 modified document");
            Assert.That((int)(deletedProp?.GetValue(summary) ?? 0), Is.EqualTo(1), "Should have 1 deleted document");
            Assert.That((int)(totalProp?.GetValue(summary) ?? 0), Is.EqualTo(3), "Total should be 3");
        }

        /// <summary>
        /// PP13-99: DoltShow change_type should be 'deleted' (not 'removed') in response.
        /// The DOLT_DIFF returns 'removed' but we normalize to 'deleted' for consistency.
        /// </summary>
        [Test]
        public async Task DoltShow_DeletedDocument_ShouldShowDeletedChangeType()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange
            await InitializeRepoWithDocumentsTableAsync();

            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO collections VALUES ('test-collection', 'Test', 'Test collection', 'default', 512, 50, 0, '{}')");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "INSERT INTO documents VALUES ('doc-to-delete', 'test-collection', 'Content', 'hash', 'Doc', 'text', '{}')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add document");

            await _doltCli.ExecuteRawCommandAsync("sql", "-q",
                "DELETE FROM documents WHERE doc_id = 'doc-to-delete'");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Delete document");

            var showTool = CreateDoltShowTool();

            // Act
            var result = await showTool.DoltShow("HEAD", include_diff: true);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var changesProp = resultType.GetProperty("changes");
            var changes = changesProp?.GetValue(result);
            var changesType = changes!.GetType();
            var documentsProp = changesType.GetProperty("documents");
            var documents = documentsProp?.GetValue(changes) as System.Collections.IList;

            Assert.That(documents, Is.Not.Null.And.Count.EqualTo(1));

            var doc = documents![0];
            var docType = doc.GetType();
            var changeTypeProp = docType.GetProperty("change_type");

            Assert.That(changeTypeProp?.GetValue(doc), Is.EqualTo("deleted"),
                "Change type should be 'deleted' (normalized from 'removed')");
        }
    }
}
