using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DMMSManualTesting;

/// <summary>
/// Manual testing class for validating VM RAG operations against live DoltHub account.
/// Provides comprehensive testing of all Dolt operations with user validation prompts.
/// </summary>
public static class VMRAGTest
{
    private static readonly string DoltAuthHelperPath = GetAuthHelperPath();
    private static readonly string DoltExecutablePath = @"C:\Program Files\Dolt\bin\dolt.exe";
    private static readonly string TestDatabaseName = "VMRAGTest";
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "DMMS_VMRAGTest");
    
    /// <summary>
    /// Gets the path to DMMS.AuthHelper.exe, checking both Debug and Release builds.
    /// Handles different working directory contexts (Visual Studio vs command line).
    /// </summary>
    private static string GetAuthHelperPath()
    {
        var possiblePaths = new[]
        {
            // From project directory (Visual Studio context)
            Path.Combine("..", "DMMS.AuthHelper", "bin", "Debug", "net9.0", "DMMS.AuthHelper.exe"),
            Path.Combine("..", "DMMS.AuthHelper", "bin", "Release", "net9.0", "DMMS.AuthHelper.exe"),
            
            // From output directory (command line context)
            Path.Combine("..", "..", "..", "..", "DMMS.AuthHelper", "bin", "Debug", "net9.0", "DMMS.AuthHelper.exe"),
            Path.Combine("..", "..", "..", "..", "DMMS.AuthHelper", "bin", "Release", "net9.0", "DMMS.AuthHelper.exe"),
            
            // From solution root (alternative context)
            Path.Combine("DMMS.AuthHelper", "bin", "Debug", "net9.0", "DMMS.AuthHelper.exe"),
            Path.Combine("DMMS.AuthHelper", "bin", "Release", "net9.0", "DMMS.AuthHelper.exe"),
            
            // Absolute paths based on current executable location
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "DMMS.AuthHelper", "bin", "Debug", "net9.0", "DMMS.AuthHelper.exe")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "DMMS.AuthHelper", "bin", "Release", "net9.0", "DMMS.AuthHelper.exe"))
        };
        
        // Check each possible path
        foreach (var path in possiblePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return Path.GetFullPath(path); // Return absolute path for clarity
                }
            }
            catch
            {
                // Ignore path resolution errors and try next path
            }
        }
        
        // Default to first path if none found (for error messaging)
        return possiblePaths[0];
    }
    
    /// <summary>
    /// Main entry point for VM RAG testing workflow.
    /// Executes comprehensive Dolt operations testing with manual validation.
    /// </summary>
    public static async Task Run()
    {
        Console.WriteLine("VM RAG Test - DoltHub Integration Validation");
        Console.WriteLine("=============================================");
        Console.WriteLine();
        
        var currentStep = 1;
        
        try
        {
            // Step 1: Authentication
            await AuthenticateWithDoltHub(currentStep++);
            
            // Step 2: Setup test environment
            await SetupTestEnvironment(currentStep++);
            
            // Step 3: Repository management operations
            await TestRepositoryOperations(currentStep++);
            
            // Step 4: Branch management operations
            await TestBranchOperations(currentStep++);
            
            // Step 5: Commit operations
            await TestCommitOperations(currentStep++);
            
            // Step 6: Remote operations
            await TestRemoteOperations(currentStep++);
            
            // Step 7: Data CRUD operations
            await TestDataOperations(currentStep++);
            
            // Step 8: Merge operations
            await TestMergeOperations(currentStep++);
            
            // Step 9: Diff operations
            await TestDiffOperations(currentStep++);
            
            // Step 10: Reset operations
            await TestResetOperations(currentStep++);
            
            Console.WriteLine();
            Console.WriteLine("✅ VM RAG Test completed successfully!");
            Console.WriteLine("All Dolt operations have been validated against live DoltHub.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ VM RAG Test failed: {ex.Message}");
            Console.WriteLine("Check the error details above and retry if necessary.");
        }
        finally
        {
            await CleanupTestEnvironment();
            
            Console.WriteLine();
            Console.WriteLine("Press any key to return to main menu...");
            Console.ReadKey();
        }
    }
    
    /// <summary>
    /// Authenticates with DoltHub using the secure authentication helper.
    /// </summary>
    private static async Task AuthenticateWithDoltHub(int stepNumber)
    {
        Console.WriteLine($"🔐 Step {stepNumber}: DoltHub Authentication");
        Console.WriteLine("=================================");
        Console.WriteLine();
        
        // Verify auth helper exists
        if (!File.Exists(DoltAuthHelperPath))
        {
            var currentDir = Directory.GetCurrentDirectory();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            throw new Exception($"DMMS.AuthHelper.exe not found at: {DoltAuthHelperPath}\n" +
                              $"Current working directory: {currentDir}\n" +
                              $"Application base directory: {baseDir}\n" +
                              $"Expected absolute path: {Path.GetFullPath(DoltAuthHelperPath)}\n" +
                              "Please ensure the solution is built. Try running: dotnet build");
        }
        
        Console.WriteLine($"✅ Using auth helper: {DoltAuthHelperPath}");
        
        Console.WriteLine("Checking current authentication status...");
        
        var statusResult = await ExecuteCommand(DoltAuthHelperPath, "status --credential-key VMRAGTest");
        
        if (statusResult.ExitCode != 0)
        {
            Console.WriteLine("❌ No valid DoltHub credentials found.");
            Console.WriteLine("Setting up DoltHub authentication...");
            Console.WriteLine();
            Console.WriteLine("This will open your browser for secure authentication.");
            
            WaitForUserInput("Press Enter to open authentication helper for DoltHub setup");
            
            // Run auth helper in a new visible console window for interactive authentication
            var setupResult = await ExecuteAuthHelperInteractive("setup --credential-key VMRAGTest");
            
            if (!setupResult)
            {
                throw new Exception("Authentication setup failed or was cancelled");
            }
            
            Console.WriteLine("✅ Authentication completed successfully!");
        }
        else
        {
            Console.WriteLine("✅ Valid DoltHub credentials found.");
            Console.WriteLine(statusResult.Output);
        }
        
        WaitForUserInput("Verify authentication is working, then press Enter to continue");
    }
    
    /// <summary>
    /// Sets up the test environment and creates test database on DoltHub.
    /// </summary>
    private static async Task SetupTestEnvironment(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"🏗️  Step {stepNumber}: Test Environment Setup");
        Console.WriteLine("==================================");
        Console.WriteLine();
        
        // Verify Dolt is accessible
        Console.WriteLine("Verifying Dolt installation...");
        var versionResult = await ExecuteDoltCommand("version");
        if (versionResult.ExitCode != 0)
        {
            throw new Exception($"Dolt is not accessible. Please ensure Dolt is installed.\n" +
                              $"Error: {versionResult.Error}");
        }
        Console.WriteLine($"✅ Dolt version: {versionResult.Output.Split('\n')[0]}");
        
        // Configure Dolt git user for commits (required for Dolt to work)
        Console.WriteLine("Configuring Dolt user for commits...");
        await ExecuteDoltCommand("config --global user.email vmragtest@example.com", null);
        await ExecuteDoltCommand("config --global user.name VMRAGTest", null);
        
        // Clean any existing test directory
        if (Directory.Exists(TestDirectory))
        {
            Directory.Delete(TestDirectory, true);
        }
        Directory.CreateDirectory(TestDirectory);
        
        Console.WriteLine($"Created test directory: {TestDirectory}");
        
        Console.WriteLine("Initializing new Dolt repository...");
        Console.WriteLine($"Working directory: {TestDirectory}");
        
        var initResult = await ExecuteDoltCommand("init", TestDirectory);
        if (initResult.ExitCode != 0)
        {
            throw new Exception($"Dolt init failed: {initResult.Error}");
        }
        
        Console.WriteLine("✅ Dolt repository initialized successfully!");
        
        WaitForUserInput("Repository initialized. Press Enter to create database on DoltHub");
        
        // Create initial commit
        await ExecuteDoltCommand("sql -q \"CREATE TABLE test_table (id INT PRIMARY KEY, data TEXT)\"", TestDirectory);
        await ExecuteDoltCommand("add .", TestDirectory);
        await ExecuteDoltCommand("commit -m \"Initial commit for VM RAG testing\"", TestDirectory);
        
        Console.WriteLine("✅ Initial repository setup completed!");
    }
    
    /// <summary>
    /// Tests repository management operations (init, clone, status).
    /// </summary>
    private static async Task TestRepositoryOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"📁 Step {stepNumber}: Repository Management Operations");
        Console.WriteLine("==========================================");
        Console.WriteLine();
        
        // Test status operation
        Console.WriteLine("Testing dolt status...");
        var statusResult = await ExecuteDoltCommand("status", TestDirectory);
        Console.WriteLine($"Status output: {statusResult.Output}");
        
        WaitForUserInput("Verify repository status is clean. Press Enter to continue");
        
        // Test log operation
        Console.WriteLine("Testing dolt log...");
        var logResult = await ExecuteDoltCommand("log --oneline -n 5", TestDirectory);
        Console.WriteLine($"Recent commits: {logResult.Output}");
        
        WaitForUserInput("Verify commit history shows initial commit. Press Enter to continue");
        
        Console.WriteLine("✅ Repository operations validated!");
    }
    
    /// <summary>
    /// Tests branch management operations (list, create, delete, checkout).
    /// </summary>
    private static async Task TestBranchOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"🌿 Step {stepNumber}: Branch Management Operations");
        Console.WriteLine("======================================");
        Console.WriteLine();
        
        // List current branches
        Console.WriteLine("Testing dolt branch (list branches)...");
        var listResult = await ExecuteDoltCommand("branch", TestDirectory);
        Console.WriteLine($"Current branches: {listResult.Output}");
        
        WaitForUserInput("Verify main branch is listed. Press Enter to continue");
        
        // Create new branch
        Console.WriteLine("Testing branch creation...");
        var createResult = await ExecuteDoltCommand("branch feature-test", TestDirectory);
        Console.WriteLine("Branch 'feature-test' created");
        
        // List branches again
        var listResult2 = await ExecuteDoltCommand("branch", TestDirectory);
        Console.WriteLine($"Updated branches: {listResult2.Output}");
        
        WaitForUserInput("Verify 'feature-test' branch was created. Press Enter to continue");
        
        // Checkout new branch
        Console.WriteLine("Testing branch checkout...");
        var checkoutResult = await ExecuteDoltCommand("checkout feature-test", TestDirectory);
        Console.WriteLine("Switched to feature-test branch");
        
        // Verify current branch
        var currentBranchResult = await ExecuteDoltCommand("sql -q \"SELECT active_branch() as branch\"", TestDirectory);
        Console.WriteLine($"Current branch: {currentBranchResult.Output}");
        
        WaitForUserInput("Verify currently on 'feature-test' branch. Press Enter to continue");
        
        // Switch back to main
        await ExecuteDoltCommand("checkout main", TestDirectory);
        Console.WriteLine("Switched back to main branch");
        
        Console.WriteLine("✅ Branch operations validated!");
    }
    
    /// <summary>
    /// Tests commit operations (stage, commit, log).
    /// </summary>
    private static async Task TestCommitOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"💾 Step {stepNumber}: Commit Operations");
        Console.WriteLine("============================");
        Console.WriteLine();
        
        // Add test data
        Console.WriteLine("Adding test data...");
        var insertResult = await ExecuteDoltCommand("sql -q \"INSERT INTO test_table VALUES (1, 'Test data for commit')\"", TestDirectory);
        
        // Check status
        var statusResult = await ExecuteDoltCommand("status", TestDirectory);
        Console.WriteLine($"Repository status: {statusResult.Output}");
        
        WaitForUserInput("Verify unstaged changes are shown. Press Enter to continue");
        
        // Stage changes
        Console.WriteLine("Staging changes...");
        var addResult = await ExecuteDoltCommand("add .", TestDirectory);
        
        // Check status after staging
        var statusResult2 = await ExecuteDoltCommand("status", TestDirectory);
        Console.WriteLine($"Status after staging: {statusResult2.Output}");
        
        WaitForUserInput("Verify changes are staged. Press Enter to continue");
        
        // Commit changes
        Console.WriteLine("Committing changes...");
        var commitResult = await ExecuteDoltCommand("commit -m \"Add test data for VM RAG validation\"", TestDirectory);
        Console.WriteLine($"Commit result: {commitResult.Output}");
        
        // Get commit hash
        var hashResult = await ExecuteDoltCommand("sql -q \"SELECT DOLT_HASHOF('HEAD') as commit_hash\"", TestDirectory);
        Console.WriteLine($"Latest commit hash: {hashResult.Output}");
        
        WaitForUserInput("Verify commit was successful. Press Enter to continue");
        
        Console.WriteLine("✅ Commit operations validated!");
    }
    
    /// <summary>
    /// Tests remote operations (add remote, push, pull, fetch).
    /// </summary>
    private static async Task TestRemoteOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"🌐 Step {stepNumber}: Remote Operations");
        Console.WriteLine("============================");
        Console.WriteLine();
        
        // Get username for remote URL construction
        Console.WriteLine("Getting DoltHub username...");
        var statusResult = await ExecuteCommand(DoltAuthHelperPath, "status --credential-key VMRAGTest");
        Console.WriteLine($"Credential status: {statusResult.Output}");
        
        // Extract username (assuming status shows username)
        var lines = statusResult.Output.Split('\n');
        var usernameLine = Array.Find(lines, line => line.Contains("Username"));
        var username = usernameLine?.Split(':')[1]?.Trim() ?? "unknown";
        
        Console.WriteLine($"DoltHub username: {username}");
        
        WaitForUserInput($"Verify username '{username}' is correct. Press Enter to continue");
        
        // Add remote (correct format for DoltHub is username/database-name)
        var remoteUrl = $"{username}/{TestDatabaseName}";
        Console.WriteLine($"Adding remote: {remoteUrl}");
        var addRemoteResult = await ExecuteDoltCommand($"remote add origin {remoteUrl}", TestDirectory);
        
        if (addRemoteResult.ExitCode != 0 && !addRemoteResult.Error.Contains("already exists"))
        {
            Console.WriteLine($"Note: Remote add result: {addRemoteResult.Error}");
        }
        
        // List remotes
        var listRemotesResult = await ExecuteDoltCommand("remote -v", TestDirectory);
        Console.WriteLine($"Configured remotes: {listRemotesResult.Output}");
        
        WaitForUserInput("Verify remote is configured correctly. Press Enter to continue");
        
        // Push to create database
        Console.WriteLine($"Creating database '{TestDatabaseName}' on DoltHub...");
        Console.WriteLine("This may take a moment for the initial push...");
        Console.WriteLine("Press Ctrl+C if this hangs or takes too long...");
        
        try
        {
            var pushResult = await ExecuteDoltCommand("push origin main", TestDirectory, timeoutSeconds: 120);
            
            if (pushResult.ExitCode != 0)
            {
                Console.WriteLine($"❌ Push failed!");
                Console.WriteLine($"Error: {pushResult.Error}");
                Console.WriteLine($"Output: {pushResult.Output}");
                
                if (pushResult.Error.Contains("Invalid repository ID") || pushResult.Error.Contains("could not be accessed"))
                {
                    Console.WriteLine();
                    Console.WriteLine("This appears to be a DoltHub authentication or permissions issue.");
                    Console.WriteLine("Possible solutions:");
                    Console.WriteLine("1. Verify your DoltHub credentials are correct");
                    Console.WriteLine("2. Check that you have permission to create databases");
                    Console.WriteLine("3. Try creating the database manually on DoltHub first");
                }
                
                WaitForUserInput("Push failed. Continue anyway to test other operations?", stepNumber);
            }
            else
            {
                Console.WriteLine("✅ Push successful!");
                Console.WriteLine($"Result: {pushResult.Output}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Push operation failed with exception: {ex.Message}");
            WaitForUserInput("Push failed with exception. Continue anyway?", stepNumber);
        }
        
        Console.WriteLine($"🌍 Please check DoltHub manually:");
        Console.WriteLine($"   URL: https://www.dolthub.com/{username}/{TestDatabaseName}");
        Console.WriteLine($"   Verify the database '{TestDatabaseName}' exists and contains your test data");
        
        WaitForUserInput("After manually verifying the database on DoltHub, press Enter to continue");
        
        Console.WriteLine("✅ Remote operations completed!");
    }
    
    /// <summary>
    /// Tests data CRUD operations (SELECT, INSERT, UPDATE, DELETE).
    /// </summary>
    private static async Task TestDataOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"🗃️  Step {stepNumber}: Data CRUD Operations");
        Console.WriteLine("=================================");
        Console.WriteLine();
        
        // SELECT operation
        Console.WriteLine("Testing SELECT operation...");
        var selectResult = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table\" -r json", TestDirectory);
        Console.WriteLine($"Current data: {selectResult.Output}");
        
        WaitForUserInput("Verify existing data is shown. Press Enter to continue");
        
        // INSERT operation
        Console.WriteLine("Testing INSERT operation...");
        var insertResult = await ExecuteDoltCommand("sql -q \"INSERT INTO test_table VALUES (2, 'Additional test data'), (3, 'More test data')\"", TestDirectory);
        
        // Verify INSERT
        var selectResult2 = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table ORDER BY id\" -r json", TestDirectory);
        Console.WriteLine($"Data after INSERT: {selectResult2.Output}");
        
        WaitForUserInput("Verify new records were inserted. Press Enter to continue");
        
        // UPDATE operation
        Console.WriteLine("Testing UPDATE operation...");
        var updateResult = await ExecuteDoltCommand("sql -q \"UPDATE test_table SET data = 'Updated test data' WHERE id = 2\"", TestDirectory);
        
        // Verify UPDATE
        var selectResult3 = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table WHERE id = 2\" -r json", TestDirectory);
        Console.WriteLine($"Updated record: {selectResult3.Output}");
        
        WaitForUserInput("Verify record was updated. Press Enter to continue");
        
        // DELETE operation
        Console.WriteLine("Testing DELETE operation...");
        var deleteResult = await ExecuteDoltCommand("sql -q \"DELETE FROM test_table WHERE id = 3\"", TestDirectory);
        
        // Verify DELETE
        var selectResult4 = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table ORDER BY id\" -r json", TestDirectory);
        Console.WriteLine($"Data after DELETE: {selectResult4.Output}");
        
        WaitForUserInput("Verify record was deleted. Press Enter to continue");
        
        // Commit data changes
        await ExecuteDoltCommand("add .", TestDirectory);
        await ExecuteDoltCommand("commit -m \"VM RAG test: CRUD operations validation\"", TestDirectory);
        
        Console.WriteLine("✅ Data CRUD operations validated!");
    }
    
    /// <summary>
    /// Tests merge operations and conflict resolution.
    /// </summary>
    private static async Task TestMergeOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"🔄 Step {stepNumber}: Merge Operations");
        Console.WriteLine("===========================");
        Console.WriteLine();
        
        // Create changes on feature branch
        Console.WriteLine("Creating changes on feature-test branch...");
        await ExecuteDoltCommand("checkout feature-test", TestDirectory);
        
        await ExecuteDoltCommand("sql -q \"INSERT INTO test_table VALUES (4, 'Feature branch data')\"", TestDirectory);
        await ExecuteDoltCommand("add .", TestDirectory);
        await ExecuteDoltCommand("commit -m \"Add data from feature branch\"", TestDirectory);
        
        Console.WriteLine("Created commit on feature-test branch");
        
        WaitForUserInput("Feature branch changes committed. Press Enter to continue");
        
        // Switch back to main and merge
        await ExecuteDoltCommand("checkout main", TestDirectory);
        Console.WriteLine("Switched back to main branch");
        
        Console.WriteLine("Testing merge operation...");
        var mergeResult = await ExecuteDoltCommand("merge feature-test", TestDirectory);
        Console.WriteLine($"Merge result: {mergeResult.Output}");
        
        if (mergeResult.ExitCode != 0)
        {
            Console.WriteLine("Merge conflicts may exist. Checking...");
            var conflictsResult = await ExecuteDoltCommand("status", TestDirectory);
            Console.WriteLine($"Status: {conflictsResult.Output}");
            
            // If conflicts exist, show how to resolve them
            if (conflictsResult.Output.Contains("conflict"))
            {
                Console.WriteLine("Conflicts detected. In a real scenario, you would resolve them manually.");
                Console.WriteLine("For this test, we'll abort the merge and continue.");
                await ExecuteDoltCommand("merge --abort", TestDirectory);
            }
        }
        else
        {
            // Verify merged data
            var selectResult = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table ORDER BY id\" -r json", TestDirectory);
            Console.WriteLine($"Data after merge: {selectResult.Output}");
        }
        
        WaitForUserInput("Verify merge operation completed. Press Enter to continue");
        
        Console.WriteLine("✅ Merge operations validated!");
    }
    
    /// <summary>
    /// Tests diff operations for tracking changes.
    /// </summary>
    private static async Task TestDiffOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"📊 Step {stepNumber}: Diff Operations");
        Console.WriteLine("==========================");
        Console.WriteLine();
        
        // Make a new change
        Console.WriteLine("Making changes for diff testing...");
        await ExecuteDoltCommand("sql -q \"INSERT INTO test_table VALUES (5, 'Diff test data')\"", TestDirectory);
        
        // Show working changes
        Console.WriteLine("Testing dolt diff (working changes)...");
        var diffResult = await ExecuteDoltCommand("diff", TestDirectory);
        Console.WriteLine($"Working diff: {diffResult.Output}");
        
        WaitForUserInput("Verify unstaged changes are shown in diff. Press Enter to continue");
        
        // Stage and commit changes
        await ExecuteDoltCommand("add .", TestDirectory);
        await ExecuteDoltCommand("commit -m \"Add data for diff testing\"", TestDirectory);
        
        // Test commit diff
        Console.WriteLine("Testing commit-to-commit diff...");
        var diffCommitsResult = await ExecuteDoltCommand("diff HEAD~1 HEAD", TestDirectory);
        Console.WriteLine($"Commit diff: {diffCommitsResult.Output}");
        
        WaitForUserInput("Verify diff between commits is shown. Press Enter to continue");
        
        // Test table diff via SQL
        Console.WriteLine("Testing table diff via SQL...");
        var tableDiffResult = await ExecuteDoltCommand("sql -q \"SELECT * FROM DOLT_DIFF('HEAD~1', 'HEAD', 'test_table')\" -r json", TestDirectory);
        Console.WriteLine($"Table diff: {tableDiffResult.Output}");
        
        WaitForUserInput("Verify table-level diff data. Press Enter to continue");
        
        Console.WriteLine("✅ Diff operations validated!");
    }
    
    /// <summary>
    /// Tests reset operations (hard and soft reset).
    /// </summary>
    private static async Task TestResetOperations(int stepNumber)
    {
        Console.WriteLine();
        Console.WriteLine($"⏪ Step {stepNumber}: Reset Operations");
        Console.WriteLine("============================");
        Console.WriteLine();
        
        // Get current commit hash
        var currentHashResult = await ExecuteDoltCommand("sql -q \"SELECT DOLT_HASHOF('HEAD') as hash\"", TestDirectory);
        Console.WriteLine($"Current HEAD: {currentHashResult.Output}");
        
        // Make changes for reset testing
        Console.WriteLine("Making changes for reset testing...");
        await ExecuteDoltCommand("sql -q \"INSERT INTO test_table VALUES (6, 'Reset test data')\"", TestDirectory);
        await ExecuteDoltCommand("add .", TestDirectory);
        await ExecuteDoltCommand("commit -m \"Temporary commit for reset testing\"", TestDirectory);
        
        var newHashResult = await ExecuteDoltCommand("sql -q \"SELECT DOLT_HASHOF('HEAD') as hash\"", TestDirectory);
        Console.WriteLine($"After temporary commit: {newHashResult.Output}");
        
        WaitForUserInput("Temporary commit created. Press Enter to test soft reset");
        
        // Test soft reset
        Console.WriteLine("Testing soft reset...");
        var softResetResult = await ExecuteDoltCommand("reset --soft HEAD~1", TestDirectory);
        Console.WriteLine("Soft reset completed");
        
        // Check status after soft reset
        var statusResult = await ExecuteDoltCommand("status", TestDirectory);
        Console.WriteLine($"Status after soft reset: {statusResult.Output}");
        
        WaitForUserInput("Verify changes are staged after soft reset. Press Enter to test hard reset");
        
        // Test hard reset
        Console.WriteLine("Testing hard reset...");
        var hardResetResult = await ExecuteDoltCommand("reset --hard HEAD", TestDirectory);
        Console.WriteLine("Hard reset completed");
        
        // Check status after hard reset
        var statusResult2 = await ExecuteDoltCommand("status", TestDirectory);
        Console.WriteLine($"Status after hard reset: {statusResult2.Output}");
        
        // Verify data
        var dataResult = await ExecuteDoltCommand("sql -q \"SELECT * FROM test_table ORDER BY id\" -r json", TestDirectory);
        Console.WriteLine($"Data after reset: {dataResult.Output}");
        
        WaitForUserInput("Verify all changes were discarded. Press Enter to continue");
        
        Console.WriteLine("✅ Reset operations validated!");
    }
    
    /// <summary>
    /// Cleans up the test environment and removes temporary files.
    /// </summary>
    private static async Task CleanupTestEnvironment()
    {
        Console.WriteLine();
        Console.WriteLine("🧹 Cleanup: Removing test environment");
        Console.WriteLine("=====================================");
        Console.WriteLine();
        
        try
        {
            if (Directory.Exists(TestDirectory))
            {
                Directory.Delete(TestDirectory, true);
                Console.WriteLine($"✅ Removed test directory: {TestDirectory}");
            }
            
            // Optionally clean up credentials
            Console.Write("Remove test credentials from Windows Credential Manager? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLower();
            
            if (response == "y" || response == "yes")
            {
                var forgetResult = await ExecuteCommand(DoltAuthHelperPath, "forget --credential-key VMRAGTest");
                if (forgetResult.ExitCode == 0)
                {
                    Console.WriteLine("✅ Test credentials removed");
                }
                else
                {
                    Console.WriteLine($"⚠️  Could not remove test credentials: {forgetResult.Error}");
                }
            }
            
            Console.WriteLine("Note: The test database 'VMRAGTest' remains on your DoltHub account.");
            Console.WriteLine("You can manually delete it from the DoltHub web interface if desired.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Cleanup warning: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Executes the auth helper in an interactive console window.
    /// </summary>
    private static async Task<bool> ExecuteAuthHelperInteractive(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = DoltAuthHelperPath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = true; // Allow window creation
            process.StartInfo.CreateNoWindow = false; // Show the window
            
            Console.WriteLine($"Starting auth helper with: {arguments}");
            Console.WriteLine("Please complete the authentication in the new window...");
            
            process.Start();
            
            // Wait for the process to complete
            await Task.Run(() => process.WaitForExit());
            
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start auth helper: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Executes a Dolt command with specified working directory using simplified approach.
    /// </summary>
    private static async Task<CommandResult> ExecuteDoltCommand(string arguments, string? workingDirectory = null, int timeoutSeconds = 30)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = DoltExecutablePath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }
            
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            // Wait for process to complete with timeout
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                process.Kill();
                return new CommandResult
                {
                    ExitCode = -1,
                    Output = "",
                    Error = $"Command timed out after {timeoutSeconds} seconds"
                };
            }
            
            var output = await outputTask;
            var error = await errorTask;
            
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                Output = "",
                Error = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Executes a command and returns the result.
    /// </summary>
    private static async Task<CommandResult> ExecuteCommand(string executable, string arguments, string? workingDirectory = null, int timeoutSeconds = 30)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;  // Add this to handle any unexpected input prompts
            process.StartInfo.CreateNoWindow = true;
            
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }
            
            // Set environment variables that might be needed for Dolt
            process.StartInfo.EnvironmentVariables["DOLT_CLI_USE_PAGER"] = "0";  // Disable pager
            process.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";  // Disable git prompts
            
            process.Start();
            
            // Close stdin immediately to prevent hanging on input
            process.StandardInput.Close();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill();
                }
                catch { }
                throw new TimeoutException($"Command timed out after {timeoutSeconds} seconds");
            }
            
            var output = await outputTask;
            var error = await errorTask;
            
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                Output = "",
                Error = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Waits for user input with a descriptive message and abort capability.
    /// </summary>
    private static void WaitForUserInput(string message, int stepNumber = 0)
    {
        Console.WriteLine();
        if (stepNumber > 0)
        {
            Console.WriteLine($"📋 Step {stepNumber}: {message}");
        }
        else
        {
            Console.WriteLine($"👤 {message}");
        }
        Console.WriteLine("Press Enter to continue, or type 'abort' to stop the test...");
        
        var input = Console.ReadLine()?.Trim().ToLower();
        if (input == "abort" || input == "a")
        {
            Console.WriteLine("❌ Test aborted by user.");
            throw new OperationCanceledException("Test aborted by user");
        }
    }
}

/// <summary>
/// Represents the result of a command execution.
/// </summary>
internal class CommandResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
}