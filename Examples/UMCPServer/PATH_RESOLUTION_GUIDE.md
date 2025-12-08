# UMCP InterpretTestResults Path Resolution Guide

## Problem Solved

The InterpretTestResults tool was failing to access test result files due to path resolution issues between the Docker container and the host filesystem. This document outlines the implemented solution.

## Root Cause

The Docker container running the UMCP server was looking for files in `/app/TestResults/` but the actual files were located on the host filesystem at paths like:
- Windows: `C:\Prespective\GIT\GIT-AGSSRefactor\AGSSRefactor\TestResults\`
- WSL: `/mnt/c/Prespective/GIT/GIT-AGSSRefactor/AGSSRefactor/TestResults/`

## Solution Overview

### 1. Enhanced Path Resolution Algorithm

The `InterpretTestResultsTool` now includes a comprehensive path resolution system that:

1. **Checks absolute paths first** - If the provided path is absolute and exists, use it directly
2. **Resolves relative paths** - Convert relative paths to absolute from current directory
3. **Uses Unity project path** - Leverages the existing `GetProjectPath` Unity connection to resolve paths relative to the Unity project
4. **Cross-platform translation** - Handles Windows/Linux/Docker path format differences
5. **Docker volume mount support** - Checks configured volume mounts for file access

### 2. Docker Configuration Updates

Updated `docker-compose.yml` to include volume mounts:

```yaml
volumes:
  # Mount Unity project TestResults directory
  - type: bind
    source: ../TestResults
    target: /app/TestResults
    read_only: true
  # Mount entire Unity project for broader access
  - type: bind
    source: ../
    target: /app/UnityProject
    read_only: true
```

### 3. Cross-Platform Path Translation

The tool now handles:
- **Windows to Linux**: `C:\path\to\file` → `/mnt/c/path/to/file`
- **Linux to Windows**: `/mnt/c/path/to/file` → `C:\path\to\file`
- **Docker internal paths**: `/app/TestResults/file.xml` → Host filesystem paths

## Usage Examples

The tool now supports all these path formats:

```bash
# Relative path (resolved from Unity project)
TestResults/TestResults_EditMode_RunTests_*.xml

# Just filename (searches in TestResults directory)
TestResults_EditMode_RunTests_*.xml

# Windows absolute path
C:/Prespective/GIT/GIT-AGSSRefactor/AGSSRefactor/TestResults/TestResults_*.xml

# Linux/WSL absolute path
/mnt/c/Prespective/GIT/GIT-AGSSRefactor/AGSSRefactor/TestResults/TestResults_*.xml

# Docker volume mount paths
/app/TestResults/TestResults_*.xml
/app/UnityProject/TestResults/TestResults_*.xml
```

## Technical Implementation

### Key Methods Added

1. **`ResolveTestResultsFilePath`** - Main path resolution orchestrator
2. **`GetUnityProjectPath`** - Retrieves Unity project path via connection service
3. **`TryResolveFromUnityProject`** - Resolves paths relative to Unity project
4. **`TranslatePath`** - Handles cross-platform path translation

### Path Resolution Order

1. Absolute path check
2. Relative path from current directory
3. Unity project path resolution
4. Cross-platform path translation
5. Docker volume mount paths

## Benefits

- **Robust**: Works across Windows, Linux, and Docker environments
- **Smart**: Automatically detects Unity project location
- **Flexible**: Supports multiple path formats
- **Maintainable**: Clear separation of concerns with individual methods

## Testing

Use the included `TestPathResolution.cs` to verify path resolution works correctly in your environment.

## Future Enhancements

- Add support for additional volume mount configurations
- Implement caching for Unity project path lookups
- Add configurable search paths via environment variables