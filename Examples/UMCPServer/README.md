# UMCP MCP Server

A C# Model Context Protocol (MCP) server that bridges MCP requests to the Unity Editor via TCP connection.

## Overview

This server acts as a bridge between MCP clients and the Unity Editor. It forwards MCP tool requests to the Unity Editor running the UMCP Unity3D Client (TCP Server) and returns the responses.

## Features

- **Unity Connection Management**: Automatically connects to Unity Editor on startup with reconnection support
- **Separate State Connection**: Dedicated TCP port for Unity state updates (runmode and context)
- **GetProjectPath Tool**: Retrieves Unity project paths including:
  - Project path
  - Data path
  - Persistent data path
  - Streaming assets path
  - Temporary cache path
- **GetServerVersion Tool**: Returns the version information of the MCP server
- **GetUnityClientState Tool**: Returns the current Unity Editor state (runmode and context)
- **ForceUpdateEditor Tool**: Forces Unity Editor to update regardless of focus; exits PlayMode if necessary and waits for EditMode_Running state
- **Real-time State Updates**: Receives Unity state changes via dedicated state port
- **Timeout Handling**: Configurable timeout for Unity responses
- **Docker Support**: Can be run in a Docker container
- **Environment Configuration**: All settings can be configured via environment variables

## Requirements

- .NET 9.0 SDK
- Unity Editor with UMCP Unity3D Client running
- Docker (optional, for containerized deployment)

## Configuration

The server can be configured using environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `UNITY_HOST` | Unity TCP server host | `localhost` |
| `UNITY_PORT` | Unity TCP command port | `6400` |
| `UNITY_STATE_PORT` | Unity TCP state port | `6401` |
| `MCP_PORT` | MCP server port | `6500` |
| `CONNECTION_TIMEOUT` | Connection timeout in seconds | `86400` (24 hours) |
| `BUFFER_SIZE` | TCP buffer size in bytes | `16777216` (16MB) |
| `MAX_RETRIES` | Maximum connection retries | `3` |
| `RETRY_DELAY` | Delay between retries in seconds | `1.0` |

## Running Locally

1. Ensure Unity Editor is running with the UMCP Unity3D Client active
2. Build and run the server:dotnet build
   dotnet run
## Running with Docker

### Build the Docker image:docker build -t umcpserver .
### Run the container:docker run -it --rm \
  -e UNITY_HOST=host.docker.internal \
  -e UNITY_PORT=6400 \
  -e UNITY_STATE_PORT=6401 \
  umcpserver
Note: Use `host.docker.internal` as the Unity host when running in Docker on Windows/Mac to connect to Unity running on the host machine.

## Versioning

The project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html):

- Version numbers are in the format `MAJOR.MINOR.PATCH[-SUFFIX]`
- MAJOR version increments for incompatible API changes
- MINOR version increments for added functionality (backwards-compatible)
- PATCH version increments for bug fixes (backwards-compatible)
- Pre-release labels may be appended as `-alpha`, `-beta`, etc.

Version information is centrally managed in `Directory.Build.props` at the solution root.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a detailed list of changes between versions.

## Architecture

The server consists of:

- **Program.cs**: Main entry point, sets up dependency injection and hosting
- **UnityConnectionService**: Manages TCP connection to Unity Editor for commands
- **UnityStateConnectionService**: Manages separate TCP connection for Unity state updates
- **GetProjectPathTool**: MCP tool that retrieves Unity project paths
- **GetServerVersionTool**: MCP tool that retrieves server version information
- **GetUnityClientStateTool**: MCP tool that retrieves Unity Editor state
- **ForceUpdateEditorTool**: MCP tool that forces Unity Editor updates and handles PlayMode transitions
- **Models**: Data models for Unity communication
- **Services**: Service layer for Unity connection management

## Adding New Tools

To add new tools that bridge to Unity:

1. Create a new tool class in the `Tools` folder
2. Inherit from `[McpServerToolType]` attribute
3. Inject `UnityConnectionService` for Unity communication
4. Use `SendCommandAsync` to forward requests to Unity
5. Register the tool in `Program.cs` using `.WithTools<YourTool>()`

Example:[McpServerToolType]
public class YourTool
{
    private readonly UnityConnectionService _unityConnection;
    
    public YourTool(UnityConnectionService unityConnection)
    {
        _unityConnection = unityConnection;
    }
    
    [McpServerTool]
    [Description("Your tool description")]
    public async Task<object> YourMethod(string param1, CancellationToken cancellationToken = default)
    {
        var parameters = new JObject
        {
            ["param1"] = param1
        };
        
        var result = await _unityConnection.SendCommandAsync("your_command", parameters, cancellationToken);
        // Process and return result
    }
}
## Error Handling

The server handles various error scenarios:

- Unity Editor not running: Returns appropriate error message
- Connection timeouts: Configurable timeout with clear error messages
- Unity errors: Forwards Unity error messages to MCP clients
- Network errors: Automatic reconnection attempts

## Logging

The server uses Microsoft.Extensions.Logging with console output. Log levels can be configured through standard .NET logging configuration.
