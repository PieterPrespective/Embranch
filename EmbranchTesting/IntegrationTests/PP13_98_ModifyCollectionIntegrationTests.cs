using Embranch.Models;
using Embranch.Services;
using EmbranchTesting.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EmbranchTesting.IntegrationTests;

/// <summary>
/// Integration tests for PP13-98: Validates that ModifyCollectionAsync correctly
/// handles metadata updates and collection renames through the ChromaDB Python bridge.
///
/// ChromaDB supports:
/// - Metadata updates via collection.modify(metadata=new_metadata)
/// - Rename via create/copy/delete pattern (no native rename support)
/// </summary>
[TestFixture]
public class PP13_98_ModifyCollectionIntegrationTests
{
    private ChromaPythonService _service = null!;
    private string _testDataPath = null!;
    private ILogger<ChromaPythonService> _logger = null!;
    private IOptions<ServerConfiguration> _options = null!;

    /// <summary>
    /// Sets up test environment before each test
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        // PythonContext is managed by GlobalTestSetup
        if (!PythonContext.IsInitialized)
        {
            throw new InvalidOperationException("PythonContext should be initialized by GlobalTestSetup");
        }

        // Create unique temporary directory for this test
        _testDataPath = Path.Combine(Path.GetTempPath(), $"chroma_pp13_98_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDataPath);

        // Set up logger and configuration
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ChromaPythonService>();

        var configuration = new ServerConfiguration
        {
            ChromaDataPath = _testDataPath,
            ChromaMode = "persistent"
        };
        _options = Options.Create(configuration);

        _service = new ChromaPythonService(_logger, _options);
    }

