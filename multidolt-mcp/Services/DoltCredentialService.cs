using DMMS.Models;
using Microsoft.Extensions.Logging;

namespace DMMS.Services;

/// <summary>
/// Comprehensive DoltHub credential management service with browser authentication support
/// </summary>
public class DoltCredentialService
{
    private readonly IDoltCredentialProvider _credentialProvider;
    private readonly IBrowserAuthProvider _browserAuthProvider;
    private readonly ILogger<DoltCredentialService> _logger;

    /// <summary>
    /// Initializes a new instance of the Dolt Credential Service
    /// </summary>
    /// <param name="credentialProvider">The credential storage provider</param>
    /// <param name="browserAuthProvider">The browser authentication provider</param>
    /// <param name="logger">Logger for service operations</param>
    public DoltCredentialService(
        IDoltCredentialProvider credentialProvider,
        IBrowserAuthProvider browserAuthProvider,
        ILogger<DoltCredentialService> logger)
    {
        _credentialProvider = credentialProvider;
        _browserAuthProvider = browserAuthProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets DoltHub credentials, prompting for browser authentication if not found
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="promptForAuth">Whether to prompt for authentication if credentials are missing</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>The DoltHub credentials or null if not available</returns>
    public async Task<DoltHubCredentials?> GetOrPromptDoltHubCredentialsAsync(string endpoint = "dolthub.com", bool promptForAuth = true, string? credentialKey = null)
    {
        try
        {
            var existingCredentials = await _credentialProvider.GetDoltHubCredentialsAsync(endpoint, credentialKey);
            if (existingCredentials.HasValue)
            {
                _logger.LogDebug("Found existing DoltHub credentials for endpoint: {Endpoint}", endpoint);
                return existingCredentials;
            }

            if (!promptForAuth)
            {
                _logger.LogDebug("No credentials found and authentication prompting disabled for endpoint: {Endpoint}", endpoint);
                return null;
            }

            _logger.LogInformation("No DoltHub credentials found for endpoint: {Endpoint}, starting browser authentication", endpoint);

            var config = new BrowserAuthConfig(
                loginUrl: $"https://www.{endpoint}/settings/tokens",
                timeoutSeconds: 300,
                autoOpenBrowser: true
            );

            var (success, credentials, errorMessage) = await _browserAuthProvider.AuthenticateWithBrowserAsync(config);
            
            if (!success || !credentials.HasValue)
            {
                _logger.LogWarning("Browser authentication failed for endpoint: {Endpoint}. Error: {Error}", endpoint, errorMessage);
                return null;
            }

            var storeResult = await _credentialProvider.StoreDoltHubCredentialsAsync(credentials.Value, credentialKey);
            if (!storeResult.IsSuccess)
            {
                _logger.LogWarning("Failed to store DoltHub credentials after authentication: {Error}", storeResult.ErrorMessage);
            }
            else
            {
                _logger.LogInformation("Successfully authenticated and stored DoltHub credentials for user: {Username} on endpoint: {Endpoint}", 
                    credentials.Value.Username, endpoint);
            }

            return credentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or prompt for DoltHub credentials for endpoint: {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Gets SQL credentials for a specific remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL remote URL</param>
    /// <returns>The SQL credentials or null if not found</returns>
    public async Task<DoltSqlCredentials?> GetSqlCredentialsAsync(string remoteUrl)
    {
        return await _credentialProvider.GetSqlCredentialsAsync(remoteUrl);
    }

    /// <summary>
    /// Stores DoltHub credentials
    /// </summary>
    /// <param name="credentials">The credentials to store</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>Result of the storage operation</returns>
    public async Task<CredentialResult> StoreDoltHubCredentialsAsync(DoltHubCredentials credentials, string? credentialKey = null)
    {
        return await _credentialProvider.StoreDoltHubCredentialsAsync(credentials, credentialKey);
    }

    /// <summary>
    /// Stores SQL credentials
    /// </summary>
    /// <param name="credentials">The credentials to store</param>
    /// <returns>Result of the storage operation</returns>
    public async Task<CredentialResult> StoreSqlCredentialsAsync(DoltSqlCredentials credentials)
    {
        return await _credentialProvider.StoreSqlCredentialsAsync(credentials);
    }

    /// <summary>
    /// Forgets all DoltHub credentials for a specific endpoint
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>Result of the forget operation</returns>
    public async Task<CredentialResult> ForgetDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null)
    {
        _logger.LogInformation("Forgetting DoltHub credentials for endpoint: {Endpoint}", endpoint);
        return await _credentialProvider.ForgetDoltHubCredentialsAsync(endpoint, credentialKey);
    }

    /// <summary>
    /// Forgets SQL credentials for a specific remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL remote URL</param>
    /// <returns>Result of the forget operation</returns>
    public async Task<CredentialResult> ForgetSqlCredentialsAsync(string remoteUrl)
    {
        _logger.LogInformation("Forgetting SQL credentials for remote: {RemoteUrl}", remoteUrl);
        return await _credentialProvider.ForgetSqlCredentialsAsync(remoteUrl);
    }

    /// <summary>
    /// Forgets all stored credentials (both DoltHub and SQL server)
    /// </summary>
    /// <returns>Result of the forget operation</returns>
    public async Task<CredentialResult> ForgetAllCredentialsAsync()
    {
        _logger.LogInformation("Forgetting all DMMS stored credentials");
        return await _credentialProvider.ForgetAllCredentialsAsync();
    }

    /// <summary>
    /// Checks if DoltHub credentials exist for the specified endpoint
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>True if credentials exist, false otherwise</returns>
    public async Task<bool> HasDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null)
    {
        return await _credentialProvider.HasDoltHubCredentialsAsync(endpoint, credentialKey);
    }

    /// <summary>
    /// Checks if SQL credentials exist for the specified remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL remote URL</param>
    /// <returns>True if credentials exist, false otherwise</returns>
    public async Task<bool> HasSqlCredentialsAsync(string remoteUrl)
    {
        return await _credentialProvider.HasSqlCredentialsAsync(remoteUrl);
    }
}

/// <summary>
/// Utility class for DoltCredentialService operations
/// </summary>
public static class DoltCredentialServiceUtility
{
    /// <summary>
    /// Validates DoltHub endpoint format
    /// </summary>
    /// <param name="endpoint">The endpoint to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidDoltHubEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        try
        {
            if (!endpoint.Contains('.'))
                return false;
                
            var uri = new Uri($"https://{endpoint}");
            return uri.Host.Equals(endpoint, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates SQL remote URL format
    /// </summary>
    /// <param name="remoteUrl">The remote URL to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidSqlRemoteUrl(string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return false;

        try
        {
            var uri = new Uri(remoteUrl);
            return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || 
                   uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sanitizes endpoint name for safe storage
    /// </summary>
    /// <param name="endpoint">The endpoint to sanitize</param>
    /// <returns>Sanitized endpoint name</returns>
    public static string SanitizeEndpoint(string endpoint)
    {
        return endpoint?.Trim().ToLowerInvariant() ?? "dolthub.com";
    }

    /// <summary>
    /// Extracts host from SQL remote URL for credential storage
    /// </summary>
    /// <param name="remoteUrl">The remote URL</param>
    /// <returns>Host portion of the URL</returns>
    public static string ExtractHostFromRemoteUrl(string remoteUrl)
    {
        try
        {
            var uri = new Uri(remoteUrl);
            return uri.Host;
        }
        catch
        {
            return remoteUrl;
        }
    }
}