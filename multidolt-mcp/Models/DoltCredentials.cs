namespace DMMS.Models;

/// <summary>
/// Represents DoltHub authentication credentials
/// </summary>
public struct DoltHubCredentials
{
    /// <summary>
    /// The DoltHub username
    /// </summary>
    public string Username { get; init; }

    /// <summary>
    /// The API token for DoltHub authentication
    /// </summary>
    public string ApiToken { get; init; }

    /// <summary>
    /// The endpoint URL for DoltHub (default: dolthub.com)
    /// </summary>
    public string Endpoint { get; init; }

    /// <summary>
    /// Initializes a new instance of DoltHub credentials
    /// </summary>
    /// <param name="username">The DoltHub username</param>
    /// <param name="apiToken">The API token</param>
    /// <param name="endpoint">The DoltHub endpoint (defaults to dolthub.com)</param>
    public DoltHubCredentials(string username, string apiToken, string endpoint = "dolthub.com")
    {
        Username = username;
        ApiToken = apiToken;
        Endpoint = endpoint;
    }
}

/// <summary>
/// Represents SQL server remote credentials for Dolt
/// </summary>
public struct DoltSqlCredentials
{
    /// <summary>
    /// The remote SQL server URL
    /// </summary>
    public string RemoteUrl { get; init; }

    /// <summary>
    /// The username for SQL server authentication
    /// </summary>
    public string Username { get; init; }

    /// <summary>
    /// The password for SQL server authentication
    /// </summary>
    public string Password { get; init; }

    /// <summary>
    /// Initializes a new instance of SQL server credentials
    /// </summary>
    /// <param name="remoteUrl">The SQL server remote URL</param>
    /// <param name="username">The username</param>
    /// <param name="password">The password</param>
    public DoltSqlCredentials(string remoteUrl, string username, string password)
    {
        RemoteUrl = remoteUrl;
        Username = username;
        Password = password;
    }
}

/// <summary>
/// Result of credential retrieval operations
/// </summary>
public struct CredentialResult
{
    /// <summary>
    /// Indicates if the credential retrieval was successful
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error message if the retrieval failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful credential result
    /// </summary>
    /// <returns>Successful credential result</returns>
    public static CredentialResult Success() => new() { IsSuccess = true };

    /// <summary>
    /// Creates a failed credential result with error message
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <returns>Failed credential result</returns>
    public static CredentialResult Failure(string errorMessage) => new() { IsSuccess = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Configuration for browser-based authentication
/// </summary>
public struct BrowserAuthConfig
{
    /// <summary>
    /// The DoltHub login URL to open in browser
    /// </summary>
    public string LoginUrl { get; init; }

    /// <summary>
    /// Timeout in seconds for the authentication process
    /// </summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// Whether to automatically open the browser
    /// </summary>
    public bool AutoOpenBrowser { get; init; }

    /// <summary>
    /// Initializes browser authentication configuration
    /// </summary>
    /// <param name="loginUrl">The login URL</param>
    /// <param name="timeoutSeconds">Authentication timeout in seconds</param>
    /// <param name="autoOpenBrowser">Whether to auto-open browser</param>
    public BrowserAuthConfig(string loginUrl = "https://www.dolthub.com/settings/tokens", int timeoutSeconds = 300, bool autoOpenBrowser = true)
    {
        LoginUrl = loginUrl;
        TimeoutSeconds = timeoutSeconds;
        AutoOpenBrowser = autoOpenBrowser;
    }
}