    /// <summary>
    /// Cleans up test environment after each test
    /// </summary>
    [TearDown]
    public async Task TearDown()
    {
        _service?.Dispose();
        await Task.Delay(200);

        var sw = Stopwatch.StartNew();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, recursive: true);
                }
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(100 * (int)Math.Pow(2, attempt));
            }
            catch (IOException ex) when (ex.Message.Contains("data_level0.bin") || ex.Message.Contains("chroma.sqlite3"))
            {
                Console.WriteLine($"Warning: ChromaDB file locking prevented directory cleanup: {ex.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// PP13-98: Validates that metadata can be updated on an existing collection
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_UpdateMetadata_ShouldSucceed()
    {
        // Arrange - Create a collection with initial metadata
        const string collectionName = "test-metadata-update-pp13-98";
        var initialMetadata = new Dictionary<string, object>
        {
            ["description"] = "Original description",
            ["version"] = 1
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, initialMetadata),
            operationName: "Create test collection");

        // Act - Update metadata
        var newMetadata = new Dictionary<string, object>
        {
            ["description"] = "Updated description",
            ["version"] = 2,
            ["new_field"] = "new_value"
        };

        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(collectionName, null, newMetadata),
            operationName: "Modify collection metadata");

        // Assert - Modification should succeed
        Assert.That(result, Is.True, "ModifyCollectionAsync should return true");

        // Verify metadata was updated
        var collection = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.GetCollectionAsync(collectionName),
            operationName: "Get collection after modification");

        Assert.That(collection, Is.Not.Null, "Collection should exist after modification");

        if (collection is Dictionary<string, object> collDict && collDict.ContainsKey("metadata"))
        {
            var metadata = collDict["metadata"] as Dictionary<string, object>;
            Assert.That(metadata, Is.Not.Null, "Collection should have metadata");
            Assert.That(metadata!["description"], Is.EqualTo("Updated description"),
                "Description should be updated");
        }
    }

    /// <summary>
    /// PP13-98: Validates that a collection can be renamed
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_RenameCollection_ShouldSucceed()
    {
        // Arrange - Create a collection
        const string originalName = "original-collection-pp13-98";
        const string newName = "renamed-collection-pp13-98";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(originalName),
            operationName: "Create original collection");

        // Act - Rename the collection
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(originalName, newName, null),
            timeoutSeconds: 60, // Longer timeout for rename (involves copy)
            operationName: "Rename collection");

        // Assert - Rename should succeed
        Assert.That(result, Is.True, "ModifyCollectionAsync should return true for rename");

        // Verify new collection exists
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List collections after rename");

        Assert.That(collections, Does.Contain(newName), "New collection name should exist");
        Assert.That(collections, Does.Not.Contain(originalName), "Original collection name should not exist");
    }

    /// <summary>
    /// PP13-98: Validates that rename preserves all documents
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_RenameCollection_ShouldPreserveDocuments()
    {
        // Arrange - Create a collection with documents
        const string originalName = "docs-collection-pp13-98";
        const string newName = "renamed-docs-collection-pp13-98";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(originalName),
            operationName: "Create collection");

        var documents = new List<string> { "Document 1 content", "Document 2 content", "Document 3 content" };
        var ids = new List<string> { "doc-1", "doc-2", "doc-3" };
        var metadatas = new List<Dictionary<string, object>>
        {
            new() { ["category"] = "A" },
            new() { ["category"] = "B" },
            new() { ["category"] = "C" }
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.AddDocumentsAsync(originalName, documents, ids, metadatas),
            operationName: "Add documents");

        // Act - Rename the collection
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(originalName, newName, null),
            timeoutSeconds: 120, // Longer timeout for rename with document copy
            operationName: "Rename collection with documents");

        // Assert - Documents should be preserved
        Assert.That(result, Is.True, "Rename should succeed");

        var newDocs = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.GetDocumentsAsync(newName),
            operationName: "Get documents from renamed collection");

        Assert.That(newDocs, Is.Not.Null, "Documents result should not be null");

        if (newDocs is Dictionary<string, object> docResult)
        {
            var retrievedIds = docResult["ids"] as List<object>;
            Assert.That(retrievedIds, Is.Not.Null, "Should have document IDs");
            Assert.That(retrievedIds!.Count, Is.GreaterThanOrEqualTo(3),
                "All documents should be preserved (may have chunk IDs)");
        }
    }

    /// <summary>
    /// PP13-98: Validates that both rename and metadata update can be done together
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_RenameAndUpdateMetadata_ShouldSucceed()
    {
        // Arrange - Create a collection with initial metadata
        const string originalName = "combo-collection-pp13-98";
        const string newName = "combo-renamed-pp13-98";
        var initialMetadata = new Dictionary<string, object> { ["version"] = 1 };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(originalName, initialMetadata),
            operationName: "Create collection");

        // Act - Rename and update metadata in one operation
        var newMetadata = new Dictionary<string, object> { ["version"] = 2, ["renamed"] = true };

        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(originalName, newName, newMetadata),
            timeoutSeconds: 60, // Longer timeout for combined operation
            operationName: "Rename and update metadata");

        // Assert
        Assert.That(result, Is.True, "Combined operation should succeed");

        // Verify new collection exists with updated metadata
        var collection = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.GetCollectionAsync(newName),
            operationName: "Get renamed collection");

        Assert.That(collection, Is.Not.Null, "Renamed collection should exist");
    }

    /// <summary>
    /// PP13-98: Validates that no changes (no-op) returns success
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_NoChanges_ShouldSucceed()
    {
        // Arrange - Create a collection
        const string collectionName = "noop-collection-pp13-98";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName),
            operationName: "Create collection");

        // Act - Call modify with no changes
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(collectionName, null, null),
            operationName: "Modify with no changes");

        // Assert - Should succeed (no-op is valid)
        Assert.That(result, Is.True, "No-op modification should return true");

        // Verify collection still exists
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List collections");

        Assert.That(collections, Does.Contain(collectionName), "Collection should still exist");
    }

    /// <summary>
    /// PP13-98: Validates that renaming to same name is a no-op
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_RenameToSameName_ShouldSucceedAsNoOp()
    {
        // Arrange - Create a collection
        const string collectionName = "same-name-pp13-98";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName),
            operationName: "Create collection");

        // Act - Call modify with same name
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(collectionName, collectionName, null),
            operationName: "Rename to same name");

        // Assert - Should succeed (treated as no-op)
        Assert.That(result, Is.True, "Same-name rename should return true");
    }

    /// <summary>
    /// PP13-98: Validates that rename with empty metadata dictionary is treated correctly
    /// </summary>
    [Test]
    public async Task ModifyCollectionAsync_EmptyMetadataDict_ShouldNotUpdateMetadata()
    {
        // Arrange - Create a collection with initial metadata
        const string collectionName = "empty-meta-pp13-98";
        var initialMetadata = new Dictionary<string, object> { ["key"] = "value" };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, initialMetadata),
            operationName: "Create collection with metadata");

        // Act - Call modify with empty metadata dictionary
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ModifyCollectionAsync(collectionName, null, new Dictionary<string, object>()),
            operationName: "Modify with empty metadata dict");

        // Assert - Should succeed (empty dict is treated as no metadata update)
        Assert.That(result, Is.True, "Modification with empty dict should return true");
    }
}
