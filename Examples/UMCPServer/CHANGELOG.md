# Changelog

All notable changes to the UMCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial setup of versioning and changelog
- GetServerVersionTool to retrieve server version information
- ForceUpdateEditorTool to force Unity Editor updates and handle PlayMode transitions
- Async state monitoring with 30-second timeout for ForceUpdateEditor operations
- Enhanced Unity Client tool for forced editor updates with automatic PlayMode exit

## [0.1.0-alpha] - 2024-07-13

### Added
- Initial project structure
- Unity connection service
- MCP server implementation
- GetProjectPathTool
- Docker support
- Readme documentation
- Configuration via environment variables

[Unreleased]: https://github.com/yourusername/UMCPServer/compare/v0.1.0-alpha...HEAD
[0.1.0-alpha]: https://github.com/yourusername/UMCPServer/releases/tag/v0.1.0-alpha