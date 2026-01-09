using NUnit.Framework;
using Microsoft.Extensions.Logging;
using DMMS.Models;
using DMMS.Services;
using Moq;
using System.Text.Json;

namespace DMMS.Testing.UnitTests
{
    /// <summary>
    /// Unit tests for ConflictAnalyzer service
    /// Tests the core conflict detection, analysis, and resolution preview functionality
    /// </summary>
    [TestFixture]
    public class ConflictAnalyzerTests
    {
        private Mock<IDoltCli> _mockDoltCli;
        private Mock<ILogger<ConflictAnalyzer>> _mockLogger;
        private ConflictAnalyzer _conflictAnalyzer;

        [SetUp]
        public void Setup()
        {
            _mockDoltCli = new Mock<IDoltCli>();
            _mockLogger = new Mock<ILogger<ConflictAnalyzer>>();
            _conflictAnalyzer = new ConflictAnalyzer(_mockDoltCli.Object, _mockLogger.Object);
        }

        [Test]
        public async Task AnalyzeMergeAsync_NoConflicts_ReturnsCanAutoMergeTrue()
        {
            // Arrange
            _mockDoltCli.Setup(x => x.PreviewMergeConflictsAsync("source", "target"))
                .ReturnsAsync("[]");
            _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
                .ReturnsAsync("abc123");
            
            // Mock for diff command used in GenerateMergePreview
            var mockDiffResult = new DoltCommandResult(true, "1 table changed, 0 rows added, 0 rows modified, 0 rows deleted", "", 0);
            
            // Note: The actual method uses reflection to call ExecuteDoltCommandAsync which is not mockable
            // The fallback will use placeholder data, which is acceptable for this test

            // Act
            var result = await _conflictAnalyzer.AnalyzeMergeAsync("source", "target", false, false);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.CanAutoMerge, Is.True);
            Assert.That(result.TotalConflictsDetected, Is.EqualTo(0));
            Assert.That(result.Conflicts, Is.Empty);
        }

        [Test]
        public async Task AnalyzeMergeAsync_WithConflicts_ParsesCorrectly()
        {
            // Arrange
            var conflictJson = """
                [
                    {
                        "table_name": "documents",
                        "document_id": "doc123",
                        "conflict_type": "content_modification",
                        "our_row": {
                            "id": "doc123",
                            "content": "Our version",
                            "metadata": "our_meta"
                        },
                        "their_row": {
                            "id": "doc123", 
                            "content": "Their version",
                            "metadata": "their_meta"
                        }
                    }
                ]
                """;

            _mockDoltCli.Setup(x => x.PreviewMergeConflictsAsync("source", "target"))
                .ReturnsAsync(conflictJson);
            _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
                .ReturnsAsync("abc123");

            // Act
            var result = await _conflictAnalyzer.AnalyzeMergeAsync("source", "target", true, false);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.TotalConflictsDetected, Is.EqualTo(1));
            Assert.That(result.Conflicts.Count, Is.EqualTo(1));

