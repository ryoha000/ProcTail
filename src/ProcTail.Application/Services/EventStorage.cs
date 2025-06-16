using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Application.Services;

/// <summary>
/// イベントストレージサービス（FIFO キュー実装）
/// </summary>
public class EventStorage : IEventStorage, IDisposable
{
    private readonly ILogger<EventStorage> _logger;
    private readonly int _maxEventsPerTag;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<BaseEventData>> _eventQueues = new();
    private readonly ConcurrentDictionary<string, int> _eventCounts = new();
    private readonly object _lockObject = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="maxEventsPerTag">タグごとの最大イベント数</param>
    public EventStorage(ILogger<EventStorage> logger, int maxEventsPerTag = 10000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxEventsPerTag = maxEventsPerTag > 0 ? maxEventsPerTag : 10000;

        // 定期的なクリーンアップタイマー（5分間隔）
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        _logger.LogInformation("EventStorageが初期化されました (MaxEventsPerTag: {MaxEventsPerTag})", _maxEventsPerTag);
    }

    /// <summary>
    /// イベントを記録
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="eventData">イベントデータ</param>
    /// <returns>非同期タスク</returns>
    public async Task StoreEventAsync(string tagName, BaseEventData eventData)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            _logger.LogWarning("イベント記録に失敗: タグ名が無効です");
            return;
        }

        if (eventData == null)
        {
            _logger.LogWarning("イベント記録に失敗: イベントデータがnullです (Tag: {TagName})", tagName);
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                // キューを取得または作成
                var queue = _eventQueues.GetOrAdd(tagName, _ => new ConcurrentQueue<BaseEventData>());
                
                // イベントを追加
                queue.Enqueue(eventData);
                
                // カウントを更新
                var currentCount = _eventCounts.AddOrUpdate(tagName, 1, (key, oldValue) => oldValue + 1);

                // 最大数を超えた場合、古いイベントを削除
                if (currentCount > _maxEventsPerTag)
                {
                    TrimQueueToLimit(tagName, queue);
                }

                _logger.LogDebug("イベントを記録しました (Tag: {TagName}, EventType: {EventType}, ProcessId: {ProcessId}, CurrentCount: {CurrentCount})",
                    tagName, eventData.GetType().Name, eventData.ProcessId, currentCount);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント記録中にエラーが発生しました (Tag: {TagName}, EventType: {EventType})",
                tagName, eventData?.GetType().Name ?? "Unknown");
        }
    }

    /// <summary>
    /// タグに関連するイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>イベント一覧</returns>
    public async Task<IReadOnlyList<BaseEventData>> GetEventsAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            _logger.LogWarning("イベント取得に失敗: タグ名が無効です");
            return Array.Empty<BaseEventData>();
        }

        try
        {
            return await Task.Run(() =>
            {
                if (_eventQueues.TryGetValue(tagName, out var queue))
                {
                    var events = queue.ToArray();
                    _logger.LogDebug("イベントを取得しました (Tag: {TagName}, Count: {Count})", tagName, events.Length);
                    return (IReadOnlyList<BaseEventData>)events;
                }

                _logger.LogDebug("イベントが見つかりません (Tag: {TagName})", tagName);
                return Array.Empty<BaseEventData>();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント取得中にエラーが発生しました (Tag: {TagName})", tagName);
            return Array.Empty<BaseEventData>();
        }
    }

    /// <summary>
    /// タグに関連するイベントをクリア
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>非同期タスク</returns>
    public async Task ClearEventsAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            _logger.LogWarning("イベントクリアに失敗: タグ名が無効です");
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                if (_eventQueues.TryRemove(tagName, out var queue))
                {
                    var removedCount = _eventCounts.TryRemove(tagName, out var count) ? count : queue.Count;
                    _logger.LogInformation("イベントをクリアしました (Tag: {TagName}, RemovedCount: {RemovedCount})", 
                        tagName, removedCount);
                }
                else
                {
                    _logger.LogDebug("クリア対象のイベントが見つかりません (Tag: {TagName})", tagName);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベントクリア中にエラーが発生しました (Tag: {TagName})", tagName);
        }
    }

    /// <summary>
    /// 全イベント数を取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <returns>イベント数</returns>
    public async Task<int> GetEventCountAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return 0;
        }

        try
        {
            return await Task.Run(() =>
            {
                if (_eventCounts.TryGetValue(tagName, out var count))
                {
                    return count;
                }

                // カウントが見つからない場合、実際のキューサイズを返す
                if (_eventQueues.TryGetValue(tagName, out var queue))
                {
                    var actualCount = queue.Count;
                    _eventCounts.TryAdd(tagName, actualCount);
                    return actualCount;
                }

                return 0;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント数取得中にエラーが発生しました (Tag: {TagName})", tagName);
            return 0;
        }
    }

    /// <summary>
    /// ストレージ統計情報
    /// </summary>
    /// <returns>統計情報</returns>
    public async Task<StorageStatistics> GetStatisticsAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                var totalTags = _eventQueues.Count;
                var totalEvents = _eventCounts.Values.Sum();
                var eventCountByTag = _eventCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                
                // メモリ使用量の概算（非常に大まかな計算）
                var estimatedMemoryUsage = CalculateEstimatedMemoryUsage();

                var statistics = new StorageStatistics(
                    totalTags,
                    totalEvents,
                    eventCountByTag,
                    estimatedMemoryUsage
                );

                _logger.LogDebug("ストレージ統計を取得しました (Tags: {TotalTags}, Events: {TotalEvents}, Memory: {MemoryMB}MB)",
                    totalTags, totalEvents, estimatedMemoryUsage / 1024 / 1024);

                return statistics;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "統計情報取得中にエラーが発生しました");
            return new StorageStatistics(0, 0, new Dictionary<string, int>(), 0);
        }
    }

    /// <summary>
    /// 全てのタグ名を取得
    /// </summary>
    /// <returns>タグ名のリスト</returns>
    public async Task<IReadOnlyList<string>> GetAllTagsAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                var tags = _eventQueues.Keys.ToList();
                _logger.LogDebug("全タグを取得しました (Count: {Count})", tags.Count);
                return (IReadOnlyList<string>)tags;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "タグ一覧取得中にエラーが発生しました");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 期間を指定してイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="fromTime">開始時刻</param>
    /// <param name="toTime">終了時刻</param>
    /// <returns>フィルタされたイベント一覧</returns>
    public async Task<IReadOnlyList<BaseEventData>> GetEventsByTimeRangeAsync(
        string tagName, 
        DateTime fromTime, 
        DateTime toTime)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return Array.Empty<BaseEventData>();
        }

        try
        {
            var allEvents = await GetEventsAsync(tagName);
            
            return await Task.Run(() =>
            {
                var filteredEvents = allEvents
                    .Where(e => e.Timestamp >= fromTime && e.Timestamp <= toTime)
                    .ToList();

                _logger.LogDebug("期間指定イベントを取得しました (Tag: {TagName}, From: {FromTime}, To: {ToTime}, Count: {Count})",
                    tagName, fromTime, toTime, filteredEvents.Count);

                return (IReadOnlyList<BaseEventData>)filteredEvents;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "期間指定イベント取得中にエラーが発生しました (Tag: {TagName})", tagName);
            return Array.Empty<BaseEventData>();
        }
    }

    /// <summary>
    /// 最新のN件のイベントを取得
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="count">取得件数</param>
    /// <returns>最新のイベント一覧</returns>
    public async Task<IReadOnlyList<BaseEventData>> GetLatestEventsAsync(string tagName, int count)
    {
        if (string.IsNullOrWhiteSpace(tagName) || count <= 0)
        {
            return Array.Empty<BaseEventData>();
        }

        try
        {
            var allEvents = await GetEventsAsync(tagName);
            
            return await Task.Run(() =>
            {
                var latestEvents = allEvents
                    .OrderByDescending(e => e.Timestamp)
                    .Take(count)
                    .ToList();

                _logger.LogDebug("最新イベントを取得しました (Tag: {TagName}, RequestedCount: {RequestedCount}, ActualCount: {ActualCount})",
                    tagName, count, latestEvents.Count);

                return (IReadOnlyList<BaseEventData>)latestEvents;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最新イベント取得中にエラーが発生しました (Tag: {TagName})", tagName);
            return Array.Empty<BaseEventData>();
        }
    }

    /// <summary>
    /// キューを最大数まで削減
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="queue">キュー</param>
    private void TrimQueueToLimit(string tagName, ConcurrentQueue<BaseEventData> queue)
    {
        try
        {
            var currentCount = queue.Count;
            var removeCount = currentCount - _maxEventsPerTag;
            
            if (removeCount > 0)
            {
                for (int i = 0; i < removeCount; i++)
                {
                    if (queue.TryDequeue(out _))
                    {
                        _eventCounts.AddOrUpdate(tagName, 0, (key, oldValue) => Math.Max(0, oldValue - 1));
                    }
                    else
                    {
                        break;
                    }
                }

                _logger.LogDebug("古いイベントを削除しました (Tag: {TagName}, RemovedCount: {RemovedCount}, NewCount: {NewCount})",
                    tagName, removeCount, queue.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キュー削減中にエラーが発生しました (Tag: {TagName})", tagName);
        }
    }

    /// <summary>
    /// 定期的なクリーンアップ
    /// </summary>
    /// <param name="state">状態オブジェクト</param>
    private void PerformCleanup(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-24); // 24時間より古いイベントは削除候補
            var totalCleaned = 0;

            foreach (var tagName in _eventQueues.Keys.ToList())
            {
                if (_eventQueues.TryGetValue(tagName, out var queue))
                {
                    var cleanedCount = CleanOldEventsFromQueue(queue, cutoffTime);
                    if (cleanedCount > 0)
                    {
                        _eventCounts.AddOrUpdate(tagName, 0, (key, oldValue) => Math.Max(0, oldValue - cleanedCount));
                        totalCleaned += cleanedCount;
                    }

                    // 空のキューは削除
                    if (queue.IsEmpty)
                    {
                        _eventQueues.TryRemove(tagName, out _);
                        _eventCounts.TryRemove(tagName, out _);
                    }
                }
            }

            if (totalCleaned > 0)
            {
                _logger.LogInformation("定期クリーンアップを実行しました (CleanedEvents: {CleanedEvents})", totalCleaned);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定期クリーンアップ中にエラーが発生しました");
        }
    }

    /// <summary>
    /// キューから古いイベントをクリーン
    /// </summary>
    /// <param name="queue">キュー</param>
    /// <param name="cutoffTime">カットオフ時刻</param>
    /// <returns>クリーンしたイベント数</returns>
    private int CleanOldEventsFromQueue(ConcurrentQueue<BaseEventData> queue, DateTime cutoffTime)
    {
        var cleanedCount = 0;
        var tempList = new List<BaseEventData>();

        // キューからすべてのアイテムを取り出し
        while (queue.TryDequeue(out var eventData))
        {
            if (eventData.Timestamp >= cutoffTime)
            {
                tempList.Add(eventData);
            }
            else
            {
                cleanedCount++;
            }
        }

        // 有効なイベントを戻す
        foreach (var eventData in tempList)
        {
            queue.Enqueue(eventData);
        }

        return cleanedCount;
    }

    /// <summary>
    /// 推定メモリ使用量を計算
    /// </summary>
    /// <returns>バイト単位のメモリ使用量</returns>
    private long CalculateEstimatedMemoryUsage()
    {
        try
        {
            long totalMemory = 0;
            
            // 非常に大まかな計算（実際のオブジェクトサイズは複雑）
            const int avgEventSize = 1024; // 1KBと仮定
            const int avgTagNameSize = 50; // 50バイトと仮定

            foreach (var kvp in _eventCounts)
            {
                totalMemory += avgTagNameSize; // タグ名
                totalMemory += kvp.Value * avgEventSize; // イベントデータ
            }

            return totalMemory;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer?.Dispose();
        
        var totalEvents = _eventCounts.Values.Sum();
        _eventQueues.Clear();
        _eventCounts.Clear();

        _logger.LogInformation("EventStorageが解放されました (TotalEvents: {TotalEvents})", totalEvents);
    }
}