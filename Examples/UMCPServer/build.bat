@echo off
echo Building UMCP Server...

echo.
echo Step 1: Building .NET project...
dotnet build -c Release

echo.
echo Step 2: Building Docker image...
docker build -t umcpserver .

echo.
echo Build complete!
echo.
echo To run locally: dotnet run
echo To run with Docker: docker run -it umcpserver
echo To run with Docker Compose: docker-compose up -d
