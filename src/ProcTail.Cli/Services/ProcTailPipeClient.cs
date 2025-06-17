using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ProcTail.Core.Models;

namespace ProcTail.Cli.Services;

/// <summary>
/// ProcTail Named Pipeクライアント
/// </summary>
public interface IProcTailPipeClient : IDisposable
{
    /// <summary>
    /// 監視対象を追加
    /// </summary>
    Task<AddWatchTargetResponse> AddWatchTargetAsync(int processId, string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 監視対象を削除
    /// </summary>
    Task<RemoveWatchTargetResponse> RemoveWatchTargetAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 監視対象一覧を取得
    /// </summary>
    Task<GetWatchTargetsResponse> GetWatchTargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 記録されたイベントを取得
    /// </summary>
    Task<GetRecordedEventsResponse> GetRecordedEventsAsync(string tagName, int maxCount = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// サービス状態を取得
    /// </summary>
    Task<ServiceStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// イベントをクリア
    /// </summary>
    Task<ClearEventsResponse> ClearEventsAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// サービスをシャットダウン
    /// </summary>
    Task<ShutdownResponse> ShutdownAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接続テスト
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ProcTail Named Pipeクライアントの実装
/// </summary>
public class ProcTailPipeClient : IProcTailPipeClient
{
    private readonly ILogger<ProcTailPipeClient> _logger;
    private readonly string _pipeName;
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcTailPipeClient(ILogger<ProcTailPipeClient> logger, string pipeName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
    }

    /// <summary>
    /// 監視対象を追加
    /// </summary>
    public async Task<AddWatchTargetResponse> AddWatchTargetAsync(int processId, string tagName, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "AddWatchTarget",
            ProcessId = processId,
            TagName = tagName
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// 監視対象を削除
    /// </summary>
    public async Task<RemoveWatchTargetResponse> RemoveWatchTargetAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "RemoveWatchTarget",
            TagName = tagName
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<RemoveWatchTargetResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// 監視対象一覧を取得
    /// </summary>
    public async Task<GetWatchTargetsResponse> GetWatchTargetsAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "GetWatchTargets"
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<GetWatchTargetsResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// 記録されたイベントを取得
    /// </summary>
    public async Task<GetRecordedEventsResponse> GetRecordedEventsAsync(string tagName, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "GetRecordedEvents",
            TagName = tagName,
            MaxCount = maxCount
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<GetRecordedEventsResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// サービス状態を取得
    /// </summary>
    public async Task<ServiceStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "GetStatus"
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<ServiceStatusResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// イベントをクリア
    /// </summary>
    public async Task<ClearEventsResponse> ClearEventsAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "ClearEvents",
            TagName = tagName
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<ClearEventsResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// サービスをシャットダウン
    /// </summary>
    public async Task<ShutdownResponse> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            RequestType = "Shutdown"
        };

        var responseJson = await SendRequestAsync(JsonSerializer.Serialize(request), cancellationToken);
        return JsonSerializer.Deserialize<ShutdownResponse>(responseJson) 
            ?? throw new InvalidOperationException("応答のデシリアライズに失敗しました");
    }

    /// <summary>
    /// 接続テスト
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetStatusAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "接続テストに失敗しました");
            return false;
        }
    }

    /// <summary>
    /// 要求を送信
    /// </summary>
    private async Task<string> SendRequestAsync(string requestJson, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcTailPipeClient));

        using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            _logger.LogDebug("Named Pipeサーバーに接続中... (パイプ名: {PipeName})", _pipeName);

            // サーバーに接続
            using var connectionCts = new CancellationTokenSource(_connectionTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionCts.Token);
            
            await pipeClient.ConnectAsync(combinedCts.Token);
            
            _logger.LogDebug("Named Pipeサーバーに接続しました");

            // 要求を送信
            await SendMessageAsync(pipeClient, requestJson, cancellationToken);
            
            _logger.LogDebug("要求を送信しました: {RequestLength}文字", requestJson.Length);

            // 応答を受信
            using var responseCts = new CancellationTokenSource(_responseTimeout);
            using var responseCombiledCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, responseCts.Token);
            
            var response = await ReceiveMessageAsync(pipeClient, responseCombiledCts.Token);
            
            _logger.LogDebug("応答を受信しました: {ResponseLength}文字", response.Length);

            return response;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Named Pipeサーバーとの通信がタイムアウトしました (パイプ名: {_pipeName})");
        }
        catch (IOException ex) when (ex.Message.Contains("pipe"))
        {
            throw new InvalidOperationException($"Named Pipeサーバーに接続できませんでした (パイプ名: {_pipeName}). サービスが起動していることを確認してください。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"Named Pipeサーバーへのアクセス権限がありません (パイプ名: {_pipeName}). 管理者権限で実行してください。", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Named Pipe通信中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// メッセージを送信
    /// </summary>
    private async Task SendMessageAsync(NamedPipeClientStream pipeClient, string message, CancellationToken cancellationToken)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        _logger.LogDebug("メッセージを送信します - 長さ: {Length}, lengthBytesサイズ: {LengthBytesSize}", 
            messageBytes.Length, lengthBytes.Length);

        // メッセージ長を送信
        await pipeClient.WriteAsync(lengthBytes, cancellationToken);
        
        // メッセージ本体を送信
        await pipeClient.WriteAsync(messageBytes, cancellationToken);
        
        await pipeClient.FlushAsync(cancellationToken);
        
        _logger.LogDebug("メッセージ送信が完了しました");
    }

    /// <summary>
    /// メッセージを受信
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(NamedPipeClientStream pipeClient, CancellationToken cancellationToken)
    {
        // メッセージ長を受信
        var lengthBuffer = new byte[4];
        var bytesRead = 0;
        
        while (bytesRead < 4)
        {
            var read = await pipeClient.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("メッセージ長の受信が予期せず終了しました");
            bytesRead += read;
        }

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // 10MB制限
        {
            throw new InvalidOperationException($"無効なメッセージ長: {messageLength}");
        }

        // メッセージ本体を受信
        var messageBuffer = new byte[messageLength];
        bytesRead = 0;
        
        while (bytesRead < messageLength)
        {
            var read = await pipeClient.ReadAsync(messageBuffer.AsMemory(bytesRead, messageLength - bytesRead), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("メッセージ受信が予期せず終了しました");
            bytesRead += read;
        }

        return Encoding.UTF8.GetString(messageBuffer);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger.LogDebug("ProcTailPipeClientが解放されました");
    }
}

/// <summary>
/// サービス状態応答
/// </summary>
public class ServiceStatusResponse
{
    public bool Success { get; set; }
    public bool IsRunning { get; set; }
    public bool IsEtwMonitoring { get; set; }
    public bool IsPipeServerRunning { get; set; }
    public int ActiveWatchTargets { get; set; }
    public int TotalTags { get; set; }
    public int TotalEvents { get; set; }
    public long EstimatedMemoryUsageMB { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}