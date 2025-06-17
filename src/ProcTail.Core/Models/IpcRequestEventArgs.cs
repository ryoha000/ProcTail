namespace ProcTail.Core.Models;

/// <summary>
/// IPC要求イベント引数
/// </summary>
public class IpcRequestEventArgs : EventArgs
{
    private readonly TaskCompletionSource<bool> _responseCompletionSource = new();
    
    /// <summary>
    /// 要求JSON文字列
    /// </summary>
    public string RequestJson { get; }

    /// <summary>
    /// キャンセレーショントークン
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 応答送信デリゲート
    /// </summary>
    public Func<string, Task>? ResponseSender { get; set; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public IpcRequestEventArgs(string requestJson, CancellationToken cancellationToken)
    {
        RequestJson = requestJson ?? throw new ArgumentNullException(nameof(requestJson));
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// 応答を送信
    /// </summary>
    public async Task SendResponseAsync(string response)
    {
        if (ResponseSender == null)
            throw new InvalidOperationException("ResponseSender が設定されていません");

        try
        {
            await ResponseSender(response);
            _responseCompletionSource.SetResult(true);
        }
        catch (Exception ex)
        {
            _responseCompletionSource.SetException(ex);
            throw;
        }
    }

    /// <summary>
    /// 応答の完了を待機
    /// </summary>
    public async Task WaitForResponseAsync(TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, timeoutCts.Token);

        try
        {
            await _responseCompletionSource.Task.WaitAsync(combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"応答送信がタイムアウトしました ({timeout.TotalSeconds}秒)");
        }
    }
}