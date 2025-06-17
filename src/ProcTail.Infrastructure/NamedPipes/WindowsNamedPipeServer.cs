using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcTail.Infrastructure.NamedPipes;

/// <summary>
/// Windows Named Pipeサーバーの実装
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsNamedPipeServer : INamedPipeServer, IDisposable
{
    private readonly ILogger<WindowsNamedPipeServer> _logger;
    private readonly INamedPipeConfiguration _configuration;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Task> _clientTasks = new();
    private readonly object _lock = new();
    private bool _isRunning;
    private bool _disposed;
    private Task? _serverTask;

    /// <summary>
    /// IPC要求受信イベント
    /// </summary>
    public event EventHandler<IpcRequestEventArgs>? RequestReceived;

    /// <summary>
    /// サーバーが実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// パイプ名
    /// </summary>
    public string PipeName => _configuration.PipeName;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public WindowsNamedPipeServer(
        ILogger<WindowsNamedPipeServer> logger,
        INamedPipeConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Windows環境のチェック
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("WindowsNamedPipeServer is only supported on Windows platform");
        }
    }

    /// <summary>
    /// Named Pipeサーバーを開始
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsNamedPipeServer));

        if (_isRunning)
        {
            _logger.LogWarning("Named Pipeサーバーは既に実行中です");
            return;
        }

        try
        {
            _logger.LogInformation("Named Pipeサーバーを開始しています... (パイプ名: {PipeName})", _configuration.PipeName);

            // サーバータスクを開始
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);

            // サーバーが開始されるまで少し待機
            await Task.Delay(100, cancellationToken);

            _isRunning = true;
            _logger.LogInformation("Named Pipeサーバーが正常に開始されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Named Pipeサーバー開始中にエラーが発生しました");
            await StopAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Named Pipeサーバーを停止
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Named Pipeサーバーを停止しています...");

            _cancellationTokenSource.Cancel();

            // サーバータスクの完了を待機
            if (_serverTask != null)
            {
                try
                {
                    await _serverTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("サーバータスクの停止がタイムアウトしました");
                }
            }

            // 実行中のクライアントタスクの完了を待機
            lock (_lock)
            {
                Task.WaitAll(_clientTasks.ToArray(), TimeSpan.FromSeconds(3));
                _clientTasks.Clear();
            }

            _isRunning = false;
            _logger.LogInformation("Named Pipeサーバーが正常に停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Named Pipeサーバー停止中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// サーバー実行ループ
    /// </summary>
    private async Task RunServerAsync()
    {
        _logger.LogInformation("Named Pipeサーバーループを開始しました");

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Named Pipeサーバーを作成
                    using var pipeServer = CreateNamedPipeServerStream();
                    
                    _logger.LogDebug("クライアント接続を待機中...");

                    // クライアント接続を待機
                    await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);
                    
                    _logger.LogDebug("クライアントが接続されました");

                    // クライアント処理タスクを開始
                    var clientTask = Task.Run(async () => await HandleClientAsync(pipeServer), _cancellationTokenSource.Token);
                    
                    lock (_lock)
                    {
                        _clientTasks.Add(clientTask);
                        
                        // 完了したタスクを削除
                        _clientTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "クライアント接続処理中にエラーが発生しました");
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Named Pipeサーバーループが停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Named Pipeサーバーループでエラーが発生しました");
        }
    }

    /// <summary>
    /// Named Pipeサーバーストリームを作成
    /// </summary>
    private NamedPipeServerStream CreateNamedPipeServerStream()
    {
        var pipeSecurity = CreatePipeSecurity();
        
        return NamedPipeServerStreamAcl.Create(
            _configuration.PipeName,
            PipeDirection.InOut,
            _configuration.MaxConcurrentConnections,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            _configuration.BufferSize,
            _configuration.BufferSize,
            pipeSecurity);
    }

    /// <summary>
    /// パイプセキュリティを作成
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static PipeSecurity CreatePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();
        
        // 現在のユーザーにフルアクセス権限を付与
        var currentUser = WindowsIdentity.GetCurrent();
        var userRule = new PipeAccessRule(currentUser.User!, PipeAccessRights.FullControl, AccessControlType.Allow);
        pipeSecurity.AddAccessRule(userRule);
        
        // Administratorsグループにフルアクセス権限を付与
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var adminRule = new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow);
        pipeSecurity.AddAccessRule(adminRule);
        
        return pipeSecurity;
    }

    /// <summary>
    /// クライアント処理
    /// </summary>
    private async Task HandleClientAsync(NamedPipeServerStream pipeStream)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogDebug("クライアント処理を開始しました (ID: {ClientId})", clientId);

        try
        {
            while (pipeStream.IsConnected && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // メッセージを受信
                    var requestMessage = await ReceiveMessageAsync(pipeStream, _cancellationTokenSource.Token);
                    
                    if (string.IsNullOrEmpty(requestMessage))
                    {
                        _logger.LogDebug("空のメッセージを受信しました (ClientId: {ClientId})", clientId);
                        continue;
                    }

                    _logger.LogDebug("メッセージを受信しました (ClientId: {ClientId}, Length: {Length})", 
                        clientId, requestMessage.Length);

                    // IPC要求イベントを発火
                    var eventArgs = new IpcRequestEventArgs(requestMessage, _cancellationTokenSource.Token);
                    
                    // 応答送信ハンドラーを設定
                    eventArgs.ResponseSender = async (response) =>
                    {
                        await SendMessageAsync(pipeStream, response, _cancellationTokenSource.Token);
                    };

                    RequestReceived?.Invoke(this, eventArgs);

                    // 応答を待機
                    await eventArgs.WaitForResponseAsync(TimeSpan.FromSeconds(_configuration.ResponseTimeoutSeconds));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex) when (ex.Message.Contains("pipe"))
                {
                    _logger.LogDebug("パイプが切断されました (ClientId: {ClientId})", clientId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "クライアント通信中にエラーが発生しました (ClientId: {ClientId})", clientId);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "クライアント処理中にエラーが発生しました (ClientId: {ClientId})", clientId);
        }
        finally
        {
            try
            {
                if (pipeStream.IsConnected)
                {
                    pipeStream.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "パイプ切断中に警告が発生しました (ClientId: {ClientId})", clientId);
            }

            _logger.LogDebug("クライアント処理を終了しました (ClientId: {ClientId})", clientId);
        }
    }

    /// <summary>
    /// メッセージを受信
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(NamedPipeServerStream pipeStream, CancellationToken cancellationToken)
    {
        // メッセージ長を受信
        var lengthBuffer = new byte[4];
        var bytesRead = 0;
        
        while (bytesRead < 4)
        {
            var read = await pipeStream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
            if (read == 0)
                return string.Empty;
            bytesRead += read;
        }

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        if (messageLength <= 0 || messageLength > 1024 * 1024) // 1MB制限
        {
            throw new InvalidOperationException($"無効なメッセージ長: {messageLength}");
        }

        // メッセージ本体を受信
        var messageBuffer = new byte[messageLength];
        bytesRead = 0;
        
        while (bytesRead < messageLength)
        {
            var read = await pipeStream.ReadAsync(messageBuffer.AsMemory(bytesRead, messageLength - bytesRead), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("メッセージ受信が予期せず終了しました");
            bytesRead += read;
        }

        return Encoding.UTF8.GetString(messageBuffer);
    }

    /// <summary>
    /// メッセージを送信
    /// </summary>
    private static async Task SendMessageAsync(NamedPipeServerStream pipeStream, string message, CancellationToken cancellationToken)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // メッセージ長を送信
        await pipeStream.WriteAsync(lengthBytes, cancellationToken);
        
        // メッセージ本体を送信
        await pipeStream.WriteAsync(messageBytes, cancellationToken);
        
        await pipeStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WindowsNamedPipeServer解放中にエラーが発生しました");
        }

        _cancellationTokenSource.Dispose();
        _logger.LogInformation("WindowsNamedPipeServerが解放されました");
    }
}