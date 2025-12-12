using System;
using System.Threading.Tasks;
using Python.Runtime;

namespace DMMSManualTesting
{
    public class PythonNetHangTest
    {
        private static bool _pythonInitialized = false;
        private static readonly object _pythonLock = new object();
        
        public static async Task Run()
        {
            Console.WriteLine("Starting Python.NET hang diagnostic...");
            
            try
            {
                InitializePython();
                Console.WriteLine("✓ Python initialized");
                
                Console.WriteLine("Testing basic Python operations...");
                await TestBasicPythonOperations();
                
                Console.WriteLine("Testing ChromaDB import...");
                await TestChromaDBImport();
                
                Console.WriteLine("Testing ChromaDB client creation...");
                await TestChromaDBClient();
                
                Console.WriteLine("Testing ChromaDB collection creation...");
                await TestChromaDBCollectionCreation();
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("Test complete. Press any key to exit...");
            Console.ReadKey();
        }
        
        private static void InitializePython()
        {
            lock (_pythonLock)
            {
                if (!_pythonInitialized)
                {
                    string pythonDll = @"C:\ProgramData\anaconda3\python311.dll";
                    if (System.IO.File.Exists(pythonDll))
                    {
                        Runtime.PythonDLL = pythonDll;
                    }
                    
                    PythonEngine.Initialize();
                    _pythonInitialized = true;
                }
            }
        }
        
        private static async Task TestBasicPythonOperations()
        {
            var task = Task.Run(() =>
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    Console.WriteLine($"✓ Python version: {sys.version}");
                    
                    dynamic math = Py.Import("math");
                    double result = math.sqrt(16);
                    Console.WriteLine($"✓ Math operation result: {result}");
                }
            });
            
            var completed = await Task.WhenAny(task, Task.Delay(10000));
            if (completed != task)
            {
                Console.WriteLine("❌ Basic Python operations timed out!");
            }
            else
            {
                await task;
                Console.WriteLine("✓ Basic Python operations completed");
            }
        }
        
        private static async Task TestChromaDBImport()
        {
            var task = Task.Run(() =>
            {
                using (Py.GIL())
                {
                    dynamic chromadb = Py.Import("chromadb");
                    Console.WriteLine($"✓ ChromaDB imported, version: {chromadb.__version__}");
                }
            });
            
            var completed = await Task.WhenAny(task, Task.Delay(10000));
            if (completed != task)
            {
                Console.WriteLine("❌ ChromaDB import timed out!");
            }
            else
            {
                await task;
                Console.WriteLine("✓ ChromaDB import completed");
            }
        }
        
        private static async Task TestChromaDBClient()
        {
            var task = Task.Run(() =>
            {
                using (Py.GIL())
                {
                    dynamic chromadb = Py.Import("chromadb");
                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pythonnet_test_{Guid.NewGuid():N}");
                    System.IO.Directory.CreateDirectory(tempPath);
                    
                    Console.WriteLine($"Creating PersistentClient at: {tempPath}");
                    dynamic client = chromadb.PersistentClient(path: tempPath);
                    Console.WriteLine("✓ PersistentClient created successfully");
                    
                    return new { client, tempPath };
                }
            });
            
            var completed = await Task.WhenAny(task, Task.Delay(15000));
            if (completed != task)
            {
                Console.WriteLine("❌ ChromaDB client creation timed out!");
            }
            else
            {
                var result = await task;
                Console.WriteLine("✓ ChromaDB client creation completed");
            }
        }
        
        private static async Task TestChromaDBCollectionCreation()
        {
            var task = Task.Run(() =>
            {
                using (Py.GIL())
                {
                    dynamic chromadb = Py.Import("chromadb");
                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pythonnet_collection_test_{Guid.NewGuid():N}");
                    System.IO.Directory.CreateDirectory(tempPath);
                    
                    Console.WriteLine($"Creating PersistentClient at: {tempPath}");
                    dynamic client = chromadb.PersistentClient(path: tempPath);
                    Console.WriteLine("✓ PersistentClient created");
                    
                    Console.WriteLine("Creating collection...");
                    dynamic collection = client.create_collection(name: "test_collection_pythonnet");
                    Console.WriteLine("✓ Collection created successfully!");
                    Console.WriteLine($"Collection name: {collection.name}");
                    
                    return tempPath;
                }
            });
            
            var completed = await Task.WhenAny(task, Task.Delay(20000));
            if (completed != task)
            {
                Console.WriteLine("❌ ChromaDB collection creation TIMED OUT!");
                Console.WriteLine("This confirms the hang occurs during collection creation in Python.NET");
            }
            else
            {
                var tempPath = await task;
                Console.WriteLine("✓ ChromaDB collection creation completed successfully");
                
                // Cleanup
                try
                {
                    if (System.IO.Directory.Exists(tempPath))
                    {
                        System.IO.Directory.Delete(tempPath, true);
                        Console.WriteLine("✓ Cleanup completed");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Cleanup failed: {ex.Message}");
                }
            }
        }
    }
}