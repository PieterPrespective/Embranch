# Docker Connectivity Troubleshooting Guide

This guide helps solve connectivity issues between the UMCP Server running in Docker and Unity Editor running on the host machine.

## Common Issues

### 1. Can't Connect to Unity from Docker Container

When running UMCP Server in a Docker container, the most common issue is that the container can't connect to the Unity Editor TCP server running on the host machine.

#### Solution A: Use host.docker.internal

The Docker configuration already uses `host.docker.internal` which maps to your host machine's IP address from within Docker:

```
UNITY_HOST=host.docker.internal
```

This works on Windows, macOS, and newer versions of Docker on Linux (with `extra_hosts` specified).

#### Solution B: Use Host Network Mode

If Solution A doesn't work, try using the host network mode:

```bash
# In docker-compose.yml, uncomment:
network_mode: "host"

# Or when running the container directly:
docker run -it --network=host umcpserver
```

#### Solution C: Use Host IP Address

If both solutions above fail, try using the actual IP address of your host machine:

```bash
# Find your host IP address
# Windows: ipconfig
# macOS/Linux: ifconfig or ip addr

# Then run with the IP address:
docker run -it -e UNITY_HOST=192.168.1.x umcpserver
```

### 2. Unity TCP Server Configuration

Make sure Unity TCP server is:
- Actually running and listening on the correct port (default: 6400)
- Configured to listen on all interfaces, not just localhost

### 3. Firewall Issues

Check if your host firewall is blocking connections:

- Windows: Check Windows Defender Firewall
- macOS: Check System Preferences > Security & Privacy > Firewall
- Linux: Check your firewall settings (`ufw status`, `iptables -L`, etc.)

Add an exception for Unity TCP Server port (default: 6400).

## Diagnostic Commands

### Check if host.docker.internal is properly mapped:

```bash
# From inside your container:
docker exec -it umcpserver ping host.docker.internal
```

### Check if the port is accessible:

```bash
# From inside your container:
docker exec -it umcpserver apk add --no-cache curl
docker exec -it umcpserver curl -v telnet://host.docker.internal:6400
```

### Check container network configuration:

```bash
docker exec umcpserver ip addr
```

## Additional Docker Configuration

For the most reliable connectivity, add these settings to your Docker run command or docker-compose.yml:

```yaml
services:
  umcpserver:
    # ... other settings ...
    extra_hosts:
      - "host.docker.internal:host-gateway"
    environment:
      - UNITY_HOST=host.docker.internal
      - ENABLE_LOGGING=true
```

## Platform-Specific Notes

### Linux

On older versions of Docker for Linux, `host.docker.internal` might not be automatically defined. The `extra_hosts` setting in docker-compose.yml should fix this, but if problems persist, try:

```bash
docker run -it --add-host=host.docker.internal:host-gateway umcpserver
```

### Windows/macOS

On Windows and macOS, Docker Desktop should automatically define `host.docker.internal`. If it doesn't work, try restarting Docker Desktop.

### Docker Toolbox (Legacy)

For older Docker Toolbox installations, you need to use the VM's IP address:

```bash
docker run -it -e UNITY_HOST=192.168.99.1 umcpserver
```