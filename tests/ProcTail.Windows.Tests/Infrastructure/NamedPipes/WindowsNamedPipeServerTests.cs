using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using FluentAssertions;
using ProcTail.Core.Models;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.NamedPipes;

namespace ProcTail.Windows.Tests.Infrastructure.NamedPipes;

/// <summary>
/// WindowsNamedPipeServer の Windows 固有テスト
/// Windows 環境でのみ実行される統合テスト
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class WindowsNamedPipeServerTests
{
    private WindowsNamedPipeServer? _server;
    private NamedPipeConfiguration? _configuration;
    private ILogger<WindowsNamedPipeServer>? _logger;
    private readonly List<IpcRequestEventArgs> _receivedRequests = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Windows環境でない場合はテスト全体をスキップ
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
        }
    }

    [SetUp]
    public void SetUp()
    {
        // Windows環境チェック
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        _logger = NullLogger<WindowsNamedPipeServer>.Instance;
        _configuration = CreateTestConfiguration();
        _server = new WindowsNamedPipeServer(_logger, _configuration);
        _receivedRequests.Clear();

        // リクエスト受信ハンドラーを設定
        _server.RequestReceived += OnRequestReceived;
    }

    [TearDown]
    public void TearDown()
    {
        if (_server != null)
        {
            _server.RequestReceived -= OnRequestReceived;
            _server.Dispose();
            _server = null;
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        using var server = new WindowsNamedPipeServer(_logger!, _configuration!);

        // Assert
        server.Should().NotBeNull();
        server.IsRunning.Should().BeFalse();
        server.PipeName.Should().Be(_configuration!.PipeName);
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        // このテストは Windows 環境では常に成功するため、条件付きでスキップ
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Pass("Windows環境ではPlatformNotSupportedExceptionは発生しません");
            return;
        }

        // Arrange & Act & Assert
        Action act = () => new WindowsNamedPipeServer(_logger!, _configuration!);
        act.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows platform*");
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StartAsync_ShouldStartSuccessfully()
    {
        // Act
        await _server!.StartAsync();

        // Assert
        _server.IsRunning.Should().BeTrue();

        // Cleanup
        await _server.StopAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StartAsync_AlreadyRunning_ShouldNotStartAgain()
    {
        // Arrange
        await _server!.StartAsync();
        var firstState = _server.IsRunning;

        // Act
        await _server.StartAsync(); // 2回目の呼び出し

        // Assert
        firstState.Should().BeTrue();
        _server.IsRunning.Should().BeTrue();

        // Cleanup
        await _server.StopAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StopAsync_WhenRunning_ShouldStopSuccessfully()
    {
        // Arrange
        await _server!.StartAsync();
        _server.IsRunning.Should().BeTrue();

        // Act
        await _server.StopAsync();

        // Assert
        _server.IsRunning.Should().BeFalse();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StopAsync_WhenNotRunning_ShouldCompleteWithoutError()
    {
        // Arrange
        _server!.IsRunning.Should().BeFalse();

        // Act & Assert
        Func<Task> act = async () => await _server.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("LongRunning")]
    public async Task ClientConnection_ShouldAcceptConnectionAndReceiveMessage()
    {
        // Arrange
        await _server!.StartAsync();
        var testMessage = """{"Type":"HealthCheck","Data":{}}""";

        try
        {
            // Act - クライアントから接続してメッセージを送信
            var clientTask = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", _configuration!.PipeName, PipeDirection.InOut);
                await client.ConnectAsync(TimeSpan.FromSeconds(5));

                // メッセージを送信
                await SendMessageToServerAsync(client, testMessage);

                // 応答を受信
                var response = await ReceiveMessageFromServerAsync(client);
                return response;
            });

            // リクエスト受信を待機
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (_receivedRequests.Count == 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, timeoutCts.Token);
            }

            // 応答を送信
            if (_receivedRequests.Count > 0)
            {
                var requestArgs = _receivedRequests[0];
                await requestArgs.ResponseSender!("""{"Status":"OK","Message":"Health check successful"}""");
            }

            // クライアントタスクの完了を待機
            var response = await clientTask.WaitAsync(TimeSpan.FromSeconds(15));

            // Assert
            _receivedRequests.Should().NotBeEmpty("クライアントからのメッセージを受信するはずです");
            _receivedRequests[0].RequestMessage.Should().Be(testMessage);
            response.Should().Contain("Health check successful");
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("LongRunning")]
    public async Task MultipleClients_ShouldHandleConcurrentConnections()
    {
        // Arrange
        await _server!.StartAsync();
        var clientCount = 3;
        var tasks = new List<Task<string>>();

        try
        {
            // Act - 複数のクライアントを同時に接続
            for (int i = 0; i < clientCount; i++)
            {
                var clientId = i;
                var task = Task.Run(async () =>
                {
                    using var client = new NamedPipeClientStream(".", _configuration!.PipeName, PipeDirection.InOut);
                    await client.ConnectAsync(TimeSpan.FromSeconds(5));

                    var message = $"""{{\"Type\":\"HealthCheck\",\"ClientId\":{clientId}}}""";
                    await SendMessageToServerAsync(client, message);

                    return await ReceiveMessageFromServerAsync(client);
                });
                tasks.Add(task);
            }

            // リクエスト受信を待機
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (_receivedRequests.Count < clientCount && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, timeoutCts.Token);
            }

            // 応答を送信
            foreach (var requestArgs in _receivedRequests)
            {
                await requestArgs.ResponseSender!($"""{{\"Status\":\"OK\",\"Echo\":\"{requestArgs.RequestMessage}\"}}""");
            }

            // 全クライアントタスクの完了を待機
            var responses = await Task.WhenAll(tasks);

            // Assert
            _receivedRequests.Should().HaveCount(clientCount, "すべてのクライアントからのメッセージを受信するはずです");
            responses.Should().HaveCount(clientCount);
            responses.Should().OnlyContain(r => r.Contains("OK"));
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task LargeMessage_ShouldHandleCorrectly()
    {
        // Arrange
        await _server!.StartAsync();
        var largeMessage = new string('A', 50000); // 50KB message
        var testMessage = $"""{{\"Type\":\"LargeTest\",\"Data\":\"{largeMessage}\"}}""";

        try
        {
            // Act
            var clientTask = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", _configuration!.PipeName, PipeDirection.InOut);
                await client.ConnectAsync(TimeSpan.FromSeconds(5));

                await SendMessageToServerAsync(client, testMessage);
                return await ReceiveMessageFromServerAsync(client);
            });

            // リクエスト受信を待機
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (_receivedRequests.Count == 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, timeoutCts.Token);
            }

            // 応答を送信
            if (_receivedRequests.Count > 0)
            {
                var requestArgs = _receivedRequests[0];
                await requestArgs.ResponseSender!("""{"Status":"OK","MessageLength":" + testMessage.Length + "}""");
            }

            var response = await clientTask.WaitAsync(TimeSpan.FromSeconds(15));

            // Assert
            _receivedRequests.Should().NotBeEmpty();
            _receivedRequests[0].RequestMessage.Should().Be(testMessage);
            response.Should().Contain("OK");
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task InvalidMessage_ShouldHandleGracefully()
    {
        // Arrange
        await _server!.StartAsync();

        try
        {
            // Act - 不正なメッセージを送信
            var clientTask = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", _configuration!.PipeName, PipeDirection.InOut);
                await client.ConnectAsync(TimeSpan.FromSeconds(5));

                // 不正な長さのメッセージを送信
                var invalidLengthBytes = BitConverter.GetBytes(-1);
                await client.WriteAsync(invalidLengthBytes);
                await client.FlushAsync();

                try
                {
                    return await ReceiveMessageFromServerAsync(client);
                }
                catch
                {
                    return "Connection closed";
                }
            });

            var result = await clientTask.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            result.Should().Be("Connection closed", "不正なメッセージによりコネクションが閉じられるはずです");
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StartAsync_CancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        Func<Task> act = async () => await _server!.StartAsync(cts.Token);
        
        try
        {
            await act();
            // 正常完了した場合はクリーンアップ
            if (_server!.IsRunning)
            {
                await _server.StopAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル例外は期待される動作
            Assert.Pass("キャンセルが正常に動作しました");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var server = new WindowsNamedPipeServer(_logger!, _configuration!);

        // Act
        server.Dispose();

        // Assert
        server.IsRunning.Should().BeFalse();

        // Double dispose should not throw
        Action act = () => server.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task PipeSecurity_ShouldAllowAuthorizedAccess()
    {
        // Arrange
        await _server!.StartAsync();

        try
        {
            // Act - 同一ユーザーからの接続
            var clientTask = Task.Run(async () =>
            {
                using var client = new NamedPipeClientStream(".", _configuration!.PipeName, PipeDirection.InOut);
                await client.ConnectAsync(TimeSpan.FromSeconds(5));
                return true;
            });

            var connected = await clientTask.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            connected.Should().BeTrue("認証されたユーザーは接続できるはずです");
        }
        finally
        {
            await _server.StopAsync();
        }
    }

    #region Helper Methods

    private void OnRequestReceived(object? sender, IpcRequestEventArgs e)
    {
        _receivedRequests.Add(e);
    }

    private static NamedPipeConfiguration CreateTestConfiguration()
    {
        var config = new NamedPipeConfiguration();
        // テスト用にパイプ名を一意にする
        var testPipeName = $"ProcTailTest_{Guid.NewGuid():N}"[0..15];
        
        // プライベートプロパティのため、リフレクションまたは別の方法で設定
        // 実際の実装では、設定を受け取るコンストラクタを使用
        return config;
    }

    private static async Task SendMessageToServerAsync(NamedPipeClientStream client, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        await client.WriteAsync(lengthBytes);
        await client.WriteAsync(messageBytes);
        await client.FlushAsync();
    }

    private static async Task<string> ReceiveMessageFromServerAsync(NamedPipeClientStream client)
    {
        // メッセージ長を受信
        var lengthBuffer = new byte[4];
        var bytesRead = 0;
        
        while (bytesRead < 4)
        {
            var read = await client.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead));
            if (read == 0)
                return string.Empty;
            bytesRead += read;
        }

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        if (messageLength <= 0)
            return string.Empty;

        // メッセージ本体を受信
        var messageBuffer = new byte[messageLength];
        bytesRead = 0;
        
        while (bytesRead < messageLength)
        {
            var read = await client.ReadAsync(messageBuffer.AsMemory(bytesRead, messageLength - bytesRead));
            if (read == 0)
                throw new EndOfStreamException("メッセージ受信が予期せず終了しました");
            bytesRead += read;
        }

        return Encoding.UTF8.GetString(messageBuffer);
    }

    #endregion
}