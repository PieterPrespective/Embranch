using DMMS.AuthHelper.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DMMS.AuthHelper;

/// <summary>
/// Main entry point for DMMS Authentication Helper
/// Provides secure DoltHub credential management via external process
/// </summary>
public class Program
{
    private static ILogger<Program>? _logger;

    /// <summary>
    /// Main entry point for the authentication helper
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code (0 = success, 1 = error)</returns>
    public static async Task<int> Main(string[] args)
    {
        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<Program>();

        try
        {
            // Parse command line arguments
            var configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            var command = GetCommand(args);
            var endpoint = configuration["endpoint"] ?? "dolthub.com";
            var credentialKey = configuration["credential-key"];
            var verbose = configuration.GetValue<bool>("verbose");

            if (verbose)
            {
                // Enable debug logging for verbose mode
                loggerFactory.Dispose();
                using var verboseLoggerFactory = LoggerFactory.Create(builder =>
                    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
                _logger = verboseLoggerFactory.CreateLogger<Program>();
            }

            _logger.LogInformation("DMMS Authentication Helper started");
            _logger.LogDebug("Command: {Command}, Endpoint: {Endpoint}, CredentialKey: {CredentialKey}", command, endpoint, credentialKey);

            // Execute command
            var exitCode = command switch
            {
                "setup" => await ExecuteSetupCommand(endpoint, credentialKey),
                "refresh" => await ExecuteRefreshCommand(endpoint, credentialKey),
                "forget" => await ExecuteForgetCommand(endpoint, credentialKey),
                "status" => await ExecuteStatusCommand(endpoint, credentialKey),
                "help" => ExecuteHelpCommand(),
                _ => ExecuteHelpCommand()
            };

            _logger.LogInformation("DMMS Authentication Helper completed with exit code: {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unhandled exception in authentication helper");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Parses the command from command line arguments
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>The command to execute</returns>
    private static string GetCommand(string[] args)
    {
        if (args.Length == 0)
            return "help";

        var firstArg = args[0].ToLowerInvariant();
        return firstArg switch
        {
            "setup" => "setup",
            "refresh" => "refresh", 
            "forget" => "forget",
            "status" => "status",
            "help" or "--help" or "-h" => "help",
            _ => "help"
        };
    }

    /// <summary>
    /// Executes the setup command to configure DoltHub authentication
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key</param>
    /// <returns>Exit code</returns>
    private static async Task<int> ExecuteSetupCommand(string endpoint, string? credentialKey)
    {
        try
        {
            _logger?.LogInformation("Executing setup command for endpoint: {Endpoint}", endpoint);

            // Validate endpoint
            if (!WindowsCredentialHelper.IsValidDoltHubEndpoint(endpoint))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Invalid DoltHub endpoint '{endpoint}'");
                Console.ResetColor();
                return 1;
            }

            // Check if credentials already exist
            var existingCredentials = WindowsCredentialHelper.GetDoltHubCredentials(endpoint, credentialKey, _logger);
            if (existingCredentials.HasValue)
            {
                Console.WriteLine($"Credentials already exist for endpoint '{endpoint}'.");
                
                if (!ConsoleInputHelper.PromptForConfirmation("Do you want to replace them?", false))
                {
                    Console.WriteLine("Setup cancelled by user.");
                    return 0;
                }
            }

            // Display help information
            ConsoleInputHelper.DisplayAuthenticationHelp();

            // Open browser to DoltHub login page
            Console.WriteLine("Opening DoltHub authentication page in your browser...");
            if (!BrowserAuthHelper.OpenDoltHubLogin(endpoint, _logger))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: Could not open browser automatically.");
                Console.WriteLine($"Please manually navigate to: {BrowserAuthHelper.GenerateLoginUrl(endpoint)}");
                Console.ResetColor();
            }

            // Wait a moment for browser to open
            await Task.Delay(2000);

            // Prompt for credentials
            var credentials = ConsoleInputHelper.PromptForDoltHubCredentials(endpoint, _logger);
            if (!credentials.HasValue)
            {
                Console.WriteLine("Setup cancelled.");
                return 1;
            }

            // Store credentials
            var result = WindowsCredentialHelper.StoreDoltHubCredentials(credentials.Value, credentialKey, _logger);
            ConsoleInputHelper.DisplayResult(result, "Credential storage");

            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during setup command");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Setup failed: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Executes the refresh command to update existing credentials
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key</param>
    /// <returns>Exit code</returns>
    private static async Task<int> ExecuteRefreshCommand(string endpoint, string? credentialKey)
    {
        try
        {
            _logger?.LogInformation("Executing refresh command for endpoint: {Endpoint}", endpoint);

            // Check if credentials exist
            var existingCredentials = WindowsCredentialHelper.GetDoltHubCredentials(endpoint, credentialKey, _logger);
            if (!existingCredentials.HasValue)
            {
                Console.WriteLine($"No existing credentials found for endpoint '{endpoint}'.");
                Console.WriteLine("Use 'setup' command to configure authentication.");
                return 1;
            }

            Console.WriteLine($"Refreshing credentials for endpoint '{endpoint}'...");
            
            // Force new authentication (same as setup but with different messaging)
            return await ExecuteSetupCommand(endpoint, credentialKey);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during refresh command");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Refresh failed: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Executes the forget command to remove stored credentials
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key</param>
    /// <returns>Exit code</returns>
    private static Task<int> ExecuteForgetCommand(string endpoint, string? credentialKey)
    {
        try
        {
            _logger?.LogInformation("Executing forget command for endpoint: {Endpoint}", endpoint);

            // Check if credentials exist
            var existingCredentials = WindowsCredentialHelper.GetDoltHubCredentials(endpoint, credentialKey, _logger);
            if (!existingCredentials.HasValue)
            {
                Console.WriteLine($"No credentials found for endpoint '{endpoint}'.");
                return Task.FromResult(0);
            }

            Console.WriteLine($"Found credentials for endpoint '{endpoint}'.");
            
            if (!ConsoleInputHelper.PromptForConfirmation("Are you sure you want to remove them?", false))
            {
                Console.WriteLine("Operation cancelled by user.");
                return Task.FromResult(0);
            }

            var result = WindowsCredentialHelper.RemoveDoltHubCredentials(endpoint, credentialKey, _logger);
            ConsoleInputHelper.DisplayResult(result, "Credential removal");

            return Task.FromResult(result.IsSuccess ? 0 : 1);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during forget command");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Forget command failed: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Executes the status command to check credential status
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key</param>
    /// <returns>Exit code</returns>
    private static Task<int> ExecuteStatusCommand(string endpoint, string? credentialKey)
    {
        try
        {
            _logger?.LogInformation("Executing status command for endpoint: {Endpoint}", endpoint);

            var credentials = WindowsCredentialHelper.GetDoltHubCredentials(endpoint, credentialKey, _logger);
            
            Console.WriteLine();
            Console.WriteLine("=== DMMS DoltHub Credential Status ===");
            Console.WriteLine($"Endpoint: {endpoint}");
            Console.WriteLine($"Credential Key: {credentialKey ?? string.Format(WindowsCredentialHelper.DefaultCredentialKeyFormat, WindowsCredentialHelper.SanitizeEndpoint(endpoint))}");
            Console.WriteLine();

            if (credentials.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Credentials are configured");
                Console.ResetColor();
                Console.WriteLine($"Username: {credentials.Value.Username}");
                Console.WriteLine($"API Token: {new string('*', Math.Min(credentials.Value.ApiToken.Length, 20))}");
                Console.WriteLine();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠ No credentials configured");
                Console.ResetColor();
                Console.WriteLine("Use 'setup' command to configure authentication.");
                Console.WriteLine();
            }

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during status command");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Status check failed: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Executes the help command to display usage information
    /// </summary>
    /// <returns>Exit code</returns>
    private static int ExecuteHelpCommand()
    {
        Console.WriteLine();
        Console.WriteLine("DMMS Authentication Helper - Secure DoltHub Credential Management");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  DMMS.AuthHelper.exe <command> [options]");
        Console.WriteLine();
        Console.WriteLine("COMMANDS:");
        Console.WriteLine("  setup          Configure DoltHub authentication (opens browser)");
        Console.WriteLine("  refresh        Update existing credentials");
        Console.WriteLine("  forget         Remove stored credentials");
        Console.WriteLine("  status         Check credential configuration status");
        Console.WriteLine("  help           Show this help information");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --endpoint <url>       DoltHub endpoint (default: dolthub.com)");
        Console.WriteLine("  --credential-key <key> Custom credential storage key");
        Console.WriteLine("  --verbose              Enable verbose logging");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  DMMS.AuthHelper.exe setup");
        Console.WriteLine("  DMMS.AuthHelper.exe setup --endpoint mydolthub.com");
        Console.WriteLine("  DMMS.AuthHelper.exe setup --credential-key MyCustomKey");
        Console.WriteLine("  DMMS.AuthHelper.exe status");
        Console.WriteLine("  DMMS.AuthHelper.exe forget --endpoint mydolthub.com");
        Console.WriteLine();
        Console.WriteLine("SECURITY:");
        Console.WriteLine("  - Credentials are stored securely in Windows Credential Manager");
        Console.WriteLine("  - API tokens are never displayed in logs or console output");
        Console.WriteLine("  - Browser-based authentication prevents credential exposure");
        Console.WriteLine();
        return 0;
    }
}
