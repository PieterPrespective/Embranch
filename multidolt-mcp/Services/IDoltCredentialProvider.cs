using DMMS.Models;

namespace DMMS.Services;

/// <summary>
/// Interface for managing DoltHub and SQL server credentials securely
/// </summary>
public interface IDoltCredentialProvider
{
    /// <summary>
    /// Stores DoltHub credentials securely
    /// </summary>
    /// <param name="credentials">The DoltHub credentials to store</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>Result of the storage operation</returns>
    Task<CredentialResult> StoreDoltHubCredentialsAsync(DoltHubCredentials credentials, string? credentialKey = null);

    /// <summary>
    /// Retrieves DoltHub credentials if they exist
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint (defaults to dolthub.com)</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>The stored credentials or null if not found</returns>
    Task<DoltHubCredentials?> GetDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null);

    /// <summary>
    /// Stores SQL server credentials securely
    /// </summary>
    /// <param name="credentials">The SQL server credentials to store</param>
    /// <returns>Result of the storage operation</returns>
    Task<CredentialResult> StoreSqlCredentialsAsync(DoltSqlCredentials credentials);

    /// <summary>
    /// Retrieves SQL server credentials for a specific remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL server remote URL</param>
    /// <returns>The stored credentials or null if not found</returns>
    Task<DoltSqlCredentials?> GetSqlCredentialsAsync(string remoteUrl);

    /// <summary>
    /// Removes all stored DoltHub credentials
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint (defaults to dolthub.com)</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>Result of the removal operation</returns>
    Task<CredentialResult> ForgetDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null);

    /// <summary>
    /// Removes SQL server credentials for a specific remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL server remote URL</param>
    /// <returns>Result of the removal operation</returns>
    Task<CredentialResult> ForgetSqlCredentialsAsync(string remoteUrl);

    /// <summary>
    /// Removes all stored credentials (both DoltHub and SQL server)
    /// </summary>
    /// <returns>Result of the removal operation</returns>
    Task<CredentialResult> ForgetAllCredentialsAsync();

    /// <summary>
    /// Checks if DoltHub credentials exist for the specified endpoint
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint (defaults to dolthub.com)</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>True if credentials exist, false otherwise</returns>
    Task<bool> HasDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null);

    /// <summary>
    /// Checks if SQL server credentials exist for the specified remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL server remote URL</param>
    /// <returns>True if credentials exist, false otherwise</returns>
    Task<bool> HasSqlCredentialsAsync(string remoteUrl);
}

/// <summary>
/// Interface for browser-based authentication flows
/// </summary>
public interface IBrowserAuthProvider
{
    /// <summary>
    /// Initiates a browser-based authentication flow for DoltHub
    /// </summary>
    /// <param name="config">Browser authentication configuration</param>
    /// <returns>The authentication result with credentials if successful</returns>
    Task<(bool Success, DoltHubCredentials? Credentials, string? ErrorMessage)> AuthenticateWithBrowserAsync(BrowserAuthConfig config);

    /// <summary>
    /// Opens the default browser to the specified URL
    /// </summary>
    /// <param name="url">The URL to open</param>
    /// <returns>True if browser was opened successfully, false otherwise</returns>
    bool OpenBrowser(string url);
}