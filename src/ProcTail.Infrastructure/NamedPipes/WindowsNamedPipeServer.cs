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
    private int _serverInstanceCount = 0;

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
            _logger.LogInformation("=== Named Pipeサーバーを開始しています ===");
            _logger.LogInformation("Configuration: PipeName={PipeName}, MaxConnections={MaxConnections}, BufferSize={BufferSize}", 
                _configuration.PipeName, _configuration.MaxConcurrentConnections, _configuration.BufferSize);

            // サーバータスクを開始
            _logger.LogInformation("サーバータスクを開始中...");
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);
            _logger.LogInformation("サーバータスクが開始されました");

            // サーバーが開始されるまで少し待機
            _logger.LogInformation("サーバー初期化を待機中...");
            await Task.Delay(100, cancellationToken);

            _isRunning = true;
            _logger.LogInformation("=== Named Pipeサーバーが正常に開始されました ===");
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
                    NamedPipeServerStream? pipeServer = null;
                    try
                    {
                        pipeServer = CreateNamedPipeServerStream();
                        
                        _logger.LogDebug("クライアント接続を待機中...");

                        // クライアント接続を待機
                        await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        pipeServer?.Dispose();
                        throw;
                    }
                    catch (Exception)
                    {
                        pipeServer?.Dispose();
                        // インスタンスカウントをデクリメント
                        Interlocked.Decrement(ref _serverInstanceCount);
                        throw;
                    }
                    
                    _logger.LogDebug("クライアントが接続されました");

                    // クライアント処理タスクを開始（タスク内でpipeServerを破棄）
                    var clientTask = Task.Run(async () => 
                    {
                        try
                        {
                            await HandleClientAsync(pipeServer);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "クライアント処理中に予期しないエラーが発生しました");
                        }
                        finally
                        {
                            try
                            {
                                if (pipeServer.IsConnected)
                                {
                                    pipeServer.Disconnect();
                                }
                            }
                            catch { }
                            
                            pipeServer?.Dispose();
                            // インスタンスカウントをデクリメント
                            Interlocked.Decrement(ref _serverInstanceCount);
                        }
                    }, _cancellationTokenSource.Token);
                    
                    lock (_lock)
                    {
                        // 完了したタスクを削除
                        _clientTasks.RemoveAll(t => t.IsCompleted);
                        
                        // 最大接続数をチェック
                        if (_clientTasks.Count >= _configuration.MaxConcurrentConnections)
                        {
                            _logger.LogWarning("最大同時接続数({MaxConnections})に達しています。古い接続を削除します", 
                                _configuration.MaxConcurrentConnections);
                            
                            // 古いタスクから順に削除
                            while (_clientTasks.Count >= _configuration.MaxConcurrentConnections && _clientTasks.Count > 0)
                            {
                                var oldestTask = _clientTasks[0];
                                _clientTasks.RemoveAt(0);
                                
                                // タスクが完了していない場合は待機
                                if (!oldestTask.IsCompleted)
                                {
                                    try
                                    {
                                        oldestTask.Wait(1000);
                                    }
                                    catch { }
                                }
                            }
                        }
                        
                        _clientTasks.Add(clientTask);
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
        
        var isFirstInstance = Interlocked.Increment(ref _serverInstanceCount) == 1;
        var pipeOptions = PipeOptions.Asynchronous;
        
        // 最初のインスタンスの場合はFirstPipeInstanceフラグを追加
        if (isFirstInstance)
        {
            pipeOptions |= PipeOptions.FirstPipeInstance;
        }
        
        return NamedPipeServerStreamAcl.Create(
            _configuration.PipeName,
            PipeDirection.InOut,
            _configuration.MaxConcurrentConnections, // 設定値に基づいた最大接続数
            PipeTransmissionMode.Message,
            pipeOptions,
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
                    _logger.LogDebug("メッセージ受信を開始します (ClientId: {ClientId})", clientId);
                    var requestMessage = await ReceiveMessageAsync(pipeStream, _cancellationTokenSource.Token);
                    
                    if (string.IsNullOrEmpty(requestMessage))
                    {
                        _logger.LogDebug("空のメッセージを受信しました (ClientId: {ClientId})", clientId);
                        continue;
                    }

                    _logger.LogDebug("メッセージを受信しました (ClientId: {ClientId}, Length: {Length})", 
                        clientId, requestMessage.Length);

                    // IPC要求イベントを発火
                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
                    var eventArgs = new IpcRequestEventArgs(requestMessage, requestCts.Token);
                    
                    // 応答送信ハンドラーを設定
                    eventArgs.ResponseSender = async (response) =>
                    {
                        await SendMessageAsync(pipeStream, response, requestCts.Token);
                    };

                    RequestReceived?.Invoke(this, eventArgs);

                    try
                    {
                        // 応答を待機
                        await eventArgs.WaitForResponseAsync(TimeSpan.FromSeconds(_configuration.ResponseTimeoutSeconds));
                    }
                    catch (TimeoutException)
                    {
                        // タイムアウト時はイベントハンドラーをキャンセル
                        requestCts.Cancel();
                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException ex)
                {
                    _logger.LogError(ex, "クライアント通信中にエラーが発生しました (ClientId: {ClientId})", clientId);
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
    private async Task<string> ReceiveMessageAsync(NamedPipeServerStream pipeStream, CancellationToken cancellationToken)
    {
        try
        {
            // メッセージ長を受信
            var lengthBuffer = new byte[4];
            var bytesRead = 0;
            
            _logger.LogDebug("メッセージ長の読み取りを開始します");
            
            while (bytesRead < 4)
            {
                var read = await pipeStream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
                if (read == 0)
                {
                    _logger.LogDebug("メッセージ長の読み取り中に接続が切断されました");
                    return string.Empty;
                }
                bytesRead += read;
            }

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            _logger.LogDebug("メッセージ長を読み取りました: {MessageLength}", messageLength);
            
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

            var message = Encoding.UTF8.GetString(messageBuffer);
            _logger.LogDebug("メッセージを受信しました: {MessagePreview}", message.Length > 100 ? message[..100] + "..." : message);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ受信中にエラーが発生しました");
            throw;
        }
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
        _serverInstanceCount = 0;
        _logger.LogInformation("WindowsNamedPipeServerが解放されました");
    }
}