using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UMCPServer.Models;

namespace UMCPServer.Services;

/// <summary>
/// Service for handling Unity state updates on a separate TCP connection
/// </summary>
public class UnityStateConnectionService : IUnityStateConnectionService
{
    private readonly ILogger<UnityStateConnectionService> _logger;
    private readonly ServerConfiguration _config;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly object _lockObject = new();
    private CancellationTokenSource? _listenCancellation;
    
    // Current Unity state storage
    private JObject? _currentUnityState;
    private readonly object _stateLock = new();
    
    public UnityStateConnectionService(ILogger<UnityStateConnectionService> logger, IOptions<ServerConfiguration> config)
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
    
    public JObject? CurrentUnityState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentUnityState?.DeepClone() as JObject;
            }
        }
    }
    
    // Event for state changes
    public event Action<JObject>? UnityStateChanged;
    
    public async Task<bool> ConnectAsync()
    {
        lock (_lockObject)
        {
            if (_tcpClient?.Connected == true)
                return true;
        }
        
        try
        {
            _tcpClient = new TcpClient();
            
            // Connect to Unity state port
            var connectTask = _tcpClient.ConnectAsync(_config.UnityHost, _config.UnityStatePort);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            
            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                throw new TimeoutException($"Connection to Unity state port {_config.UnityHost}:{_config.UnityStatePort} timed out");
            }
            
            _stream = _tcpClient.GetStream();
            
            _logger.LogInformation("Connected to Unity state port at {UnityHost}:{UnityStatePort}", 
                _config.UnityHost, _config.UnityStatePort);
            
            // Start listening for state updates
            _listenCancellation = new CancellationTokenSource();
            _ = Task.Run(() => ListenForStateUpdates(_listenCancellation.Token));
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Unity state port");
            Disconnect();
            return false;
        }
    }
    
    private async Task ListenForStateUpdates(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var stateBuffer = new List<byte>();
        
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                if (_stream == null || !_stream.CanRead)
                    break;
                    
                var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("Unity state connection closed by remote host");
                    break;
                }
                    
                stateBuffer.AddRange(buffer.Take(bytesRead));
                
                // Try to parse as JSON
                string currentText = Encoding.UTF8.GetString(stateBuffer.ToArray());
                
                // Look for complete JSON objects
                int braceCount = 0;
                int lastCompleteIndex = -1;
                bool inString = false;
                char? escapeNext = null;
                
                for (int i = 0; i < currentText.Length; i++)
                {
                    char c = currentText[i];
                    
                    if (escapeNext.HasValue)
                    {
                        escapeNext = null;
                        continue;
                    }
                    
                    if (c == '\\' && inString)
                    {
                        escapeNext = c;
                        continue;
                    }
                    
                    if (c == '"')
                    {
                        inString = !inString;
                        continue;
                    }
                    
                    if (!inString)
                    {
                        if (c == '{') braceCount++;
                        else if (c == '}')
                        {
                            braceCount--;
                            if (braceCount == 0)
                            {
                                lastCompleteIndex = i;
                            }
                        }
                    }
                }
                
                // Process complete JSON objects
                if (lastCompleteIndex >= 0)
                {
                    string completeJson = currentText.Substring(0, lastCompleteIndex + 1);
                    try
                    {
                        var stateMessage = JsonConvert.DeserializeObject<JObject>(completeJson);
                        ProcessStateMessage(stateMessage);
                        
                        // Remove processed data from buffer
                        stateBuffer.RemoveRange(0, Encoding.UTF8.GetByteCount(completeJson));
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse state JSON");
                        stateBuffer.Clear();
                    }
                }
                
                // Prevent buffer from growing too large
                if (stateBuffer.Count > 100000)
                {
                    _logger.LogWarning("State buffer too large, clearing");
                    stateBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    _logger.LogInformation("State update listener cancelled");
                    break;
                }
                else
                {
                    _logger.LogError(ex, "Error in state update listener");
                    break;
                }
            }
        }
        
        _logger.LogInformation("State listener stopped");
        Disconnect();
    }
    
    private void ProcessStateMessage(JObject? message)
    {
        if (message == null) return;
        
        string? messageType = message["type"]?.ToString();
        var @params = message["params"] as JObject;
        
        if (@params == null) return;
        
        switch (messageType)
        {
            case "state_update":
                // Initial state update
                lock (_stateLock)
                {
                    _currentUnityState = @params;
                }
                _logger.LogInformation("Received initial Unity state: runmode={Runmode}, context={Context}",
                    @params["runmode"], @params["context"]);
                UnityStateChanged?.Invoke(@params);
                break;
                
            case "state_change":
                // State change notification
                lock (_stateLock)
                {
                    if (_currentUnityState == null)
                        _currentUnityState = new JObject();
                        
                    _currentUnityState["runmode"] = @params["currentRunmode"]?.ToString();
                    _currentUnityState["context"] = @params["currentContext"]?.ToString();
                    _currentUnityState["timestamp"] = @params["timestamp"]?.ToString();
                    _currentUnityState["canModifyProjectFiles"] = @params["canModifyProjectFiles"];
                    _currentUnityState["isEditorResponsive"] = @params["isEditorResponsive"];
                    
                    // Add change details
                    _currentUnityState["lastChange"] = new JObject
                    {
                        ["stateType"] = @params["stateType"],
                        ["previousValue"] = @params["previousValue"],
                        ["newValue"] = @params["newValue"]
                    };
                }
                
                _logger.LogInformation("Unity state changed: {StateType} from {Previous} to {New}",
                    @params["stateType"], @params["previousValue"], @params["newValue"]);
                    
                UnityStateChanged?.Invoke(_currentUnityState!);
                break;
                
            default:
                _logger.LogWarning("Unknown state message type: {Type}", messageType);
                break;
        }
    }
    
    public void Disconnect()
    {
        lock (_lockObject)
        {
            try
            {
                _listenCancellation?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from Unity state port");
            }
            finally
            {
                _listenCancellation?.Dispose();
                _listenCancellation = null;
                _stream = null;
                _tcpClient = null;
            }
        }
    }
    
    public void Dispose()
    {
        Disconnect();
    }
}
