# Configuring DMMS in Claude Desktop

This guide will walk you through configuring the Dolt Multi-Database MCP Server (DMMS) in Claude Desktop for Windows.

## Locating Claude's Configuration File

The Claude Desktop configuration file is located at:
```
%APPDATA%\Claude\claude_desktop_config.json
```

You can navigate to this location by:
1. Press `Win + R` to open the Run dialog
2. Type `%APPDATA%\Claude` and press Enter
3. Open `claude_desktop_config.json` in a text editor (e.g., Notepad++)

## Adding DMMS to Claude Configuration

### Basic Configuration

Add the following to your `claude_desktop_config.json` file:

```json
{
  "mcpServers": {
    "dmms": {
      "command": "C:\\Path\\To\\DMMS.exe"
    }
  }
}
```

Replace `C:\\Path\\To\\DMMS.exe` with the actual path to your DMMS executable. Note the double backslashes (`\\`) in the path.

### Configuration with Arguments

If you need to pass arguments to the DMMS server:

```json
{
  "mcpServers": {
    "dmms": {
      "command": "C:\\Path\\To\\DMMS.exe",
      "args": ["--config", "C:\\Path\\To\\config.json"]
    }
  }
}
```

### Configuration with Environment Variables

To set environment variables for the DMMS server:

```json
{
  "mcpServers": {
    "dmms": {
      "command": "C:\\Path\\To\\DMMS.exe",
      "env": {
        "DMMS_LOG_LEVEL": "Debug",
        "DMMS_DATABASE_PATH": "C:\\DoltDatabases"
      }
    }
  }
}
```

### Complete Example Configuration

Here's a complete example with multiple MCP servers configured:

```json
{
  "mcpServers": {
    "dmms": {
      "command": "C:\\Program Files\\DMMS\\DMMS.exe",
      "args": ["--verbose"],
      "env": {
        "DMMS_LOG_LEVEL": "Info"
      }
    },
    "other-server": {
      "command": "C:\\Path\\To\\Other\\Server.exe"
    }
  }
}
```

## Applying Configuration Changes

After editing the configuration file:

1. Save the `claude_desktop_config.json` file
2. Completely quit Claude Desktop:
   - Right-click the Claude icon in the system tray
   - Select "Quit Claude"
3. Restart Claude Desktop

## Verifying the Connection

Once Claude Desktop restarts:

1. Open a new conversation
2. Look for the MCP indicator (usually shows connected servers)
3. You can test the connection by asking Claude to use DMMS tools

Example test message:
```
Can you check what version of the DMMS server is running?
```

## Troubleshooting

### Server Not Appearing in Claude

1. **Check the path**: Ensure the path to DMMS.exe is correct and uses double backslashes
2. **Check file permissions**: Ensure Claude has permission to execute DMMS.exe
3. **Check JSON syntax**: Validate your JSON configuration using a JSON validator

### Server Fails to Start

1. **Check logs**: Look for error messages in:
   - Claude's developer console (if available)
   - Windows Event Viewer
   - DMMS log files (if configured)

2. **Test manually**: Try running the DMMS executable directly:
   ```bash
   "C:\Path\To\DMMS.exe"
   ```

3. **Verify .NET installation**: Ensure .NET 9.0 runtime is installed:
   ```bash
   dotnet --list-runtimes
   ```

### Common Issues and Solutions

| Issue | Solution |
|-------|----------|
| "Command not found" | Verify the executable path is correct |
| "Access denied" | Run Claude as administrator or check file permissions |
| "Invalid JSON" | Use a JSON validator to check syntax |
| Server crashes immediately | Check DMMS logs for error messages |
| No tools available | Ensure DMMS is properly implementing MCP protocol |

## Advanced Configuration

For advanced configuration options, see:
- [Advanced Configuration Guide](advanced-configuration.md)
- [Environment Variables Reference](environment-variables.md)
- [Logging Configuration](logging.md)