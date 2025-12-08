#Requires -Version 5.0
<#
.SYNOPSIS
    Manages version information for the UMCP Server project.
.DESCRIPTION
    This script provides functionality to update version information in Directory.Build.props
    and update CHANGELOG.md when releasing a new version.
.EXAMPLE
    ./version-manager.ps1 -GetVersion
    Returns the current version.
.EXAMPLE
    ./version-manager.ps1 -BumpMajor
    Bumps the major version number (1.0.0 -> 2.0.0).
.EXAMPLE
    ./version-manager.ps1 -BumpMinor
    Bumps the minor version number (1.0.0 -> 1.1.0).
.EXAMPLE
    ./version-manager.ps1 -BumpPatch
    Bumps the patch version number (1.0.0 -> 1.0.1).
.EXAMPLE
    ./version-manager.ps1 -SetVersion 1.2.3-beta
    Sets the version to a specific value.
.EXAMPLE
    ./version-manager.ps1 -Release
    Releases the current version (removes any pre-release suffix).
.NOTES
    Author: Your Name
    Date: [Date]
#>

param (
    [switch]$GetVersion,
    [switch]$BumpMajor,
    [switch]$BumpMinor,
    [switch]$BumpPatch,
    [string]$SetVersion,
    [switch]$Release
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Get-Item $scriptDir).Parent.FullName
$buildPropsPath = Join-Path $rootDir "Directory.Build.props"
$changelogPath = Join-Path $rootDir "CHANGELOG.md"

# Read current version from Directory.Build.props
function Get-CurrentVersion {
    [xml]$buildProps = Get-Content $buildPropsPath
    $versionPrefix = $buildProps.Project.PropertyGroup[1].VersionPrefix
    $versionSuffix = $buildProps.Project.PropertyGroup[1].VersionSuffix
    
    if ([string]::IsNullOrEmpty($versionSuffix)) {
        return $versionPrefix
    } else {
        return "$versionPrefix-$versionSuffix"
    }
}

# Update version in Directory.Build.props
function Update-Version($newVersionPrefix, $newVersionSuffix) {
    [xml]$buildProps = Get-Content $buildPropsPath
    
    $buildProps.Project.PropertyGroup[1].VersionPrefix = $newVersionPrefix
    $buildProps.Project.PropertyGroup[1].VersionSuffix = $newVersionSuffix
    $buildProps.Project.PropertyGroup[1].AssemblyVersion = "$newVersionPrefix.0"
    $buildProps.Project.PropertyGroup[1].FileVersion = "$newVersionPrefix.0"
    
    if ([string]::IsNullOrEmpty($newVersionSuffix)) {
        $buildProps.Project.PropertyGroup[1].InformationalVersion = $newVersionPrefix
    } else {
        $buildProps.Project.PropertyGroup[1].InformationalVersion = "$newVersionPrefix-$newVersionSuffix"
    }
    
    $buildProps.Save($buildPropsPath)
    
    Write-Output "Updated version to $($buildProps.Project.PropertyGroup[1].InformationalVersion)"
}

# Update CHANGELOG.md when releasing a new version
function Update-Changelog($version) {
    $content = Get-Content $changelogPath -Raw
    $date = Get-Date -Format "yyyy-MM-dd"
    
    # Replace [Unreleased] section with new version section
    $newContent = $content -replace "(?s)## \[Unreleased\](.*?)## \[", @"
## [Unreleased]

### Added
- 

### Changed
- 

### Fixed
- 

## [$version] - $date$1## [
"@
    
    # Add/update the version links at the end of the file
    $lines = $newContent -split "\r?\n"
    $lastLine = $lines[-1]
    
    # Check if we need to update the Unreleased link
    if ($lastLine -match "\[Unreleased\]:") {
        $gitHubUrl = ($lastLine -split "/compare/")[0]
        $lines[-1] = "[Unreleased]: $gitHubUrl/compare/v$version...HEAD"
        $lines += "[v$version]: $gitHubUrl/releases/tag/v$version"
    } else {
        # Assuming GitHub URL format
        $gitHubUrl = "https://github.com/yourusername/UMCPServer"
        $lines += "[Unreleased]: $gitHubUrl/compare/v$version...HEAD"
        $lines += "[v$version]: $gitHubUrl/releases/tag/v$version"
    }
    
    $newContent = $lines -join [Environment]::NewLine
    Set-Content -Path $changelogPath -Value $newContent
    
    Write-Output "Updated CHANGELOG.md for version $version"
}

# Main logic
if ($GetVersion) {
    $currentVersion = Get-CurrentVersion
    Write-Output $currentVersion
    return
}

if ($BumpMajor -or $BumpMinor -or $BumpPatch -or $SetVersion -or $Release) {
    $currentVersion = Get-CurrentVersion
    
    if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)(?:-([a-zA-Z0-9.-]+))?$') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = [int]$Matches[3]
        $suffix = if ($Matches.Count -eq 5) { $Matches[4] } else { "" }
        
        if ($BumpMajor) {
            $major += 1
            $minor = 0
            $patch = 0
        } elseif ($BumpMinor) {
            $minor += 1
            $patch = 0
        } elseif ($BumpPatch) {
            $patch += 1
        } elseif ($SetVersion) {
            if ($SetVersion -match '^(\d+)\.(\d+)\.(\d+)(?:-([a-zA-Z0-9.-]+))?$') {
                $major = [int]$Matches[1]
                $minor = [int]$Matches[2]
                $patch = [int]$Matches[3]
                $suffix = if ($Matches.Count -eq 5) { $Matches[4] } else { "" }
            } else {
                Write-Error "Invalid version format: $SetVersion"
                return
            }
        } elseif ($Release) {
            # Just remove the suffix
            $suffix = ""
        }
        
        $newVersionPrefix = "$major.$minor.$patch"
        Update-Version $newVersionPrefix $suffix
        
        # Only update changelog when removing suffix (actual release)
        if ($Release -or ($SetVersion -and -not $SetVersion.Contains('-'))) {
            Update-Changelog $newVersionPrefix
        }
        
    } else {
        Write-Error "Current version in Directory.Build.props does not match expected format"
    }
}

if (-not ($GetVersion -or $BumpMajor -or $BumpMinor -or $BumpPatch -or $SetVersion -or $Release)) {
    Write-Output "No action specified. Use -GetVersion, -BumpMajor, -BumpMinor, -BumpPatch, -SetVersion, or -Release"
}