using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Application.Services;

/// <summary>
/// イベント処理サービス
/// </summary>
public class EventProcessor : IEventProcessor
{
    private readonly ILogger<EventProcessor> _logger;
    private readonly IWatchTargetManager _watchTargetManager;
    private readonly IReadOnlyList<string> _enabledProviders;
    private readonly IReadOnlyList<string> _enabledEventNames;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="watchTargetManager">監視対象管理</param>
    /// <param name="etwConfiguration">ETW設定</param>
    public EventProcessor(
        ILogger<EventProcessor> logger,
        IWatchTargetManager watchTargetManager,
        IEtwConfiguration etwConfiguration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watchTargetManager = watchTargetManager ?? throw new ArgumentNullException(nameof(watchTargetManager));
        
        var config = etwConfiguration ?? throw new ArgumentNullException(nameof(etwConfiguration));
        _enabledProviders = config.EnabledProviders;
        _enabledEventNames = config.EnabledEventNames;
    }

    /// <summary>
    /// 生ETWイベントを処理してドメインイベントに変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <returns>処理結果</returns>
    public async Task<ProcessingResult> ProcessEventAsync(RawEventData rawEvent)
    {
        if (rawEvent == null)
        {
            return new ProcessingResult(false, ErrorMessage: "Raw event data is null");
        }

        try
        {
            _logger.LogTrace("生ETWイベントを受信: {Provider}.{Event}, ProcessId: {ProcessId}", 
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);

            // イベントをフィルタリング
            if (!ShouldProcessEvent(rawEvent))
            {
                _logger.LogDebug("イベントはフィルタリングされました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId})",
                    rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);
                return new ProcessingResult(false, ErrorMessage: "Event filtered out");
            }

            // 監視対象プロセスかチェック
            if (!_watchTargetManager.IsWatchedProcess(rawEvent.ProcessId))
            {
                _logger.LogDebug("監視対象外のプロセスです (ProcessId: {ProcessId})", rawEvent.ProcessId);
                return new ProcessingResult(false, ErrorMessage: "Process not watched");
            }

            // タグ名を取得
            var tagName = _watchTargetManager.GetTagForProcess(rawEvent.ProcessId);
            if (string.IsNullOrEmpty(tagName))
            {
                _logger.LogWarning("監視対象プロセスのタグが見つかりません (ProcessId: {ProcessId})", rawEvent.ProcessId);
                return new ProcessingResult(false, ErrorMessage: "Tag not found for watched process");
            }

            // ドメインイベントに変換
            var eventData = await ConvertToEventDataAsync(rawEvent, tagName);
            if (eventData == null)
            {
                return new ProcessingResult(false, ErrorMessage: "Failed to convert to domain event");
            }

            _logger.LogDebug("イベント処理完了 (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId}, Tag: {Tag})",
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId, tagName);

            return new ProcessingResult(true, eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント処理中にエラーが発生しました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);
            return new ProcessingResult(false, ErrorMessage: $"Processing error: {ex.Message}");
        }
    }

