# Installing DMMS (Dolt Multi-Database MCP Server)

## Prerequisites

Before installing DMMS, ensure you have the following installed on your Windows system:

1. **.NET 9.0 Runtime or SDK**  
   Download from: [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)
   
2. **Dolt Database** (Optional - if you plan to use Dolt databases)  
   Download from: [https://www.dolthub.com/docs/getting-started/installation/](https://www.dolthub.com/docs/getting-started/installation/)

3. **Claude Desktop Application**  
   Download from: [https://claude.ai/download](https://claude.ai/download)

## Download Options

### Option 1: Using the Pre-built Executable

1. Download the latest DMMS release from the releases page
2. Extract the ZIP file to a location of your choice (e.g., `C:\Program Files\DMMS`)
3. Note the full path to `DMMS.exe` for configuration

### Option 2: Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/dmms.git
   cd dmms
   ```

2. Build the project:
   ```bash
   dotnet build multidolt-mcp/DMMS.csproj -c Release
   ```

3. The executable will be located at:
   ```
   multidolt-mcp\bin\Release\net9.0\DMMS.exe
   ```

## Verifying Installation

Open a Command Prompt or PowerShell and run:

```bash
"C:\Path\To\DMMS.exe" --version
```

You should see the version information for the DMMS server.

## Next Steps

- [Configuring DMMS in Claude](claude-configuration.md)
- [Basic Usage](basic-usage.md)
- [Advanced Configuration](advanced-configuration.md)