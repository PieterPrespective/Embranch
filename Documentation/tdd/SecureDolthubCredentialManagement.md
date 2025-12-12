# Secure credential management for DoltHub in .NET MCP servers

**Dolt uses a unique JWK-based authentication system that differs from Git**, requiring a tailored credential management strategy. For a small team on Windows with future cross-platform needs, the optimal approach combines **Devlooped.CredentialManager** for cross-platform OS keychain abstraction with DoltHub's native credential system. This provides secure encrypted storage, minimal setup complexity, and a clear migration path when team requirements evolve.

The most critical finding is that Dolt does **not** support Git credential helpers—it uses its own JWK keypair system for DoltHub authentication and environment variables for SQL-server remotes. This fundamentally shapes the credential management architecture.

## DoltHub authentication uses JWK keypairs, not Git credentials

DoltHub's authentication model differs significantly from Git's approach. Understanding these mechanisms is essential before implementing any credential storage:

**Primary authentication methods:**
- **JWK keypairs** (DoltHub/DoltLab): Public/private key pairs in JSON Web Key format, managed via `dolt creds` commands
- **API tokens**: REST API access tokens created at dolthub.com/settings/tokens
- **SQL username/password**: For remotesapi SQL-server remotes via `DOLT_REMOTE_PASSWORD` environment variable

DoltHub stores credentials in `~/.dolt/creds/` as `.jwk` files. On Windows, this translates to `%USERPROFILE%\.dolt\creds\`. The `dolt login` command creates a keypair, opens a browser to register the public key with DoltHub, and stores the private key locally.

```bash
# DoltHub credential workflow
dolt login                    # Creates JWK keypair, opens browser for registration
dolt creds ls                 # List available credentials
dolt creds use <key-id>       # Select active credential
dolt clone dolthub/repo       # Uses selected credential automatically
```

For SQL-server remotes (including Hosted Dolt), authentication uses MySQL-style credentials:

```bash
# SQL-server remote authentication
export DOLT_REMOTE_PASSWORD=yourpassword
dolt clone https://host.com/database --user username
```

The **DOLT_ROOT_PATH** environment variable can override the default credential directory, which is useful for containerized deployments or custom credential isolation.

## Windows-native storage delivers the strongest security baseline

For Windows-primary deployment, two native mechanisms provide robust credential protection without external dependencies:

**Windows Credential Manager** integrates with the OS credential vault, providing user-visible management through Control Panel and enterprise roaming support. The `AdysTech.CredentialManager` NuGet package (v2.6.0) offers a clean .NET Standard 2.0 API:

```csharp
using AdysTech.CredentialManager;
using System.Net;

public class DoltCredentialStore
{
    private const string TargetPrefix = "DoltHub:";
    
    public void SaveCredential(string remoteName, string username, string secret)
    {
        var cred = new NetworkCredential(username, secret);
        CredentialManager.SaveCredentials($"{TargetPrefix}{remoteName}", cred);
    }
    
    public NetworkCredential? GetCredential(string remoteName)
    {
        return CredentialManager.GetCredentials($"{TargetPrefix}{remoteName}");
    }
    
    public void RemoveCredential(string remoteName)
    {
        CredentialManager.RemoveCredentials($"{TargetPrefix}{remoteName}");
    }
}
```

**DPAPI (Data Protection API)** encrypts data using keys derived from the Windows user account, eliminating manual key management. For .NET 9.0, use `System.Security.Cryptography.ProtectedData` version 9.0.0:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class DpapiTokenStorage
{
    private readonly string _filePath;
    private readonly byte[] _entropy;
    
    public DpapiTokenStorage(string appName)
    {
        _entropy = Encoding.UTF8.GetBytes($"{appName}-entropy-v1");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, appName);
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "tokens.dat");
    }
    
    public void SaveToken(string name, string value)
    {
        var tokens = LoadTokens();
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            _entropy,
            DataProtectionScope.CurrentUser  // Only current user can decrypt
        );
        tokens[name] = Convert.ToBase64String(encrypted);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(tokens));
    }
    
    public string? GetToken(string name)
    {
        var tokens = LoadTokens();
        if (!tokens.TryGetValue(name, out var encrypted)) return null;
        
        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(encrypted),
                _entropy,
                DataProtectionScope.CurrentUser
            );
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return null; // Encrypted by different user
        }
    }
    
    private Dictionary<string, string> LoadTokens()
    {
        if (!File.Exists(_filePath)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(_filePath)) ?? new();
    }
}
```

