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

    /// <summary>
    /// フィルタリング設定
    /// </summary>
    EtwFilteringOptions FilteringOptions { get; }

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    EtwPerformanceOptions PerformanceOptions { get; }
}

/// <summary>
/// ETWフィルタリング設定
/// </summary>
public class EtwFilteringOptions
{
    /// <summary>
    /// システムプロセスを除外するか
    /// </summary>
    public bool ExcludeSystemProcesses { get; init; }

    /// <summary>
    /// 監視対象の最小プロセスID
    /// </summary>
    public int MinimumProcessId { get; init; }

    /// <summary>
    /// 除外するプロセス名一覧
    /// </summary>
    public IReadOnlyList<string> ExcludedProcessNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 対象とするファイル拡張子
    /// </summary>
    public IReadOnlyList<string> IncludeFileExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 除外するファイルパターン
    /// </summary>
    public IReadOnlyList<string> ExcludeFilePatterns { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ETWパフォーマンス設定
/// </summary>
public class EtwPerformanceOptions
{
    /// <summary>
    /// イベント処理バッチサイズ
    /// </summary>
    public int EventProcessingBatchSize { get; init; }

    /// <summary>
    /// 最大イベントキューサイズ
    /// </summary>
    public int MaxEventQueueSize { get; init; }

    /// <summary>
    /// イベント処理間隔（ミリ秒）
    /// </summary>
    public int EventProcessingIntervalMs { get; init; }

    /// <summary>
    /// 高頻度イベントを有効にするか
    /// </summary>
    public bool EnableHighFrequencyEvents { get; init; }

    /// <summary>
    /// 1秒あたりの最大イベント数
    /// </summary>
    public int MaxEventsPerSecond { get; init; }
}