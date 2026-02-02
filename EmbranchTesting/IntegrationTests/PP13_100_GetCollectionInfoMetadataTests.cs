using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;
using EmbranchTesting.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EmbranchTesting.IntegrationTests;

/// <summary>
/// Integration tests for PP13-100: Validates that GetCollectionInfo correctly returns
/// collection metadata that was provided during collection creation.
///
/// Problem: GetCollectionInfo was returning empty metadata {} instead of user-provided metadata.
/// Fix: Extract metadata from the collection Dictionary returned by GetCollectionAsync.
/// </summary>
[TestFixture]
public class PP13_100_GetCollectionInfoMetadataTests
{
    private ChromaPythonService _service = null!;
    private ChromaGetCollectionInfoTool _tool = null!;
    private string _testDataPath = null!;
    private ILogger<ChromaPythonService> _serviceLogger = null!;
    private ILogger<ChromaGetCollectionInfoTool> _toolLogger = null!;
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
        _testDataPath = Path.Combine(Path.GetTempPath(), $"chroma_pp13_100_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDataPath);

        // Set up loggers and configuration
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _serviceLogger = loggerFactory.CreateLogger<ChromaPythonService>();
        _toolLogger = loggerFactory.CreateLogger<ChromaGetCollectionInfoTool>();

        var configuration = new ServerConfiguration
        {
            ChromaDataPath = _testDataPath,
            ChromaMode = "persistent"
        };
        _options = Options.Create(configuration);

        _service = new ChromaPythonService(_serviceLogger, _options);
        _tool = new ChromaGetCollectionInfoTool(_toolLogger, _service);
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
    /// PP13-100: Validates that GetCollectionInfo returns user-provided metadata correctly.
    /// This is the primary test case from the bug report.
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_ShouldReturnUserProvidedMetadata()
    {
        // Arrange - Create collection with metadata (test scenario from bug report)
        const string collectionName = "test-docs-pp13-100";
        var metadata = new Dictionary<string, object>
        {
            ["purpose"] = "testing",
            ["platform"] = "linux"
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, metadata),
            operationName: "Create test collection with metadata");

        // Act - Get collection info using the tool
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo");

        // Assert - Verify metadata is returned
        Assert.That(result, Is.Not.Null, "Result should not be null");

        // Parse the anonymous object result
        var resultType = result.GetType();
        var successProp = resultType.GetProperty("success");
        var metadataProp = resultType.GetProperty("metadata");

        Assert.That(successProp?.GetValue(result), Is.True, "Operation should succeed");
        Assert.That(metadataProp?.GetValue(result), Is.Not.Null, "Metadata should not be null");

        var returnedMetadata = metadataProp?.GetValue(result) as Dictionary<string, object>;
        Assert.That(returnedMetadata, Is.Not.Null, "Metadata should be a Dictionary<string, object>");
        Assert.That(returnedMetadata!.ContainsKey("purpose"), Is.True, "Metadata should contain 'purpose' key");
        Assert.That(returnedMetadata["purpose"], Is.EqualTo("testing"), "Purpose should match");
        Assert.That(returnedMetadata.ContainsKey("platform"), Is.True, "Metadata should contain 'platform' key");
        Assert.That(returnedMetadata["platform"], Is.EqualTo("linux"), "Platform should match");
    }

