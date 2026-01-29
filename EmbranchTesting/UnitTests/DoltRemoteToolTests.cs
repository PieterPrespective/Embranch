using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;

namespace EmbranchTesting.UnitTests;

/// <summary>
/// PP13-88: Unit tests for DoltRemoteTool.
/// Tests the add, remove, and list operations for Dolt remotes.
/// </summary>
[TestFixture]
public class DoltRemoteToolTests
{
    private Mock<ILogger<DoltRemoteTool>> _mockLogger = null!;
    private Mock<IDoltCli> _mockDoltCli = null!;
    private Mock<IRepositoryStateDetector> _mockStateDetector = null!;
    private Mock<ISyncStateChecker> _mockSyncStateChecker = null!;
    private DoltRemoteTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<DoltRemoteTool>>();
        _mockDoltCli = new Mock<IDoltCli>();
        _mockStateDetector = new Mock<IRepositoryStateDetector>();
        _mockSyncStateChecker = new Mock<ISyncStateChecker>();

        _mockDoltCli.Setup(x => x.CheckDoltAvailableAsync())
            .ReturnsAsync(new DoltCommandResult(true, "dolt version 1.0.0", "", 0));

        _mockDoltCli.Setup(x => x.IsInitializedAsync())
            .ReturnsAsync(true);

        _mockSyncStateChecker.Setup(x => x.GetProjectRootAsync())
            .ReturnsAsync("/test/project");

