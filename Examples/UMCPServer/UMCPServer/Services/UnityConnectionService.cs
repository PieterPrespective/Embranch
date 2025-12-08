using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UMCPServer.Models;

namespace UMCPServer.Services;

public class UnityConnectionService : IUnityConnectionService
{
    private readonly ILogger<UnityConnectionService> _logger;
    private readonly ServerConfiguration _config;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly object _lockObject = new();
    private int _retryCount = 0;
    
    public UnityConnectionService(ILogger<UnityConnectionService> logger, IOptions<ServerConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
    }
    
    public bool IsConnected
    {
        get
        {
            lock (_lockObject)
            {
                return _tcpClient?.Connected ?? false;
            }
        }
    }
    
    public async Task<bool> ConnectAsync()
    {
        lock (_lockObject)
        {
            if (_tcpClient?.Connected == true)
                return true;
        }
        
        try
        {
            // Reset retry count if this is a new connection attempt
            _retryCount = 0;
            return await AttemptConnectionWithRetryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Unity");
            LogConnectionTroubleshooting();
            Disconnect();
            return false;
        }
    }
    
    private async Task<bool> AttemptConnectionWithRetryAsync()
    {
        while (_retryCount <= _config.MaxRetries)
        {
            try
            {
                if (_retryCount > 0)
                {
                    _logger.LogInformation("Connection attempt {RetryCount} of {MaxRetries}", 
                        _retryCount, _config.MaxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                }
                
                _retryCount++;
                
                // Check if host is reachable in Docker container
                if (_config.IsRunningInContainer)
                {
                    _logger.LogInformation("Checking connectivity to {UnityHost}:{UnityPort} from Docker container", 
                        _config.UnityHost, _config.UnityPort);
                }
                
                _tcpClient = new TcpClient();
                
                // Use shorter connection timeout for initial attempts
                var connectTask = _tcpClient.ConnectAsync(_config.UnityHost, _config.UnityPort);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Min(5, _config.ConnectionTimeoutSeconds)));
                
                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    throw new TimeoutException($"Connection to {_config.UnityHost}:{_config.UnityPort} timed out");
                }
                
                _stream = _tcpClient.GetStream();
                
                _logger.LogInformation("Connected to Unity at {UnityHost}:{UnityPort}", 
                    _config.UnityHost, _config.UnityPort);
                
                // Verify connection with ping
                var pingResult = await SendCommandAsync("ping", null);
                if (pingResult == null)
                {
                    throw new Exception("Failed to verify connection with ping");
                }
                
                // Connection established successfully
                
                _retryCount = 0;
                return true;
            }
            catch (Exception ex) when (
                ex is SocketException || 
                ex is TimeoutException || 
                ex.Message.Contains("Failed to verify"))
            {
                if (_retryCount > _config.MaxRetries)
                {
                    _logger.LogError(ex, "Connection failed after {RetryCount} attempts", _retryCount);
                    throw;
                }
                
                _logger.LogWarning(ex, "Connection attempt {RetryCount} failed, retrying...", _retryCount);
                Disconnect();
            }
        }
        
        return false;
    }
    
    private void LogConnectionTroubleshooting()
    {
        if (_config.IsRunningInContainer)
        {
            _logger.LogError("Docker container connection troubleshooting:");
            _logger.LogError("1. Verify Unity is running on host machine with UMCP Unity3D Client active");
            _logger.LogError("2. Check that Unity is configured to listen on {UnityHost}:{UnityPort}", 
                _config.UnityHost, _config.UnityPort);
            _logger.LogError("3. For Docker on Linux, try using the host's actual IP address instead of 'host.docker.internal'");
            _logger.LogError("4. Verify that no firewall is blocking the connection");
            _logger.LogError("5. Try adding '--network=host' to your Docker run command");
            _logger.LogError("6. Check that the container has network connectivity with 'docker exec <container> ping {0}'", 
                _config.UnityHost == "host.docker.internal" ? "8.8.8.8" : _config.UnityHost);
        }
    }
    
    public void Disconnect()
    {
        lock (_lockObject)
        {
            try
            {
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from Unity");
            }
            finally
            {
                _stream = null;
                _tcpClient = null;
            }
        }
    }
    



    public async Task<JObject?> SendCommandAsync(string commandType, JObject? parameters, CancellationToken cancellationToken = default, bool _logResult = false)
    {
        if (!IsConnected && !await ConnectAsync())
        {
            throw new InvalidOperationException("Not connected to Unity");
        }
        
        var command = new UnityCommand
        {
            Type = commandType,
            Params = parameters
        };
        
        try
        {
            // Special handling for ping
            if (commandType == "ping")
            {
                _logger.LogDebug("Sending ping to verify connection");
                byte[] pingBytes = Encoding.UTF8.GetBytes("ping");
                await _stream!.WriteAsync(pingBytes, 0, pingBytes.Length, cancellationToken);
            }
            else
            {
                // Normal command
                string commandJson = JsonConvert.SerializeObject(command);
                _logger.LogInformation("Sending command: {CommandType} with params size: {Size} bytes", 
                    commandType, commandJson.Length);
                
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandJson);
                await _stream!.WriteAsync(commandBytes, 0, commandBytes.Length, cancellationToken);
            }
            
            // Read response
            byte[] responseData = await ReceiveFullResponseAsync(cancellationToken);
            string responseJson = Encoding.UTF8.GetString(responseData);

            if(_logResult)
            {
                _logger.LogInformation("Received response: {ResponseSize} bytes = '{ResponseMsg}'", responseJson.Length, responseJson);
            }

            var response = JsonConvert.DeserializeObject<UnityResponse>(responseJson);
            if (response?.Status == "error")
            {
                string errorMessage = response.Error ?? response.Message ?? "Unknown Unity error";
                _logger.LogError("Unity error: {ErrorMessage}", errorMessage);
                throw new Exception(errorMessage);
            }

            return response?.Result ?? new JObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Communication error with Unity for command: {CommandType}", commandType);
            Disconnect();
            throw new Exception($"Failed to communicate with Unity: {ex.Message}", ex);
        }
    }
    
    private async Task<byte[]> ReceiveFullResponseAsync(CancellationToken cancellationToken)
    {
        var chunks = new List<byte[]>();
        var buffer = new byte[_config.BufferSize];
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.ConnectionTimeoutSeconds));
        
        try
        {
            while (true)
            {
                int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead == 0)
                {
                    if (chunks.Count == 0)
                        throw new Exception("Connection closed before receiving data");
                    break;
                }
                
                chunks.Add(buffer.Take(bytesRead).ToArray());
                
                // Try to parse as JSON to check if we have a complete response
                byte[] currentData = chunks.SelectMany(c => c).ToArray();
                string currentText = Encoding.UTF8.GetString(currentData);
                
                try
                {
                    // Special case for ping
                    if (currentText.Trim().StartsWith("{\"status\":\"success\",\"result\":{\"message\":\"pong\""))
                    {
                        _logger.LogDebug("Received ping response");
                        return currentData;
                    }
                    
                    // Try to parse as JSON
                    JsonConvert.DeserializeObject<UnityResponse>(currentText);
                    
                    // If successful, we have a complete response
                    _logger.LogInformation("Received complete response ({Size} bytes)", currentData.Length);
                    return currentData;
                }
                catch (JsonException)
                {
                    // Not complete yet, continue reading
                    continue;
                }
            }
            
            return chunks.SelectMany(c => c).ToArray();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Socket timeout during receive");
            throw new TimeoutException("Timeout receiving Unity response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during receive");
            throw;
        }
    }
    

    
    public void Dispose()
    {
        Disconnect();
    }
}
