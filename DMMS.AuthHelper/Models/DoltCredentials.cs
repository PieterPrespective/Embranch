namespace DMMS.AuthHelper.Models;

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
/// Result of credential operations
/// </summary>
public struct CredentialResult
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <returns>Successful result</returns>
    public static CredentialResult Success() => new() { IsSuccess = true };

    /// <summary>
    /// Creates a failed result with error message
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <returns>Failed result</returns>
    public static CredentialResult Failure(string errorMessage) => new() { IsSuccess = false, ErrorMessage = errorMessage };
}