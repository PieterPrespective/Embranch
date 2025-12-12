using DMMS.Models;
using DMMS.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DMMS.Tools;

/// <summary>
/// MCP tool for DoltHub authentication and credential management
/// </summary>
[McpServerToolType]
public class DoltHubAuthenticateTool
{
    private readonly DoltCredentialService _credentialService;
    private readonly ILogger<DoltHubAuthenticateTool> _logger;

    /// <summary>
    /// Initializes a new instance of the DoltHub Authentication Tool
    /// </summary>
    /// <param name="credentialService">The credential service</param>
    /// <param name="logger">Logger for tool operations</param>
    public DoltHubAuthenticateTool(DoltCredentialService credentialService, ILogger<DoltHubAuthenticateTool> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates with DoltHub and stores credentials securely
    /// </summary>
    [McpServerTool]
    [Description("Check DoltHub authentication status and provide secure authentication instructions if needed.")]
    public virtual async Task<object> AuthenticateDoltHub(string endpoint = "dolthub.com", string? credentialKey = null, bool forceReauth = false)
    {
        try
        {
            // Validate endpoint
            if (!DoltCredentialServiceUtility.IsValidDoltHubEndpoint(endpoint))
            {
                return new
                {
                    success = false,
                    error = $"Invalid DoltHub endpoint: {endpoint}"
                };
            }

            endpoint = DoltCredentialServiceUtility.SanitizeEndpoint(endpoint);
            
            _logger.LogInformation("Processing DoltHub authentication request for endpoint: {Endpoint}", endpoint);

            // Check if we need to force re-authentication
            if (forceReauth)
            {
                await _credentialService.ForgetDoltHubCredentialsAsync(endpoint, credentialKey);
                _logger.LogInformation("Forced credential removal for re-authentication on endpoint: {Endpoint}", endpoint);
            }

            // Check for existing credentials WITHOUT prompting
            var credentials = await _credentialService.GetOrPromptDoltHubCredentialsAsync(endpoint, promptForAuth: false, credentialKey: credentialKey);
            
            if (credentials == null)
            {
                // Credentials are missing - return secure authentication instructions
                var authHelperCommand = credentialKey != null 
                    ? $"DMMS.AuthHelper.exe setup --endpoint {endpoint} --credential-key \"{credentialKey}\""
                    : $"DMMS.AuthHelper.exe setup --endpoint {endpoint}";
                
                return new
                {
                    success = false,
                    error = "DoltHub authentication required",
                    endpoint = endpoint,
                    has_credentials = false,
                    action_required = new
                    {
                        type = "external_auth",
                        instructions = authHelperCommand,
                        description = "Run the secure authentication helper to configure DoltHub credentials",
                        security_note = "This opens a secure browser window for authentication - credentials never enter the LLM conversation"
                    }
                };
            }

            return new
            {
                success = true,
                message = $"DoltHub authentication verified for endpoint: {endpoint}",
                endpoint = endpoint,
                username = credentials.Value.Username,
                has_credentials = true,
                credential_key_used = credentialKey ?? "default",
                last_checked = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute DoltHub authentication");
            return new
            {
                success = false,
                error = $"Authentication failed: {ex.Message}"
            };
        }
    }
}