Both approaches tie encryption to the Windows user profile—data encrypted by one user cannot be decrypted by another, providing strong isolation for per-developer credential storage.

## Cross-platform credential storage through OS keychain abstraction

For future Linux/macOS support, **Devlooped.CredentialManager** provides the best balance of simplicity and cross-platform coverage. Built on Git Credential Manager's battle-tested codebase, it abstracts platform-specific keystores behind a unified API:

| Platform | Backend Storage |
|----------|----------------|
| Windows | Windows Credential Manager |
| macOS | Login Keychain |
| Linux | libsecret (GNOME Keyring), GPG, or credential cache |

```csharp
using GitCredentialManager;

public class CrossPlatformDoltCredentials : ICredentialService
{
    private readonly ICredentialStore _store;
    
    public CrossPlatformDoltCredentials(string appNamespace = "dolthub.mcp-server")
    {
        _store = CredentialManager.Create(appNamespace);
    }
    
    public void StoreApiToken(string endpoint, string token)
    {
        _store.AddOrUpdate(endpoint, "api-token", token);
    }
    
    public void StoreSqlCredential(string remoteUrl, string username, string password)
    {
        _store.AddOrUpdate(remoteUrl, username, password);
    }
    
    public (string? username, string? password) GetSqlCredential(string remoteUrl)
    {
        // Try to find credential for this remote
        var cred = _store.Get(remoteUrl, null);
        return (cred?.Account, cred?.Password);
    }
    
    public string? GetApiToken(string endpoint)
    {
        var cred = _store.Get(endpoint, "api-token");
        return cred?.Password;
    }
}
```

Install via NuGet:
```xml
<PackageReference Include="Devlooped.CredentialManager" Version="2.6.1.1" />
```

For teams that will remain Windows-only, **Meziantou.Framework.Win32.CredentialManager** offers a simpler alternative with fewer dependencies:

```csharp
using Meziantou.Framework.Win32;

// Store credential
CredentialManager.WriteCredential(
    applicationName: "DoltHub-MCP",
    userName: "api-token",
    secret: "your-token-here",
    persistence: CredentialPersistence.LocalMachine);

// Retrieve credential
var cred = CredentialManager.ReadCredential("DoltHub-MCP");
string token = cred?.Password;
```

## Environment variables require careful handling to avoid exposure

Environment variables work well for CI/CD and containerized deployments but introduce security risks on developer workstations. Credentials set via environment variables are visible to process inspection tools and may leak into shell history or logs.

**Secure environment variable practices:**

```bash
# AVOID: Credentials in shell history
export DOLT_REMOTE_PASSWORD=secret123

# BETTER: Prefix with space to exclude from bash history
 export DOLT_REMOTE_PASSWORD=secret123

# Configure bash to ignore sensitive commands
HISTIGNORE="*DOLT_REMOTE*:export *PASSWORD*:export *TOKEN*"
```

**Reading environment variables securely in C#:**

```csharp
public class DoltEnvironmentCredentials
{
    public string? GetRemotePassword()
    {
        return Environment.GetEnvironmentVariable("DOLT_REMOTE_PASSWORD");
    }
    
    public void SetForChildProcess(ProcessStartInfo startInfo, string password)
    {
        // Set only for child process, not current environment
        startInfo.EnvironmentVariables["DOLT_REMOTE_PASSWORD"] = password;
    }
    
    // Hierarchical credential resolution
    public string? ResolveCredential(string credentialName)
    {
        // 1. Environment variable (highest priority for CI/CD)
        var envValue = Environment.GetEnvironmentVariable(
            $"DOLT_{credentialName.ToUpperInvariant()}");
        if (!string.IsNullOrEmpty(envValue)) return envValue;
        
        // 2. Fall back to secure storage
        return _secureStorage.GetToken(credentialName);
    }
}
```