        _tool = new DoltRemoteTool(
            _mockLogger.Object,
            _mockDoltCli.Object,
            _mockStateDetector.Object,
            _mockSyncStateChecker.Object
        );
    }

    #region List Action Tests

    [Test]
    [Description("ListRemotes returns configured remotes when remotes exist")]
    public async Task ListRemotes_WithConfiguredRemotes_ReturnsRemotesList()
    {
        // Arrange
        var remotes = new List<RemoteInfo>
        {
            new RemoteInfo("origin", "dolthub.com/user/repo"),
            new RemoteInfo("backup", "dolthub.com/user/repo-backup")
        };
        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(remotes);

        // Act
        var result = await _tool.DoltRemote(action: "list");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.action, Is.EqualTo("list"));
        Assert.That(resultObj?.count, Is.EqualTo(2));
        Assert.That(resultObj?.message, Does.Contain("2 configured remote(s)"));
    }

    [Test]
    [Description("ListRemotes returns empty array message when no remotes configured")]
    public async Task ListRemotes_NoRemotes_ReturnsEmptyArrayWithGuidance()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        // Act
        var result = await _tool.DoltRemote(action: "list");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.count, Is.EqualTo(0));
        Assert.That(resultObj?.message, Does.Contain("No remotes configured"));
        Assert.That(resultObj?.message, Does.Contain("DoltRemote(action='add'"));
    }

    #endregion

    #region Add Action Tests

    [Test]
    [Description("AddRemote succeeds with valid name and URL")]
    public async Task AddRemote_WithValidParams_ReturnsSuccess()
    {
        // Arrange
        const string remoteName = "origin";
        const string remoteUrl = "dolthub.com/user/repo";

        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        _mockDoltCli.Setup(x => x.AddRemoteAsync(remoteName, remoteUrl))
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        // Act
        var result = await _tool.DoltRemote(action: "add", name: remoteName, url: remoteUrl);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.action, Is.EqualTo("add"));
        Assert.That(resultObj?.remote?.name, Is.EqualTo(remoteName));
        Assert.That(resultObj?.remote?.url, Is.EqualTo(remoteUrl));
        Assert.That(resultObj?.message, Does.Contain("Successfully added remote"));
    }

    [Test]
    [Description("AddRemote returns error when name is missing")]
    public async Task AddRemote_MissingName_ReturnsRemoteNameRequiredError()
    {
        // Act
        var result = await _tool.DoltRemote(action: "add", name: null, url: "dolthub.com/user/repo");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("REMOTE_NAME_REQUIRED"));
        Assert.That(resultObj?.example, Is.Not.Null);
    }

    [Test]
    [Description("AddRemote returns error when URL is missing")]
    public async Task AddRemote_MissingUrl_ReturnsRemoteUrlRequiredError()
    {
        // Act
        var result = await _tool.DoltRemote(action: "add", name: "origin", url: null);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("REMOTE_URL_REQUIRED"));
        Assert.That(resultObj?.example, Is.Not.Null);
    }

    [Test]
    [Description("AddRemote returns error when remote already exists")]
    public async Task AddRemote_RemoteAlreadyExists_ReturnsRemoteExistsError()
    {
        // Arrange
        const string remoteName = "origin";
        const string existingUrl = "dolthub.com/existing/repo";

        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo> { new RemoteInfo(remoteName, existingUrl) });

        // Act
        var result = await _tool.DoltRemote(action: "add", name: remoteName, url: "dolthub.com/new/repo");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("REMOTE_EXISTS"));
        Assert.That(resultObj?.existing_remote?.url, Is.EqualTo(existingUrl));
        Assert.That(resultObj?.suggestion, Does.Contain("remove"));
    }

    [Test]
    [Description("AddRemote returns error when Dolt command fails")]
    public async Task AddRemote_DoltCommandFails_ReturnsAddRemoteFailedError()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        _mockDoltCli.Setup(x => x.AddRemoteAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DoltCommandResult(false, "", "Network error", 1));

        // Act
        var result = await _tool.DoltRemote(action: "add", name: "origin", url: "dolthub.com/user/repo");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("ADD_REMOTE_FAILED"));
        Assert.That(resultObj?.message, Does.Contain("Network error"));
    }

    #endregion

    #region Remove Action Tests

    [Test]
    [Description("RemoveRemote succeeds with valid existing remote")]
    public async Task RemoveRemote_WithExistingRemote_ReturnsSuccess()
    {
        // Arrange
        const string remoteName = "origin";
        const string remoteUrl = "dolthub.com/user/repo";

        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo> { new RemoteInfo(remoteName, remoteUrl) });

        _mockDoltCli.Setup(x => x.RemoveRemoteAsync(remoteName))
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        // Act
        var result = await _tool.DoltRemote(action: "remove", name: remoteName);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.action, Is.EqualTo("remove"));
        Assert.That(resultObj?.removed_remote?.name, Is.EqualTo(remoteName));
        Assert.That(resultObj?.removed_remote?.url, Is.EqualTo(remoteUrl));
        Assert.That(resultObj?.message, Does.Contain("Successfully removed remote"));
    }

    [Test]
    [Description("RemoveRemote returns error when name is missing")]
    public async Task RemoveRemote_MissingName_ReturnsRemoteNameRequiredError()
    {
        // Act
        var result = await _tool.DoltRemote(action: "remove", name: null);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("REMOTE_NAME_REQUIRED"));
        Assert.That(resultObj?.example, Is.Not.Null);
    }

    [Test]
    [Description("RemoveRemote returns error when remote not found")]
    public async Task RemoveRemote_RemoteNotFound_ReturnsRemoteNotFoundError()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo> { new RemoteInfo("backup", "dolthub.com/backup") });

        // Act
        var result = await _tool.DoltRemote(action: "remove", name: "origin");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("REMOTE_NOT_FOUND"));
        Assert.That(resultObj?.message, Does.Contain("origin"));
        Assert.That(resultObj?.message, Does.Contain("backup")); // Lists available remotes
    }

    #endregion

    #region Validation Tests

    [Test]
    [Description("Invalid action returns error with valid actions list")]
    public async Task DoltRemote_InvalidAction_ReturnsInvalidActionError()
    {
        // Act
        var result = await _tool.DoltRemote(action: "invalid");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("INVALID_ACTION"));
        Assert.That(resultObj?.message, Does.Contain("invalid"));
    }

    [Test]
    [Description("NOT_INITIALIZED error includes state-aware guidance from PP13-87")]
    public async Task DoltRemote_NotInitialized_ReturnsErrorWithGuidance()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.IsInitializedAsync())
            .ReturnsAsync(false);

        var stateAnalysis = new RepositoryStateAnalysis
        {
            State = RepositoryState.Uninitialized,
            StateDescription = "No repository initialized",
            AvailableActions = System.Array.Empty<string>(),
            RecommendedAction = ""
        };
        _mockStateDetector.Setup(x => x.AnalyzeStateAsync(It.IsAny<string>()))
            .ReturnsAsync(stateAnalysis);

        // Act
        var result = await _tool.DoltRemote(action: "list");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("NOT_INITIALIZED"));
        Assert.That(resultObj?.repository_state, Is.EqualTo("Uninitialized"));
        Assert.That(resultObj?.suggested_action, Does.Contain("RepositoryStatus"));
    }

    [Test]
    [Description("DOLT_EXECUTABLE_NOT_FOUND error when Dolt is not available")]
    public async Task DoltRemote_DoltNotAvailable_ReturnsDoltNotFoundError()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.CheckDoltAvailableAsync())
            .ReturnsAsync(new DoltCommandResult(false, "", "Dolt not found in PATH", 1));

        // Act
        var result = await _tool.DoltRemote(action: "list");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.False);
        Assert.That(resultObj?.error, Is.EqualTo("DOLT_EXECUTABLE_NOT_FOUND"));
    }

    [Test]
    [Description("Default action is 'list' when not specified")]
    public async Task DoltRemote_DefaultAction_IsList()
    {
        // Arrange
        _mockDoltCli.Setup(x => x.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        // Act
        var result = await _tool.DoltRemote();

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.action, Is.EqualTo("list"));

        // Verify ListRemotesAsync was called
        _mockDoltCli.Verify(x => x.ListRemotesAsync(), Times.Once);
    }

    #endregion
}
