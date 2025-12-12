using DMMS.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DMMS.Tools;

/// <summary>
/// MCP tool for forgetting stored DoltHub and SQL credentials
/// </summary>
[McpServerToolType]
public class DoltCredentialForgetTool
{
    private readonly DoltCredentialService _credentialService;
    private readonly ILogger<DoltCredentialForgetTool> _logger;

    /// <summary>
    /// Initializes a new instance of the Dolt Credential Forget Tool
    /// </summary>
    /// <param name="credentialService">The credential service</param>
    /// <param name="logger">Logger for tool operations</param>
    public DoltCredentialForgetTool(DoltCredentialService credentialService, ILogger<DoltCredentialForgetTool> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    /// <summary>
    /// Forgets stored DoltHub or SQL credentials
    /// </summary>
    [McpServerTool]
    [Description("Forget stored DoltHub or SQL credentials. Can remove specific credentials or all stored credentials.")]
    public virtual async Task<object> ForgetCredentials(string type = "all", string? endpoint = null, string? remoteUrl = null, string? credentialKey = null)
    {
        try
        {
            type = type?.ToLowerInvariant() ?? "all";
            
            _logger.LogInformation("Processing credential forget request for type: {Type}", type);

            switch (type)
            {
                case "dolthub":
                    return await ForgetDoltHubCredentialsAsync(endpoint, credentialKey);
                
                case "sql":
                    return await ForgetSqlCredentialsAsync(remoteUrl);
                
                case "all":
                    return await ForgetAllCredentialsAsync();
                
                default:
                    return new
                    {
                        success = false,
                        error = $"Invalid credential type: {type}. Must be 'dolthub', 'sql', or 'all'."
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute credential forget operation");
            return new
            {
                success = false,
                error = $"Forget operation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Forgets DoltHub credentials for a specific endpoint
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="credentialKey">Optional custom credential storage key</param>
    /// <returns>Tool execution result</returns>
    private async Task<object> ForgetDoltHubCredentialsAsync(string? endpoint, string? credentialKey = null)
    {
        endpoint = endpoint ?? "dolthub.com";
        
        if (!DoltCredentialServiceUtility.IsValidDoltHubEndpoint(endpoint))
        {
            return new
            {
                success = false,
                error = $"Invalid DoltHub endpoint: {endpoint}"
            };
        }

        endpoint = DoltCredentialServiceUtility.SanitizeEndpoint(endpoint);
        
        var result = await _credentialService.ForgetDoltHubCredentialsAsync(endpoint, credentialKey);
        
        if (result.IsSuccess)
        {
            return new
            {
                success = true,
                message = $"Successfully forgot DoltHub credentials for endpoint: {endpoint}",
                type = "dolthub",
                endpoint = endpoint
            };
        }
        
        return new
        {
            success = false,
            error = $"Failed to forget DoltHub credentials: {result.ErrorMessage}"
        };
    }

    /// <summary>
    /// Forgets SQL credentials for a specific remote URL
    /// </summary>
    /// <param name="remoteUrl">The SQL remote URL</param>
    /// <returns>Tool execution result</returns>
    private async Task<object> ForgetSqlCredentialsAsync(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return new
            {
                success = false,
                error = "Remote URL is required for forgetting SQL credentials"
            };
        }

        if (!DoltCredentialServiceUtility.IsValidSqlRemoteUrl(remoteUrl))
        {
            return new
            {
                success = false,
                error = $"Invalid SQL remote URL: {remoteUrl}"
            };
        }

        var result = await _credentialService.ForgetSqlCredentialsAsync(remoteUrl);
        
        if (result.IsSuccess)
        {
            return new
            {
                success = true,
                message = $"Successfully forgot SQL credentials for remote: {remoteUrl}",
                type = "sql",
                remote_url = remoteUrl
            };
        }
        
        return new
        {
            success = false,
            error = $"Failed to forget SQL credentials: {result.ErrorMessage}"
        };
    }

    /// <summary>
    /// Forgets all stored credentials
    /// </summary>
    /// <returns>Tool execution result</returns>
    private async Task<object> ForgetAllCredentialsAsync()
    {
        var result = await _credentialService.ForgetAllCredentialsAsync();
        
        if (result.IsSuccess)
        {
            return new
            {
                success = true,
                message = "Successfully forgot all stored credentials",
                type = "all"
            };
        }
        
        return new
        {
            success = false,
            error = $"Failed to forget all credentials: {result.ErrorMessage}"
        };
    }
}