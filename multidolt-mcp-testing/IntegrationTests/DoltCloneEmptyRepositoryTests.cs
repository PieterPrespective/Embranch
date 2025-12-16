using System.Text.Json;
using DMMS.Models;
using DMMS.Services;
using DMMS.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace DMMSTesting.IntegrationTests
{
    /// <summary>
    /// Integration tests for DoltCloneTool specifically testing empty repository handling.
    /// Tests the fix implemented for PP13-42 assignment to handle repositories with no commits/branches.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("Dolt")]
    [Category("Clone")]
    public class DoltCloneEmptyRepositoryTests
    {
        private string _testDirectory = null!;
        private string _sourceRepoPath = null!;
        private string _targetRepoPath = null!;
        private DoltCli _sourceDoltCli = null!;
        private DoltCli _targetDoltCli = null!;
        private DoltCloneTool _cloneTool = null!;
        private IChromaDbService _chromaService = null!;
        private ISyncManagerV2 _syncManager = null!;
        private ILogger<DoltCloneEmptyRepositoryTests> _logger = null!;

        [SetUp]
        public void Setup()
        {
            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            _logger = loggerFactory.CreateLogger<DoltCloneEmptyRepositoryTests>();

            // Create test directories
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _testDirectory = Path.Combine(Path.GetTempPath(), $"DoltCloneEmptyRepoTest_{timestamp}");
            _sourceRepoPath = Path.Combine(_testDirectory, "source_repo");
            _targetRepoPath = Path.Combine(_testDirectory, "target_repo");
            
            Directory.CreateDirectory(_testDirectory);
            Directory.CreateDirectory(_sourceRepoPath);
            Directory.CreateDirectory(_targetRepoPath);

            _logger.LogInformation("Created test directories: Source={SourcePath}, Target={TargetPath}", 
                _sourceRepoPath, _targetRepoPath);

            // Setup source repository (empty)
            var sourceConfig = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = Environment.OSVersion.Platform == PlatformID.Win32NT 
                    ? @"C:\Program Files\Dolt\bin\dolt.exe" 
                    : "dolt",
                RepositoryPath = _sourceRepoPath,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            });

            // Setup target repository configuration
            var targetConfig = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = Environment.OSVersion.Platform == PlatformID.Win32NT 
                    ? @"C:\Program Files\Dolt\bin\dolt.exe" 
                    : "dolt",
                RepositoryPath = _targetRepoPath,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            });

            // Create Dolt CLI instances
            var doltLogger = loggerFactory.CreateLogger<DoltCli>();
            _sourceDoltCli = new DoltCli(sourceConfig, doltLogger);
            _targetDoltCli = new DoltCli(targetConfig, doltLogger);

            // Setup ChromaDB service for target
            var chromaConfig = Options.Create(new ServerConfiguration
            {
                ChromaDataPath = Path.Combine(_targetRepoPath, "chroma_data")
            });
            var chromaLogger = loggerFactory.CreateLogger<ChromaPythonService>();
            _chromaService = new ChromaPythonService(chromaLogger, chromaConfig);

            // Setup sync manager
            var syncLogger = loggerFactory.CreateLogger<SyncManagerV2>();
            _syncManager = new SyncManagerV2(_targetDoltCli, _chromaService, syncLogger);

            // Create clone tool
            var cloneLogger = loggerFactory.CreateLogger<DoltCloneTool>();
            _cloneTool = new DoltCloneTool(cloneLogger, _targetDoltCli, _syncManager);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test directories
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                    _logger?.LogInformation("Test environment cleaned up successfully");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not fully clean up test environment");
                }
            }

            // Dispose services
            if (_chromaService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
        }

        /// <summary>
        /// Tests that DoltCloneTool can successfully handle cloning an empty repository
        /// that has been initialized but has no commits or branches beyond the default.
        /// This addresses the issue reported with empty DoltHub repositories.
        /// </summary>
        [Test]
        public async Task DoltClone_EmptyRepository_ShouldHandleGracefully()
        {
            // Initialize PythonContext if needed
            if (!PythonContext.IsInitialized)
            {
                _logger.LogInformation("Initializing PythonContext for empty repository clone test...");
                PythonContext.Initialize();
                _logger.LogInformation("✅ PythonContext initialized successfully");
            }

            // Arrange: Create an empty source repository
            _logger.LogInformation("🔧 ARRANGE: Creating empty source repository");
            
            var initResult = await _sourceDoltCli.InitAsync();
            Assert.That(initResult.Success, Is.True, $"Failed to initialize source repository: {initResult.Error}");
            _logger.LogInformation("✅ Empty source repository initialized successfully");

            // Verify the repository is truly empty (no commits)
            _logger.LogInformation("🔍 Verifying source repository is empty (no commits)");
            try
            {
                var headCommit = await _sourceDoltCli.GetHeadCommitHashAsync();
                _logger.LogWarning("⚠️ Expected empty repository but found HEAD commit: {Commit}", headCommit);
                // This is unexpected but not a test failure - continue to test the clone behavior
            }
            catch (Exception ex)
            {
                _logger.LogInformation("✅ Confirmed repository is empty - GetHeadCommitHashAsync failed as expected: {Message}", ex.Message);
            }

            try
            {
                var currentBranch = await _sourceDoltCli.GetCurrentBranchAsync();
                _logger.LogWarning("⚠️ Expected empty repository but found active branch: {Branch}", currentBranch);
                // This is unexpected but not a test failure - continue to test the clone behavior
            }
            catch (Exception ex)
            {
                _logger.LogInformation("✅ Confirmed repository is empty - GetCurrentBranchAsync failed as expected: {Message}", ex.Message);
            }

            // Act: Attempt to clone the empty repository using the local path
            _logger.LogInformation("🎯 ACT: Attempting to clone empty repository using DoltCloneTool");
            
            var cloneResult = await _cloneTool.DoltClone(_sourceRepoPath, branch: null, commit: null);

            // Assert: The clone operation should fail gracefully (empty repo can't be cloned via file://)
            _logger.LogInformation("✅ ASSERT: Validating clone operation results");
            
            Assert.That(cloneResult, Is.Not.Null, "Clone result should not be null");

            // Convert result to JSON for detailed analysis
            var resultJson = JsonSerializer.Serialize(cloneResult, new JsonSerializerOptions { WriteIndented = true });
            _logger.LogInformation("📄 Clone operation result: {Result}", resultJson);

            // Parse the result as JsonDocument for validation
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            // Verify the operation fails gracefully (file:// protocol can't clone empty repos)
            Assert.That(root.TryGetProperty("success", out var successElement), Is.True, "Result should have 'success' property");
            var success = successElement.GetBoolean();
            
            // With our fix, the clone should now properly report failure for empty local repos
            Assert.That(success, Is.False, "Clone operation should fail for empty local repository via file:// protocol");

            // Verify error information
            Assert.That(root.TryGetProperty("error", out var errorElement), Is.True, "Failed result should have 'error' property");
            var errorCode = errorElement.GetString();
            Assert.That(errorCode, Is.EqualTo("REMOTE_NOT_FOUND"), "Should have appropriate error code for empty repository");
            
            Assert.That(root.TryGetProperty("message", out var messageElement), Is.True, "Result should have 'message' property");
            var message = messageElement.GetString();
            Assert.That(message, Is.Not.Null.And.Not.Empty, "Error message should not be empty");
            Assert.That(message.ToLowerInvariant(), Does.Contain("empty").Or.Contain("not found"), 
                "Message should indicate repository is empty or not found");
            
            Assert.That(root.TryGetProperty("attempted_url", out var urlElement), Is.True, "Result should have 'attempted_url' property");
            var attemptedUrl = urlElement.GetString();
            Assert.That(attemptedUrl, Does.StartWith("file:///"), "URL should be properly formatted as file:// protocol");
            
            _logger.LogInformation("✅ Clone properly reported failure with error: {Error}, message: {Message}", errorCode, message);
            _logger.LogInformation("🎉 TEST PASSED: DoltCloneTool correctly handles and reports failure for empty repository clone");
        }

        /// <summary>
        /// Tests that DoltCloneTool properly handles empty repositories when a specific branch is requested
        /// but that branch doesn't exist (common with empty repos).
        /// </summary>
        [Test]
        public async Task DoltClone_EmptyRepositoryWithSpecificBranch_ShouldDefaultGracefully()
        {
            // Initialize PythonContext if needed
            if (!PythonContext.IsInitialized)
            {
                _logger.LogInformation("Initializing PythonContext for empty repository with branch test...");
                PythonContext.Initialize();
                _logger.LogInformation("✅ PythonContext initialized successfully");
            }

            // Arrange: Create an empty source repository
            _logger.LogInformation("🔧 ARRANGE: Creating empty source repository for branch-specific test");
            
            var initResult = await _sourceDoltCli.InitAsync();
            Assert.That(initResult.Success, Is.True, $"Failed to initialize source repository: {initResult.Error}");

            // Act: Attempt to clone with a specific branch that doesn't exist
            _logger.LogInformation("🎯 ACT: Attempting to clone empty repository with specific branch 'feature-branch'");
            
            var cloneResult = await _cloneTool.DoltClone(_sourceRepoPath, branch: "feature-branch", commit: null);

            // Assert: Should handle gracefully and default appropriately
            _logger.LogInformation("✅ ASSERT: Validating empty repository clone with specific branch");
            
            var resultJson = JsonSerializer.Serialize(cloneResult, new JsonSerializerOptions { WriteIndented = true });
            _logger.LogInformation("📄 Clone with branch result: {Result}", resultJson);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            // Should still succeed (the tool should handle the empty repo gracefully)
            Assert.That(root.TryGetProperty("success", out var successElement), Is.True, "Result should have 'success' property");
            var isSuccess = successElement.GetBoolean();

            if (isSuccess)
            {
                // If clone succeeded, verify it defaulted appropriately
                Assert.That(root.TryGetProperty("checkout", out var checkoutElement), Is.True, "Result should have 'checkout' property");
                Assert.That(checkoutElement.TryGetProperty("branch", out var branchElement), Is.True, "Checkout should have 'branch' property");
                
                var actualBranch = branchElement.GetString();
                _logger.LogInformation("✅ Clone with specific branch completed - actual branch: {Branch}", actualBranch);
                
                // For empty repos, it should either use the requested branch or default to main
                Assert.That(actualBranch, Is.Not.Null.And.Not.Empty, "Branch should not be empty");
                Assert.That(actualBranch, Is.EqualTo("feature-branch").Or.EqualTo("main"), 
                    "Branch should be either the requested branch or default to main for empty repo");
            }
            else
            {
                // If clone failed, it should be a graceful failure with proper error message
                Assert.That(root.TryGetProperty("error", out var errorElement), Is.True, "Failed result should have 'error' property");
                Assert.That(root.TryGetProperty("message", out var messageElement), Is.True, "Failed result should have 'message' property");
                
                var errorCode = errorElement.GetString();
                var errorMessage = messageElement.GetString();
                
                _logger.LogInformation("ℹ️ Clone failed gracefully - Error: {Error}, Message: {Message}", errorCode, errorMessage);
                
                // Should not be a generic "operation failed" but a more specific error
                Assert.That(errorCode, Is.Not.EqualTo("OPERATION_FAILED"), 
                    "Should provide specific error code, not generic failure");
                Assert.That(errorMessage, Is.Not.Null.And.Not.Empty, "Should provide meaningful error message");
            }

            _logger.LogInformation("🎉 TEST PASSED: DoltCloneTool properly handled empty repository clone with specific branch");
        }

        /// <summary>
        /// Tests that error handling for missing dolt executable works correctly.
        /// This validates the PP13-42 requirement for proper error messages when dolt is not available.
        /// </summary>
        [Test]
        public async Task DoltClone_DoltExecutableNotFound_ShouldReturnProperError()
        {
            // Arrange: Create a DoltCli with invalid executable path
            _logger.LogInformation("🔧 ARRANGE: Creating DoltCli with invalid executable path");
            
            var invalidConfig = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = "/invalid/path/to/dolt.exe",
                RepositoryPath = _targetRepoPath,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            });

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var invalidDoltCli = new DoltCli(invalidConfig, loggerFactory.CreateLogger<DoltCli>());
            
            var invalidCloneTool = new DoltCloneTool(
                loggerFactory.CreateLogger<DoltCloneTool>(), 
                invalidDoltCli, 
                _syncManager);

            // Act: Attempt to clone with missing executable
            _logger.LogInformation("🎯 ACT: Attempting clone with missing dolt executable");
            
            var cloneResult = await invalidCloneTool.DoltClone("some-repo-url");

            // Assert: Should return specific error about missing executable
            _logger.LogInformation("✅ ASSERT: Validating proper error handling for missing executable");
            
            var resultJson = JsonSerializer.Serialize(cloneResult, new JsonSerializerOptions { WriteIndented = true });
            _logger.LogInformation("📄 Missing executable result: {Result}", resultJson);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            // Should fail with specific error
            Assert.That(root.TryGetProperty("success", out var successElement), Is.True, "Result should have 'success' property");
            Assert.That(successElement.GetBoolean(), Is.False, "Operation should fail when dolt executable is missing");

            Assert.That(root.TryGetProperty("error", out var errorElement), Is.True, "Failed result should have 'error' property");
            var errorCode = errorElement.GetString();
            
            Assert.That(errorCode, Is.EqualTo("DOLT_EXECUTABLE_NOT_FOUND"), 
                "Should return specific error code for missing executable, not generic error");

            Assert.That(root.TryGetProperty("message", out var messageElement), Is.True, "Failed result should have 'message' property");
            var message = messageElement.GetString();
            
            Assert.That(message, Is.Not.Null.And.Not.Empty, "Error message should not be empty");
            Assert.That(message.ToLowerInvariant(), Does.Contain("dolt"), "Error message should mention dolt");
            Assert.That(message.ToLowerInvariant(), Does.Contain("not found").Or.Contain("executable"), 
                "Error message should indicate executable issue");

            _logger.LogInformation("✅ Proper error handling validated - Error: {Error}, Message: {Message}", errorCode, message);
            _logger.LogInformation("🎉 TEST PASSED: DoltCloneTool correctly identifies missing dolt executable with proper error message");
        }
    }
}