using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Services;
using Embranch.Models;
using Embranch.Tools;
using System.Collections.Generic;

namespace EmbranchTesting.IntegrationTests;

/// <summary>
/// PP13-88: Integration tests for Remote Configuration and Manifest Tool Consistency.
/// Tests:
/// 1. DoltRemote tool add/list/remove lifecycle
/// 2. ManifestSetRemote with configure_dolt_remote option
/// 3. Project root consistency across manifest tools
/// 4. Enhanced error messages with suggested actions
/// </summary>
[TestFixture]
public class PP13_88_RemoteConfigurationTests
{
    private Mock<ILogger<DoltRemoteTool>> _remoteToolLoggerMock = null!;
    private Mock<ILogger<ManifestSetRemoteTool>> _manifestSetRemoteLoggerMock = null!;
    private Mock<ILogger<InitManifestTool>> _initManifestLoggerMock = null!;
    private Mock<ILogger<UpdateManifestTool>> _updateManifestLoggerMock = null!;
    private Mock<IDoltCli> _doltCliMock = null!;
    private Mock<IEmbranchStateManifest> _manifestMock = null!;
    private Mock<IGitIntegration> _gitIntegrationMock = null!;
    private Mock<ISyncStateChecker> _syncStateCheckerMock = null!;
    private Mock<IRepositoryStateDetector> _stateDetectorMock = null!;
    private const string TestProjectRoot = "D:\\TestProject";
    private const string TestRemoteUrl = "dolthub.com/testuser/testrepo";

    [SetUp]
    public void Setup()
    {
        _remoteToolLoggerMock = new Mock<ILogger<DoltRemoteTool>>();
        _manifestSetRemoteLoggerMock = new Mock<ILogger<ManifestSetRemoteTool>>();
        _initManifestLoggerMock = new Mock<ILogger<InitManifestTool>>();
        _updateManifestLoggerMock = new Mock<ILogger<UpdateManifestTool>>();
        _doltCliMock = new Mock<IDoltCli>();
        _manifestMock = new Mock<IEmbranchStateManifest>();
        _gitIntegrationMock = new Mock<IGitIntegration>();
        _syncStateCheckerMock = new Mock<ISyncStateChecker>();
        _stateDetectorMock = new Mock<IRepositoryStateDetector>();

        // Default setup - Dolt available and initialized
        _doltCliMock.Setup(d => d.CheckDoltAvailableAsync())
            .ReturnsAsync(new DoltCommandResult(true, "dolt version 1.0.0", "", 0));
        _doltCliMock.Setup(d => d.IsInitializedAsync())
            .ReturnsAsync(true);

        // Default sync state checker returns test project root
        _syncStateCheckerMock.Setup(s => s.GetProjectRootAsync())
            .ReturnsAsync(TestProjectRoot);
    }

    #region DoltRemote Add/List/Remove Lifecycle Tests

    [Test]
    [Description("PP13-88: Complete remote lifecycle - add, list, remove, list")]
    public async Task DoltRemote_AddListRemove_CompletesLifecycle()
    {
        // Arrange
        var remoteList = new List<RemoteInfo>();

        _doltCliMock.Setup(d => d.ListRemotesAsync())
            .ReturnsAsync(() => remoteList);

        _doltCliMock.Setup(d => d.AddRemoteAsync("origin", TestRemoteUrl))
            .Callback(() => remoteList.Add(new RemoteInfo("origin", TestRemoteUrl)))
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        _doltCliMock.Setup(d => d.RemoveRemoteAsync("origin"))
            .Callback(() => remoteList.Clear())
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        var tool = CreateDoltRemoteTool();

        // Act & Assert - Step 1: List (empty)
        var listResult1 = await tool.DoltRemote(action: "list");
        var listObj1 = listResult1 as dynamic;
        Assert.That(listObj1?.success, Is.True);
        Assert.That(listObj1?.count, Is.EqualTo(0));

        // Act & Assert - Step 2: Add
        var addResult = await tool.DoltRemote(action: "add", name: "origin", url: TestRemoteUrl);
        var addObj = addResult as dynamic;
        Assert.That(addObj?.success, Is.True);
        Assert.That(addObj?.remote?.name, Is.EqualTo("origin"));

        // Act & Assert - Step 3: List (has remote)
        var listResult2 = await tool.DoltRemote(action: "list");
        var listObj2 = listResult2 as dynamic;
        Assert.That(listObj2?.success, Is.True);
        Assert.That(listObj2?.count, Is.EqualTo(1));

        // Act & Assert - Step 4: Remove
        var removeResult = await tool.DoltRemote(action: "remove", name: "origin");
        var removeObj = removeResult as dynamic;
        Assert.That(removeObj?.success, Is.True);
        Assert.That(removeObj?.removed_remote?.name, Is.EqualTo("origin"));

        // Act & Assert - Step 5: List (empty again)
        var listResult3 = await tool.DoltRemote(action: "list");
        var listObj3 = listResult3 as dynamic;
        Assert.That(listObj3?.success, Is.True);
        Assert.That(listObj3?.count, Is.EqualTo(0));
    }

