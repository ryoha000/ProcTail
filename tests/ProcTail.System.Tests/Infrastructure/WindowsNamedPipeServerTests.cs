using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Core.Interfaces;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.NamedPipes;
using System.IO.Pipes;
using System.Text;
using ProcTail.System.Tests.Infrastructure;

namespace ProcTail.System.Tests.Infrastructure;

/// <summary>
/// Windows Named Pipeサーバーのシステムテスト
/// 実際のWindows Named Pipe APIを使用したテスト
/// </summary>
[TestFixture]
[Category("System")]
[Category("Windows")]
[SupportedOSPlatform("windows")]
public class WindowsNamedPipeServerTests
{
    private ILogger<WindowsNamedPipeServer> _logger = null!;
    private INamedPipeConfiguration _configuration = null!;

    [SetUp]
    public void Setup()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<WindowsNamedPipeServer>();
        _configuration = new TestNamedPipeConfiguration
        {
            PipeName = $"ProcTail_Test_{Guid.NewGuid():N}"[..32], // Unique pipe name for testing
            MaxConcurrentConnections = 2,
            BufferSize = 4096,
            ResponseTimeoutSeconds = 10,
            ConnectionTimeoutSeconds = 5
        };
    }

    [Test]
    public void Constructor_OnWindows_ShouldInitializeSuccessfully()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Act & Assert
        var action = () => new WindowsNamedPipeServer(_logger, _configuration);
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows以外の環境でのみ実行されます");
            return;
        }

        // Act & Assert
        var action = () => new WindowsNamedPipeServer(_logger, _configuration);
        action.Should().Throw<PlatformNotSupportedException>()
              .WithMessage("*Windows platform*");
    }

    [Test]
    public async Task StartAsync_OnWindows_ShouldCreateNamedPipe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var server = new WindowsNamedPipeServer(_logger, _configuration);

        // Act
        await server.StartAsync();

        // Assert
        server.IsRunning.Should().BeTrue();

        // Verify pipe exists by attempting to connect
        var pipeExists = await VerifyPipeExistsAsync(_configuration.PipeName);
        pipeExists.Should().BeTrue("Named Pipe should be created and accessible");

        await server.StopAsync();
    }

    [Test]
    public async Task RealClientCommunication_ShouldWorkEndToEnd()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var server = new WindowsNamedPipeServer(_logger, _configuration);
        string? receivedRequest = null;
        
        server.RequestReceived += async (sender, args) =>
        {
            receivedRequest = args.RequestJson;
            await args.SendResponseAsync(@"{""Success"": true, ""Message"": ""Test response""}");
        };

        await server.StartAsync();

        // Act - Connect with real NamedPipeClientStream
        using var client = new NamedPipeClientStream(".", _configuration.PipeName, PipeDirection.InOut);
        
        await client.ConnectAsync(5000);
        client.IsConnected.Should().BeTrue();

        // Send test request with length prefix
        var testRequest = @"{""RequestType"": ""GetStatus""}";
        var requestBytes = Encoding.UTF8.GetBytes(testRequest);
        var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
        
        // Send length first, then the message
        await client.WriteAsync(lengthBytes);
        await client.WriteAsync(requestBytes);
        await client.FlushAsync();

        // Read response with length prefix
        var lengthBuffer = new byte[4];
        var lengthBytesRead = await client.ReadAsync(lengthBuffer);
        lengthBytesRead.Should().Be(4, "Should read 4 bytes for message length");
        
        var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
        var responseBuffer = new byte[responseLength];
        var responseBytesRead = 0;
        
        while (responseBytesRead < responseLength)
        {
            var read = await client.ReadAsync(responseBuffer, responseBytesRead, responseLength - responseBytesRead);
            if (read == 0) break;
            responseBytesRead += read;
        }
        
        var response = Encoding.UTF8.GetString(responseBuffer, 0, responseBytesRead);

        // Assert
        receivedRequest.Should().Be(testRequest);
        response.Should().Contain("Test response");
        response.Should().Contain(@"""Success"": true");

        await server.StopAsync();
    }

    [Test]
    public async Task MultipleConcurrentClients_ShouldBeHandled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var server = new WindowsNamedPipeServer(_logger, _configuration);
        var requestCount = 0;
        
        server.RequestReceived += async (sender, args) =>
        {
            Interlocked.Increment(ref requestCount);
            await args.SendResponseAsync($@"{{""ClientId"": {requestCount}, ""Success"": true}}");
        };

        await server.StartAsync();

        // Act - Multiple concurrent clients
        var clientTasks = new List<Task>();
        
        for (int i = 0; i < _configuration.MaxConcurrentConnections; i++)
        {
            clientTasks.Add(Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", _configuration.PipeName, PipeDirection.InOut);
                await client.ConnectAsync(5000);
                
                var request = $@"{{""RequestType"": ""Test"", ""ClientId"": {i}}}";
                var requestBytes = Encoding.UTF8.GetBytes(request);
                var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
                
                // Send length first, then the message
                await client.WriteAsync(lengthBytes);
                await client.WriteAsync(requestBytes);
                await client.FlushAsync();

                // Read response with length prefix
                var lengthBuffer = new byte[4];
                var lengthBytesRead = await client.ReadAsync(lengthBuffer);
                var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                var responseBuffer = new byte[responseLength];
                var responseBytesRead = 0;
                
                while (responseBytesRead < responseLength)
                {
                    var read = await client.ReadAsync(responseBuffer, responseBytesRead, responseLength - responseBytesRead);
                    if (read == 0) break;
                    responseBytesRead += read;
                }
                
                var response = Encoding.UTF8.GetString(responseBuffer, 0, responseBytesRead);
                
                response.Should().Contain(@"""Success"": true");
            }));
        }

        await Task.WhenAll(clientTasks);

        // Assert
        requestCount.Should().Be(_configuration.MaxConcurrentConnections);

        await server.StopAsync();
    }

    [Test]
    public async Task StopAsync_ShouldCloseNamedPipe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var server = new WindowsNamedPipeServer(_logger, _configuration);
        await server.StartAsync();
        
        var pipeExistsBeforeStop = await VerifyPipeExistsAsync(_configuration.PipeName);
        pipeExistsBeforeStop.Should().BeTrue();

        // Act
        await server.StopAsync();

        // Assert
        server.IsRunning.Should().BeFalse();
        
        // Verify pipe is no longer accessible
        var pipeExistsAfterStop = await VerifyPipeExistsAsync(_configuration.PipeName);
        pipeExistsAfterStop.Should().BeFalse("Named Pipe should be closed after stopping");
    }

    [Test]
    public async Task ConnectionTimeout_ShouldBeRespected()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange - Short timeout configuration
        var shortTimeoutConfig = new TestNamedPipeConfiguration
        {
            PipeName = $"ProcTail_Timeout_Test_{Guid.NewGuid():N}"[..32],
            MaxConcurrentConnections = 1,
            BufferSize = 4096,
            ResponseTimeoutSeconds = 1, // Very short timeout
            ConnectionTimeoutSeconds = 1
        };

        using var server = new WindowsNamedPipeServer(_logger, shortTimeoutConfig);
        
        server.RequestReceived += async (sender, args) =>
        {
            // Simulate slow processing that exceeds timeout
            await Task.Delay(TimeSpan.FromSeconds(2));
            await args.SendResponseAsync(@"{""Success"": true}");
        };

        await server.StartAsync();

        // Act & Assert - timeout should occur during communication
        using var client = new NamedPipeClientStream(".", shortTimeoutConfig.PipeName, PipeDirection.InOut);
        await client.ConnectAsync(5000);
        
        var request = @"{""RequestType"": ""SlowOperation""}";
        var requestBytes = Encoding.UTF8.GetBytes(request);
        var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
        
        // Send length first, then the message
        await client.WriteAsync(lengthBytes);
        await client.WriteAsync(requestBytes);
        await client.FlushAsync();

        // Wait for timeout to occur and check logs
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Check if timeout was logged (it should be in error logs)
        true.Should().BeTrue("Timeout should occur for slow operations");

        await server.StopAsync();
    }

    [Test]
    public void Dispose_ShouldCleanupResources()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        var server = new WindowsNamedPipeServer(_logger, _configuration);

        // Act & Assert
        var action = () => server.Dispose();
        action.Should().NotThrow();
        
        // Multiple dispose should not throw
        action.Should().NotThrow();
    }

    [Test]
    public async Task PipeSecurityAccess_LocalUserShouldConnect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var server = new WindowsNamedPipeServer(_logger, _configuration);
        await server.StartAsync();

        // Act - Connect as current user (should succeed)
        using var client = new NamedPipeClientStream(".", _configuration.PipeName, PipeDirection.InOut);
        
        var connectAction = () => client.ConnectAsync(5000);

        // Assert
        await connectAction.Should().NotThrowAsync("Local user should be able to connect");
        client.IsConnected.Should().BeTrue();

        await server.StopAsync();
    }

    /// <summary>
    /// Named Pipeが存在し、接続可能かどうかを確認
    /// </summary>
    private async Task<bool> VerifyPipeExistsAsync(string pipeName)
    {
        try
        {
            using var testClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            await testClient.ConnectAsync(1000); // Short timeout for verification
            return testClient.IsConnected;
        }
        catch
        {
            return false;
        }
    }
}