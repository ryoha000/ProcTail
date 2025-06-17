using ProcTail.Core.Models;

namespace ProcTail.Core.Interfaces;

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
    /// パイプ名
    /// </summary>
    string PipeName { get; }

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
public interface INamedPipeConfiguration
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
    /// バッファサイズ（バイト）
    /// </summary>
    int BufferSize { get; }

    /// <summary>
    /// 応答タイムアウト（秒）
    /// </summary>
    int ResponseTimeoutSeconds { get; }

    /// <summary>
    /// 接続タイムアウト（秒）
    /// </summary>
    int ConnectionTimeoutSeconds { get; }
}