    [Test]
    [Description("PP13-88: Add remote after DoltInit without remote - the original gap this fixes")]
    public async Task DoltRemote_AddAfterDoltInit_FillsOriginalGap()
    {
        // Arrange - Repository initialized without remote
        _doltCliMock.Setup(d => d.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        _doltCliMock.Setup(d => d.AddRemoteAsync("origin", TestRemoteUrl))
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        var tool = CreateDoltRemoteTool();

        // Act - Add remote to existing repo
        var result = await tool.DoltRemote(action: "add", name: "origin", url: TestRemoteUrl);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.message, Does.Contain("Successfully added remote"));
        Assert.That(resultObj?.next_steps, Is.Not.Null);

        // Verify AddRemoteAsync was called
        _doltCliMock.Verify(d => d.AddRemoteAsync("origin", TestRemoteUrl), Times.Once);
    }

    #endregion

    #region ManifestSetRemote with configure_dolt_remote Tests

    [Test]
    [Description("PP13-88: ManifestSetRemote with configure_dolt_remote=true updates both manifest and Dolt")]
    public async Task ManifestSetRemote_WithConfigureDoltRemote_UpdatesBoth()
    {
        // Arrange
        _doltCliMock.Setup(d => d.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo>());

        _doltCliMock.Setup(d => d.AddRemoteAsync("origin", TestRemoteUrl))
            .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

        _manifestMock.Setup(m => m.ReadManifestAsync(TestProjectRoot))
            .ReturnsAsync(new DmmsManifest
            {
                Version = "1.0",
                Dolt = new DoltManifestConfig { DefaultBranch = "main" }
            });

        _manifestMock.Setup(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(TestProjectRoot))
            .Returns($"{TestProjectRoot}\\.dmms\\state.json");

        var tool = CreateManifestSetRemoteTool();

        // Act
        var result = await tool.ManifestSetRemote(
            remote_url: TestRemoteUrl,
            configure_dolt_remote: true,
            remote_name: "origin");

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.dolt_remote?.configured, Is.True);
        Assert.That(resultObj?.dolt_remote?.name, Is.EqualTo("origin"));

