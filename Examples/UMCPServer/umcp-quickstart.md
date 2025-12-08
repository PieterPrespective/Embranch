# UMCP Server Quick Start Guide

## 1. Build the Project

### Option A: Using PowerShell
```powershell
cd C:\Prespective\250328_TestMLStuffUnity3d\UnityDockerMCP\UMCPServer
.\build.ps1
```

### Option B: Using Command Prompt
```cmd
cd C:\Prespective\250328_TestMLStuffUnity3d\UnityDockerMCP\UMCPServer
build.bat
```

### Option C: Manual Build
```bash
# Build .NET project
dotnet build -c Release

# Build Docker image
docker build -t umcpserver .
```

## 2. Run the Server

### For Testing (Local)
```bash
dotnet run
```

### For Production (Docker)
```bash
docker run -it umcpserver
```

### Using Docker Compose
```bash
docker-compose up -d
```

## 3. Configure Claude Desktop

Add one of these configurations to your Claude Desktop settings file:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Linux: `~/.config/Claude/claude_desktop_config.json`

### Docker Configuration (Recommended)
```json
{
  "mcpServers": {
    "umcp": {
      "command": "docker",
      "args": ["run", "-i", "umcpserver"]
    }
  }
}
```

### Local Development Configuration
```json
{
  "mcpServers": {
    "umcp-local": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Prespective\\250328_TestMLStuffUnity3d\\UnityDockerMCP\\UMCPServer\\UMCPServer.csproj"]
    }
  }
}
```

## 4. Test the Tool

After restarting Claude Desktop, you should see the UMCP server in the MCP tools list. The `echo` tool will return: "Hello from UMCPServer A14655"

## Troubleshooting

- **Docker not found**: Ensure Docker Desktop is installed and running
- **.NET SDK not found**: Install .NET 9.0 SDK from Microsoft
- **Permission denied**: Run terminal as administrator or use sudo on Linux/macOS
- **Server not appearing in Claude**: Check the logs in Claude Desktop settings