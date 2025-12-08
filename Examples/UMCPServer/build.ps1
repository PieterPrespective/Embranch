#!/usr/bin/env pwsh

Write-Host "Building UMCP Server..." -ForegroundColor Green

Write-Host "`nStep 1: Building .NET project..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ .NET build successful" -ForegroundColor Green
} else {
    Write-Host "✗ .NET build failed" -ForegroundColor Red
    exit 1
}

Write-Host "`nStep 2: Building Docker image..." -ForegroundColor Yellow
docker build -t umcpserver .

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Docker build successful" -ForegroundColor Green
} else {
    Write-Host "✗ Docker build failed" -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild complete!" -ForegroundColor Green
Write-Host "`nUsage options:" -ForegroundColor Cyan
Write-Host "  - Run locally: dotnet run"
Write-Host "  - Run with Docker: docker run -it umcpserver"
Write-Host "  - Run with Docker Compose: docker-compose up -d"
