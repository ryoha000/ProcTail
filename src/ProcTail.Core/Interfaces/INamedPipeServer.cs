namespace ProcTail.Core.Interfaces;

/// <summary>
/// IPC要求イベント引数
/// </summary>
public class IpcRequestEventArgs : EventArgs
{
    /// <summary>
    /// 要求JSON
    /// </summary>
    public string RequestJson { get; init; } = string.Empty;

    /// <summary>
    /// 応答送信関数
    /// </summary>
    public required Func<string, Task> SendResponseAsync { get; init; }

    /// <summary>
    /// キャンセレーショントークン
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Named Pipe通信の抽象化
/// </summary>
public interface INamedPipeServer : IDisposable
{
    /// <summary>
    /// IPC要求受信時に発火するイベント
    /// </summary>
    event EventHandler<IpcRequestEventArgs> RequestReceived;

    /// <summary>
    /// サーバーが実行中かどうか
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Named Pipeサーバーを開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Named Pipeサーバーを停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Named Pipe設定の抽象化
/// </summary>
public interface IPipeConfiguration
{
    /// <summary>
    /// パイプ名
    /// </summary>
    string PipeName { get; }

    /// <summary>
    /// 最大同時接続数
    /// </summary>
    int MaxConcurrentConnections { get; }

    /// <summary>
    /// 接続タイムアウト
    /// </summary>
    TimeSpan ConnectionTimeout { get; }

    /// <summary>
    /// セキュリティ記述子
    /// </summary>
    string SecurityDescriptor { get; }
}