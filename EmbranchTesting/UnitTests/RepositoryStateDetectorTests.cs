using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Embranch.Models;
using Embranch.Services;

namespace EmbranchTesting.UnitTests;

/// <summary>
/// PP13-87: Unit tests for RepositoryStateDetector service
/// </summary>
[TestFixture]
[Category("Unit")]
public class RepositoryStateDetectorTests
{
    private Mock<ILogger<RepositoryStateDetector>> _loggerMock = null!;
    private Mock<IEmbranchStateManifest> _manifestServiceMock = null!;
    private Mock<IDoltCli> _doltCliMock = null!;
    private Mock<IChromaDbService> _chromaServiceMock = null!;
    private IOptions<DoltConfiguration> _doltConfig = null!;
    private IOptions<ServerConfiguration> _serverConfig = null!;
    private RepositoryStateDetector _detector = null!;
    private string _testDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        _manifestServiceMock = new Mock<IEmbranchStateManifest>();
        _doltCliMock = new Mock<IDoltCli>();
        _chromaServiceMock = new Mock<IChromaDbService>();

        // Create test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), "PP13_87_StateDetectorTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var doltConfigPath = Path.Combine(_testDirectory, "dolt-repo");
        var chromaConfigPath = Path.Combine(_testDirectory, "chroma-data");

        _doltConfig = Options.Create(new DoltConfiguration
        {
            RepositoryPath = doltConfigPath
        });

        _serverConfig = Options.Create(new ServerConfiguration
        {
            ChromaDataPath = chromaConfigPath
        });

