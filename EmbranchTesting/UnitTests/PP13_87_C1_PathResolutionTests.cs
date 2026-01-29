using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Embranch.Models;
using Embranch.Services;

namespace EmbranchTesting.UnitTests;

/// <summary>
/// PP13-87-C1: Unit tests for path resolution and manifest discovery fixes.
/// Tests the enhanced manifest search, .dolt validation, rogue detection, and effective path handling.
/// </summary>
[TestFixture]
[Category("Unit")]
public class PP13_87_C1_PathResolutionTests
{
    private Mock<ILogger<RepositoryStateDetector>> _loggerMock = null!;
    private Mock<ILogger<EmbranchStateManifest>> _manifestLoggerMock = null!;
    private Mock<IEmbranchStateManifest> _manifestServiceMock = null!;
    private Mock<IDoltCli> _doltCliMock = null!;
    private Mock<IChromaDbService> _chromaServiceMock = null!;
    private IOptions<DoltConfiguration> _doltConfig = null!;
    private IOptions<ServerConfiguration> _serverConfig = null!;
    private RepositoryStateDetector _detector = null!;
    private EmbranchStateManifest _manifestService = null!;
    private string _testDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        _manifestLoggerMock = new Mock<ILogger<EmbranchStateManifest>>();
        _manifestServiceMock = new Mock<IEmbranchStateManifest>();
        _doltCliMock = new Mock<IDoltCli>();
        _chromaServiceMock = new Mock<IChromaDbService>();

        // Create test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), "PP13_87_C1_PathResolutionTests", Guid.NewGuid().ToString());
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

        _manifestService = new EmbranchStateManifest(_manifestLoggerMock.Object);
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

    #region Manifest Path Discovery Tests

