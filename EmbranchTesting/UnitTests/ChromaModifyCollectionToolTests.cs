using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;

namespace EmbranchTesting.UnitTests;

[TestFixture]
public class ChromaModifyCollectionToolTests
{
    private Mock<ILogger<ChromaModifyCollectionTool>> _mockLogger = null!;
    private Mock<IChromaDbService> _mockChromaService = null!;
    private Mock<IDeletionTracker> _mockDeletionTracker = null!;
    private Mock<IDoltCli> _mockDoltCli = null!;
    private Mock<IOptions<DoltConfiguration>> _mockDoltOptions = null!;
    private DoltConfiguration _doltConfig = null!;
    private ChromaModifyCollectionTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<ChromaModifyCollectionTool>>();
        _mockChromaService = new Mock<IChromaDbService>();
        _mockDeletionTracker = new Mock<IDeletionTracker>();
        _mockDoltCli = new Mock<IDoltCli>();
        _mockDoltOptions = new Mock<IOptions<DoltConfiguration>>();
        
        _doltConfig = new DoltConfiguration
        {
            RepositoryPath = "/test/repo"
        };
        _mockDoltOptions.Setup(x => x.Value).Returns(_doltConfig);

        _tool = new ChromaModifyCollectionTool(
            _mockLogger.Object,
            _mockChromaService.Object,
            _mockDeletionTracker.Object,
            _mockDoltCli.Object,
            _mockDoltOptions.Object
        );
    }

    /// <summary>
    /// Tests that renaming a collection tracks the operation and calls ModifyCollectionAsync
    /// </summary>
    [Test]
    public async Task ModifyCollection_WithRename_ShouldTrackRenameOperation()
    {
        // Arrange
        const string originalName = "test-collection";
        const string newName = "renamed-collection";
        var collectionData = new Dictionary<string, object>
        {
            ["id"] = "12345",
            ["name"] = originalName,
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(originalName))
            .ReturnsAsync(collectionData);

        _mockChromaService.Setup(x => x.ModifyCollectionAsync(originalName, newName, null))
            .ReturnsAsync(true);

        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        // Act
        var result = await _tool.ModifyCollection(originalName, newName);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.tracked, Is.True);
        Assert.That(resultObj?.collection?.original_name, Is.EqualTo(originalName));
        Assert.That(resultObj?.collection?.new_name, Is.EqualTo(newName));
        Assert.That(resultObj?.changes?.name_changed, Is.True);

        // Verify tracking was called for rename
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            _doltConfig.RepositoryPath,
            originalName,
            newName,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<Dictionary<string, object>>(),
            "main",
            "abc123"
        ), Times.Once);

        // Verify backend was called
        _mockChromaService.Verify(x => x.ModifyCollectionAsync(originalName, newName, null), Times.Once);
    }

    /// <summary>
    /// Tests that metadata update tracks the operation and calls ModifyCollectionAsync
    /// </summary>
    [Test]
    public async Task ModifyCollection_WithMetadataUpdate_ShouldTrackMetadataOperation()
    {
        // Arrange
        const string collectionName = "test-collection";
        var newMetadata = new Dictionary<string, object> { ["key2"] = "value2", ["key3"] = 123 };
        var collectionData = new Dictionary<string, object>
        {
            ["id"] = "12345",
            ["name"] = collectionName,
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync(collectionData);

        _mockChromaService.Setup(x => x.ModifyCollectionAsync(collectionName, null, newMetadata))
            .ReturnsAsync(true);

        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        // Act
        var result = await _tool.ModifyCollection(collectionName, null, newMetadata);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.tracked, Is.True);
        Assert.That(resultObj?.changes?.metadata_changed, Is.True);
        Assert.That(resultObj?.changes?.name_changed, Is.False);

        // Verify tracking was called for metadata update
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            _doltConfig.RepositoryPath,
            collectionName,
            collectionName, // New name same as old for metadata-only update
            It.IsAny<Dictionary<string, object>>(),
            newMetadata,
            "main",
            "abc123"
        ), Times.Once);

        // Verify backend was called
        _mockChromaService.Verify(x => x.ModifyCollectionAsync(collectionName, null, newMetadata), Times.Once);
    }

    /// <summary>
    /// Tests that both rename and metadata update are tracked and executed
    /// </summary>
    [Test]
    public async Task ModifyCollection_WithBothRenameAndMetadata_ShouldTrackRenameOperation()
    {
        // Arrange
        const string originalName = "test-collection";
        const string newName = "renamed-collection";
        var newMetadata = new Dictionary<string, object> { ["key2"] = "value2" };
        var collectionData = new Dictionary<string, object>
        {
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(originalName))
            .ReturnsAsync(collectionData);

        _mockChromaService.Setup(x => x.ModifyCollectionAsync(originalName, newName, newMetadata))
            .ReturnsAsync(true);

        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        // Act
        var result = await _tool.ModifyCollection(originalName, newName, newMetadata);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.tracked, Is.True);
        Assert.That(resultObj?.changes?.name_changed, Is.True);
        Assert.That(resultObj?.changes?.metadata_changed, Is.True);

        // Verify tracking was called
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            _doltConfig.RepositoryPath,
            originalName,
            newName,
            It.IsAny<Dictionary<string, object>>(),
            newMetadata,
            "main",
            "abc123"
        ), Times.Once);

        // Verify backend was called
        _mockChromaService.Verify(x => x.ModifyCollectionAsync(originalName, newName, newMetadata), Times.Once);
    }

    /// <summary>
    /// Tests that no tracking or backend call happens when no changes are requested
    /// </summary>
    [Test]
    public async Task ModifyCollection_WithNoChanges_ShouldNotTrack()
    {
        // Arrange
        const string collectionName = "test-collection";
        var collectionData = new Dictionary<string, object>
        {
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync(collectionData);

        // ModifyCollectionAsync with no changes should still succeed (no-op)
        _mockChromaService.Setup(x => x.ModifyCollectionAsync(collectionName, null, null))
            .ReturnsAsync(true);

        // Act - no new name and no new metadata
        var result = await _tool.ModifyCollection(collectionName);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True); // Should succeed even with no changes
        Assert.That(resultObj?.tracked, Is.False); // Should not track when no changes

        // Verify tracking was not called
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(), It.IsAny<string>()
        ), Times.Never);

        // Verify backend was still called (even for no-op)
        _mockChromaService.Verify(x => x.ModifyCollectionAsync(collectionName, null, null), Times.Once);
    }

    [Test]
    public async Task ModifyCollection_WithNonExistentCollection_ShouldReturnNotFoundError()
    {
        // Arrange
        const string collectionName = "nonexistent-collection";
        
        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _tool.ModifyCollection(collectionName, "new-name");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("COLLECTION_NOT_FOUND"));
        Assert.That(resultObj?.message, Does.Contain("does not exist"));

        // Verify tracking was not called
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(), It.IsAny<string>()
        ), Times.Never);
    }

    [Test]
    public async Task ModifyCollection_WithEmptyCollectionName_ShouldReturnError()
    {
        // Arrange & Act
        var result = await _tool.ModifyCollection("");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("COLLECTION_NAME_REQUIRED"));

        // Verify no service calls were made
        _mockChromaService.Verify(x => x.GetCollectionAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ModifyCollection_WhenTrackingThrowsException_ShouldReturnError()
    {
        // Arrange
        const string collectionName = "test-collection";
        var newMetadata = new Dictionary<string, object> { ["key2"] = "value2" };
        var collectionData = new Dictionary<string, object>
        {
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync(collectionData);
        
        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");
        
        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");
        
        _mockDeletionTracker.Setup(x => x.TrackCollectionUpdateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(), It.IsAny<string>()
        )).ThrowsAsync(new Exception("Tracking failed"));

        // Act
        var result = await _tool.ModifyCollection(collectionName, null, newMetadata);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("OPERATION_FAILED"));
        Assert.That(resultObj?.message, Does.Contain("Failed to modify collection"));
    }

    /// <summary>
    /// Tests that collection metadata is extracted correctly, excluding id and name fields
    /// </summary>
    [Test]
    public async Task ExtractCollectionMetadata_WithComplexData_ShouldExtractCorrectly()
    {
        // Arrange
        const string collectionName = "test-collection";
        var collectionData = new Dictionary<string, object>
        {
            ["id"] = "12345",
            ["name"] = collectionName,
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" },
            ["custom_field"] = "custom_value",
            ["numeric_field"] = 42
        };
        var newName = "new-name";

        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync(collectionData);

        _mockChromaService.Setup(x => x.ModifyCollectionAsync(collectionName, newName, null))
            .ReturnsAsync(true);

        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        // Act
        await _tool.ModifyCollection(collectionName, newName);

        // Assert - Verify tracking was called with correctly extracted metadata (excluding id and name)
        _mockDeletionTracker.Verify(x => x.TrackCollectionUpdateAsync(
            _doltConfig.RepositoryPath,
            collectionName,
            newName,
            It.Is<Dictionary<string, object>>(m =>
                m.ContainsKey("metadata") &&
                m.ContainsKey("custom_field") &&
                m.ContainsKey("numeric_field") &&
                !m.ContainsKey("id") &&
                !m.ContainsKey("name")),
            It.IsAny<Dictionary<string, object>>(),
            "main",
            "abc123"
        ), Times.Once);
    }

    /// <summary>
    /// Tests that when ModifyCollectionAsync returns false, the tool returns failure
    /// </summary>
    [Test]
    public async Task ModifyCollection_WhenBackendFails_ShouldReturnFailure()
    {
        // Arrange
        const string collectionName = "test-collection";
        var newMetadata = new Dictionary<string, object> { ["key2"] = "value2" };
        var collectionData = new Dictionary<string, object>
        {
            ["metadata"] = new Dictionary<string, object> { ["key1"] = "value1" }
        };

        _mockChromaService.Setup(x => x.GetCollectionAsync(collectionName))
            .ReturnsAsync(collectionData);

        _mockChromaService.Setup(x => x.ModifyCollectionAsync(collectionName, null, newMetadata))
            .ReturnsAsync(false); // Backend returns failure

        _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        // Act
        var result = await _tool.ModifyCollection(collectionName, null, newMetadata);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("MODIFICATION_FAILED"));
        Assert.That(resultObj?.tracked, Is.True); // Tracking was still done before the backend call
    }
}