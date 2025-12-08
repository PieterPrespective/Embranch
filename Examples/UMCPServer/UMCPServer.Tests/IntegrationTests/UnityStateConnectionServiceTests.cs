using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMCPServer.Models;
using UMCPServer.Services;

namespace UMCPServer.Tests.IntegrationTests;

[TestFixture]
public class UnityStateConnectionServiceTests
{
    private UnityStateConnectionService? _service;
    private Mock<ILogger<UnityStateConnectionService>>? _loggerMock;
    private Mock<IOptions<ServerConfiguration>>? _configMock;
    private TcpListener? _mockUnityStateListener;
    private Thread? _mockUnityStateThread;
    private CancellationTokenSource? _cancellationSource;
    
    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<UnityStateConnectionService>>();
        _configMock = new Mock<IOptions<ServerConfiguration>>();
        
        var config = new ServerConfiguration
        {
            UnityHost = "localhost",
            UnityStatePort = 16402, // Different port for testing
            ConnectionTimeoutSeconds = 5.0,
            BufferSize = 4096
        };
        
        _configMock.Setup(x => x.Value).Returns(config);
        _cancellationSource = new CancellationTokenSource();
    }
    
    [TearDown]
    public void TearDown()
    {
        _cancellationSource?.Cancel();
        _service?.Dispose();
        _mockUnityStateListener?.Stop();
        _mockUnityStateThread?.Join(1000);
    }
    
    [Test]
    public async Task ConnectAsync_ShouldEstablishConnection()
    {
        // Arrange
        StartMockUnityStateServer();
        _service = new UnityStateConnectionService(_loggerMock!.Object, _configMock!.Object);
        
        // Act
        var result = await _service.ConnectAsync();
        
        // Assert
        Assert.That(result, Is.True);
        Assert.That(_service.IsConnected, Is.True);
    }
    
    [Test]
    public async Task ConnectAsync_ShouldReceiveInitialState()
    {
        // Arrange
        JObject? receivedState = null;
        var stateReceivedEvent = new ManualResetEventSlim(false);
        
        StartMockUnityStateServer(sendInitialState: true);
        _service = new UnityStateConnectionService(_loggerMock!.Object, _configMock!.Object);
        _service.UnityStateChanged += (state) =>
        {
            receivedState = state;
            stateReceivedEvent.Set();
        };
        
        // Act
        var connected = await _service.ConnectAsync();
        var stateReceived = stateReceivedEvent.Wait(TimeSpan.FromSeconds(2));
        
        // Assert
        Assert.That(connected, Is.True);
        Assert.That(stateReceived, Is.True);
        Assert.That(receivedState, Is.Not.Null);
        Assert.That(receivedState!["runmode"]?.ToString(), Is.EqualTo("EditMode_Scene"));
        Assert.That(receivedState["context"]?.ToString(), Is.EqualTo("Running"));
    }
    
    [Test]
    public async Task StateConnection_ShouldReceiveStateChanges()
    {
        // Arrange
        var stateChanges = new List<JObject>();
        var stateChangeEvent = new CountdownEvent(2); // Expect initial state + 1 change
        
        StartMockUnityStateServer(sendInitialState: true, sendStateChanges: true);
        _service = new UnityStateConnectionService(_loggerMock!.Object, _configMock!.Object);
        _service.UnityStateChanged += (state) =>
        {
            stateChanges.Add(state);
            stateChangeEvent.Signal();
        };
        
        // Act
        var connected = await _service.ConnectAsync();
        var allStatesReceived = stateChangeEvent.Wait(TimeSpan.FromSeconds(3));
        
        // Assert
        Assert.That(connected, Is.True);
        Assert.That(allStatesReceived, Is.True);
        Assert.That(stateChanges.Count, Is.GreaterThanOrEqualTo(2));
        
        // Check initial state
        Assert.That(stateChanges[0]["runmode"]?.ToString(), Is.EqualTo("EditMode_Scene"));
        
        // Check state change
        var lastChange = stateChanges.Last()["lastChange"] as JObject;
        Assert.That(lastChange, Is.Not.Null);
        Assert.That(lastChange!["stateType"]?.ToString(), Is.EqualTo("runmode"));
        Assert.That(lastChange["previousValue"]?.ToString(), Is.EqualTo("EditMode_Scene"));
        Assert.That(lastChange["newValue"]?.ToString(), Is.EqualTo("PlayMode"));
    }
    
    [Test]
    public async Task ConnectAsync_ShouldFailWhenNoServerRunning()
    {
        // Arrange
        _service = new UnityStateConnectionService(_loggerMock!.Object, _configMock!.Object);
        
        // Act
        var result = await _service.ConnectAsync();
        
        // Assert
        Assert.That(result, Is.False);
        Assert.That(_service.IsConnected, Is.False);
    }
    
    [Test]
    public void Disconnect_ShouldCloseConnection()
    {
        // Arrange
        StartMockUnityStateServer();
        _service = new UnityStateConnectionService(_loggerMock!.Object, _configMock!.Object);
        _service.ConnectAsync().Wait();
        
        // Act
        _service.Disconnect();
        
        // Assert
        Assert.That(_service.IsConnected, Is.False);
    }
    
    private void StartMockUnityStateServer(bool sendInitialState = false, bool sendStateChanges = false)
    {
        _mockUnityStateListener = new TcpListener(IPAddress.Loopback, 16402);
        _mockUnityStateListener.Start();
        
        _mockUnityStateThread = new Thread(async () =>
        {
            try
            {
                while (!_cancellationSource!.Token.IsCancellationRequested)
                {
                    var tcpClient = await AcceptClientAsync(_mockUnityStateListener, _cancellationSource.Token);
                    if (tcpClient == null) break;
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using (tcpClient)
                            using (var stream = tcpClient.GetStream())
                            {
                                if (sendInitialState)
                                {
                                    // Send initial state
                                    var initialState = new
                                    {
                                        type = "state_update",
                                        @params = new JObject
                                        {
                                            ["runmode"] = "EditMode_Scene",
                                            ["context"] = "Running",
                                            ["canModifyProjectFiles"] = true,
                                            ["isEditorResponsive"] = true,
                                            ["timestamp"] = DateTime.UtcNow.ToString("o")
                                        }
                                    };
                                    
                                    await SendJsonMessage(stream, initialState);
                                    await Task.Delay(100);
                                }
                                
                                if (sendStateChanges)
                                {
                                    // Send state change after a delay
                                    await Task.Delay(500);
                                    
                                    var stateChange = new
                                    {
                                        type = "state_change",
                                        @params = new JObject
                                        {
                                            ["stateType"] = "runmode",
                                            ["previousValue"] = "EditMode_Scene",
                                            ["newValue"] = "PlayMode",
                                            ["timestamp"] = DateTime.UtcNow.ToString("o"),
                                            ["currentRunmode"] = "PlayMode",
                                            ["currentContext"] = "Running"
                                        }
                                    };
                                    
                                    await SendJsonMessage(stream, stateChange);
                                }
                                
                                // Keep connection open
                                while (!_cancellationSource.Token.IsCancellationRequested && tcpClient.Connected)
                                {
                                    await Task.Delay(100);
                                }
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        });
        
        _mockUnityStateThread.Start();
        Thread.Sleep(100); // Give server time to start
    }
    
    private static async Task<TcpClient?> AcceptClientAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            using (cancellationToken.Register(() => listener.Stop()))
            {
                return await listener.AcceptTcpClientAsync();
            }
        }
        catch
        {
            return null;
        }
    }
    
    private static async Task SendJsonMessage(NetworkStream stream, object message)
    {
        var json = JsonConvert.SerializeObject(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes, 0, bytes.Length);
        await stream.FlushAsync();
    }
}
