@echo off
echo ===========================================
echo UMCP Unity Bridge Integration Test Runner
echo ===========================================
echo.

REM Check if dotnet is installed
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo ERROR: .NET SDK is not installed or not in PATH
    echo Please install .NET SDK from https://dotnet.microsoft.com/download
    exit /b 1
)

REM Change to the test project directory
cd /d "%~dp0"

echo Building test project...
dotnet build --configuration Release
if %errorlevel% neq 0 (
    echo ERROR: Build failed
    exit /b 1
)

echo.
echo Select which test to run:
echo 1. Run UMCPBridgeRealConnectionTest (requires Unity to be running)
echo 2. Run UMCPBridgeIntegrationTest (starts Unity automatically - experimental)
echo 3. Run all Unity Bridge integration tests
echo 4. Run all integration tests
echo 5. Exit
echo.

choice /c 12345 /n /m "Enter your choice: "

if %errorlevel% equ 1 (
    echo.
    echo Running UMCPBridgeRealConnectionTest...
    echo Please ensure Unity is running with the UMCPClient project open.
    echo.
    dotnet test --filter "FullyQualifiedName~UMCPBridgeRealConnectionTest" --logger "console;verbosity=detailed"
) else if %errorlevel% equ 2 (
    echo.
    echo Running UMCPBridgeIntegrationTest...
    echo This will attempt to start Unity in headless mode.
    echo.
    dotnet test --filter "FullyQualifiedName~UMCPBridgeIntegrationTest" --logger "console;verbosity=detailed"
) else if %errorlevel% equ 3 (
    echo.
    echo Running all Unity Bridge integration tests...
    echo.
    dotnet test --filter "FullyQualifiedName~UMCPBridge" --logger "console;verbosity=detailed"
) else if %errorlevel% equ 4 (
    echo.
    echo Running all integration tests...
    echo.
    dotnet test --filter "Category=Integration" --logger "console;verbosity=detailed"
) else (
    echo.
    echo Exiting...
    exit /b 0
)

echo.
echo Test execution completed.
pause