        // Verify both manifest and Dolt remote were configured
        _manifestMock.Verify(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()), Times.Once);
        _doltCliMock.Verify(d => d.AddRemoteAsync("origin", TestRemoteUrl), Times.Once);
    }

    [Test]
    [Description("PP13-88: ManifestSetRemote with configure_dolt_remote=true but remote already exists")]
    public async Task ManifestSetRemote_RemoteAlreadyExists_ManifestUpdatedWithWarning()
    {
        // Arrange
        _doltCliMock.Setup(d => d.ListRemotesAsync())
            .ReturnsAsync(new List<RemoteInfo> { new RemoteInfo("origin", "dolthub.com/other/repo") });

        _manifestMock.Setup(m => m.ReadManifestAsync(TestProjectRoot))
            .ReturnsAsync(new DmmsManifest
            {
                Version = "1.0",
                Dolt = new DoltManifestConfig { DefaultBranch = "main" }
            });

        _manifestMock.Setup(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(TestProjectRoot))
            .Returns($"{TestProjectRoot}\\.dmms\\state.json");

        var tool = CreateManifestSetRemoteTool();

        // Act
        var result = await tool.ManifestSetRemote(
            remote_url: TestRemoteUrl,
            configure_dolt_remote: true);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);  // Manifest update succeeds
        Assert.That(resultObj?.dolt_remote?.configured, Is.False);  // Dolt remote not configured
        Assert.That(resultObj?.dolt_remote?.already_exists, Is.True);
        Assert.That(resultObj?.dolt_remote?.warning, Does.Contain("already exists"));

        // Verify manifest was still updated
        _manifestMock.Verify(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()), Times.Once);
    }

    [Test]
    [Description("PP13-88: ManifestSetRemote with configure_dolt_remote=false (default) only updates manifest")]
    public async Task ManifestSetRemote_DefaultBehavior_OnlyUpdatesManifest()
    {
        // Arrange
        _manifestMock.Setup(m => m.ReadManifestAsync(TestProjectRoot))
            .ReturnsAsync(new DmmsManifest
            {
                Version = "1.0",
                Dolt = new DoltManifestConfig { DefaultBranch = "main" }
            });

        _manifestMock.Setup(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(TestProjectRoot))
            .Returns($"{TestProjectRoot}\\.dmms\\state.json");

        var tool = CreateManifestSetRemoteTool();

        // Act - configure_dolt_remote defaults to false
        var result = await tool.ManifestSetRemote(remote_url: TestRemoteUrl);

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);
        Assert.That(resultObj?.dolt_remote?.configured, Is.False);

        // Verify manifest was updated but AddRemoteAsync was NOT called
        _manifestMock.Verify(m => m.WriteManifestAsync(TestProjectRoot, It.IsAny<DmmsManifest>()), Times.Once);
        _doltCliMock.Verify(d => d.AddRemoteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Project Root Consistency Tests

    [Test]
    [Description("PP13-88: InitManifest uses ISyncStateChecker for project root when not specified")]
    public async Task InitManifest_UsesEmbranchProjectRoot_WhenAvailable()
    {
        // Arrange
        const string embranchProjectRoot = "D:\\EmbranchConfiguredRoot";
        _syncStateCheckerMock.Setup(s => s.GetProjectRootAsync())
            .ReturnsAsync(embranchProjectRoot);

        _manifestMock.Setup(m => m.ManifestExistsAsync(embranchProjectRoot))
            .ReturnsAsync(false);

        _manifestMock.Setup(m => m.WriteManifestAsync(embranchProjectRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(embranchProjectRoot))
            .Returns($"{embranchProjectRoot}\\.dmms\\state.json");

        _gitIntegrationMock.Setup(g => g.IsGitRepositoryAsync(embranchProjectRoot))
            .ReturnsAsync(false);

        _doltCliMock.Setup(d => d.IsInitializedAsync())
            .ReturnsAsync(false);

        var tool = CreateInitManifestTool();

        // Act - Don't specify project_root
        var result = await tool.InitManifest();

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);

        // Verify it used the EMBRANCH_PROJECT_ROOT path
        _manifestMock.Verify(m => m.ManifestExistsAsync(embranchProjectRoot), Times.Once);
        _manifestMock.Verify(m => m.WriteManifestAsync(embranchProjectRoot, It.IsAny<DmmsManifest>()), Times.Once);
    }

    [Test]
    [Description("PP13-88: UpdateManifest uses ISyncStateChecker for project root when not specified")]
    public async Task UpdateManifest_UsesEmbranchProjectRoot_WhenAvailable()
    {
        // Arrange
        const string embranchProjectRoot = "D:\\EmbranchConfiguredRoot";
        _syncStateCheckerMock.Setup(s => s.GetProjectRootAsync())
            .ReturnsAsync(embranchProjectRoot);

        _manifestMock.Setup(m => m.ReadManifestAsync(embranchProjectRoot))
            .ReturnsAsync(new DmmsManifest
            {
                Version = "1.0",
                Dolt = new DoltManifestConfig { DefaultBranch = "main" },
                GitMapping = new GitMappingConfig { Enabled = false }
            });

        _manifestMock.Setup(m => m.WriteManifestAsync(embranchProjectRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(embranchProjectRoot))
            .Returns($"{embranchProjectRoot}\\.dmms\\state.json");

        _doltCliMock.Setup(d => d.IsInitializedAsync())
            .ReturnsAsync(true);

        _doltCliMock.Setup(d => d.GetHeadCommitHashAsync())
            .ReturnsAsync("abc123");

        _doltCliMock.Setup(d => d.GetCurrentBranchAsync())
            .ReturnsAsync("main");

        var tool = CreateUpdateManifestTool();

        // Act - Don't specify project_root
        var result = await tool.UpdateManifest();

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);

        // Verify it used the EMBRANCH_PROJECT_ROOT path
        _manifestMock.Verify(m => m.ReadManifestAsync(embranchProjectRoot), Times.Once);
        _manifestMock.Verify(m => m.WriteManifestAsync(embranchProjectRoot, It.IsAny<DmmsManifest>()), Times.Once);
    }

    [Test]
    [Description("PP13-88: All manifest tools fallback to Git root when ISyncStateChecker returns null")]
    public async Task ManifestTools_FallbackToGitRoot_WhenSyncStateCheckerReturnsNull()
    {
        // Arrange
        const string gitRoot = "D:\\GitDetectedRoot";
        _syncStateCheckerMock.Setup(s => s.GetProjectRootAsync())
            .ReturnsAsync((string?)null);

        _gitIntegrationMock.Setup(g => g.GetGitRootAsync(It.IsAny<string>()))
            .ReturnsAsync(gitRoot);

        _manifestMock.Setup(m => m.ManifestExistsAsync(gitRoot))
            .ReturnsAsync(false);

        _manifestMock.Setup(m => m.WriteManifestAsync(gitRoot, It.IsAny<DmmsManifest>()))
            .Returns(Task.CompletedTask);

        _manifestMock.Setup(m => m.GetManifestPath(gitRoot))
            .Returns($"{gitRoot}\\.dmms\\state.json");

        _gitIntegrationMock.Setup(g => g.IsGitRepositoryAsync(gitRoot))
            .ReturnsAsync(true);

        _gitIntegrationMock.Setup(g => g.GetCurrentGitCommitAsync(gitRoot))
            .ReturnsAsync("gitcommit123");

        _doltCliMock.Setup(d => d.IsInitializedAsync())
            .ReturnsAsync(false);

        var tool = CreateInitManifestTool();

        // Act
        var result = await tool.InitManifest();

        // Assert
        var resultObj = result as dynamic;
        Assert.That(resultObj?.success, Is.True);

        // Verify it fell back to git root detection
        _gitIntegrationMock.Verify(g => g.GetGitRootAsync(It.IsAny<string>()), Times.Once);
        _manifestMock.Verify(m => m.WriteManifestAsync(gitRoot, It.IsAny<DmmsManifest>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private DoltRemoteTool CreateDoltRemoteTool()
    {
        return new DoltRemoteTool(
            _remoteToolLoggerMock.Object,
            _doltCliMock.Object,
            _stateDetectorMock.Object,
            _syncStateCheckerMock.Object
        );
    }

    private ManifestSetRemoteTool CreateManifestSetRemoteTool()
    {
        return new ManifestSetRemoteTool(
            _manifestSetRemoteLoggerMock.Object,
            _manifestMock.Object,
            _syncStateCheckerMock.Object,
            _gitIntegrationMock.Object,
            _doltCliMock.Object
        );
    }

    private InitManifestTool CreateInitManifestTool()
    {
        return new InitManifestTool(
            _initManifestLoggerMock.Object,
            _manifestMock.Object,
            _doltCliMock.Object,
            _gitIntegrationMock.Object,
            _syncStateCheckerMock.Object
        );
    }

    private UpdateManifestTool CreateUpdateManifestTool()
    {
        return new UpdateManifestTool(
            _updateManifestLoggerMock.Object,
            _manifestMock.Object,
            _doltCliMock.Object,
            _gitIntegrationMock.Object,
            _syncStateCheckerMock.Object
        );
    }

    #endregion
}