        _detector = new RepositoryStateDetector(
            _loggerMock.Object,
            _manifestServiceMock.Object,
            _doltCliMock.Object,
            _chromaServiceMock.Object,
            _doltConfig,
            _serverConfig);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public async Task AnalyzeStateAsync_NoManifestNoDoltNoChroma_ReturnsUninitialized()
    {
        // Arrange - PP13-87-C1: Updated to use FindManifestAsync
        SetupFindManifestMock(false, null);

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.Uninitialized));
        Assert.That(result.AvailableActions, Does.Contain("DoltClone"));
        Assert.That(result.AvailableActions, Does.Contain("DoltInit"));
    }

    /// <summary>
    /// PP13-87-C1: Helper method to setup FindManifestAsync mock with proper return values
    /// </summary>
    private void SetupFindManifestMock(bool found, string? manifestPath, DmmsManifest? manifest = null)
    {
        var searchedLocations = new[]
        {
            Path.Combine(_testDirectory, ".dmms", "state.json"),
            Path.Combine(_testDirectory, "mcpdata", "server", ".dmms", "state.json")
        };

        // PP13-87-C1: Updated to handle optional chromaDataPath parameter
        _manifestServiceMock.Setup(m => m.FindManifestAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((found, manifestPath, searchedLocations));

        if (found && manifest != null)
        {
            _manifestServiceMock.Setup(m => m.ReadManifestAsync(It.IsAny<string>()))
                .ReturnsAsync(manifest);
        }
    }

    [Test]
    public async Task AnalyzeStateAsync_ManifestExistsNoDoltNoChroma_ReturnsNeedsFullBootstrap()
    {
        // Arrange - PP13-87-C1: Updated to use FindManifestAsync
        var manifestPath = Path.Combine(_testDirectory, ".dmms", "state.json");
        SetupFindManifestMock(true, manifestPath, CreateTestManifest("https://dolthub.com/test/repo"));

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.ManifestOnly_NeedsFullBootstrap));
        Assert.That(result.AvailableActions, Does.Contain("BootstrapRepository"));
        Assert.That(result.Manifest, Is.Not.Null);
        Assert.That(result.Manifest!.RemoteUrl, Is.EqualTo("https://dolthub.com/test/repo"));
    }

    [Test]
    public async Task AnalyzeStateAsync_ManifestAndDoltExistsNoChroma_ReturnsNeedsChromaBootstrap()
    {
        // Arrange - PP13-87-C1: Updated to use FindManifestAsync
        var manifestPath = Path.Combine(_testDirectory, ".dmms", "state.json");
        SetupFindManifestMock(true, manifestPath, CreateTestManifest("https://dolthub.com/test/repo"));

        // Create a valid .dolt directory (PP13-87-C1: must have noms/ or config)
        var doltPath = Path.Combine(_doltConfig.Value.RepositoryPath);
        Directory.CreateDirectory(doltPath);
        var doltDir = Path.Combine(doltPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));  // PP13-87-C1: make it valid

        // Mock Dolt as initialized
        _doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(true);
        _doltCliMock.Setup(d => d.GetCurrentBranchAsync()).ReturnsAsync("main");
        _doltCliMock.Setup(d => d.GetHeadCommitHashAsync()).ReturnsAsync("abc1234567890");

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.ManifestOnly_NeedsChromaBootstrap));
        Assert.That(result.DoltInfo, Is.Not.Null);
        Assert.That(result.DoltInfo!.IsValid, Is.True);
    }

    [Test]
    public async Task AnalyzeStateAsync_AllExists_ReturnsReady()
    {
        // Arrange - PP13-87-C1: Updated to use FindManifestAsync
        var manifestPath = Path.Combine(_testDirectory, ".dmms", "state.json");
        SetupFindManifestMock(true, manifestPath, CreateTestManifest("https://dolthub.com/test/repo"));

        // Create valid Dolt directory (PP13-87-C1: must have noms/ or config)
        var doltPath = _doltConfig.Value.RepositoryPath;
        Directory.CreateDirectory(doltPath);
        var doltDir = Path.Combine(doltPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Create Chroma directory with sqlite file
        var chromaPath = _serverConfig.Value.ChromaDataPath;
        Directory.CreateDirectory(chromaPath);
        File.WriteAllText(Path.Combine(chromaPath, "chroma.sqlite3"), "test");

        // Mock services
        _doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(true);
        _doltCliMock.Setup(d => d.GetCurrentBranchAsync()).ReturnsAsync("main");
        _doltCliMock.Setup(d => d.GetHeadCommitHashAsync()).ReturnsAsync("abc1234567890");
        _chromaServiceMock.Setup(c => c.ListCollectionsAsync(It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string> { "collection1", "collection2" });

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.Ready));
        Assert.That(result.State == RepositoryState.Ready, Is.True);
        Assert.That(result.ChromaInfo, Is.Not.Null);
        Assert.That(result.ChromaInfo!.CollectionCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AnalyzeStateAsync_DoltExistsNoManifest_ReturnsNeedsManifest()
    {
        // Arrange - PP13-87-C1: Updated to use FindManifestAsync
        SetupFindManifestMock(false, null);

        // Create valid Dolt directory (PP13-87-C1: must have noms/ or config)
        var doltPath = _doltConfig.Value.RepositoryPath;
        Directory.CreateDirectory(doltPath);
        var doltDir = Path.Combine(doltPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Mock Dolt as initialized
        _doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(true);
        _doltCliMock.Setup(d => d.GetCurrentBranchAsync()).ReturnsAsync("main");
        _doltCliMock.Setup(d => d.GetHeadCommitHashAsync()).ReturnsAsync("abc1234567890");

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.InfrastructureOnly_NeedsManifest));
        Assert.That(result.AvailableActions, Does.Contain("InitManifest"));
    }

    [Test]
    public async Task FindDoltRepositoryAsync_DirectDolt_ReturnsNotNested()
    {
        // Arrange - PP13-87-C1: Create valid .dolt with noms/
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        Directory.CreateDirectory(configuredPath);
        var doltDir = Path.Combine(configuredPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var (exists, actualPath, isNested) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert
        Assert.That(exists, Is.True);
        Assert.That(actualPath, Is.EqualTo(Path.GetFullPath(configuredPath)));
        Assert.That(isNested, Is.False);
    }

    [Test]
    public async Task FindDoltRepositoryAsync_NestedDolt_ReturnsNested()
    {
        // Arrange - simulate CLI clone creating nested directory
        // PP13-87-C1: Create valid .dolt with noms/
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        var nestedPath = Path.Combine(configuredPath, "my-database");
        Directory.CreateDirectory(configuredPath);
        Directory.CreateDirectory(nestedPath);
        var doltDir = Path.Combine(nestedPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var (exists, actualPath, isNested) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert
        Assert.That(exists, Is.True);
        Assert.That(actualPath, Is.EqualTo(nestedPath));
        Assert.That(isNested, Is.True);
    }

    [Test]
    public async Task FindDoltRepositoryAsync_NoDolt_ReturnsFalse()
    {
        // Arrange
        var configuredPath = Path.Combine(_testDirectory, "empty-dir");
        Directory.CreateDirectory(configuredPath);

        // Act
        var (exists, actualPath, isNested) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert
        Assert.That(exists, Is.False);
        Assert.That(actualPath, Is.Null);
    }

    [Test]
    public void GetEffectiveDoltPath_DirectDolt_ReturnsConfiguredPath()
    {
        // Arrange - PP13-87-C1: Create valid .dolt with noms/
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        Directory.CreateDirectory(configuredPath);
        var doltDir = Path.Combine(configuredPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var result = _detector.GetEffectiveDoltPath(configuredPath);

        // Assert
        Assert.That(result, Is.EqualTo(Path.GetFullPath(configuredPath)));
    }

    [Test]
    public void GetEffectiveDoltPath_NestedDolt_ReturnsNestedPath()
    {
        // Arrange - PP13-87-C1: Create valid .dolt with noms/
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        var nestedPath = Path.Combine(configuredPath, "nested-db");
        Directory.CreateDirectory(configuredPath);
        Directory.CreateDirectory(nestedPath);
        var doltDir = Path.Combine(nestedPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var result = _detector.GetEffectiveDoltPath(configuredPath);

        // Assert
        Assert.That(result, Is.EqualTo(nestedPath));
    }

    [Test]
    public async Task ChromaDataExistsAsync_WithSqliteFile_ReturnsTrue()
    {
        // Arrange
        var chromaPath = Path.Combine(_testDirectory, "chroma-data");
        Directory.CreateDirectory(chromaPath);
        File.WriteAllText(Path.Combine(chromaPath, "chroma.sqlite3"), "test");

        // Act
        var result = await _detector.ChromaDataExistsAsync(chromaPath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ChromaDataExistsAsync_EmptyDirectory_ReturnsFalse()
    {
        // Arrange
        var chromaPath = Path.Combine(_testDirectory, "empty-chroma");
        Directory.CreateDirectory(chromaPath);

        // Act
        var result = await _detector.ChromaDataExistsAsync(chromaPath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ValidatePathsAsync_NoDolt_ReturnsValidWithNoIssues()
    {
        // Arrange - empty test directory

        // Act
        var result = await _detector.ValidatePathsAsync(_testDirectory);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public async Task ValidatePathsAsync_NestedDolt_ReturnsInvalidWithIssue()
    {
        // Arrange - create nested Dolt structure with valid .dolt
        // PP13-87-C1: Create valid .dolt with noms/
        var configuredPath = _doltConfig.Value.RepositoryPath;
        var nestedPath = Path.Combine(configuredPath, "nested-db");
        Directory.CreateDirectory(configuredPath);
        Directory.CreateDirectory(nestedPath);
        var doltDir = Path.Combine(nestedPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var result = await _detector.ValidatePathsAsync(_testDirectory);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues, Has.Length.GreaterThan(0));
        Assert.That(result.Issues[0].ActualDotDoltLocation, Is.EqualTo(nestedPath));
    }

    private DmmsManifest CreateTestManifest(string? remoteUrl = null)
    {
        return new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = remoteUrl,
                CurrentBranch = "main",
                DefaultBranch = "main"
            },
            Initialization = new InitializationConfig
            {
                Mode = InitializationMode.Auto
            }
        };
    }
}
