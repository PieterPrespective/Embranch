using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;
using Embranch.Services;
using Embranch.Tools;
using Moq;

namespace EmbranchTesting.IntegrationTests
{
    /// <summary>
    /// Integration tests for PP13-96: DoltCLI Parsing Issues Fix.
    /// Tests that commit tools return clean hashes and proper author info.
    /// Requires Dolt to be installed and available in PATH.
    /// </summary>
    [TestFixture]
    public class PP13_96_CommitInfoTests
    {
        private string _tempDir = null!;
        private DoltCli _doltCli = null!;
        private Mock<ILogger<DoltCli>> _mockLogger = null!;
        private Mock<ISyncStateTracker> _mockSyncStateTracker = null!;
        private IOptions<DoltConfiguration> _doltConfigOptions = null!;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PP13_96_Test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);

            _mockLogger = new Mock<ILogger<DoltCli>>();
            _mockSyncStateTracker = new Mock<ISyncStateTracker>();
            _doltConfigOptions = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = "dolt",
                RepositoryPath = _tempDir,
                CommandTimeoutMs = 30000,
                EnableDebugLogging = true
            });
            _doltCli = new DoltCli(_doltConfigOptions, _mockLogger.Object);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        /// <summary>
        /// PP13-96: GetLogAsync should return hashes without ANSI escape codes.
        /// This is a regression test for the bug where hashes contained \x1B[33m prefix.
        /// </summary>
        [Test]
        public async Task GetLogAsync_ShouldReturnCleanHashes()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commits
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(100))");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Initial commit with test table");

            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "INSERT INTO test VALUES (1, 'test1')");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Add first test row");

            // Act
            var commits = await _doltCli.GetLogAsync(5);

            // Assert
            Assert.That(commits, Is.Not.Null);
            Assert.That(commits.Count(), Is.GreaterThanOrEqualTo(2), "Should have at least 2 commits");

            foreach (var commit in commits)
            {
                // Verify hash is clean (no ANSI codes)
                // PP13-96: The regex pattern ^[a-z0-9]+$ is sufficient to verify:
                // 1. Only lowercase letters and digits (no escape chars, brackets, etc.)
                // 2. Non-empty string
                Assert.That(commit.Hash, Does.Match(@"^[a-z0-9]+$"),
                    $"Hash '{commit.Hash}' should only contain lowercase alphanumeric characters (no ANSI codes)");

                // Additional check: hashes shouldn't contain any non-alphanumeric chars
                Assert.That(commit.Hash, Does.Not.Match(@"[\[\]\x00-\x1f]"),
                    $"Hash '{commit.Hash}' should not contain control characters or brackets");
            }
        }

        /// <summary>
        /// PP13-96: GetLogWithAuthorAsync should return author information.
        /// </summary>
        [Test]
        public async Task GetLogWithAuthorAsync_ShouldReturnAuthorField()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for author check");

            // Act
            var commits = await _doltCli.GetLogWithAuthorAsync(5);

            // Assert
            Assert.That(commits, Is.Not.Null);
            Assert.That(commits.Count(), Is.GreaterThanOrEqualTo(1), "Should have at least 1 commit");

            var firstCommit = commits.First();
            Assert.That(firstCommit.Author, Is.Not.Null.And.Not.Empty,
                "Author field should not be empty when using GetLogWithAuthorAsync");
        }

        /// <summary>
        /// PP13-96: GetLogWithAuthorAsync should return clean hashes (same as GetLogAsync fix).
        /// </summary>
        [Test]
        public async Task GetLogWithAuthorAsync_ShouldReturnCleanHashes()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for hash check");

            // Act
            var commits = await _doltCli.GetLogWithAuthorAsync(5);

            // Assert
            Assert.That(commits, Is.Not.Null);

            foreach (var commit in commits)
            {
                Assert.That(commit.Hash, Does.Match(@"^[a-z0-9]+$"),
                    $"Hash '{commit.Hash}' should only contain lowercase alphanumeric characters");
            }
        }

        /// <summary>
        /// PP13-96: GetLogWithAuthorAsync should parse date field correctly.
        /// </summary>
        [Test]
        public async Task GetLogWithAuthorAsync_ShouldParseDateField()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for date check");

            // Act
            var commits = await _doltCli.GetLogWithAuthorAsync(5);

            // Assert
            Assert.That(commits, Is.Not.Null);
            Assert.That(commits.Count(), Is.GreaterThanOrEqualTo(1));

            var firstCommit = commits.First();
            // Date should be reasonably recent (within last hour for a test commit)
            var timeDiff = DateTime.Now - firstCommit.Date;
            Assert.That(timeDiff.TotalHours, Is.LessThan(1),
                "Commit date should be within the last hour (just created)");
        }

        /// <summary>
        /// PP13-96: DoltShowTool should find commits by short hash.
        /// This was failing because hash comparison with ANSI-prefixed hashes failed.
        /// </summary>
        [Test]
        public async Task DoltShowTool_WithShortHash_ShouldFindCommit()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for DoltShow");

            // Get the commit hash from log
            var commits = await _doltCli.GetLogAsync(1);
            Assert.That(commits, Is.Not.Null);
            var fullHash = commits.First().Hash;
            Assert.That(fullHash, Is.Not.Null.And.Length.GreaterThanOrEqualTo(7));

            var shortHash = fullHash![..7];

            // Create the DoltShowTool (PP13-99: added ISyncStateTracker and IOptions<DoltConfiguration>)
            var showToolLogger = new Mock<ILogger<DoltShowTool>>();
            var showTool = new DoltShowTool(showToolLogger.Object, _doltCli, _mockSyncStateTracker.Object, _doltConfigOptions);

            // Act
            var result = await showTool.DoltShow(shortHash);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var errorProp = resultType.GetProperty("error");

            Assert.That(successProp?.GetValue(result), Is.True,
                $"DoltShow should find commit by short hash. Error: {errorProp?.GetValue(result)}");
        }

        /// <summary>
        /// PP13-96: DoltShowTool should find commits by HEAD reference.
        /// </summary>
        [Test]
        public async Task DoltShowTool_WithHEAD_ShouldFindCommit()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for HEAD reference");

            // Create the DoltShowTool (PP13-99: added ISyncStateTracker and IOptions<DoltConfiguration>)
            var showToolLogger = new Mock<ILogger<DoltShowTool>>();
            var showTool = new DoltShowTool(showToolLogger.Object, _doltCli, _mockSyncStateTracker.Object, _doltConfigOptions);

            // Act
            var result = await showTool.DoltShow("HEAD");

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");

            Assert.That(successProp?.GetValue(result), Is.True,
                "DoltShow should find commit by HEAD reference");
        }

        /// <summary>
        /// PP13-96: DoltFindTool should find commits by hash search.
        /// This was failing because hash comparison with ANSI-prefixed hashes failed.
        /// </summary>
        [Test]
        public async Task DoltFindTool_WithHashSearch_ShouldFindCommit()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for hash search");

            // Get the commit hash
            var commits = await _doltCli.GetLogAsync(1);
            Assert.That(commits, Is.Not.Null);
            var fullHash = commits.First().Hash;
            Assert.That(fullHash, Is.Not.Null.And.Length.GreaterThanOrEqualTo(7));

            var shortHash = fullHash![..7];

            // Create the DoltFindTool
            var findToolLogger = new Mock<ILogger<DoltFindTool>>();
            var findTool = new DoltFindTool(findToolLogger.Object, _doltCli);

            // Act
            var result = await findTool.DoltFind(shortHash, search_type: "hash");

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var totalFoundProp = resultType.GetProperty("total_found");

            Assert.That(successProp?.GetValue(result), Is.True,
                "DoltFind should succeed when searching by hash");
            Assert.That((int)(totalFoundProp?.GetValue(result) ?? 0), Is.GreaterThan(0),
                "DoltFind should find at least one commit by hash");
        }

        /// <summary>
        /// PP13-96: DoltFindTool should find commits by author search.
        /// </summary>
        [Test]
        public async Task DoltFindTool_WithAuthorSearch_ShouldFindCommit()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for author search");

            // Get the author from log
            var commits = await _doltCli.GetLogWithAuthorAsync(1);
            Assert.That(commits, Is.Not.Null);
            var author = commits.First().Author;
            Assert.That(author, Is.Not.Null.And.Not.Empty, "Author should be populated");

            // Extract just a part of the author for search (e.g., first name or email domain)
            // This is more flexible for different Dolt configurations
            var searchTerm = author!.Split(' ')[0]; // Get first word (typically first name)

            // Create the DoltFindTool
            var findToolLogger = new Mock<ILogger<DoltFindTool>>();
            var findTool = new DoltFindTool(findToolLogger.Object, _doltCli);

            // Act
            var result = await findTool.DoltFind(searchTerm, search_type: "author");

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var totalFoundProp = resultType.GetProperty("total_found");

            Assert.That(successProp?.GetValue(result), Is.True,
                "DoltFind should succeed when searching by author");
            Assert.That((int)(totalFoundProp?.GetValue(result) ?? 0), Is.GreaterThan(0),
                $"DoltFind should find at least one commit by author search term '{searchTerm}'");
        }

        /// <summary>
        /// PP13-96: DoltCommitsTool should return author info (not empty string).
        /// </summary>
        [Test]
        public async Task DoltCommitsTool_ShouldReturnAuthorInfo()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commit
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Test commit for author info");

            // Create the DoltCommitsTool
            var commitsToolLogger = new Mock<ILogger<DoltCommitsTool>>();
            var commitsTool = new DoltCommitsTool(commitsToolLogger.Object, _doltCli);

            // Act
            var result = await commitsTool.DoltCommits(limit: 5);

            // Assert
            Assert.That(result, Is.Not.Null);
            var resultType = result.GetType();
            var successProp = resultType.GetProperty("success");
            var commitsProp = resultType.GetProperty("commits");

            Assert.That(successProp?.GetValue(result), Is.True,
                "DoltCommits should succeed");

            var commitsArray = commitsProp?.GetValue(result) as object[];
            Assert.That(commitsArray, Is.Not.Null.And.Not.Empty,
                "DoltCommits should return commits");

            // Check first commit has author info
            var firstCommit = commitsArray![0];
            var commitType = firstCommit.GetType();
            var authorProp = commitType.GetProperty("author");
            var authorValue = authorProp?.GetValue(firstCommit) as string;

            Assert.That(authorValue, Is.Not.Null.And.Not.Empty,
                "DoltCommits should return author info (not empty string)");
        }

        /// <summary>
        /// PP13-96: All commit hashes in responses should match alphanumeric pattern.
        /// </summary>
        [Test]
        public async Task AllCommitTools_ShouldReturnCleanHashes()
        {
            // Skip if Dolt is not available
            var doltCheck = await _doltCli.CheckDoltAvailableAsync();
            if (!doltCheck.Success)
            {
                Assert.Ignore("Dolt is not available on this system");
                return;
            }

            // Arrange - Initialize repo and create commits
            await _doltCli.InitAsync();
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "CREATE TABLE test (id INT PRIMARY KEY)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("First commit");
            await _doltCli.ExecuteRawCommandAsync("sql", "-q", "INSERT INTO test VALUES (1)");
            await _doltCli.AddAllAsync();
            await _doltCli.CommitAsync("Second commit");

            // Test DoltCommitsTool
            var commitsToolLogger = new Mock<ILogger<DoltCommitsTool>>();
            var commitsTool = new DoltCommitsTool(commitsToolLogger.Object, _doltCli);
            var commitsResult = await commitsTool.DoltCommits(limit: 10);

            var resultType = commitsResult.GetType();
            var commitsProp = resultType.GetProperty("commits");
            var commitsArray = commitsProp?.GetValue(commitsResult) as object[];

            Assert.That(commitsArray, Is.Not.Null.And.Not.Empty);

            foreach (var commit in commitsArray!)
            {
                var commitType = commit.GetType();
                var hashProp = commitType.GetProperty("hash");
                var hash = hashProp?.GetValue(commit) as string;

                Assert.That(hash, Does.Match(@"^[a-z0-9]+$"),
                    $"DoltCommitsTool hash '{hash}' should match ^[a-z0-9]+$ pattern");
            }
        }
    }
}
