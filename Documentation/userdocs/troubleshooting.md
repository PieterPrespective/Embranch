# Troubleshooting DMMS

This guide helps you diagnose and fix common issues with the Dolt Multi-Database MCP Server.

## Quick Diagnostics

### 1. Test DMMS Executable

Open Command Prompt or PowerShell and run:
```bash
"C:\Path\To\DMMS.exe" --help
```

If this fails, check:
- The path is correct
- .NET 9.0 runtime is installed
- The executable has proper permissions

### 2. Check .NET Installation

```bash
dotnet --info
```

You should see .NET 9.0 in the list of installed runtimes.

### 3. Verify Claude Configuration

Check your `%APPDATA%\Claude\claude_desktop_config.json` for:
- Correct JSON syntax
- Proper path formatting (double backslashes)
- No trailing commas

## Common Issues and Solutions

### DMMS Not Appearing in Claude

**Symptoms:**
- No MCP indicator in Claude
- Tools not available
- No response from DMMS commands

**Solutions:**

1. **Verify configuration file location:**
   ```
   %APPDATA%\Claude\claude_desktop_config.json
   ```

2. **Check JSON syntax:**
   ```json
   {
     "mcpServers": {
       "dmms": {
         "command": "C:\\Path\\To\\DMMS.exe"
       }
     }
   }
   ```

3. **Restart Claude completely:**
   - Exit Claude from system tray
   - Wait 10 seconds
   - Start Claude again

### "Command Not Found" Error

**Cause:** Incorrect path to DMMS.exe

**Solution:**
1. Verify the exact path to DMMS.exe
2. Use double backslashes in the path
3. Avoid spaces in the path if possible, or use quotes

**Example with spaces:**
```json
{
  "mcpServers": {
    "dmms": {
      "command": "\"C:\\Program Files\\DMMS\\DMMS.exe\""
    }
  }
}
```

### Server Starts but Immediately Crashes

**Possible causes:**
- Missing dependencies
- Configuration errors
- Permission issues

**Debugging steps:**

1. **Run DMMS manually to see error messages:**
   ```bash
   "C:\Path\To\DMMS.exe"
   ```

2. **Check Windows Event Viewer:**
   - Press `Win + X`, select "Event Viewer"
   - Navigate to Windows Logs > Application
   - Look for .NET or DMMS errors

3. **Enable debug logging:**
   ```json
   {
     "mcpServers": {
       "dmms": {
         "command": "C:\\Path\\To\\DMMS.exe",
         "env": {
           "DMMS_LOG_LEVEL": "Debug"
         }
       }
     }
   }
   ```

### .NET Runtime Errors

**Error:** "The required library hostfxr.dll was not found"

**Solution:**
1. Install .NET 9.0 Runtime: https://dotnet.microsoft.com/download/dotnet/9.0
2. Choose the x64 version for 64-bit Windows
3. Restart your computer after installation

### Permission Denied Errors

**Symptoms:**
- "Access is denied" errors
- Unable to execute DMMS.exe

**Solutions:**

1. **Check file permissions:**
   - Right-click DMMS.exe
   - Select Properties > Security
   - Ensure your user has "Read & Execute" permission

2. **Unblock the file (if downloaded):**
   - Right-click DMMS.exe
   - Select Properties
   - Check for "Unblock" checkbox at the bottom
   - Click "Unblock" if present

3. **Run Claude as Administrator (last resort):**
   - Right-click Claude Desktop
   - Select "Run as administrator"

### Tools Not Working

**Symptom:** Claude says tools are not available or not responding

**Debugging:**

1. **Check if DMMS is running:**
   - Open Task Manager
   - Look for DMMS.exe in the processes

2. **Test with a simple command:**
   Ask Claude: "Can you get the DMMS server version?"

3. **Check for error messages in Claude's response**

## Logging and Debugging

### Enable Verbose Logging

Add to your configuration:
```json
{
  "mcpServers": {
    "dmms": {
      "command": "C:\\Path\\To\\DMMS.exe",
      "args": ["--verbose"],
      "env": {
        "DMMS_LOG_LEVEL": "Debug",
        "DMMS_LOG_FILE": "C:\\Logs\\dmms.log"
      }
    }
  }
}
```

### Log File Locations

Default log locations:
- DMMS logs: `%TEMP%\DMMS\logs\`
- Claude logs: `%APPDATA%\Claude\logs\`

### Reading Log Files

Look for:
- ERROR level messages
- Stack traces
- Connection failures
- Tool execution errors

## Getting Further Help

If you're still experiencing issues:

1. **Collect diagnostic information:**
   - DMMS version (`DMMS.exe --version`)
   - .NET version (`dotnet --info`)
   - Claude Desktop version
   - Complete error messages
   - Relevant log entries

2. **Check for updates:**
   - Latest DMMS release
   - Latest Claude Desktop version
   - .NET runtime updates

3. **Report issues:**
   - GitHub Issues page
   - Include diagnostic information
   - Provide reproduction steps

## Frequently Asked Questions

**Q: Can I use DMMS with Claude.ai web version?**
A: No, MCP servers only work with Claude Desktop application.

**Q: Does DMMS work on Windows 11?**
A: Yes, DMMS is compatible with Windows 10 and Windows 11.

**Q: Can I run multiple instances of DMMS?**
A: Yes, you can configure multiple instances with different names in the configuration.

**Q: How do I update DMMS?**
A: Download the latest release and replace the executable, then restart Claude Desktop.

**Q: Is DMMS compatible with WSL?**
A: DMMS runs natively on Windows. For WSL compatibility, see the WSL documentation.