using DMMS.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DMMS.Services;

/// <summary>
/// Browser-based authentication provider for DoltHub login flows
/// </summary>
public class BrowserAuthProvider : IBrowserAuthProvider
{
    private readonly ILogger<BrowserAuthProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the Browser Authentication Provider
    /// </summary>
    /// <param name="logger">Logger for authentication operations</param>
    public BrowserAuthProvider(ILogger<BrowserAuthProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)> AuthenticateWithBrowserAsync(BrowserAuthConfig config)
    {
        try
        {
            _logger.LogInformation("Starting browser-based authentication for DoltHub");
            
            if (config.AutoOpenBrowser)
            {
                if (!OpenBrowser(config.LoginUrl))
                {
                    return Task.FromResult<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)>((false, null, "Failed to open browser for authentication"));
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== DoltHub Authentication Required ===");
            Console.WriteLine($"Please complete authentication in your browser at: {config.LoginUrl}");
            Console.WriteLine();
            Console.WriteLine("After logging in:");
            Console.WriteLine("1. Go to Settings > API Tokens");
            Console.WriteLine("2. Create a new API token");
            Console.WriteLine("3. Copy the token and return here");
            Console.WriteLine();

            Console.Write("Enter your DoltHub username: ");
            var username = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(username))
            {
                return Task.FromResult<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)>((false, null, "Username cannot be empty"));
            }

            Console.Write("Enter your DoltHub API token: ");
            var apiToken = ReadPasswordFromConsole();
            
            if (string.IsNullOrEmpty(apiToken))
            {
                return Task.FromResult<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)>((false, null, "API token cannot be empty"));
            }

            Console.Write("Enter DoltHub endpoint (press Enter for default 'dolthub.com'): ");
            var endpoint = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(endpoint))
            {
                endpoint = "dolthub.com";
            }

            var credentials = new DoltHubCredentials(username, apiToken, endpoint);
            
            _logger.LogInformation("Browser authentication completed successfully for user: {Username} on endpoint: {Endpoint}", 
                username, endpoint);
            
            return Task.FromResult<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)>((true, credentials, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during browser authentication");
            return Task.FromResult<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)>((false, null, $"Authentication failed: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public bool OpenBrowser(string url)
    {
        try
        {
            _logger.LogDebug("Opening browser to URL: {Url}", url);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
                return true;
            }
            else
            {
                _logger.LogWarning("Unsupported platform for opening browser");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open browser to URL: {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Reads password input from console with hidden characters
    /// </summary>
    /// <returns>The entered password</returns>
    private string ReadPasswordFromConsole()
    {
        var password = "";
        ConsoleKeyInfo keyInfo;

        do
        {
            keyInfo = Console.ReadKey(true);
            
            if (keyInfo.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
                Console.Write("\b \b");
            }
            else if (keyInfo.Key != ConsoleKey.Enter && keyInfo.Key != ConsoleKey.Backspace)
            {
                password += keyInfo.KeyChar;
                Console.Write("*");
            }
        }
        while (keyInfo.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return password;
    }
}