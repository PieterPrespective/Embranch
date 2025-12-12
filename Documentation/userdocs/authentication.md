# DMMS Authentication Guide

This guide explains how to securely authenticate with DoltHub using the DMMS authentication system.

## Overview

DMMS uses a secure two-part authentication system:

1. **DMMS.AuthHelper.exe** - A standalone utility for secure credential setup
2. **Enhanced MCP Tools** - Tools that check for credentials and guide you through setup

**🔒 Security Note**: Your DoltHub credentials never enter the AI conversation context. All authentication happens through secure, isolated processes.

## Quick Start

### First-Time Setup

1. **When you encounter an authentication request**, you'll see a message like:
   ```json
   {
     "success": false,
     "error": "DoltHub authentication required",
     "action_required": {
       "instructions": "DMMS.AuthHelper.exe setup --endpoint dolthub.com",
       "description": "Run the secure authentication helper"
     }
   }
   ```

2. **Run the authentication helper**:
   ```bash
   DMMS.AuthHelper.exe setup
   ```

3. **Follow the secure authentication flow**:
   - The helper will open your browser to DoltHub's token settings page
   - Sign in to your DoltHub account
   - Create or copy an API token
   - Return to the terminal and enter your credentials securely

4. **Retry your original operation** - it will now work seamlessly!

## Authentication Helper Commands

### `setup` - Configure Authentication

Sets up new DoltHub credentials with secure browser-based authentication.

```bash
# Basic setup for dolthub.com
DMMS.AuthHelper.exe setup

# Setup for custom DoltHub endpoint
DMMS.AuthHelper.exe setup --endpoint mydolthub.company.com

# Setup with custom credential storage key
DMMS.AuthHelper.exe setup --credential-key MyProject-DoltHub
```

**What happens during setup:**
1. Browser opens to `https://www.dolthub.com/settings/tokens` (or your custom endpoint)
2. You sign in and create/copy an API token
3. You enter your username and token in the secure terminal prompt
4. Credentials are encrypted and stored in Windows Credential Manager

### `status` - Check Authentication Status

Checks if credentials are configured and shows their status.

```bash
# Check default credentials
DMMS.AuthHelper.exe status

# Check specific endpoint
DMMS.AuthHelper.exe status --endpoint mydolthub.company.com

# Check custom credential key
DMMS.AuthHelper.exe status --credential-key MyProject-DoltHub
```

**Example output:**
```
=== DMMS DoltHub Credential Status ===
Endpoint: dolthub.com
Credential Key: DMMS-DoltHub-dolthub.com

✓ Credentials are configured
Username: myusername
API Token: ********************
```

### `refresh` - Update Existing Credentials

Updates existing credentials with new authentication information.

```bash
# Refresh default credentials
DMMS.AuthHelper.exe refresh

# Refresh specific endpoint
DMMS.AuthHelper.exe refresh --endpoint mydolthub.company.com

# Refresh custom credential key
DMMS.AuthHelper.exe refresh --credential-key MyProject-DoltHub
```

### `forget` - Remove Stored Credentials

Securely removes stored credentials from Windows Credential Manager.

```bash
# Remove default credentials
DMMS.AuthHelper.exe forget

# Remove specific endpoint credentials
DMMS.AuthHelper.exe forget --endpoint mydolthub.company.com

# Remove custom credential key
DMMS.AuthHelper.exe forget --credential-key MyProject-DoltHub
```

**Security Note**: This permanently removes credentials. You'll need to run `setup` again to re-authenticate.

### `help` - Show Usage Information

Displays complete usage information and examples.

```bash
DMMS.AuthHelper.exe help
```

## Advanced Configuration

### Custom Credential Keys

Use custom credential keys when you need to:
- **Isolate credentials** between different projects
- **Use multiple DoltHub accounts** for different purposes
- **Share a computer** with different DMMS configurations

```bash
# Project-specific credentials
DMMS.AuthHelper.exe setup --credential-key ProjectA-DoltHub
DMMS.AuthHelper.exe setup --credential-key ProjectB-DoltHub

# User-specific credentials
DMMS.AuthHelper.exe setup --credential-key JohnDoe-DoltHub
DMMS.AuthHelper.exe setup --credential-key JaneSmith-DoltHub
```

### Multiple Endpoints

Configure authentication for different DoltHub instances:

```bash
# Public DoltHub
DMMS.AuthHelper.exe setup --endpoint dolthub.com

# Corporate DoltHub
DMMS.AuthHelper.exe setup --endpoint dolthub.company.com

# Development DoltHub
DMMS.AuthHelper.exe setup --endpoint dev.dolthub.company.com
```

### Combining Custom Keys and Endpoints

For complex scenarios, combine both:

```bash
# Production environment credentials
DMMS.AuthHelper.exe setup --endpoint prod.dolthub.company.com --credential-key Prod-DoltHub

# Development environment credentials
DMMS.AuthHelper.exe setup --endpoint dev.dolthub.company.com --credential-key Dev-DoltHub
```

## MCP Server Integration

### Using with Claude Desktop

When using DMMS with Claude Desktop, the MCP server will automatically guide you through authentication:

