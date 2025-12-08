# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /app

# Copy project file and restore dependencies
COPY UMCPServer/UMCPServer.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine
WORKDIR /app

# Metadata
LABEL maintainer="UMCP Team"
LABEL description="UMCP Server for Unity - MCP bridge over TCP"
LABEL version="1.0"

# Set default environment variables for Docker
ENV UNITY_HOST=host.docker.internal
ENV UNITY_PORT=6400
ENV MCP_PORT=6500
ENV ENABLE_LOGGING=false
ENV MAX_RETRIES=5
ENV RETRY_DELAY=2.0

# Copy published application
COPY --from=build /app/out .

# Healthcheck to verify container is working
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD dotnet UMCPServer.dll --version || exit 1

# Set the entrypoint
ENTRYPOINT ["./UMCPServer"]