**Never pass credentials via command-line arguments**—they're visible in process lists. Dolt correctly uses environment variables (`DOLT_REMOTE_PASSWORD`) rather than CLI flags for password transmission.

## Development-time secrets through .NET User Secrets

For local development, .NET's built-in User Secrets mechanism stores configuration outside the project directory, preventing accidental commits. Note that secrets are stored as **plaintext JSON** in the user profile—this provides source control protection, not encryption at rest.

```bash
# Initialize in project
dotnet user-secrets init

# Set DoltHub credentials
dotnet user-secrets set "DoltHub:ApiToken" "your-api-token"
dotnet user-secrets set "DoltHub:RemotePassword" "your-password"
```

```csharp
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>(optional: true)  // Development only
    .AddEnvironmentVariables()
    .Build();

// Access through IConfiguration
string? apiToken = configuration["DoltHub:ApiToken"];
```

The configuration system's layering allows environment variables to override user secrets, enabling smooth transitions from development to production without code changes.

## Bridging Dolt's credential system with application storage

Since Dolt manages its own credentials in `~/.dolt/creds/`, your MCP server needs a strategy for integrating with this system. Two approaches work well:

**Option 1: Delegate to Dolt's native credential system**

Let Dolt handle DoltHub authentication natively, and manage only SQL-server remote credentials in your application:

```csharp
public class DoltCredentialBridge
{
    private readonly ICredentialService _secureStorage;
    
    public DoltCredentialBridge(ICredentialService secureStorage)
    {
        _secureStorage = secureStorage;
    }
    
    public ProcessStartInfo PrepareDoltCommand(string arguments, string? remoteUrl = null)
    {
        var startInfo = new ProcessStartInfo("dolt", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        // For SQL-server remotes, inject password from secure storage
        if (remoteUrl != null && !remoteUrl.Contains("dolthub.com"))
        {
            var password = _secureStorage.GetToken($"dolt-remote:{remoteUrl}");
            if (password != null)
            {
                startInfo.EnvironmentVariables["DOLT_REMOTE_PASSWORD"] = password;
            }
        }
        
        return startInfo;
    }
    
    public void EnsureDoltHubAuthenticated()
    {
        // Check if dolt has valid credentials
        var checkProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "dolt",
            Arguments = "creds check",
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        
        checkProcess?.WaitForExit();
        if (checkProcess?.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "DoltHub credentials not configured. Run 'dolt login' first.");
        }
    }
}
```

**Option 2: Programmatic DoltHub API access**

For operations that don't require the full Dolt CLI, use DoltHub's REST API directly with stored tokens:

```csharp
public class DoltHubApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialService _credentials;
    
    public DoltHubApiClient(ICredentialService credentials)
    {
        _credentials = credentials;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.dolthub.com/api/v1alpha1/")
        };
    }
    
    public async Task<string> QueryAsync(string owner, string repo, string branch, string sql)
    {
        var token = _credentials.GetToken("dolthub-api") 
            ?? throw new InvalidOperationException("DoltHub API token not configured");
        
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("token", token);
        
        var response = await _httpClient.GetAsync(
            $"{owner}/{repo}/{branch}?q={Uri.EscapeDataString(sql)}");
        
        return await response.Content.ReadAsStringAsync();
    }
}
```

## Team credential strategies scale from shared accounts to per-user access

For a team of **1-5 developers initially sharing credentials**, start simple and add complexity only as needed:

**Phase 1 (Current): Shared service account**
- Single DoltHub organization credential
- Each developer runs `dolt login` with the same account
- SQL-server remotes use shared service account password
- Store shared password in team password manager (1Password, Bitwarden)

**Phase 2 (Growth): Per-user credentials**
- Each developer creates individual DoltHub account
- Add developers as collaborators to repositories
- Personal JWK credentials provide audit trail
- Rotate shared credentials to per-user