    [Test]
    [Description("FindManifestAsync should find manifest at standard project root location")]
    public async Task FindManifestAsync_ManifestAtProjectRoot_ReturnsCorrectPath()
    {
        // Arrange
        var dmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var manifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, searchedLocations) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True, "Manifest should be found");
        Assert.That(path, Is.EqualTo(manifestPath), "Should return correct manifest path");
        Assert.That(searchedLocations, Does.Contain(manifestPath), "Should include searched location");
    }

    [Test]
    [Description("FindManifestAsync should find manifest in mcpdata subdirectory")]
    public async Task FindManifestAsync_ManifestInMcpdata_ReturnsCorrectPath()
    {
        // Arrange - simulate PSKD deployment structure
        var serverDir = Path.Combine(_testDirectory, "mcpdata", "PSKD");
        var dmmsDir = Path.Combine(serverDir, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var manifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, searchedLocations) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True, "Manifest should be found in mcpdata subdirectory");
        Assert.That(path, Is.EqualTo(manifestPath), "Should return correct manifest path in mcpdata");
        Assert.That(searchedLocations.Length, Is.GreaterThan(1), "Should have searched multiple locations");
    }

    [Test]
    [Description("FindManifestAsync should return false when no manifest exists")]
    public async Task FindManifestAsync_NoManifest_ReturnsFalse()
    {
        // Arrange - empty directory

        // Act
        var (found, path, searchedLocations) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.False, "Should not find manifest");
        Assert.That(path, Is.Null, "Path should be null");
        Assert.That(searchedLocations.Length, Is.GreaterThan(0), "Should report searched locations");
    }

    [Test]
    [Description("FindManifestAsync should prefer project root over mcpdata when both exist")]
    public async Task FindManifestAsync_BothLocationsExist_PrefersProjectRoot()
    {
        // Arrange - create manifests at both locations
        var rootDmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(rootDmmsDir);
        var rootManifestPath = Path.Combine(rootDmmsDir, "state.json");
        File.WriteAllText(rootManifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        var serverDir = Path.Combine(_testDirectory, "mcpdata", "PSKD");
        var serverDmmsDir = Path.Combine(serverDir, ".dmms");
        Directory.CreateDirectory(serverDmmsDir);
        var serverManifestPath = Path.Combine(serverDmmsDir, "state.json");
        File.WriteAllText(serverManifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, _) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True);
        Assert.That(path, Is.EqualTo(rootManifestPath), "Should prefer project root location");
    }

    [Test]
    [Description("FindManifestAsync should find manifest relative to EMBRANCH_DATA_PATH")]
    public async Task FindManifestAsync_ManifestRelativeToDataPath_ReturnsCorrectPath()
    {
        // Arrange - simulate PSKD deployment structure
        // EMBRANCH_DATA_PATH = ./mcpdata/PSKD/data
        // Manifest at ./mcpdata/PSKD/.dmms/state.json
        var serverDir = Path.Combine(_testDirectory, "mcpdata", "PSKD");
        var dataPath = Path.Combine(serverDir, "data");
        Directory.CreateDirectory(dataPath);

        var dmmsDir = Path.Combine(serverDir, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var manifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act - pass dataPath to find manifest relative to it
        var (found, path, searchedLocations) = await _manifestService.FindManifestAsync(_testDirectory, dataPath);

        // Assert
        Assert.That(found, Is.True, "Manifest should be found relative to EMBRANCH_DATA_PATH");
        Assert.That(path, Is.EqualTo(manifestPath), "Should return correct manifest path");
        Assert.That(searchedLocations, Does.Contain(manifestPath), "Should include data-relative location in search");
    }

    #endregion

    #region PP13-87-C2: .embranch Folder Name Tests

    [Test]
    [Description("PP13-87-C2: FindManifestAsync should find manifest in .embranch folder")]
    public async Task FindManifestAsync_ManifestInEmbranch_ReturnsCorrectPath()
    {
        // Arrange
        var embranchDir = Path.Combine(_testDirectory, ".embranch");
        Directory.CreateDirectory(embranchDir);
        var manifestPath = Path.Combine(embranchDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, searchedLocations) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True, "Manifest should be found in .embranch folder");
        Assert.That(path, Is.EqualTo(manifestPath), "Should return correct manifest path");
        Assert.That(searchedLocations, Does.Contain(manifestPath), "Should include .embranch location in search");
    }

    [Test]
    [Description("PP13-87-C2: FindManifestAsync should prefer .embranch over .dmms when both exist")]
    public async Task FindManifestAsync_BothEmbranchAndDmmsExist_PrefersEmbranch()
    {
        // Arrange - create manifests in both .embranch and .dmms
        var embranchDir = Path.Combine(_testDirectory, ".embranch");
        Directory.CreateDirectory(embranchDir);
        var embranchManifestPath = Path.Combine(embranchDir, "state.json");
        File.WriteAllText(embranchManifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        var dmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var dmmsManifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(dmmsManifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, _) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True);
        Assert.That(path, Is.EqualTo(embranchManifestPath), "Should prefer .embranch over .dmms");
    }

    [Test]
    [Description("PP13-87-C2: FindManifestAsync should fall back to .dmms if .embranch doesn't exist")]
    public async Task FindManifestAsync_OnlyDmmsExists_FindsLegacyManifest()
    {
        // Arrange - only create manifest in legacy .dmms location
        var dmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var manifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","dolt":{},"git_mapping":{"enabled":true},"initialization":{},"collections":{}}""");

        // Act
        var (found, path, _) = await _manifestService.FindManifestAsync(_testDirectory);

        // Assert
        Assert.That(found, Is.True, "Should find manifest in legacy .dmms location");
        Assert.That(path, Is.EqualTo(manifestPath), "Should return legacy manifest path");
    }

    [Test]
    [Description("PP13-87-C2: GetManifestPath should return .embranch path for new projects")]
    public void GetManifestPath_NoExistingManifest_ReturnsEmbranchPath()
    {
        // Arrange - empty directory (no .embranch or .dmms)

        // Act
        var path = _manifestService.GetManifestPath(_testDirectory);

        // Assert
        Assert.That(path, Does.Contain(".embranch"), "Should use .embranch for new projects");
        Assert.That(path, Does.EndWith("state.json"), "Should end with state.json");
    }

    [Test]
    [Description("PP13-87-C2: GetManifestPath should return .dmms path if legacy manifest exists")]
    public void GetManifestPath_LegacyManifestExists_ReturnsDmmsPath()
    {
        // Arrange - create manifest in legacy .dmms location
        var dmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(dmmsDir);
        var manifestPath = Path.Combine(dmmsDir, "state.json");
        File.WriteAllText(manifestPath, """{"version":"1.0"}""");

        // Act
        var path = _manifestService.GetManifestPath(_testDirectory);

        // Assert
        Assert.That(path, Is.EqualTo(manifestPath), "Should return existing .dmms manifest path");
    }

    [Test]
    [Description("PP13-87-C2: GetManifestDirectoryPath should return .embranch for new projects")]
    public void GetManifestDirectoryPath_NoExistingDirectory_ReturnsEmbranchPath()
    {
        // Arrange - empty directory

        // Act
        var path = _manifestService.GetManifestDirectoryPath(_testDirectory);

        // Assert
        Assert.That(path, Does.EndWith(".embranch"), "Should use .embranch for new projects");
    }

    [Test]
    [Description("PP13-87-C2: GetManifestDirectoryPath should return .dmms if legacy directory exists")]
    public void GetManifestDirectoryPath_LegacyDirectoryExists_ReturnsDmmsPath()
    {
        // Arrange - create legacy .dmms directory
        var dmmsDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(dmmsDir);

        // Act
        var path = _manifestService.GetManifestDirectoryPath(_testDirectory);

        // Assert
        Assert.That(path, Is.EqualTo(dmmsDir), "Should return existing .dmms directory");
    }

    #endregion

    #region Dolt Validation Tests

    [Test]
    [Description("FindDoltRepositoryAsync should return valid for .dolt with noms directory")]
    public async Task FindDoltRepository_ValidWithNoms_ReturnsValid()
    {
        // Arrange
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        var doltDir = Path.Combine(configuredPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "noms"));

        // Act
        var (exists, actualPath, isNested) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert
        Assert.That(exists, Is.True, "Should find valid .dolt");
        Assert.That(actualPath, Is.EqualTo(Path.GetFullPath(configuredPath)));
        Assert.That(isNested, Is.False);
    }

    [Test]
    [Description("FindDoltRepositoryAsync should skip incomplete outer .dolt and find valid nested")]
    public async Task FindDoltRepository_IncompleteOuterValidNested_ReturnsNestedPath()
    {
        // Arrange - incomplete outer .dolt (only tmp/)
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        var outerDoltDir = Path.Combine(configuredPath, ".dolt");
        Directory.CreateDirectory(outerDoltDir);
        Directory.CreateDirectory(Path.Combine(outerDoltDir, "tmp"));  // Only tmp = incomplete

        // Valid nested .dolt
        var nestedPath = Path.Combine(configuredPath, "prespective-knowledge");
        var nestedDoltDir = Path.Combine(nestedPath, ".dolt");
        Directory.CreateDirectory(nestedDoltDir);
        Directory.CreateDirectory(Path.Combine(nestedDoltDir, "noms"));  // Has noms = valid

        // Act
        var (exists, actualPath, isNested) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert
        Assert.That(exists, Is.True, "Should find valid .dolt");
        Assert.That(actualPath, Is.EqualTo(nestedPath), "Should return nested path");
        Assert.That(isNested, Is.True, "Should be marked as nested");
    }

    [Test]
    [Description("FindDoltRepositoryAsync validates .dolt contains essential files")]
    public async Task ValidateDoltDirectory_OnlyTmpFolder_ReturnsFalse()
    {
        // Arrange - .dolt with only tmp (incomplete/rogue)
        var configuredPath = Path.Combine(_testDirectory, "dolt-repo");
        var doltDir = Path.Combine(configuredPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "tmp"));  // Only tmp

        // Act
        var (exists, actualPath, _) = await _detector.FindDoltRepositoryAsync(configuredPath);

        // Assert - should still report it exists, but validation in analysis will mark it invalid
        Assert.That(exists, Is.True, "Should report .dolt exists");
        Assert.That(actualPath, Is.EqualTo(Path.GetFullPath(configuredPath)), "Should return path");
    }

    #endregion

    #region Rogue Dolt Detection Tests

    [Test]
    [Description("DetectRogueDoltAtProjectRoot finds .dolt at project root when configured path differs")]
    public void DetectRogueDolt_DoltAtProjectRoot_ReportsIssue()
    {
        // Arrange - create .dolt at project root (different from configured path)
        var rogueDoltPath = Path.Combine(_testDirectory, ".dolt");
        Directory.CreateDirectory(rogueDoltPath);
        Directory.CreateDirectory(Path.Combine(rogueDoltPath, "tmp"));

        // Act
        var result = _detector.DetectRogueDoltAtProjectRoot(_testDirectory);

        // Assert
        Assert.That(result, Is.Not.Null, "Should detect rogue .dolt");
        Assert.That(result, Does.Contain(".dolt"), "Should return rogue path");
    }

    [Test]
    [Description("DetectRogueDoltAtProjectRoot returns null when no rogue .dolt exists")]
    public void DetectRogueDolt_NoRogueDolt_ReturnsNull()
    {
        // Arrange - no .dolt at project root

        // Act
        var result = _detector.DetectRogueDoltAtProjectRoot(_testDirectory);

        // Assert
        Assert.That(result, Is.Null, "Should not detect rogue .dolt");
    }

    #endregion

    #region Effective Path Tests

    [Test]
    [Description("SetEffectiveRepositoryPath updates the working directory for Dolt operations")]
    public void SetEffectiveRepositoryPath_UpdatesWorkingDirectory()
    {
        // Arrange
        var doltCliMock = new Mock<IDoltCli>();
        string? capturedPath = null;
        doltCliMock.Setup(d => d.SetEffectiveRepositoryPath(It.IsAny<string>()))
            .Callback<string>(p => capturedPath = p);
        doltCliMock.Setup(d => d.GetEffectiveRepositoryPath())
            .Returns(() => capturedPath ?? "default");

        // Act
        var newPath = Path.Combine(_testDirectory, "new-path");
        doltCliMock.Object.SetEffectiveRepositoryPath(newPath);

        // Assert
        Assert.That(doltCliMock.Object.GetEffectiveRepositoryPath(), Is.EqualTo(newPath));
    }

    #endregion

    #region Analysis with PathIssue Tests

    [Test]
    [Description("AnalyzeStateAsync reports incomplete .dolt in PathIssue")]
    public async Task AnalyzeStateAsync_IncompleteDolt_ReportsInPathIssue()
    {
        // Arrange
        SetupFindManifestMock(false, null);  // No manifest

        // Create incomplete .dolt (only tmp/)
        var doltPath = _doltConfig.Value.RepositoryPath;
        var doltDir = Path.Combine(doltPath, ".dolt");
        Directory.CreateDirectory(doltDir);
        Directory.CreateDirectory(Path.Combine(doltDir, "tmp"));

        _doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(false);

        // Act
        var result = await _detector.AnalyzeStateAsync(_testDirectory);

        // Assert - incomplete .dolt should be reported but not considered valid
        Assert.That(result.DoltInfo, Is.Not.Null);
        Assert.That(result.DoltInfo!.IsValid, Is.False, "Incomplete .dolt should not be valid");
    }

    #endregion

    #region Helper Methods

    private void SetupFindManifestMock(bool found, string? manifestPath)
    {
        var searchedLocations = new[]
        {
            Path.Combine(_testDirectory, ".dmms", "state.json"),
            Path.Combine(_testDirectory, "mcpdata", "PSKD", ".dmms", "state.json")
        };

        // PP13-87-C1: Updated to handle optional chromaDataPath parameter
        _manifestServiceMock.Setup(m => m.FindManifestAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((found, manifestPath, searchedLocations));

        if (found && manifestPath != null)
        {
            _manifestServiceMock.Setup(m => m.ReadManifestAsync(It.IsAny<string>()))
                .ReturnsAsync(CreateTestManifest("https://dolthub.com/test/repo"));
        }
    }

    private static DmmsManifest CreateTestManifest(string? remoteUrl = null)
    {
        return new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = remoteUrl,
                DefaultBranch = "main",
                CurrentBranch = "main"
            },
            GitMapping = new GitMappingConfig { Enabled = true },
            Initialization = new InitializationConfig
            {
                Mode = "auto",
                OnClone = "sync_to_manifest",
                OnBranchChange = "preserve_local"
            },
            Collections = new CollectionTrackingConfig
            {
                Tracked = new List<string> { "*" },
                Excluded = new List<string>()
            },
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion
}
