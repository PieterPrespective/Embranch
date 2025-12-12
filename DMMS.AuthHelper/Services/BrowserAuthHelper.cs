using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DMMS.AuthHelper.Services;

/// <summary>
/// Helper for browser-based authentication
/// </summary>
public static class BrowserAuthHelper
{
    /// <summary>
    /// Opens the DoltHub login page in the default browser
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>True if browser was opened successfully</returns>
    public static bool OpenDoltHubLogin(string endpoint = "dolthub.com", ILogger? logger = null)
    {
        try
        {
            var loginUrl = GenerateLoginUrl(endpoint);
            logger?.LogInformation("Opening DoltHub login page: {LoginUrl}", loginUrl);
            
            return OpenBrowser(loginUrl, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to open DoltHub login page for endpoint: {Endpoint}", endpoint);
            return false;
        }
    }

    /// <summary>
    /// Generates the login URL for a DoltHub endpoint
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <returns>The login URL</returns>
    public static string GenerateLoginUrl(string endpoint = "dolthub.com")
    {
        var sanitizedEndpoint = WindowsCredentialHelper.SanitizeEndpoint(endpoint);
        
        // Use HTTPS by default
        if (!sanitizedEndpoint.StartsWith("http"))
        {
            sanitizedEndpoint = $"https://{sanitizedEndpoint}";
        }
        
        return $"{sanitizedEndpoint.TrimEnd('/')}/settings/tokens";
    }

    /// <summary>
    /// Opens a URL in the default browser (cross-platform)
    /// </summary>
    /// <param name="url">The URL to open</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>True if successful</returns>
    private static bool OpenBrowser(string url, ILogger? logger = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows implementation
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                logger?.LogDebug("Opened browser on Windows");
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux implementation
                Process.Start("xdg-open", url);
                logger?.LogDebug("Opened browser on Linux");
                return true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS implementation
                Process.Start("open", url);
                logger?.LogDebug("Opened browser on macOS");
                return true;
            }
            else
            {
                logger?.LogWarning("Unsupported platform for browser opening");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to open browser with URL: {Url}", url);
            return false;
        }
    }
}