**Credential rotation checklist:**
1. Generate new credential/token
2. Store in secure location (credential manager or secret store)
3. Update all services using the credential
4. Verify functionality with new credential
5. Revoke old credential
6. Document rotation date and next rotation due

**Centralized secret managers** (HashiCorp Vault, Azure Key Vault) are typically **overkill for teams under 5 developers** unless you have regulatory requirements. Simpler alternatives that provide adequate security:

| Solution | Best For | Complexity |
|----------|----------|------------|
| dotnet user-secrets | Development only | Very Low |
| OS Credential Manager | Small teams, single platform | Low |
| Devlooped.CredentialManager | Small teams, cross-platform | Low |
| SOPS (encrypted git files) | Team secrets in version control | Medium |
| Azure Key Vault | Azure-native teams 5+ developers | Medium |

## Comprehensive security checklist for credential handling

**Source control protection:**
```gitignore
# .gitignore for Dolt MCP server
.env
.env.*
appsettings.Development.json
appsettings.Local.json
secrets.json
*.jwk
.dolt/creds/
```

**Process-level security:**
- Never pass credentials as command-line arguments
- Use environment variables or stdin for password transmission
- Clear sensitive variables from memory after use

**File permission hardening (for credential files):**
```powershell
# Windows: Restrict to current user only
$path = ".\credentials.json"
$acl = Get-Acl $path
$acl.SetAccessRuleProtection($true, $false)
$rule = New-Object Security.AccessControl.FileSystemAccessRule(
    [Security.Principal.WindowsIdentity]::GetCurrent().Name,
    "FullControl", "Allow")
$acl.SetAccessRule($rule)
Set-Acl $path $acl
```

```bash
# Linux/macOS
chmod 600 credentials.json
chmod 700 ~/.dolt/creds/
```

## Ranked recommendations by security and implementation effort

### By security level (strongest to weakest):

1. **Devlooped.CredentialManager + OS keychain** — Encrypted at rest, per-user isolation, cross-platform
2. **DPAPI-encrypted local files** — Strong Windows-native encryption, user-bound keys
3. **Windows Credential Manager direct** — OS-managed vault, enterprise roaming support
4. **dotnet user-secrets** — Outside source control, but plaintext on disk
5. **Environment variables** — No encryption, visible to process inspection

### By implementation ease (simplest to most complex):

1. **Environment variables** — No code required, works everywhere
2. **dotnet user-secrets** — Built into .NET SDK, one command to initialize
3. **Meziantou.Framework.Win32.CredentialManager** — Simple API, Windows-only
4. **DPAPI with ProtectedData** — Few lines of code, Windows-only
5. **Devlooped.CredentialManager** — Cross-platform abstraction layer

### Recommended architecture for your scenario:

```csharp
public interface IDoltCredentialProvider
{
    string? GetDoltHubApiToken();
    string? GetRemotePassword(string remoteUrl);
    void StoreCredential(string key, string value);
}

// Implementation selection based on platform
public static IDoltCredentialProvider CreateProvider()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Use Windows Credential Manager for immediate deployment
        return new WindowsCredentialProvider();
    }
    // Future: Cross-platform support
    return new CrossPlatformCredentialProvider();
}
```

Start with **Windows Credential Manager** (via `AdysTech.CredentialManager`) for immediate deployment, design the abstraction layer now, and swap in **Devlooped.CredentialManager** when cross-platform support becomes necessary. This provides strong security, minimal complexity, and a clear upgrade path.

## Conclusion

The key insight for DoltHub credential management is that **Dolt does not integrate with Git credential helpers**—it maintains its own JWK-based system. For an MCP server, this means treating DoltHub credentials separately from SQL-server remote credentials. Use Dolt's native `dolt login` flow for DoltHub authentication, and manage SQL-server passwords through your application's secure credential storage.

For a small Windows-first team, the practical path forward is: Windows Credential Manager for production credential storage, .NET User Secrets for development configuration, and environment variables for CI/CD pipelines. This combination provides encrypted storage, prevents source control exposure, and requires minimal infrastructure investment. As the team grows or cross-platform needs emerge, the abstraction layer enables migration to centralized secret management without architectural changes.