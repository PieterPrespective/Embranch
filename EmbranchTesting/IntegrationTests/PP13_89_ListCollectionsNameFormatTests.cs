using Embranch.Models;
using Embranch.Services;
using EmbranchTesting.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EmbranchTesting.IntegrationTests;

/// <summary>
/// Integration tests for PP13-89: Validates that ListCollectionsAsync returns proper collection names
/// instead of Python's __repr__() format "Collection(name=xxx)".
///
/// This bug was discovered during Linux Test Plan (PP13-88) Scenario 5 where change detection
/// was failing silently because collection names were in the wrong format.
/// </summary>
[TestFixture]
public class PP13_89_ListCollectionsNameFormatTests
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
        _testDataPath = Path.Combine(Path.GetTempPath(), $"chroma_pp13_89_test_{Guid.NewGuid():N}");
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
    /// PP13-89: Validates that ListCollectionsAsync returns plain collection names
    /// without the Python __repr__() format "Collection(name=xxx)".
    ///
    /// This is the primary acceptance test for the PP13-89 fix.
    /// </summary>
    [Test]
    public async Task ListCollectionsAsync_ShouldReturnPlainCollectionNames_NotPythonRepr()
    {
        // Arrange - Create a collection with a known name
        const string testCollectionName = "test-docs-pp13-89";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(testCollectionName),
            operationName: "Create test collection");

        // Act - List collections
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List collections");

        // Assert - Collection name should be plain, not wrapped in Collection(name=...)
        Assert.That(collections, Is.Not.Null, "Collections list should not be null");
        Assert.That(collections.Count, Is.EqualTo(1), "Should have exactly one collection");

        var collectionName = collections[0];

        // Primary assertion: Name should be exactly the test collection name
        Assert.That(collectionName, Is.EqualTo(testCollectionName),
            $"Collection name should be '{testCollectionName}', but got '{collectionName}'");

        // Additional assertions to verify the bug is fixed
        Assert.That(collectionName, Does.Not.StartWith("Collection("),
            "Collection name should not start with 'Collection(' (Python __repr__ format)");
        Assert.That(collectionName, Does.Not.Contain("name="),
            "Collection name should not contain 'name=' (Python __repr__ format)");
        Assert.That(collectionName, Does.Not.EndWith(")"),
            "Collection name should not end with ')' if it doesn't start with 'Collection('");
    }

    /// <summary>
    /// PP13-89: Validates that returned collection names can be used in subsequent
    /// GetDocumentsAsync calls without failure.
    ///
    /// This test verifies the cascading failure chain is broken - if ListCollections
    /// returns correct names, downstream operations should succeed.
    /// </summary>
    [Test]
    public async Task CollectionNameFromList_CanBeUsedInSubsequentOperations()
    {
        // Arrange - Create collection and add a document
        const string testCollectionName = "test-downstream-pp13-89";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(testCollectionName),
            operationName: "Create test collection");

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.AddDocumentsAsync(
                testCollectionName,
                new List<string> { "Test document content for PP13-89" },
                new List<string> { "doc-1" }),
            operationName: "Add test document");

        // Act - List collections and use the returned name
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List collections");

        Assert.That(collections.Count, Is.GreaterThan(0), "Should have at least one collection");
        var collectionNameFromList = collections[0];

        // Use the returned name to get documents (this would fail with the bug)
        var documents = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.GetDocumentsAsync(collectionNameFromList),
            operationName: "Get documents using listed collection name");

        // Assert - Should successfully retrieve documents
        Assert.That(documents, Is.Not.Null, "Documents result should not be null");

        if (documents is Dictionary<string, object> docResult)
        {
            Assert.That(docResult.ContainsKey("ids"), Is.True, "Result should contain 'ids' key");
            var ids = docResult["ids"] as List<object>;
            Assert.That(ids, Is.Not.Null.And.Count.GreaterThan(0),
                "Should have retrieved at least one document using the listed collection name");
        }
    }

    /// <summary>
    /// PP13-89: Validates that multiple collections are all returned with correct names.
    /// </summary>
    [Test]
    public async Task ListCollectionsAsync_MultipleCollections_AllReturnPlainNames()
    {
        // Arrange - Create multiple collections
        var collectionNames = new[] { "alpha-collection", "beta-collection", "gamma-collection" };

        foreach (var name in collectionNames)
        {
            await TestUtilities.ExecuteWithTimeoutAsync(
                _service.CreateCollectionAsync(name),
                operationName: $"Create collection {name}");
        }

        // Act - List collections
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List all collections");

        // Assert - All names should be plain
        Assert.That(collections.Count, Is.EqualTo(3), "Should have exactly 3 collections");

        foreach (var collectionName in collections)
        {
            Assert.That(collectionName, Does.Not.StartWith("Collection("),
                $"Collection name '{collectionName}' should not have Python __repr__ format");
            Assert.That(collectionName, Does.Not.Contain("name="),
                $"Collection name '{collectionName}' should not contain 'name='");
        }

        // All original names should be present
        foreach (var expectedName in collectionNames)
        {
            Assert.That(collections, Does.Contain(expectedName),
                $"Collections should contain '{expectedName}'");
        }
    }

    /// <summary>
    /// PP13-89: Validates that collection names with special characters are handled correctly.
    /// </summary>
    [Test]
    public async Task ListCollectionsAsync_CollectionWithSpecialChars_ReturnsCorrectName()
    {
        // Arrange - Create collection with underscores and numbers (common patterns)
        const string testCollectionName = "test_collection_v2_2024";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(testCollectionName),
            operationName: "Create collection with special chars");

        // Act
        var collections = await TestUtilities.ExecuteWithTimeoutAsync(
            _service.ListCollectionsAsync(),
            operationName: "List collections");

        // Assert
        Assert.That(collections, Does.Contain(testCollectionName),
            $"Should find collection with exact name '{testCollectionName}'");

        var foundName = collections.FirstOrDefault(c => c.Contains("test_collection"));
        Assert.That(foundName, Is.EqualTo(testCollectionName),
            $"Found name '{foundName}' should exactly match '{testCollectionName}'");
    }
}