    /// <summary>
    /// PP13-100: Validates that GetCollectionInfo returns empty dictionary for collections without metadata
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_WithNoMetadata_ShouldReturnEmptyDictionary()
    {
        // Arrange - Create collection without metadata
        const string collectionName = "no-meta-pp13-100";

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName),
            operationName: "Create collection without metadata");

        // Act - Get collection info
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo");

        // Assert - Metadata should be empty dictionary, not null
        Assert.That(result, Is.Not.Null, "Result should not be null");

        var resultType = result.GetType();
        var successProp = resultType.GetProperty("success");
        var metadataProp = resultType.GetProperty("metadata");

        Assert.That(successProp?.GetValue(result), Is.True, "Operation should succeed");

        var returnedMetadata = metadataProp?.GetValue(result) as Dictionary<string, object>;
        Assert.That(returnedMetadata, Is.Not.Null, "Metadata should not be null (should be empty dict)");
        Assert.That(returnedMetadata!.Count, Is.EqualTo(0), "Metadata should be empty for collection without metadata");
    }

    /// <summary>
    /// PP13-100: Validates that GetCollectionInfo handles various metadata value types
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_ShouldHandleVariousMetadataTypes()
    {
        // Arrange - Create collection with various metadata types
        const string collectionName = "various-types-pp13-100";
        var metadata = new Dictionary<string, object>
        {
            ["string_value"] = "hello",
            ["int_value"] = 42,
            ["float_value"] = 3.14,
            ["bool_value"] = true
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, metadata),
            operationName: "Create collection with various metadata types");

        // Act - Get collection info
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo");

        // Assert - Verify all types are preserved
        Assert.That(result, Is.Not.Null, "Result should not be null");

        var resultType = result.GetType();
        var successProp = resultType.GetProperty("success");
        var metadataProp = resultType.GetProperty("metadata");

        Assert.That(successProp?.GetValue(result), Is.True, "Operation should succeed");

        var returnedMetadata = metadataProp?.GetValue(result) as Dictionary<string, object>;
        Assert.That(returnedMetadata, Is.Not.Null, "Metadata should be a Dictionary<string, object>");

        // Check string value
        Assert.That(returnedMetadata!.ContainsKey("string_value"), Is.True, "Should contain string_value");
        Assert.That(returnedMetadata["string_value"], Is.EqualTo("hello"), "String value should match");

        // Check int value
        Assert.That(returnedMetadata.ContainsKey("int_value"), Is.True, "Should contain int_value");
        Assert.That(returnedMetadata["int_value"], Is.EqualTo(42), "Int value should match");

        // Check float value (may be double due to conversion)
        Assert.That(returnedMetadata.ContainsKey("float_value"), Is.True, "Should contain float_value");
        Assert.That(Convert.ToDouble(returnedMetadata["float_value"]), Is.EqualTo(3.14).Within(0.001),
            "Float value should match within tolerance");

        // Check bool value
        Assert.That(returnedMetadata.ContainsKey("bool_value"), Is.True, "Should contain bool_value");
        Assert.That(returnedMetadata["bool_value"], Is.True, "Bool value should be true");
    }

    /// <summary>
    /// PP13-100: Validates that GetCollectionInfo returns correct document count alongside metadata
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_ShouldReturnDocumentCountWithMetadata()
    {
        // Arrange - Create collection with metadata and add documents
        const string collectionName = "with-docs-pp13-100";
        var metadata = new Dictionary<string, object>
        {
            ["purpose"] = "document-count-test"
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, metadata),
            operationName: "Create collection");

        // Add some documents
        var documents = new List<string> { "Document 1", "Document 2", "Document 3" };
        var ids = new List<string> { "doc-1", "doc-2", "doc-3" };
        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.AddDocumentsAsync(collectionName, documents, ids),
            operationName: "Add documents");

        // Act - Get collection info
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo");

        // Assert - Verify both metadata and document count
        var resultType = result.GetType();
        var successProp = resultType.GetProperty("success");
        var metadataProp = resultType.GetProperty("metadata");
        var docCountProp = resultType.GetProperty("document_count");

        Assert.That(successProp?.GetValue(result), Is.True, "Operation should succeed");

        // Verify metadata
        var returnedMetadata = metadataProp?.GetValue(result) as Dictionary<string, object>;
        Assert.That(returnedMetadata, Is.Not.Null, "Metadata should not be null");
        Assert.That(returnedMetadata!["purpose"], Is.EqualTo("document-count-test"), "Purpose should match");

        // Verify document count
        var docCount = docCountProp?.GetValue(result);
        Assert.That(docCount, Is.GreaterThanOrEqualTo(3), "Document count should be at least 3");
    }

    /// <summary>
    /// PP13-100: Validates that GetCollectionInfo handles non-existent collection correctly.
    /// Note: GetCollectionAsync throws PythonException for non-existent collections, which
    /// is caught by the exception handler returning OPERATION_FAILED.
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_NonExistentCollection_ShouldReturnError()
    {
        // Arrange - Use a collection name that doesn't exist
        const string collectionName = "non-existent-collection-pp13-100";

        // Act - Get collection info for non-existent collection
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo for non-existent collection");

        // Assert - Should return error
        var resultType = result.GetType();
        var successProp = resultType.GetProperty("success");
        var errorProp = resultType.GetProperty("error");
        var messageProp = resultType.GetProperty("message");

        Assert.That(successProp?.GetValue(result), Is.False, "Operation should fail");
        // Note: GetCollectionAsync throws PythonException which is caught by exception handler
        Assert.That(errorProp?.GetValue(result), Is.EqualTo("OPERATION_FAILED"),
            "Error code should be OPERATION_FAILED when collection doesn't exist");
        Assert.That(messageProp?.GetValue(result)?.ToString(), Does.Contain("does not exist"),
            "Error message should indicate collection doesn't exist");
    }

    /// <summary>
    /// PP13-100: Validates that metadata is preserved after document operations
    /// </summary>
    [Test]
    public async Task GetCollectionInfo_MetadataPreservedAfterDocumentOperations()
    {
        // Arrange - Create collection with metadata
        const string collectionName = "preserved-meta-pp13-100";
        var metadata = new Dictionary<string, object>
        {
            ["created_by"] = "test-suite",
            ["version"] = 1
        };

        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.CreateCollectionAsync(collectionName, metadata),
            operationName: "Create collection");

        // Add documents
        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.AddDocumentsAsync(collectionName,
                new List<string> { "Test document" },
                new List<string> { "test-doc-1" }),
            operationName: "Add document");

        // Update document
        await TestUtilities.ExecuteWithTimeoutAsync(
            _service.UpdateDocumentsAsync(collectionName,
                new List<string> { "test-doc-1" },
                new List<string> { "Updated content" }),
            operationName: "Update document");

        // Act - Get collection info
        var result = await TestUtilities.ExecuteWithTimeoutAsync(
            _tool.GetCollectionInfo(collectionName),
            operationName: "GetCollectionInfo");

        // Assert - Metadata should still be intact
        var resultType = result.GetType();
        var metadataProp = resultType.GetProperty("metadata");

        var returnedMetadata = metadataProp?.GetValue(result) as Dictionary<string, object>;
        Assert.That(returnedMetadata, Is.Not.Null, "Metadata should not be null");
        Assert.That(returnedMetadata!["created_by"], Is.EqualTo("test-suite"),
            "Metadata should be preserved after document operations");
        Assert.That(returnedMetadata["version"], Is.EqualTo(1),
            "Version metadata should be preserved");
    }
}
