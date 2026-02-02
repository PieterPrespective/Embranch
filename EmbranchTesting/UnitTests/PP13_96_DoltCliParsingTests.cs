using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Embranch.Models;
using Embranch.Services;
using Moq;
using System.Reflection;

namespace EmbranchTesting.UnitTests
{
    /// <summary>
    /// Unit tests for PP13-96: DoltCLI Parsing Issues Fix.
    /// Tests ANSI stripping from commit hashes and GetLogWithAuthorAsync parsing.
    /// </summary>
    [TestFixture]
    public class PP13_96_DoltCliParsingTests
    {
        private Mock<ILogger<DoltCli>> _mockLogger;
        private IOptions<DoltConfiguration> _options;
        private DoltCli _doltCli;

        [SetUp]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<DoltCli>>();
            _options = Options.Create(new DoltConfiguration
            {
                DoltExecutablePath = "dolt",
                RepositoryPath = Path.GetTempPath(),
                CommandTimeoutMs = 5000,
                EnableDebugLogging = false
            });
            _doltCli = new DoltCli(_options, _mockLogger.Object);
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should remove standard ANSI escape codes like \x1B[33m
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_StandardAnsiCodes_ShouldRemove()
        {
            // Arrange - Access private static method via reflection
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "StripAnsiColorCodes method should exist");

            var input = "\x1B[33mabcd1234\x1B[m";

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo("abcd1234"), "ANSI escape codes should be stripped from hash");
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should remove bracket-only ANSI format like [0m[33m
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_BracketOnlyFormat_ShouldRemove()
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var input = "[0m[33mabcd1234[0m";

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo("abcd1234"), "Bracket-only ANSI codes should be stripped");
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should remove branch info in parentheses like (HEAD -> main)
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_BranchInfo_ShouldRemove()
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var input = "abcd1234 (HEAD -> main) Initial commit";

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo("abcd1234 Initial commit"), "Branch info in parentheses should be stripped");
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should handle null and empty input gracefully
        /// </summary>
        [Test]
        [TestCase(null, null)]
        [TestCase("", "")]
        public void StripAnsiColorCodes_NullOrEmpty_ShouldReturnSame(string? input, string? expected)
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            // Act
            var result = method!.Invoke(null, new object[] { input! });

            // Assert
            Assert.That(result, Is.EqualTo(expected), "Null or empty input should be returned unchanged");
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should handle already clean input
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_CleanInput_ShouldReturnUnchanged()
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var input = "abcd1234efgh5678";

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo("abcd1234efgh5678"), "Clean input should remain unchanged");
        }

        /// <summary>
        /// PP13-96: StripAnsiColorCodes should handle complex ANSI sequences with multiple color codes
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_ComplexAnsiSequences_ShouldRemoveAll()
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            // Multiple ANSI codes with varying parameters
            var input = "\x1B[1;33mabcd1234\x1B[0m \x1B[1;31mtest message\x1B[0m";

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo("abcd1234 test message"), "All ANSI codes should be stripped from complex input");
        }

        /// <summary>
        /// PP13-96: Verify that hash pattern after stripping matches expected format
        /// </summary>
        [TestCase("\x1B[33ma1b2c3d4\x1B[m", "a1b2c3d4")]
        [TestCase("[33ma1b2c3d4[0m", "a1b2c3d4")]
        [TestCase("a1b2c3d4", "a1b2c3d4")]
        [TestCase("\x1B[33m\x1B[1ma1b2c3d4\x1B[0m\x1B[0m", "a1b2c3d4")]
        public void StripAnsiColorCodes_VariousHashFormats_ShouldReturnCleanHash(string input, string expected)
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            // Act
            var result = method!.Invoke(null, new object[] { input });

            // Assert
            Assert.That(result, Is.EqualTo(expected), $"Hash '{input}' should be cleaned to '{expected}'");
        }

        /// <summary>
        /// PP13-96: Verify cleaned hash matches alphanumeric regex pattern
        /// </summary>
        [Test]
        public void StripAnsiColorCodes_CleanedHash_ShouldMatchAlphanumericPattern()
        {
            // Arrange
            var method = typeof(DoltCli).GetMethod("StripAnsiColorCodes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var inputWithAnsi = "\x1B[33mabcd1234efgh5678ijkl9012\x1B[m";

            // Act
            var result = (string)method!.Invoke(null, new object[] { inputWithAnsi })!;

            // Assert
            Assert.That(result, Does.Match(@"^[a-z0-9]+$"),
                "Cleaned hash should match pattern ^[a-z0-9]+$ (alphanumeric lowercase)");
        }
    }
}
