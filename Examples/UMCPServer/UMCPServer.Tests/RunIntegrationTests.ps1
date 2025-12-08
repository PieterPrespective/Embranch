#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs UMCP Unity Bridge Integration Tests
.DESCRIPTION
    This script builds and runs the UMCP Unity Bridge integration tests that create
    real connections between the server and Unity3D without mocking.
#>

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "UMCP Unity Bridge Integration Test Runner" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Check if dotnet is installed
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK is not installed or not in PATH" -ForegroundColor Red
    Write-Host "Please install .NET SDK from https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

# Change to the script directory
Set-Location $PSScriptRoot

# Build the test project
Write-Host "Building test project..." -ForegroundColor Yellow
$buildResult = dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed" -ForegroundColor Red
    exit 1
}

# Display menu
Write-Host ""
Write-Host "Select which test to run:" -ForegroundColor Green
Write-Host "1. Run UMCPBridgeRealConnectionTest (requires Unity to be running)"
Write-Host "2. Run UMCPBridgeIntegrationTest (starts Unity automatically - experimental)"
Write-Host "3. Run all Unity Bridge integration tests"
Write-Host "4. Run all integration tests"
Write-Host "5. Check Unity connection status"
Write-Host "6. Exit"
Write-Host ""

$choice = Read-Host "Enter your choice (1-6)"

switch ($choice) {
    "1" {
        Write-Host ""
        Write-Host "Running UMCPBridgeRealConnectionTest..." -ForegroundColor Yellow
        Write-Host "Please ensure Unity is running with the UMCPClient project open." -ForegroundColor Cyan
        Write-Host ""
        dotnet test --filter "FullyQualifiedName~UMCPBridgeRealConnectionTest" --logger "console;verbosity=detailed"
    }
    "2" {
        Write-Host ""
        Write-Host "Running UMCPBridgeIntegrationTest..." -ForegroundColor Yellow
        Write-Host "This will attempt to start Unity in headless mode." -ForegroundColor Cyan
        Write-Host ""
        dotnet test --filter "FullyQualifiedName~UMCPBridgeIntegrationTest" --logger "console;verbosity=detailed"
    }
    "3" {
        Write-Host ""
        Write-Host "Running all Unity Bridge integration tests..." -ForegroundColor Yellow
        Write-Host ""
        dotnet test --filter "FullyQualifiedName~UMCPBridge" --logger "console;verbosity=detailed"
    }
    "4" {
        Write-Host ""
        Write-Host "Running all integration tests..." -ForegroundColor Yellow
        Write-Host ""
        dotnet test --filter "Category=Integration" --logger "console;verbosity=detailed"
    }
    "5" {
        Write-Host ""
        Write-Host "Checking Unity connection status..." -ForegroundColor Yellow
        try {
            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $tcpClient.Connect("localhost", 6400)
            Write-Host "✓ Unity is running and UMCP Bridge is active on port 6400" -ForegroundColor Green
            $tcpClient.Close()
        }
        catch {
            Write-Host "✗ Unity is not running or UMCP Bridge is not active" -ForegroundColor Red
            Write-Host "  Please start Unity and open the UMCPClient project" -ForegroundColor Yellow
        }
    }
    "6" {
        Write-Host ""
        Write-Host "Exiting..." -ForegroundColor Gray
        exit 0
    }
    default {
        Write-Host ""
        Write-Host "Invalid choice. Exiting..." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Test execution completed." -ForegroundColor Green
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
