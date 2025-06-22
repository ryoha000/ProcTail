using ProcTail.Core.Models;

namespace ProcTail.Core.Interfaces;

/// <summary>
/// 監視対象管理の抽象化
/// </summary>
public interface IWatchTargetManager
{
    /// <summary>
    /// 監視中の対象数
    /// </summary>
    int ActiveTargetCount { get; }

    /// <summary>
    /// 監視対象を追加
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="tagName">タグ名</param>
    /// <returns>追加成功の場合true</returns>
    Task<bool> AddTargetAsync(int processId, string tagName);

    /// <summary>
    /// 子プロセスを自動追加
    /// </summary>
    /// <param name="childProcessId">子プロセスID</param>
    /// <param name="parentProcessId">親プロセスID</param>
    /// <returns>追加成功の場合true</returns>
    Task<bool> AddChildProcessAsync(int childProcessId, int parentProcessId);

    /// <summary>
    /// プロセスが監視対象かチェック
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>監視対象の場合true</returns>
    bool IsWatchedProcess(int processId);

    /// <summary>
    /// プロセスのタグ名を取得
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>タグ名（監視対象でない場合null）</returns>
    string? GetTagForProcess(int processId);

    /// <summary>
    /// 監視対象一覧を取得
    /// </summary>
    /// <returns>監視対象一覧</returns>
    IReadOnlyList<WatchTarget> GetWatchTargets();

    /// <summary>
    /// 監視対象を削除（プロセス終了時）
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>削除成功の場合true</returns>
    bool RemoveTarget(int processId);

    /// <summary>
    /// タグ名で監視対象を削除
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除された監視対象数</returns>
    Task<int> RemoveWatchTargetsByTagAsync(string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 監視対象の詳細情報を取得
    /// </summary>
    /// <returns>監視対象詳細情報のリスト</returns>
    Task<List<WatchTargetInfo>> GetWatchTargetInfosAsync();
}

/// <summary>
/// イベント処理の抽象化
/// </summary>
public interface IEventProcessor
{
    /// <summary>
    /// 生ETWイベントを処理してドメインイベントに変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <returns>処理結果</returns>
    Task<ProcessingResult> ProcessEventAsync(RawEventData rawEvent);

    /// <summary>
    /// イベントタイプのフィルタリング
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <returns>処理すべき場合true</returns>
    bool ShouldProcessEvent(RawEventData rawEvent);
}

/// <summary>
/// イベントストレージの抽象化
/// </summary>
public interface IEventStorage
{
    /// <summary>
    /// イベントを記録
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="eventData">イベントデータ</param>
    /// <returns>非同期タスク</returns>
    Task StoreEventAsync(string tagName, BaseEventData eventData);

    /// <summary>
    /// タグに関連するイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>イベント一覧</returns>
    Task<IReadOnlyList<BaseEventData>> GetEventsAsync(string tagName);

    /// <summary>
    /// タグに関連するイベントをクリア
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>非同期タスク</returns>
    Task ClearEventsAsync(string tagName);

    /// <summary>
    /// 全イベント数を取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>イベント数</returns>
    Task<int> GetEventCountAsync(string tagName);

    /// <summary>
    /// ストレージ統計情報
    /// </summary>
    /// <returns>統計情報</returns>
    Task<StorageStatistics> GetStatisticsAsync();

    /// <summary>
    /// 全てのタグ名を取得
    /// </summary>
    /// <returns>タグ名一覧</returns>
    Task<IReadOnlyList<string>> GetAllTagsAsync();

    /// <summary>
    /// 時間範囲でイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="startTime">開始時刻</param>
    /// <param name="endTime">終了時刻</param>
    /// <returns>イベント一覧</returns>
    Task<IReadOnlyList<BaseEventData>> GetEventsByTimeRangeAsync(string tagName, DateTime startTime, DateTime endTime);

    /// <summary>
    /// 最新のイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="count">取得件数</param>
    /// <returns>イベント一覧</returns>
    Task<IReadOnlyList<BaseEventData>> GetLatestEventsAsync(string tagName, int count);
}

/// <summary>
/// ヘルスチェックの抽象化
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// システム全体のヘルスチェック
    /// </summary>
    /// <returns>ヘルスチェック結果</returns>
    Task<HealthCheckResult> CheckHealthAsync();

    /// <summary>
    /// 個別コンポーネントのヘルスチェック
    /// </summary>
    /// <param name="componentName">コンポーネント名</param>
    /// <returns>ヘルスチェック結果</returns>
    Task<HealthCheckResult> CheckComponentHealthAsync(string componentName);

    /// <summary>
    /// ヘルスチェック項目の登録
    /// </summary>
    /// <param name="name">項目名</param>
    /// <param name="healthCheck">ヘルスチェック関数</param>
    void RegisterHealthCheck(string name, Func<Task<HealthCheckResult>> healthCheck);
}