            var conflict = result.Conflicts.First();
            Assert.That(conflict.Collection, Is.EqualTo("documents"));
            Assert.That(conflict.DocumentId, Is.EqualTo("doc123"));
            Assert.That(conflict.Type, Is.EqualTo(ConflictType.ContentModification));
        }

        [Test]
        public async Task ParseSingleConflictElement_ExtractsDocumentIdFromMultipleSources()
        {
            // Arrange - Test various JSON structures
            var testCases = new[]
            {
                // Standard format
                """{"table_name": "docs", "document_id": "test123"}""",
                // Alternative naming
                """{"table": "docs", "doc_id": "test456"}""", 
                // Nested structure
                """{"collection": "docs", "our_id": "test789"}""",
                // Dolt conflict format
                """{"our_table": "docs", "their_id": "test999"}"""
            };

            var expectedIds = new[] { "test123", "test456", "test789", "test999" };

            for (int i = 0; i < testCases.Length; i++)
            {
                var jsonDoc = JsonDocument.Parse(testCases[i]);
                
                // Use reflection to access private method for testing
                var parseMethod = typeof(ConflictAnalyzer).GetMethod(
                    "ParseSingleConflictElement", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                // Act
                var result = parseMethod?.Invoke(_conflictAnalyzer, new object[] { jsonDoc.RootElement }) 
                    as DetailedConflictInfo;

                // Assert
                Assert.That(result, Is.Not.Null, $"Test case {i} should parse successfully");
                Assert.That(result.DocumentId, Is.EqualTo(expectedIds[i]), 
                    $"Test case {i} should extract correct document ID");
                Assert.That(result.Collection, Is.EqualTo("docs"), 
                    $"Test case {i} should extract collection name");
            }
        }

        [Test]
        public async Task GetContentComparisonAsync_ReturnsAccurateComparison()
        {
            // Arrange
            var tableName = "documents";
            var documentId = "doc123";

            // Mock merge base
            _mockDoltCli.Setup(x => x.GetHeadCommitHashAsync())
                .ReturnsAsync("abc123");

            // Mock document queries for different commits
            var baseQuery = """{"rows": [{"id": "doc123", "content": "base content", "metadata": "base"}]}""";
            var sourceQuery = """{"rows": [{"id": "doc123", "content": "source content", "metadata": "source"}]}""";
            var targetQuery = """{"rows": [{"id": "doc123", "content": "target content", "metadata": "target"}]}""";

            _mockDoltCli.Setup(x => x.QueryJsonAsync(It.Is<string>(s => s.Contains("AS OF"))))
                .ReturnsAsync(baseQuery);
            _mockDoltCli.Setup(x => x.GetCurrentBranchAsync())
                .ReturnsAsync("main");
            _mockDoltCli.Setup(x => x.CheckoutAsync("source", false))
                .ReturnsAsync(new DoltCommandResult(true, "", "", 0));
            _mockDoltCli.Setup(x => x.CheckoutAsync("target", false))
                .ReturnsAsync(new DoltCommandResult(true, "", "", 0));
            _mockDoltCli.Setup(x => x.CheckoutAsync("main", false))
                .ReturnsAsync(new DoltCommandResult(true, "", "", 0));

            // Act
            var result = await _conflictAnalyzer.GetContentComparisonAsync(
                tableName, documentId, "source", "target");

            // Assert
            Assert.That(result.TableName, Is.EqualTo(tableName));
            Assert.That(result.DocumentId, Is.EqualTo(documentId));
            Assert.That(result.BaseContent, Is.Not.Null);
            Assert.That(result.SourceContent, Is.Not.Null);
            Assert.That(result.TargetContent, Is.Not.Null);
        }

        [Test]
        public async Task GenerateResolutionPreviewAsync_KeepOurs_ShowsDataLossWarnings()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_conflict",
                Collection = "docs",
                DocumentId = "doc123",
                OurValues = new Dictionary<string, object>
                {
                    { "content", "our content" },
                    { "field1", "our_value1" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "their content" },
                    { "field2", "their_value2" }
                }
            };

            // Act
            var result = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.KeepOurs);

            // Assert
            Assert.That(result.ConflictId, Is.EqualTo("test_conflict"));
            Assert.That(result.ResolutionType, Is.EqualTo(ResolutionType.KeepOurs));
            Assert.That(result.ConfidenceLevel, Is.EqualTo(100));
            Assert.That(result.DataLossWarnings, Is.Not.Empty);
            Assert.That(result.DataLossWarnings.Any(w => w.Contains("field2")), Is.True,
                "Should warn about losing their field2");
            Assert.That(result.ResultingContent.Content, Is.EqualTo("our content"));
        }

        [Test]
        public async Task GenerateResolutionPreviewAsync_KeepTheirs_ShowsDataLossWarnings()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_conflict",
                OurValues = new Dictionary<string, object>
                {
                    { "content", "our content" },
                    { "our_field", "our_value" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "their content" },
                    { "their_field", "their_value" }
                }
            };

            // Act
            var result = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.KeepTheirs);

            // Assert
            Assert.That(result.ResolutionType, Is.EqualTo(ResolutionType.KeepTheirs));
            Assert.That(result.ConfidenceLevel, Is.EqualTo(100));
            Assert.That(result.DataLossWarnings.Any(w => w.Contains("our_field")), Is.True,
                "Should warn about losing our field");
            Assert.That(result.ResultingContent.Content, Is.EqualTo("their content"));
        }

        [Test]
        public async Task GenerateResolutionPreviewAsync_FieldMerge_HandlesTimestamps()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_conflict",
                BaseValues = new Dictionary<string, object>
                {
                    { "content", "base content" },
                    { "timestamp", "2024-01-01T00:00:00Z" }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "content", "our content" },
                    { "timestamp", "2024-01-02T00:00:00Z" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "their content" },
                    { "timestamp", "2024-01-03T00:00:00Z" }
                }
            };

            // Act
            var result = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.FieldMerge);

            // Assert
            Assert.That(result.ResolutionType, Is.EqualTo(ResolutionType.FieldMerge));
            Assert.That(result.ConfidenceLevel, Is.LessThan(100), 
                "Should have lower confidence due to content conflict");
            
            // Should prefer the newer timestamp
            var resultTimestamp = result.ResultingContent.Metadata["timestamp"].ToString();
            Assert.That(resultTimestamp, Is.EqualTo("2024-01-03T00:00:00Z"),
                "Should use the newer timestamp from their side");
        }

        [Test]
        public async Task GenerateResolutionPreviewAsync_FieldMerge_HandlesVersionNumbers()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_conflict",
                BaseValues = new Dictionary<string, object>
                {
                    { "version", 1 }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "version", 2 }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "version", 3 }
                }
            };

            // Act
            var result = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.FieldMerge);

            // Assert
            var resultVersion = Convert.ToInt32(result.ResultingContent.Metadata["version"]);
            Assert.That(resultVersion, Is.EqualTo(3),
                "Should use the higher version number");
            Assert.That(result.ConfidenceLevel, Is.EqualTo(100),
                "Should have high confidence for version merging");
        }

        [Test]
        public async Task GenerateResolutionPreviewAsync_FieldMerge_PreservesNonConflictingFields()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                ConflictId = "test_conflict",
                BaseValues = new Dictionary<string, object>
                {
                    { "field1", "base1" },
                    { "field2", "base2" }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "field1", "our1" }, // Changed
                    { "field2", "base2" } // Unchanged
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "field1", "base1" }, // Unchanged
                    { "field2", "their2" } // Changed
                }
            };

            // Act
            var result = await _conflictAnalyzer.GenerateResolutionPreviewAsync(
                conflict, ResolutionType.FieldMerge);

            // Assert
            Assert.That(result.ResultingContent.Metadata["field1"], Is.EqualTo("our1"),
                "Should keep our change when they didn't change it");
            Assert.That(result.ResultingContent.Metadata["field2"], Is.EqualTo("their2"),
                "Should keep their change when we didn't change it");
            Assert.That(result.ConfidenceLevel, Is.EqualTo(100),
                "Should have high confidence for non-conflicting field changes");
        }

        [Test]
        public async Task CanAutoResolveConflictAsync_NonOverlappingChanges_ReturnsTrue()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Type = ConflictType.ContentModification,
                BaseValues = new Dictionary<string, object>
                {
                    { "field1", "base1" },
                    { "field2", "base2" }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "field1", "our1" }, // We changed field1
                    { "field2", "base2" } // We didn't touch field2
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "field1", "base1" }, // They didn't touch field1
                    { "field2", "their2" } // They changed field2
                }
            };

            // Act
            var result = await _conflictAnalyzer.CanAutoResolveConflictAsync(conflict);

            // Assert
            Assert.That(result, Is.True, "Non-overlapping field changes should be auto-resolvable");
        }

        [Test]
        public async Task CanAutoResolveConflictAsync_OverlappingChanges_ReturnsFalse()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Type = ConflictType.ContentModification,
                BaseValues = new Dictionary<string, object>
                {
                    { "field1", "base1" }
                },
                OurValues = new Dictionary<string, object>
                {
                    { "field1", "our1" } // We changed field1
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "field1", "their1" } // They also changed field1
                }
            };

            // Act
            var result = await _conflictAnalyzer.CanAutoResolveConflictAsync(conflict);

            // Assert
            Assert.That(result, Is.False, "Overlapping field changes should not be auto-resolvable");
        }

        [Test]
        public async Task CanAutoResolveConflictAsync_AddAddIdenticalContent_ReturnsTrue()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Type = ConflictType.AddAdd,
                OurValues = new Dictionary<string, object>
                {
                    { "content", "identical content" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "identical content" }
                }
            };

            // Act
            var result = await _conflictAnalyzer.CanAutoResolveConflictAsync(conflict);

            // Assert
            Assert.That(result, Is.True, "AddAdd conflicts with identical content should be auto-resolvable");
        }

        [Test]
        public async Task CanAutoResolveConflictAsync_AddAddDifferentContent_ReturnsFalse()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Type = ConflictType.AddAdd,
                OurValues = new Dictionary<string, object>
                {
                    { "content", "our content" }
                },
                TheirValues = new Dictionary<string, object>
                {
                    { "content", "their content" }
                }
            };

            // Act
            var result = await _conflictAnalyzer.CanAutoResolveConflictAsync(conflict);

            // Assert
            Assert.That(result, Is.False, "AddAdd conflicts with different content should not be auto-resolvable");
        }

        [Test]
        public async Task CanAutoResolveConflictAsync_MetadataConflict_ReturnsTrue()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Type = ConflictType.MetadataConflict
            };

            // Act
            var result = await _conflictAnalyzer.CanAutoResolveConflictAsync(conflict);

            // Assert
            Assert.That(result, Is.True, "Metadata conflicts should generally be auto-resolvable");
        }

        [Test]
        public void GenerateConflictId_ProducesStableIds()
        {
            // Arrange
            var conflict = new DetailedConflictInfo
            {
                Collection = "test_collection",
                DocumentId = "test_doc",
                Type = ConflictType.ContentModification
            };

            // Use reflection to access private method
            var generateIdMethod = typeof(ConflictAnalyzer).GetMethod(
                "GenerateConflictId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var id1 = generateIdMethod?.Invoke(_conflictAnalyzer, new object[] { conflict }) as string;
            var id2 = generateIdMethod?.Invoke(_conflictAnalyzer, new object[] { conflict }) as string;

            // Assert
            Assert.That(id1, Is.Not.Null.And.Not.Empty);
            Assert.That(id1, Is.EqualTo(id2), "Same conflict should generate same ID");
            Assert.That(id1, Does.StartWith("conf_"), "ID should have conflict prefix");
        }

        [Test]
        public async Task GetDetailedConflictsAsync_ProcessesMultipleConflicts()
        {
            // Arrange
            var tableName = "test_table";
            var mockConflictData = new List<Dictionary<string, object>>
            {
                new()
                {
                    { "our_doc_id", "doc1" },
                    { "base_content", "base1" },
                    { "our_content", "our1" },
                    { "their_content", "their1" }
                },
                new()
                {
                    { "our_doc_id", "doc2" },
                    { "base_content", "base2" },
                    { "our_content", "our2" },
                    { "their_content", "their2" }
                }
            };

            _mockDoltCli.Setup(x => x.GetConflictDetailsAsync(tableName))
                .ReturnsAsync(mockConflictData);

            // Act
            var result = await _conflictAnalyzer.GetDetailedConflictsAsync(tableName);

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].DocumentId, Is.EqualTo("doc1"));
            Assert.That(result[1].DocumentId, Is.EqualTo("doc2"));
            Assert.That(result.All(c => c.Collection == tableName), Is.True);
        }
    }
}