using DMMS.AuthHelper.Models;
using AdysTech.CredentialManager;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DMMS.AuthHelper.Services;

/// <summary>
/// Windows Credential Manager helper for DoltHub credentials
/// </summary>
public static class WindowsCredentialHelper
{
    /// <summary>
    /// Default credential key format for DoltHub credentials
    /// </summary>
    public const string DefaultCredentialKeyFormat = "DMMS-DoltHub-{0}";

    /// <summary>
    /// Stores DoltHub credentials in Windows Credential Manager
    /// </summary>
    /// <param name="credentials">The credentials to store</param>
    /// <param name="credentialKey">Optional custom credential key (if null, uses default format)</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>Result of the storage operation</returns>
    public static CredentialResult StoreDoltHubCredentials(DoltHubCredentials credentials, string? credentialKey = null, ILogger? logger = null)
    {
        try
        {
            var key = credentialKey ?? string.Format(DefaultCredentialKeyFormat, SanitizeEndpoint(credentials.Endpoint));
            var credentialJson = JsonSerializer.Serialize(credentials);

            CredentialManager.SaveCredentials(key, new System.Net.NetworkCredential(credentials.Username, credentialJson));
            
            logger?.LogInformation("Successfully stored DoltHub credentials for endpoint: {Endpoint} with key: {Key}", credentials.Endpoint, key);
            return CredentialResult.Success();
        }
        catch (Exception ex)
        {
            var errorMsg = $"Exception storing credentials: {ex.Message}";
            logger?.LogError(ex, "{ErrorMessage}", errorMsg);
            return CredentialResult.Failure(errorMsg);
        }
    }

    /// <summary>
    /// Retrieves DoltHub credentials from Windows Credential Manager
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key (if null, uses default format)</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>The credentials if found, null otherwise</returns>
    public static DoltHubCredentials? GetDoltHubCredentials(string endpoint, string? credentialKey = null, ILogger? logger = null)
    {
        try
        {
            var key = credentialKey ?? string.Format(DefaultCredentialKeyFormat, SanitizeEndpoint(endpoint));
            var credential = CredentialManager.GetCredentials(key);

            if (credential?.Password == null)
            {
                logger?.LogDebug("No credentials found for key: {Key}", key);
                return null;
            }

            var credentialData = JsonSerializer.Deserialize<DoltHubCredentials>(credential.Password);
            logger?.LogDebug("Successfully retrieved credentials for endpoint: {Endpoint} with key: {Key}", endpoint, key);
            return credentialData;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error retrieving credentials for endpoint: {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Removes DoltHub credentials from Windows Credential Manager
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key (if null, uses default format)</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>Result of the removal operation</returns>
    public static CredentialResult RemoveDoltHubCredentials(string endpoint, string? credentialKey = null, ILogger? logger = null)
    {
        try
        {
            var key = credentialKey ?? string.Format(DefaultCredentialKeyFormat, SanitizeEndpoint(endpoint));
            CredentialManager.RemoveCredentials(key);

            logger?.LogInformation("Successfully removed credentials for endpoint: {Endpoint} with key: {Key}", endpoint, key);
            return CredentialResult.Success();
        }
        catch (Exception ex)
        {
            var errorMsg = $"Exception removing credentials for endpoint {endpoint}: {ex.Message}";
            logger?.LogError(ex, "{ErrorMessage}", errorMsg);
            return CredentialResult.Failure(errorMsg);
        }
    }

    /// <summary>
    /// Sanitizes endpoint URL for use in credential keys
    /// </summary>
    /// <param name="endpoint">The endpoint to sanitize</param>
    /// <returns>Sanitized endpoint string</returns>
    public static string SanitizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "dolthub.com";

        // Remove protocol prefixes
        endpoint = endpoint.Replace("https://", "").Replace("http://", "");
        
        // Remove trailing slashes
        endpoint = endpoint.TrimEnd('/');
        
        // Replace invalid characters for credential keys
        endpoint = endpoint.Replace(":", "-").Replace("/", "-");
        
        return endpoint.ToLowerInvariant();
    }

    /// <summary>
    /// Validates if endpoint is a valid DoltHub endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidDoltHubEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        // Basic validation - should contain dolthub or be a valid domain
        var sanitized = SanitizeEndpoint(endpoint);
        return sanitized.Contains("dolthub") || Uri.CheckHostName(sanitized) != UriHostNameType.Unknown;
    }
}