# Getting Started with DMMS

Welcome to the Dolt Multi-Database MCP Server (DMMS) documentation. DMMS enables Claude Desktop to interact with and manage Dolt databases through the Model Context Protocol (MCP).

## What is DMMS?

DMMS is a Model Context Protocol server that provides tools for:
- Managing multiple Dolt databases simultaneously
- Executing Dolt commands and SQL queries
- Version control operations on database schemas and data
- Database branching, merging, and collaboration features

## What is MCP?

The Model Context Protocol (MCP) is a standard that allows AI assistants like Claude to interact with external tools and services. DMMS implements this protocol to give Claude access to Dolt database functionality.

## Quick Start

Follow these steps to get DMMS up and running:

1. **Install Prerequisites**
   - [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
   - [Claude Desktop](https://claude.ai/download)
   - [Dolt](https://www.dolthub.com/docs/getting-started/installation/) (optional)

2. **Install DMMS**
   - Download the latest DMMS release
   - Extract to a folder (e.g., `C:\Program Files\DMMS`)
   - Note the path to `DMMS.exe`

3. **Configure Claude Desktop**
   - Edit `%APPDATA%\Claude\claude_desktop_config.json`
   - Add DMMS configuration:
   ```json
   {
     "mcpServers": {
       "dmms": {
         "command": "C:\\Program Files\\DMMS\\DMMS.exe"
       }
     }
   }
   ```

4. **Restart Claude Desktop**
   - Completely quit Claude from the system tray
   - Start Claude Desktop again

5. **Verify Installation**
   - Ask Claude: "What version of DMMS is running?"
   - Claude should respond with the server version

## Features

### Current Features
- **Server Version Check**: Verify DMMS is running and get version information
- **Extensible Architecture**: Ready for additional tool implementation

### Planned Features
- **Database Management**: Create, clone, and delete Dolt databases
- **Query Execution**: Run SQL queries and view results
- **Version Control**: Commit changes, create branches, merge data
- **Diff Operations**: Compare database states and schemas
- **Sync Operations**: Push and pull from remote repositories

## System Requirements

### Minimum Requirements
- Windows 10 version 1903 or later (64-bit)
- .NET 9.0 Runtime
- 4 GB RAM
- 100 MB available disk space (plus space for databases)

### Recommended Requirements
- Windows 11 (64-bit)
- .NET 9.0 SDK (for development)
- 8 GB RAM or more
- SSD with adequate space for your databases

## Documentation Structure

This documentation is organized into the following sections:

- **[Installation](installation.md)**: Detailed installation instructions
- **[Claude Configuration](claude-configuration.md)**: Setting up DMMS in Claude Desktop
- **[Basic Usage](basic-usage.md)**: Common operations and workflows
- **[Troubleshooting](troubleshooting.md)**: Solutions to common problems

## Getting Help

If you need assistance:

1. Check the [Troubleshooting Guide](troubleshooting.md)
2. Review the [FAQ](#frequently-asked-questions)
3. Report issues on GitHub
4. Contact the development team

## Frequently Asked Questions

**Q: Is DMMS free to use?**
A: Yes, DMMS is open-source software.

**Q: Can I use DMMS without Dolt installed?**
A: DMMS requires either Dolt to be installed or access to Dolt databases for full functionality.

**Q: Does DMMS work with other AI assistants?**
A: DMMS is designed for Claude Desktop but can work with any MCP-compatible client.

**Q: Can I contribute to DMMS development?**
A: Yes! Check our GitHub repository for contribution guidelines.

## Next Steps

Now that you understand what DMMS is and how it works, proceed to:
1. [Complete the installation](installation.md)
2. [Configure Claude Desktop](claude-configuration.md)
3. [Learn basic usage](basic-usage.md)