1. **AI requests DoltHub operation**
2. **MCP server checks for credentials**
3. **If missing, returns authentication instructions**
4. **You run the auth helper outside Claude**
5. **Retry the operation - it now works**

### Using with Custom MCP Clients

When building custom MCP clients, handle authentication responses:

```json
// Example authentication required response
{
  "success": false,
  "error": "DoltHub authentication required",
  "action_required": {
    "type": "external_auth",
    "instructions": "DMMS.AuthHelper.exe setup --endpoint dolthub.com",
    "description": "Run the secure authentication helper",
    "security_note": "Opens secure browser window - credentials never enter LLM conversation"
  }
}
```

## Getting Your DoltHub API Token

### Step-by-Step Token Creation

1. **Visit DoltHub Token Settings**:
   - Public DoltHub: https://www.dolthub.com/settings/tokens
   - Corporate instance: `https://your-dolthub-instance.com/settings/tokens`

2. **Sign In** to your DoltHub account

3. **Create New Token**:
   - Click "Create New Token"
   - Give it a descriptive name (e.g., "DMMS Authentication")
   - Copy the generated token

4. **Use in Authentication Helper**:
   - Username: Your DoltHub username
   - API Token: The token you just copied

### Token Security Best Practices

- **Keep tokens secure** - treat them like passwords
- **Use descriptive names** for easy management
- **Rotate tokens periodically** for security
- **Delete unused tokens** to minimize exposure
- **Don't share tokens** between users or projects

## Troubleshooting

### Common Issues

#### "No credentials configured"

**Problem**: MCP tools report missing authentication.

**Solution**: Run the setup command:
```bash
DMMS.AuthHelper.exe setup
```

#### "Invalid DoltHub endpoint"

**Problem**: Endpoint URL is malformed or inaccessible.

**Solution**: Verify the endpoint format:
```bash
# Correct formats
DMMS.AuthHelper.exe setup --endpoint dolthub.com
DMMS.AuthHelper.exe setup --endpoint mydolthub.company.com
```

#### "Browser won't open automatically"

**Problem**: Auth helper can't open browser automatically.

**Solution**: Manually navigate to the URL shown in the warning message.

#### "API token doesn't work"

**Problem**: Authentication fails with valid-looking credentials.

**Solutions**:
1. Verify the token was copied correctly (no extra spaces)
2. Check if the token has expired
3. Ensure you're using the correct endpoint
4. Regenerate the token on DoltHub

#### "Multiple credential sets conflict"

**Problem**: Wrong credentials being used.

**Solution**: Use specific credential keys:
```bash
# Check what's configured
DMMS.AuthHelper.exe status --credential-key YourSpecificKey

# Set up isolated credentials
DMMS.AuthHelper.exe setup --credential-key YourSpecificKey
```

### Verbose Logging

For detailed troubleshooting, enable verbose logging:

```bash
DMMS.AuthHelper.exe setup --verbose
```

This provides detailed logs of the authentication process.

### Getting Help

If you continue experiencing issues:

1. **Check the logs** with `--verbose` flag
2. **Verify your DoltHub account** works in a browser
3. **Test with a fresh API token**
4. **Try the `forget` and `setup` commands** to start clean

## Security Features

### Credential Protection

- **Encrypted Storage**: Credentials stored in Windows Credential Manager with OS-level encryption
- **Process Isolation**: Authentication happens in separate processes from AI conversation
- **No Logging**: API tokens never appear in logs or console output (shown as `****`)
- **Secure Input**: Password input is hidden during terminal entry

### Zero-Trust Architecture

- **No Implicit Access**: MCP tools cannot access credentials directly
- **External Authentication**: All credential setup happens outside AI context
- **Temporal Separation**: Authentication occurs before AI operations, not during

### Audit and Compliance

- **Clear Audit Trail**: Separate logs for authentication events vs. operations
- **No Credential Leakage**: Credentials never enter conversation history
- **User-Controlled**: All authentication happens in user-controlled context

## Advanced Usage

### Scripting and Automation

For automated setups (CI/CD, etc.), you can check authentication status:

```bash
# Check if authentication is configured
DMMS.AuthHelper.exe status --endpoint dolthub.com
echo "Exit code: $?"

# Exit code 0 = configured, 1 = not configured
```

### Corporate Environments

For enterprise deployments:

```bash
# Corporate endpoint with custom credential key
DMMS.AuthHelper.exe setup \
  --endpoint corporate.dolthub.company.com \
  --credential-key CorporateProject-DoltHub
```

### Multi-User Systems

Each Windows user gets separate credential storage:

```bash
# User A logs in
DMMS.AuthHelper.exe setup --credential-key UserA-DoltHub

# User B logs in (different Windows user account)
DMMS.AuthHelper.exe setup --credential-key UserB-DoltHub
```

---

## Summary

The DMMS authentication system provides enterprise-grade security while maintaining a smooth user experience. Key benefits:

- 🔒 **Secure**: Credentials never enter AI conversation context
- 🚀 **Easy**: One-time setup with guided browser authentication
- 🔧 **Flexible**: Support for multiple endpoints and credential keys
- 🛡️ **Compliant**: Enterprise-ready security architecture

For additional help or advanced configurations, refer to the technical documentation or contact your system administrator.