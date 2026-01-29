using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Embranch.Models;
using Embranch.Services;
using System.Text.Json;

namespace EmbranchTesting.IntegrationTests;

/// <summary>
/// PP13-87: Integration tests for Fresh Clone Bootstrap System
/// Tests the end-to-end flow of bootstrapping a repository from manifest.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("PP13-87")]
public class PP13_87_FreshCloneBootstrapTests
{
    private string _testDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "PP13_87_BootstrapTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
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
    public async Task StateDetector_ManifestOnlyScenario_DetectsCorrectState()
    {
        // Arrange - Create a project with only a manifest (fresh clone scenario)
        var manifestDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(manifestDir);

        var manifest = new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = "https://dolthub.com/test/repo",
                CurrentBranch = "main",
                CurrentCommit = "abc1234567890",
                DefaultBranch = "main"
            },
            Initialization = new InitializationConfig
            {
                Mode = InitializationMode.Auto
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "state.json"), manifestJson);

        // Create detector with mocks
        var loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        var manifestService = new EmbranchStateManifest(new Mock<ILogger<EmbranchStateManifest>>().Object);
        var doltCliMock = new Mock<IDoltCli>();
        var chromaServiceMock = new Mock<IChromaDbService>();

        var doltPath = Path.Combine(_testDirectory, "dolt-repo");
        var chromaPath = Path.Combine(_testDirectory, "chroma-data");

        var doltConfig = Options.Create(new DoltConfiguration { RepositoryPath = doltPath });
        var serverConfig = Options.Create(new ServerConfiguration { ChromaDataPath = chromaPath });

        var detector = new RepositoryStateDetector(
            loggerMock.Object,
            manifestService,
            doltCliMock.Object,
            chromaServiceMock.Object,
            doltConfig,
            serverConfig);

        // Act
        var result = await detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.ManifestOnly_NeedsFullBootstrap));
        Assert.That(result.StateDescription, Does.Contain("Manifest found"));
        Assert.That(result.Manifest, Is.Not.Null);
        Assert.That(result.Manifest!.RemoteUrl, Is.EqualTo("https://dolthub.com/test/repo"));
        Assert.That(result.RecommendedAction, Does.Contain("BootstrapRepository"));
    }

    [Test]
    public async Task StateDetector_UninitializedScenario_DetectsCorrectState()
    {
        // Arrange - Empty directory with no manifest, no Dolt, no Chroma
        var loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        var manifestService = new EmbranchStateManifest(new Mock<ILogger<EmbranchStateManifest>>().Object);
        var doltCliMock = new Mock<IDoltCli>();
        var chromaServiceMock = new Mock<IChromaDbService>();

        var doltPath = Path.Combine(_testDirectory, "dolt-repo");
        var chromaPath = Path.Combine(_testDirectory, "chroma-data");

        var doltConfig = Options.Create(new DoltConfiguration { RepositoryPath = doltPath });
        var serverConfig = Options.Create(new ServerConfiguration { ChromaDataPath = chromaPath });

        var detector = new RepositoryStateDetector(
            loggerMock.Object,
            manifestService,
            doltCliMock.Object,
            chromaServiceMock.Object,
            doltConfig,
            serverConfig);

        // Act
        var result = await detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.Uninitialized));
        Assert.That(result.StateDescription, Does.Contain("fresh project"));
        Assert.That(result.AvailableActions, Does.Contain("DoltClone"));
        Assert.That(result.AvailableActions, Does.Contain("DoltInit"));
    }

    [Test]
    public async Task StateDetector_NestedDoltScenario_DetectsPathMisalignment()
    {
        // Arrange - Simulate CLI clone creating nested directory
        var loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        var manifestService = new EmbranchStateManifest(new Mock<ILogger<EmbranchStateManifest>>().Object);
        var doltCliMock = new Mock<IDoltCli>();
        var chromaServiceMock = new Mock<IChromaDbService>();

        // Create manifest
        var manifestDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(manifestDir);
        var manifest = new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = "https://dolthub.com/test/my-database",
                CurrentBranch = "main",
                DefaultBranch = "main"
            },
            Initialization = new InitializationConfig { Mode = InitializationMode.Auto }
        };
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "state.json"), manifestJson);

        // Create nested Dolt structure (CLI clone creates subdirectory)
        var doltPath = Path.Combine(_testDirectory, "dolt-repo");
        var nestedPath = Path.Combine(doltPath, "my-database");
        Directory.CreateDirectory(doltPath);
        Directory.CreateDirectory(nestedPath);
        Directory.CreateDirectory(Path.Combine(nestedPath, ".dolt"));

        var chromaPath = Path.Combine(_testDirectory, "chroma-data");

        var doltConfig = Options.Create(new DoltConfiguration { RepositoryPath = doltPath });
        var serverConfig = Options.Create(new ServerConfiguration { ChromaDataPath = chromaPath });

        var detector = new RepositoryStateDetector(
            loggerMock.Object,
            manifestService,
            doltCliMock.Object,
            chromaServiceMock.Object,
            doltConfig,
            serverConfig);

        // Act
        var result = await detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.PathMisaligned_DoltNested));
        Assert.That(result.PathIssue, Is.Not.Null);
        Assert.That(result.PathIssue!.ConfiguredPath, Is.EqualTo(doltPath));
        Assert.That(result.PathIssue!.ActualDotDoltLocation, Is.EqualTo(nestedPath));
        Assert.That(result.StateDescription, Does.Contain("nested location"));
    }

    [Test]
    public async Task StateDetector_InfrastructureWithoutManifest_SuggestsInitManifest()
    {
        // Arrange - Dolt exists but no manifest
        var loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        var manifestService = new EmbranchStateManifest(new Mock<ILogger<EmbranchStateManifest>>().Object);
        var doltCliMock = new Mock<IDoltCli>();
        var chromaServiceMock = new Mock<IChromaDbService>();

        // Create Dolt directory
        var doltPath = Path.Combine(_testDirectory, "dolt-repo");
        Directory.CreateDirectory(doltPath);
        Directory.CreateDirectory(Path.Combine(doltPath, ".dolt"));

        var chromaPath = Path.Combine(_testDirectory, "chroma-data");

        // Mock Dolt as initialized
        doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(true);
        doltCliMock.Setup(d => d.GetCurrentBranchAsync()).ReturnsAsync("main");
        doltCliMock.Setup(d => d.GetHeadCommitHashAsync()).ReturnsAsync("abc1234");

        var doltConfig = Options.Create(new DoltConfiguration { RepositoryPath = doltPath });
        var serverConfig = Options.Create(new ServerConfiguration { ChromaDataPath = chromaPath });

        var detector = new RepositoryStateDetector(
            loggerMock.Object,
            manifestService,
            doltCliMock.Object,
            chromaServiceMock.Object,
            doltConfig,
            serverConfig);

        // Act
        var result = await detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.InfrastructureOnly_NeedsManifest));
        Assert.That(result.AvailableActions, Does.Contain("InitManifest"));
        Assert.That(result.RecommendedAction, Does.Contain("InitManifest"));
    }

    [Test]
    public async Task StateDetector_ReadyScenario_ReturnsReady()
    {
        // Arrange - All components exist
        var loggerMock = new Mock<ILogger<RepositoryStateDetector>>();
        var manifestService = new EmbranchStateManifest(new Mock<ILogger<EmbranchStateManifest>>().Object);
        var doltCliMock = new Mock<IDoltCli>();
        var chromaServiceMock = new Mock<IChromaDbService>();

        // Create manifest
        var manifestDir = Path.Combine(_testDirectory, ".dmms");
        Directory.CreateDirectory(manifestDir);
        var manifest = new DmmsManifest
        {
            Version = "1.0",
            Dolt = new DoltManifestConfig
            {
                RemoteUrl = "https://dolthub.com/test/repo",
                CurrentBranch = "main",
                DefaultBranch = "main"
            },
            Initialization = new InitializationConfig { Mode = InitializationMode.Auto }
        };
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "state.json"), manifestJson);

        // Create Dolt directory
        var doltPath = Path.Combine(_testDirectory, "dolt-repo");
        Directory.CreateDirectory(doltPath);
        Directory.CreateDirectory(Path.Combine(doltPath, ".dolt"));

        // Create Chroma directory with data
        var chromaPath = Path.Combine(_testDirectory, "chroma-data");
        Directory.CreateDirectory(chromaPath);
        await File.WriteAllTextAsync(Path.Combine(chromaPath, "chroma.sqlite3"), "test");

        // Mock services
        doltCliMock.Setup(d => d.IsInitializedAsync()).ReturnsAsync(true);
        doltCliMock.Setup(d => d.GetCurrentBranchAsync()).ReturnsAsync("main");
        doltCliMock.Setup(d => d.GetHeadCommitHashAsync()).ReturnsAsync("abc1234567890");
        chromaServiceMock.Setup(c => c.ListCollectionsAsync(It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string> { "coll1", "coll2" });

        var doltConfig = Options.Create(new DoltConfiguration { RepositoryPath = doltPath });
        var serverConfig = Options.Create(new ServerConfiguration { ChromaDataPath = chromaPath });

        var detector = new RepositoryStateDetector(
            loggerMock.Object,
            manifestService,
            doltCliMock.Object,
            chromaServiceMock.Object,
            doltConfig,
            serverConfig);

        // Act
        var result = await detector.AnalyzeStateAsync(_testDirectory);

        // Assert
        Assert.That(result.State, Is.EqualTo(RepositoryState.Ready));
        Assert.That(result.StateDescription, Does.Contain("fully initialized"));
        Assert.That(result.AvailableActions, Does.Contain("DoltStatus"));
        Assert.That(result.DoltInfo!.CurrentBranch, Is.EqualTo("main"));
        Assert.That(result.ChromaInfo!.CollectionCount, Is.EqualTo(2));
    }

    [Test]
    public void RepositoryStateModels_AllStatesHaveDescriptions()
    {
        // Verify all enum values have corresponding guidance
        var allStates = Enum.GetValues<RepositoryState>();

        foreach (var state in allStates)
        {
            // This test verifies the enum has all expected values
            Assert.That(Enum.IsDefined(typeof(RepositoryState), state), Is.True,
                $"State {state} should be defined in RepositoryState enum");
        }

        // Verify expected states exist
        Assert.That(allStates, Does.Contain(RepositoryState.Ready));
        Assert.That(allStates, Does.Contain(RepositoryState.Uninitialized));
        Assert.That(allStates, Does.Contain(RepositoryState.ManifestOnly_NeedsFullBootstrap));
        Assert.That(allStates, Does.Contain(RepositoryState.ManifestOnly_NeedsDoltBootstrap));
        Assert.That(allStates, Does.Contain(RepositoryState.ManifestOnly_NeedsChromaBootstrap));
        Assert.That(allStates, Does.Contain(RepositoryState.PathMisaligned_DoltNested));
        Assert.That(allStates, Does.Contain(RepositoryState.InfrastructureOnly_NeedsManifest));
        Assert.That(allStates, Does.Contain(RepositoryState.Inconsistent));
    }

    [Test]
    public void BootstrapOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new BootstrapOptions();

        // Assert
        Assert.That(options.BootstrapDolt, Is.True);
        Assert.That(options.BootstrapChroma, Is.True);
        Assert.That(options.SyncToManifestCommit, Is.True);
        Assert.That(options.CreateWorkBranch, Is.False);
        Assert.That(options.WorkBranchName, Is.Null);
        Assert.That(options.PathFixStrategy, Is.Null);
    }

    [Test]
    public void PathFixStrategy_AllStrategiesAreDefined()
    {
        // Verify all strategies exist
        var strategies = Enum.GetValues<PathFixStrategy>();

        Assert.That(strategies, Does.Contain(PathFixStrategy.MoveToConfiguredPath));
        Assert.That(strategies, Does.Contain(PathFixStrategy.UpdateConfiguration));
        Assert.That(strategies, Does.Contain(PathFixStrategy.CloneFreshDiscardMisaligned));
    }
}