    /// <summary>
    /// イベントタイプのフィルタリング
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <returns>処理すべき場合true</returns>
    public bool ShouldProcessEvent(RawEventData rawEvent)
    {
        if (rawEvent == null)
        {
            return false;
        }

        // プロバイダーのフィルタリング
        if (!_enabledProviders.Contains(rawEvent.ProviderName))
        {
            return false;
        }

        // イベント名のフィルタリング
        if (!_enabledEventNames.Contains(rawEvent.EventName))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 生ETWイベントをドメインイベントに変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <param name="tagName">タグ名</param>
    /// <returns>変換されたイベントデータ</returns>
    private async Task<BaseEventData?> ConvertToEventDataAsync(RawEventData rawEvent, string tagName)
    {
        try
        {
            var baseProperties = new
            {
                Timestamp = rawEvent.Timestamp,
                TagName = tagName,
                ProcessId = rawEvent.ProcessId,
                ThreadId = rawEvent.ThreadId,
                ProviderName = rawEvent.ProviderName,
                EventName = rawEvent.EventName,
                ActivityId = rawEvent.ActivityId,
                RelatedActivityId = rawEvent.RelatedActivityId,
                Payload = new Dictionary<string, object>(rawEvent.Payload)
            };

            // プロバイダーとイベント名に基づいて適切な型に変換
            return rawEvent.ProviderName switch
            {
                "Microsoft-Windows-Kernel-FileIO" => await ConvertFileEventAsync(rawEvent, baseProperties),
                "Microsoft-Windows-Kernel-Process" => await ConvertProcessEventAsync(rawEvent, baseProperties),
                _ => new GenericEventData
                {
                    Timestamp = baseProperties.Timestamp,
                    TagName = baseProperties.TagName,
                    ProcessId = baseProperties.ProcessId,
                    ThreadId = baseProperties.ThreadId,
                    ProviderName = baseProperties.ProviderName,
                    EventName = baseProperties.EventName,
                    ActivityId = baseProperties.ActivityId,
                    RelatedActivityId = baseProperties.RelatedActivityId,
                    Payload = baseProperties.Payload
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント変換中にエラーが発生しました (Provider: {Provider}, Event: {Event})",
                rawEvent.ProviderName, rawEvent.EventName);
            return null;
        }
    }

    /// <summary>
    /// ファイルイベントを変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <param name="baseProperties">基本プロパティ</param>
    /// <returns>ファイルイベントデータ</returns>
    private async Task<FileEventData?> ConvertFileEventAsync(RawEventData rawEvent, dynamic baseProperties)
    {
        await Task.CompletedTask; // 非同期処理の準備

        try
        {
            // ファイルパスの抽出
            var filePath = ExtractFilePathFromPayload(rawEvent.Payload);
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogWarning("ファイルパスが見つかりません (Event: {Event}, ProcessId: {ProcessId})",
                    rawEvent.EventName, rawEvent.ProcessId);
                return null;
            }

            return new FileEventData
            {
                Timestamp = baseProperties.Timestamp,
                TagName = baseProperties.TagName,
                ProcessId = baseProperties.ProcessId,
                ThreadId = baseProperties.ThreadId,
                ProviderName = baseProperties.ProviderName,
                EventName = baseProperties.EventName,
                ActivityId = baseProperties.ActivityId,
                RelatedActivityId = baseProperties.RelatedActivityId,
                Payload = baseProperties.Payload,
                FilePath = filePath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ファイルイベント変換エラー (Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.EventName, rawEvent.ProcessId);
            return null;
        }
    }

    /// <summary>
    /// プロセスイベントを変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <param name="baseProperties">基本プロパティ</param>
    /// <returns>プロセスイベントデータ</returns>
    private async Task<BaseEventData?> ConvertProcessEventAsync(RawEventData rawEvent, dynamic baseProperties)
    {
        try
        {
            return rawEvent.EventName switch
            {
                "Process/Start" => await ConvertProcessStartEventAsync(rawEvent, baseProperties),
                "Process/End" => await ConvertProcessEndEventAsync(rawEvent, baseProperties),
                _ => new GenericEventData
                {
                    Timestamp = baseProperties.Timestamp,
                    TagName = baseProperties.TagName,
                    ProcessId = baseProperties.ProcessId,
                    ThreadId = baseProperties.ThreadId,
                    ProviderName = baseProperties.ProviderName,
                    EventName = baseProperties.EventName,
                    ActivityId = baseProperties.ActivityId,
                    RelatedActivityId = baseProperties.RelatedActivityId,
                    Payload = baseProperties.Payload
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセスイベント変換エラー (Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.EventName, rawEvent.ProcessId);
            return null;
        }
    }

    /// <summary>
    /// プロセス開始イベントを変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <param name="baseProperties">基本プロパティ</param>
    /// <returns>プロセス開始イベントデータ</returns>
    private async Task<ProcessStartEventData?> ConvertProcessStartEventAsync(RawEventData rawEvent, dynamic baseProperties)
    {
        await Task.CompletedTask;

        try
        {
            // 子プロセス情報の抽出
            var childProcessInfo = ExtractChildProcessInfo(rawEvent.Payload);
            if (childProcessInfo == null)
            {
                _logger.LogWarning("子プロセス情報が見つかりません (Event: {Event}, ProcessId: {ProcessId})",
                    rawEvent.EventName, rawEvent.ProcessId);
                return null;
            }

            var (childProcessId, childProcessName) = childProcessInfo.Value;

            // 子プロセスを監視対象に自動追加
            if (childProcessId != rawEvent.ProcessId)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _watchTargetManager.AddChildProcessAsync(childProcessId, rawEvent.ProcessId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "子プロセス自動追加でエラーが発生しました (ChildId: {ChildId}, ParentId: {ParentId})",
                            childProcessId, rawEvent.ProcessId);
                    }
                });
            }

            return new ProcessStartEventData
            {
                Timestamp = baseProperties.Timestamp,
                TagName = baseProperties.TagName,
                ProcessId = baseProperties.ProcessId,
                ThreadId = baseProperties.ThreadId,
                ProviderName = baseProperties.ProviderName,
                EventName = baseProperties.EventName,
                ActivityId = baseProperties.ActivityId,
                RelatedActivityId = baseProperties.RelatedActivityId,
                Payload = baseProperties.Payload,
                ChildProcessId = childProcessId,
                ChildProcessName = childProcessName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセス開始イベント変換エラー (Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.EventName, rawEvent.ProcessId);
            return null;
        }
    }

    /// <summary>
    /// プロセス終了イベントを変換
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <param name="baseProperties">基本プロパティ</param>
    /// <returns>プロセス終了イベントデータ</returns>
    private async Task<ProcessEndEventData?> ConvertProcessEndEventAsync(RawEventData rawEvent, dynamic baseProperties)
    {
        await Task.CompletedTask;

        try
        {
            // 終了コードの抽出
            var exitCode = ExtractExitCodeFromPayload(rawEvent.Payload);

            // プロセス終了時に監視対象から除去
            _ = Task.Run(() =>
            {
                try
                {
                    _watchTargetManager.RemoveTarget(rawEvent.ProcessId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "プロセス終了時の監視対象除去でエラーが発生しました (ProcessId: {ProcessId})", rawEvent.ProcessId);
                }
            });

            return new ProcessEndEventData
            {
                Timestamp = baseProperties.Timestamp,
                TagName = baseProperties.TagName,
                ProcessId = baseProperties.ProcessId,
                ThreadId = baseProperties.ThreadId,
                ProviderName = baseProperties.ProviderName,
                EventName = baseProperties.EventName,
                ActivityId = baseProperties.ActivityId,
                RelatedActivityId = baseProperties.RelatedActivityId,
                Payload = baseProperties.Payload,
                ExitCode = exitCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセス終了イベント変換エラー (Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.EventName, rawEvent.ProcessId);
            return null;
        }
    }

    /// <summary>
    /// ペイロードからファイルパスを抽出
    /// </summary>
    /// <param name="payload">ペイロード</param>
    /// <returns>ファイルパス</returns>
    private static string ExtractFilePathFromPayload(IReadOnlyDictionary<string, object> payload)
    {
        // ETWイベントの一般的なファイルパスフィールド名
        var filePathKeys = new[] { "FileName", "OpenPath", "FilePath", "Name" };

        foreach (var key in filePathKeys)
        {
            if (payload.TryGetValue(key, out var value) && value is string filePath && !string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// ペイロードから子プロセス情報を抽出
    /// </summary>
    /// <param name="payload">ペイロード</param>
    /// <returns>子プロセス情報</returns>
    private static (int ChildProcessId, string ChildProcessName)? ExtractChildProcessInfo(IReadOnlyDictionary<string, object> payload)
    {
        try
        {
            // 子プロセスIDの抽出
            if (!payload.TryGetValue("ProcessId", out var processIdObj) || 
                !int.TryParse(processIdObj.ToString(), out var childProcessId))
            {
                return null;
            }

            // プロセス名の抽出
            var processName = string.Empty;
            var processNameKeys = new[] { "ProcessName", "ImageFileName", "ImageName" };
            
            foreach (var key in processNameKeys)
            {
                if (payload.TryGetValue(key, out var value) && value is string name && !string.IsNullOrEmpty(name))
                {
                    processName = name;
                    break;
                }
            }

            return (childProcessId, processName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ペイロードから終了コードを抽出
    /// </summary>
    /// <param name="payload">ペイロード</param>
    /// <returns>終了コード</returns>
    private static int ExtractExitCodeFromPayload(IReadOnlyDictionary<string, object> payload)
    {
        var exitCodeKeys = new[] { "ExitStatus", "ExitCode", "Status" };

        foreach (var key in exitCodeKeys)
        {
            if (payload.TryGetValue(key, out var value) && int.TryParse(value.ToString(), out var exitCode))
            {
                return exitCode;
            }
        }

        return 0; // デフォルトは正常終了
    }
}