using AdysTech.CredentialManager;
using DMMS.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DMMS.Services;

/// <summary>
/// Windows Credential Manager implementation for secure DoltHub and SQL credential storage
/// </summary>
public class WindowsCredentialProvider : IDoltCredentialProvider
{
    private const string DoltHubTargetPrefix = "DMMS-DoltHub";
    private const string SqlTargetPrefix = "DMMS-DoltSql";
    private readonly ILogger<WindowsCredentialProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the Windows Credential Provider
    /// </summary>
    /// <param name="logger">Logger for credential operations</param>
    public WindowsCredentialProvider(ILogger<WindowsCredentialProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CredentialResult> StoreDoltHubCredentialsAsync(DoltHubCredentials credentials, string? credentialKey = null)
    {
        try
        {
            var targetName = GetDoltHubTargetName(credentials.Endpoint, credentialKey);
            _logger.LogDebug("Storing DoltHub credentials with target name: {TargetName}", targetName);
            
            var credData = JsonSerializer.Serialize(credentials);
            var networkCred = new NetworkCredential(credentials.Username, credData);
            
            CredentialManager.SaveCredentials(targetName, networkCred);
            
            _logger.LogInformation("Successfully stored DoltHub credentials for endpoint: {Endpoint}, target: {TargetName}", credentials.Endpoint, targetName);
            return Task.FromResult(CredentialResult.Success());
        }
        catch (Exception ex)
        {
            var targetName = GetDoltHubTargetName(credentials.Endpoint);
            _logger.LogError(ex, "Failed to store DoltHub credentials for endpoint: {Endpoint}, target: {TargetName}", credentials.Endpoint, targetName);
            return Task.FromResult(CredentialResult.Failure($"Failed to store credentials: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<DoltHubCredentials?> GetDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null)
    {
        try
        {
            var targetName = GetDoltHubTargetName(endpoint, credentialKey);
            var networkCred = CredentialManager.GetCredentials(targetName);
            
            if (networkCred?.Password == null)
            {
                _logger.LogDebug("No DoltHub credentials found for endpoint: {Endpoint}", endpoint);
                return Task.FromResult<DoltHubCredentials?>(null);
            }

            var credentials = JsonSerializer.Deserialize<DoltHubCredentials>(networkCred.Password);
            _logger.LogDebug("Successfully retrieved DoltHub credentials for endpoint: {Endpoint}", endpoint);
            return Task.FromResult<DoltHubCredentials?>(credentials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve DoltHub credentials for endpoint: {Endpoint}", endpoint);
            return Task.FromResult<DoltHubCredentials?>(null);
        }
    }

    /// <inheritdoc />
    public Task<CredentialResult> StoreSqlCredentialsAsync(DoltSqlCredentials credentials)
    {
        try
        {
            var targetName = GetSqlTargetName(credentials.RemoteUrl);
            var credData = JsonSerializer.Serialize(credentials);
            var networkCred = new NetworkCredential(credentials.Username, credData);
            
            CredentialManager.SaveCredentials(targetName, networkCred);
            
            _logger.LogInformation("Successfully stored SQL credentials for remote: {RemoteUrl}", credentials.RemoteUrl);
            return Task.FromResult(CredentialResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store SQL credentials for remote: {RemoteUrl}", credentials.RemoteUrl);
            return Task.FromResult(CredentialResult.Failure($"Failed to store credentials: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<DoltSqlCredentials?> GetSqlCredentialsAsync(string remoteUrl)
    {
        try
        {
            var targetName = GetSqlTargetName(remoteUrl);
            var networkCred = CredentialManager.GetCredentials(targetName);
            
            if (networkCred?.Password == null)
            {
                _logger.LogDebug("No SQL credentials found for remote: {RemoteUrl}", remoteUrl);
                return Task.FromResult<DoltSqlCredentials?>(null);
            }

            var credentials = JsonSerializer.Deserialize<DoltSqlCredentials>(networkCred.Password);
            _logger.LogDebug("Successfully retrieved SQL credentials for remote: {RemoteUrl}", remoteUrl);
            return Task.FromResult<DoltSqlCredentials?>(credentials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SQL credentials for remote: {RemoteUrl}", remoteUrl);
            return Task.FromResult<DoltSqlCredentials?>(null);
        }
    }

    /// <inheritdoc />
    public Task<CredentialResult> ForgetDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null)
    {
        try
        {
            var targetName = GetDoltHubTargetName(endpoint, credentialKey);
            _logger.LogDebug("Attempting to remove credentials for target: {TargetName}", targetName);
            
            // First check if credential exists
            var existingCred = CredentialManager.GetCredentials(targetName);
            if (existingCred == null)
            {
                _logger.LogWarning("No credentials found for target: {TargetName}", targetName);
                return Task.FromResult(CredentialResult.Success()); // Consider it successful if already gone
            }
            
            _logger.LogDebug("Found existing credential, attempting removal for target: {TargetName}", targetName);
            CredentialManager.RemoveCredentials(targetName);
            
            _logger.LogInformation("Successfully removed DoltHub credentials for endpoint: {Endpoint}", endpoint);
            return Task.FromResult(CredentialResult.Success());
        }
        catch (Exception ex)
        {
            var targetName = GetDoltHubTargetName(endpoint);
            _logger.LogError(ex, "Failed to remove DoltHub credentials for endpoint: {Endpoint}, target: {TargetName}", endpoint, targetName);
            
            // Try alternative removal methods or return partial success
            try
            {
                // Attempt with different credential type
                CredentialManager.RemoveCredentials(targetName, AdysTech.CredentialManager.CredentialType.Generic);
                _logger.LogInformation("Successfully removed credentials using Generic type for endpoint: {Endpoint}", endpoint);
                return Task.FromResult(CredentialResult.Success());
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Alternative removal method also failed for endpoint: {Endpoint}", endpoint);
                return Task.FromResult(CredentialResult.Failure($"Failed to remove credentials: {ex.Message}"));
            }
        }
    }

    /// <inheritdoc />
    public Task<CredentialResult> ForgetSqlCredentialsAsync(string remoteUrl)
    {
        try
        {
            var targetName = GetSqlTargetName(remoteUrl);
            CredentialManager.RemoveCredentials(targetName);
            
            _logger.LogInformation("Successfully removed SQL credentials for remote: {RemoteUrl}", remoteUrl);
            return Task.FromResult(CredentialResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove SQL credentials for remote: {RemoteUrl}", remoteUrl);
            return Task.FromResult(CredentialResult.Failure($"Failed to remove credentials: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<CredentialResult> ForgetAllCredentialsAsync()
    {
        try
        {
            // Note: AdysTech.CredentialManager doesn't provide enumeration capability
            // This method serves as a best-effort cleanup for known credential patterns
            // In a production environment, we would maintain a registry of created credentials
            
            var errors = new List<string>();
            
            // Try to remove common endpoint variations
            var commonEndpoints = new[] { "dolthub.com", "test.dolthub.com", "localhost" };
            foreach (var endpoint in commonEndpoints)
            {
                try
                {
                    var targetName = GetDoltHubTargetName(endpoint);
                    CredentialManager.RemoveCredentials(targetName);
                }
                catch
                {
                    // Ignore errors for credentials that don't exist
                }
            }
            
            _logger.LogInformation("Completed best-effort removal of DMMS credentials");
            _logger.LogWarning("ForgetAllCredentials provides best-effort cleanup only due to credential manager limitations");
            return Task.FromResult(CredentialResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove credentials");
            return Task.FromResult(CredentialResult.Failure($"Failed to remove credentials: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<bool> HasDoltHubCredentialsAsync(string endpoint = "dolthub.com", string? credentialKey = null)
    {
        try
        {
            var targetName = GetDoltHubTargetName(endpoint, credentialKey);
            var networkCred = CredentialManager.GetCredentials(targetName);
            return Task.FromResult(networkCred != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> HasSqlCredentialsAsync(string remoteUrl)
    {
        try
        {
            var targetName = GetSqlTargetName(remoteUrl);
            var networkCred = CredentialManager.GetCredentials(targetName);
            return Task.FromResult(networkCred != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Generates the Windows Credential Manager target name for DoltHub credentials
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential key</param>
    /// <returns>Target name for credential storage</returns>
    private static string GetDoltHubTargetName(string endpoint, string? credentialKey = null)
    {
        if (!string.IsNullOrWhiteSpace(credentialKey))
        {
            // Use custom credential key format
            return credentialKey;
        }
        
        // Use default format
        return $"{DoltHubTargetPrefix}:{endpoint}";
    }

    /// <summary>
    /// Generates the Windows Credential Manager target name for SQL credentials
    /// </summary>
    /// <param name="remoteUrl">The SQL remote URL</param>
    /// <returns>Target name for credential storage</returns>
    private static string GetSqlTargetName(string remoteUrl) => $"{SqlTargetPrefix}:{remoteUrl}";

    /// <summary>
    /// Gets all DoltHub credential target names from Windows Credential Manager
    /// </summary>
    /// <returns>List of DoltHub credential target names</returns>
    private static List<string> GetAllDoltHubTargets()
    {
        return GetTargetsWithPrefix(DoltHubTargetPrefix);
    }

    /// <summary>
    /// Gets all SQL credential target names from Windows Credential Manager
    /// </summary>
    /// <returns>List of SQL credential target names</returns>
    private static List<string> GetAllSqlTargets()
    {
        return GetTargetsWithPrefix(SqlTargetPrefix);
    }

    /// <summary>
    /// Gets all credential targets that start with the specified prefix
    /// </summary>
    /// <param name="prefix">The target prefix to filter by</param>
    /// <returns>List of matching target names</returns>
    private static List<string> GetTargetsWithPrefix(string prefix)
    {
        var targets = new List<string>();
        try
        {
            // Note: AdysTech.CredentialManager doesn't provide an enumerate method
            // For now, we'll return an empty list and handle cleanup on a best-effort basis
            // In a production environment, we could maintain our own registry of created targets
        }
        catch
        {
            // Return empty list in case of any errors
        }
        return targets;
    }
}