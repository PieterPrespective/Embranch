using DMMS.Models;
using DMMS.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace DMMSTesting.IntegrationTests;

/// <summary>
/// Minimal reproducible test for Python.NET ChromaDB deadlock issue
/// This test isolates the exact conditions causing the deadlock
/// </summary>
[TestFixture]
public class PythonNetDeadlockTest
{
    private ILogger<PythonNetDeadlockTest>? _logger;
    private string _testDirectory = null!;

    [SetUp]
    public void Setup()
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<PythonNetDeadlockTest>();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DeadlockTest_{timestamp}");
        Directory.CreateDirectory(_testDirectory);
        
        _logger.LogInformation("Test directory: {TestDirectory}", _testDirectory);
    }

    [Test]
    [Timeout(30000)] // 30 second timeout to detect deadlock
    public async Task MultipleChromaServices_CreateCollection_ShouldNotDeadlock()
    {
        _logger!.LogInformation("=== Starting Python.NET Deadlock Reproduction Test ===");
        
        // Initialize PythonContext once
        if (!PythonContext.IsInitialized)
        {
            _logger.LogInformation("Initializing PythonContext...");
            PythonContext.Initialize();
            _logger.LogInformation("✅ PythonContext initialized");
        }

        try
        {
            // Test Case 1: Single service, multiple operations
            _logger.LogInformation("\n--- Test Case 1: Single Service Multiple Operations ---");
            await TestSingleServiceMultipleOperations();
            
            // Test Case 2: Multiple services, same path
            _logger.LogInformation("\n--- Test Case 2: Multiple Services Same Path ---");
            await TestMultipleServicesSamePath();
            
            // Test Case 3: Multiple services, different paths
            _logger.LogInformation("\n--- Test Case 3: Multiple Services Different Paths ---");
            await TestMultipleServicesDifferentPaths();
            
            // Test Case 4: Service disposal and recreation
            _logger.LogInformation("\n--- Test Case 4: Service Disposal and Recreation ---");
            await TestServiceDisposalAndRecreation();
            
            _logger.LogInformation("\n✅ All test cases completed without deadlock!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Test failed with exception");
            throw;
        }
    }

    private async Task TestSingleServiceMultipleOperations()
    {
        _logger!.LogInformation("Creating single ChromaPythonService...");
        
        var config = Options.Create(new ServerConfiguration
        {
            ChromaDataPath = Path.Combine(_testDirectory, "test1")
        });
        
        var serviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ChromaPythonService>();
        using var service = new ChromaPythonService(serviceLogger, config);
        
        // Multiple operations on same service
        _logger.LogInformation("Listing collections...");
        var collections = await service.ListCollectionsAsync();
        _logger.LogInformation("Collections found: {Count}", collections.Count());
        
        _logger.LogInformation("Creating collection 'test_collection_1'...");
        await service.CreateCollectionAsync("test_collection_1");
        _logger.LogInformation("✅ Collection created");
        
        _logger.LogInformation("Creating collection 'test_collection_2'...");
        await service.CreateCollectionAsync("test_collection_2");
        _logger.LogInformation("✅ Second collection created");
        
        _logger.LogInformation("✅ Single service test passed");
    }

    private async Task TestMultipleServicesSamePath()
    {
        _logger!.LogInformation("Testing multiple services with same path...");
        
        var sharedPath = Path.Combine(_testDirectory, "shared");
        var config = Options.Create(new ServerConfiguration
        {
            ChromaDataPath = sharedPath
        });
        
        var serviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ChromaPythonService>();
        
        // First service
        _logger.LogInformation("Creating first service...");
        using (var service1 = new ChromaPythonService(serviceLogger, config))
        {
            _logger.LogInformation("Service 1: Creating collection...");
            await service1.CreateCollectionAsync("shared_collection_1");
            _logger.LogInformation("✅ Service 1: Collection created");
        }
        
        // Allow cleanup
        await Task.Delay(500);
        GC.Collect();
        
        // Second service - THIS IS WHERE DEADLOCK TYPICALLY OCCURS
        _logger.LogInformation("Creating second service (potential deadlock point)...");
        using (var service2 = new ChromaPythonService(serviceLogger, config))
        {
            _logger.LogInformation("Service 2: Creating collection...");
            await service2.CreateCollectionAsync("shared_collection_2");
            _logger.LogInformation("✅ Service 2: Collection created - NO DEADLOCK!");
        }
        
        _logger.LogInformation("✅ Multiple services same path test passed");
    }

    private async Task TestMultipleServicesDifferentPaths()
    {
        _logger!.LogInformation("Testing multiple services with different paths...");
        
        var path1 = Path.Combine(_testDirectory, "path1");
        var path2 = Path.Combine(_testDirectory, "path2");
        
        var config1 = Options.Create(new ServerConfiguration { ChromaDataPath = path1 });
        var config2 = Options.Create(new ServerConfiguration { ChromaDataPath = path2 });
        
        var serviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ChromaPythonService>();
        
        // First service
        _logger.LogInformation("Creating first service with path1...");
        using (var service1 = new ChromaPythonService(serviceLogger, config1))
        {
            await service1.CreateCollectionAsync("path1_collection");
            _logger.LogInformation("✅ Service 1: Collection created");
        }
        
        // Allow cleanup
        await Task.Delay(500);
        GC.Collect();
        
        // Second service with different path
        _logger.LogInformation("Creating second service with path2...");
        using (var service2 = new ChromaPythonService(serviceLogger, config2))
        {
            await service2.CreateCollectionAsync("path2_collection");
            _logger.LogInformation("✅ Service 2: Collection created");
        }
        
        _logger.LogInformation("✅ Multiple services different paths test passed");
    }

    private async Task TestServiceDisposalAndRecreation()
    {
        _logger!.LogInformation("Testing service disposal and recreation pattern...");
        
        var testPath = Path.Combine(_testDirectory, "disposal_test");
        var config = Options.Create(new ServerConfiguration { ChromaDataPath = testPath });
        var serviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ChromaPythonService>();
        
        // Create, use, and dispose service multiple times
        for (int i = 1; i <= 3; i++)
        {
            _logger.LogInformation($"Iteration {i}: Creating service...");
            
            using (var service = new ChromaPythonService(serviceLogger, config))
            {
                var collectionName = $"disposal_test_{i}";
                _logger.LogInformation($"Creating collection '{collectionName}'...");
                await service.CreateCollectionAsync(collectionName);
                _logger.LogInformation($"✅ Collection '{collectionName}' created");
                
                // Perform additional operation
                var collections = await service.ListCollectionsAsync();
                _logger.LogInformation($"Total collections: {collections.Count()}");
            }
            
            _logger.LogInformation($"Service {i} disposed");
            
            // Small delay between iterations
            await Task.Delay(200);
        }
        
        _logger.LogInformation("✅ Service disposal and recreation test passed");
    }

    [Test]
    [Timeout(30000)]
    public async Task ReproduceExactSyncManagerScenario()
    {
        _logger!.LogInformation("=== Reproducing Exact SyncManager Scenario ===");
        
        if (!PythonContext.IsInitialized)
        {
            PythonContext.Initialize();
        }

        try
        {
            var inputPath = Path.Combine(_testDirectory, "input_chroma");
            var outputPath = Path.Combine(_testDirectory, "output_chroma");
            
            Directory.CreateDirectory(inputPath);
            Directory.CreateDirectory(outputPath);
            
            var inputConfig = Options.Create(new ServerConfiguration { ChromaDataPath = inputPath });
            var outputConfig = Options.Create(new ServerConfiguration { ChromaDataPath = outputPath });
            var serviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ChromaPythonService>();
            
            // Step 1: Input service operations (simulating test setup)
            _logger.LogInformation("Step 1: Input service operations...");
            using (var inputService = new ChromaPythonService(serviceLogger, inputConfig))
            {
                await inputService.ListCollectionsAsync();
                _logger.LogInformation("✅ Input service list operation completed");
            }
            
            // Step 2: Cleanup delay (as in the actual test)
            _logger.LogInformation("Step 2: Cleanup delay...");
            await Task.Delay(1000);
            GC.Collect();
            
            // Step 3: Output service with collection creation (THIS IS THE DEADLOCK POINT)
            _logger.LogInformation("Step 3: Output service collection creation (deadlock point)...");
            using (var outputService = new ChromaPythonService(serviceLogger, outputConfig))
            {
                _logger.LogInformation("Attempting to create 'dolt_sync' collection...");
                await outputService.CreateCollectionAsync("dolt_sync");
                _logger.LogInformation("✅ Collection 'dolt_sync' created successfully - NO DEADLOCK!");
            }
            
            _logger.LogInformation("✅ Exact SyncManager scenario completed without deadlock!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exact scenario reproduction failed");
            throw;
        }
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Thread.Sleep(1000);
                Directory.Delete(_testDirectory, recursive: true);
                _logger?.LogInformation("Test environment cleaned up");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not clean up test environment");
        }
    }
}