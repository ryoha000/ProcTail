using ProcTail.Core.Models;

namespace ProcTail.Core.Interfaces;

/// <summary>
/// ETWイベントプロバイダーの抽象化
/// </summary>
public interface IEtwEventProvider : IDisposable
{
    /// <summary>
    /// ETWイベント受信時に発火するイベント
    /// </summary>
    event EventHandler<RawEventData> EventReceived;

    /// <summary>
    /// 監視中かどうか
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// ETW監視を開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ETW監視を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ETW設定の抽象化
/// </summary>
public interface IEtwConfiguration
{
    /// <summary>
    /// 有効なプロバイダー一覧
    /// </summary>
    IReadOnlyList<string> EnabledProviders { get; }

    /// <summary>
    /// 有効なイベント名一覧
    /// </summary>
    IReadOnlyList<string> EnabledEventNames { get; }

    /// <summary>
    /// イベントバッファタイムアウト
    /// </summary>
    TimeSpan EventBufferTimeout { get; }

    /// <summary>
    /// ETWセッションのバッファサイズ（MB）
    /// </summary>
    int BufferSizeMB { get; }

    /// <summary>
    /// ETWセッションのバッファ数
    /// </summary>
    int BufferCount { get; }
}