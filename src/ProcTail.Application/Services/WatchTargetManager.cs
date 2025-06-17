using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Application.Services;

/// <summary>
/// 監視対象管理サービス
/// </summary>
public class WatchTargetManager : IWatchTargetManager, IDisposable
{
    private readonly ILogger<WatchTargetManager> _logger;
    private readonly ConcurrentDictionary<int, WatchTarget> _watchTargets = new();
    private readonly ConcurrentDictionary<string, HashSet<int>> _tagToProcessMap = new();
    private readonly object _lockObject = new();
    private bool _disposed;

    /// <summary>
    /// 監視中の対象数
    /// </summary>
    public int ActiveTargetCount => _watchTargets.Count;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public WatchTargetManager(ILogger<WatchTargetManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 監視対象を追加
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="tagName">タグ名</param>
    /// <returns>追加結果</returns>
    public Task<bool> AddTargetAsync(int processId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            _logger.LogWarning("監視対象追加に失敗: タグ名が無効です (ProcessId: {ProcessId})", processId);
            return Task.FromResult(false);
        }

        if (processId <= 0)
        {
            _logger.LogWarning("監視対象追加に失敗: プロセスIDが無効です (ProcessId: {ProcessId}, Tag: {TagName})", processId, tagName);
            return Task.FromResult(false);
        }

        try
        {
            var watchTarget = new WatchTarget(
                processId,
                tagName,
                DateTime.UtcNow
            );

            // 監視対象を追加
            if (_watchTargets.TryAdd(processId, watchTarget))
            {
                // タグマッピングを更新
                lock (_lockObject)
                {
                    if (!_tagToProcessMap.TryGetValue(tagName, out var processSet))
                    {
                        processSet = new HashSet<int>();
                        _tagToProcessMap[tagName] = processSet;
                    }
                    processSet.Add(processId);
                }

                _logger.LogInformation("監視対象を追加しました (ProcessId: {ProcessId}, Tag: {TagName})", 
                    processId, tagName);
                return Task.FromResult(true);
            }
            else
            {
                _logger.LogWarning("監視対象追加に失敗: 既に監視中です (ProcessId: {ProcessId}, Tag: {TagName})", processId, tagName);
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "監視対象追加中にエラーが発生しました (ProcessId: {ProcessId}, Tag: {TagName})", processId, tagName);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 子プロセスを自動追加
    /// </summary>
    /// <param name="childProcessId">子プロセスID</param>
    /// <param name="parentProcessId">親プロセスID</param>
    /// <returns>追加成功の場合true</returns>
    public Task<bool> AddChildProcessAsync(int childProcessId, int parentProcessId)
    {
        // 親プロセスが監視対象かチェック
        if (!_watchTargets.TryGetValue(parentProcessId, out var parentTarget))
        {
            _logger.LogWarning("子プロセス追加に失敗: 親プロセスが監視対象ではありません (ParentId: {ParentProcessId}, ChildId: {ChildProcessId})", 
                parentProcessId, childProcessId);
            return Task.FromResult(false);
        }

        try
        {
            var childTarget = new WatchTarget(
                childProcessId,
                parentTarget.TagName,
                DateTime.UtcNow,
                IsChildProcess: true,
                ParentProcessId: parentProcessId
            );

            if (_watchTargets.TryAdd(childProcessId, childTarget))
            {
                // タグマッピングを更新
                lock (_lockObject)
                {
                    if (_tagToProcessMap.TryGetValue(parentTarget.TagName, out var processSet))
                    {
                        processSet.Add(childProcessId);
                    }
                }

                _logger.LogInformation("子プロセスを追加しました (ChildId: {ChildProcessId}, ParentId: {ParentProcessId}, Tag: {TagName})", 
                    childProcessId, parentProcessId, parentTarget.TagName);
                return Task.FromResult(true);
            }
            else
            {
                _logger.LogWarning("子プロセス追加に失敗: 既に監視中です (ChildId: {ChildProcessId})", childProcessId);
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "子プロセス追加中にエラーが発生しました (ChildId: {ChildProcessId}, ParentId: {ParentProcessId})", 
                childProcessId, parentProcessId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 監視対象を除去
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>除去結果</returns>
    public bool RemoveTarget(int processId)
    {
        try
        {
            if (_watchTargets.TryRemove(processId, out var watchTarget))
            {
                // タグマッピングからも除去
                lock (_lockObject)
                {
                    if (_tagToProcessMap.TryGetValue(watchTarget.TagName, out var processSet))
                    {
                        processSet.Remove(processId);
                        if (processSet.Count == 0)
                        {
                            _tagToProcessMap.TryRemove(watchTarget.TagName, out _);
                        }
                    }
                }

                _logger.LogInformation("監視対象を除去しました (ProcessId: {ProcessId}, Tag: {TagName})", 
                    processId, watchTarget.TagName);
                return true;
            }
            else
            {
                _logger.LogWarning("監視対象除去に失敗: 対象が見つかりません (ProcessId: {ProcessId})", processId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "監視対象除去中にエラーが発生しました (ProcessId: {ProcessId})", processId);
            return false;
        }
    }

    /// <summary>
    /// タグ名で監視対象を除去
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>除去された対象数</returns>
    public Task<int> RemoveWatchTargetsByTagAsync(string tagName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return Task.FromResult(0);
        }

        try
        {
            int removedCount = 0;
            HashSet<int>? processIds = null;

            lock (_lockObject)
            {
                if (_tagToProcessMap.TryGetValue(tagName, out processIds))
                {
                    processIds = new HashSet<int>(processIds); // コピーを作成
                }
            }

            if (processIds != null)
            {
                foreach (var processId in processIds)
                {
                    if (RemoveTarget(processId))
                    {
                        removedCount++;
                    }
                }
            }

            _logger.LogInformation("タグによる監視対象除去完了 (Tag: {TagName}, RemovedCount: {RemovedCount})", 
                tagName, removedCount);
            return Task.FromResult(removedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "タグによる監視対象除去中にエラーが発生しました (Tag: {TagName})", tagName);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// 指定されたプロセスIDが監視対象かどうかを判定
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>監視対象の場合true</returns>
    public bool IsWatchedProcess(int processId)
    {
        return _watchTargets.ContainsKey(processId);
    }

    /// <summary>
    /// プロセスIDに対応するタグ名を取得
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>タグ名（見つからない場合はnull）</returns>
    public string? GetTagForProcess(int processId)
    {
        return _watchTargets.TryGetValue(processId, out var watchTarget) ? watchTarget.TagName : null;
    }

    /// <summary>
    /// 全ての監視対象を取得
    /// </summary>
    /// <returns>全監視対象のリスト</returns>
    public IReadOnlyList<WatchTarget> GetWatchTargets()
    {
        return _watchTargets.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 全ての監視対象の詳細情報を取得
    /// </summary>
    /// <returns>監視対象詳細情報のリスト</returns>
    public Task<List<WatchTargetInfo>> GetWatchTargetInfosAsync()
    {
        try
        {
            var watchTargetInfos = new List<WatchTargetInfo>();

            foreach (var watchTarget in _watchTargets.Values)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(watchTarget.ProcessId);
                    var processName = process.ProcessName;
                    var executablePath = GetProcessExecutablePath(process);

                    watchTargetInfos.Add(new WatchTargetInfo(
                        watchTarget.ProcessId,
                        processName,
                        executablePath,
                        watchTarget.RegisteredAt,
                        watchTarget.TagName
                    ));
                }
                catch (ArgumentException)
                {
                    // プロセスが既に終了している場合
                    watchTargetInfos.Add(new WatchTargetInfo(
                        watchTarget.ProcessId,
                        "[Terminated]",
                        "[Unknown]",
                        watchTarget.RegisteredAt,
                        watchTarget.TagName
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "プロセス情報取得に失敗しました (ProcessId: {ProcessId})", watchTarget.ProcessId);
                    watchTargetInfos.Add(new WatchTargetInfo(
                        watchTarget.ProcessId,
                        "[Error]",
                        "[Unknown]",
                        watchTarget.RegisteredAt,
                        watchTarget.TagName
                    ));
                }
            }

            return Task.FromResult(watchTargetInfos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "監視対象詳細情報取得中にエラーが発生しました");
            return Task.FromResult(new List<WatchTargetInfo>());
        }
    }

    /// <summary>
    /// プロセスの実行ファイルパスを取得
    /// </summary>
    /// <param name="process">プロセス</param>
    /// <returns>実行ファイルパス</returns>
    private static string GetProcessExecutablePath(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "[Unknown]";
        }
        catch
        {
            return "[Access Denied]";
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
        _watchTargets.Clear();
        
        lock (_lockObject)
        {
            _tagToProcessMap.Clear();
        }

        _logger.LogInformation("WatchTargetManagerが解放されました");
    }
}