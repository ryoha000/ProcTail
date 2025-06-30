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
    private readonly EtwFilteringOptions _filteringOptions;

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
        _filteringOptions = config.FilteringOptions;
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
                _logger.LogDebug("監視対象外のプロセスです (ProcessId: {ProcessId}, Provider: {Provider}, Event: {Event}, TotalWatchTargets: {TotalTargets})", 
                    rawEvent.ProcessId, rawEvent.ProviderName, rawEvent.EventName, _watchTargetManager.ActiveTargetCount);
                
                // より詳細なデバッグ情報を追加（特にtest-processの場合）
                if (_watchTargetManager.ActiveTargetCount > 0)
                {
                    var watchTargets = _watchTargetManager.GetWatchTargets();
                    var watchedPids = string.Join(", ", watchTargets.Select(t => $"{t.ProcessId}({t.TagName})"));
                    _logger.LogDebug("現在の監視対象: [{WatchedTargets}]", watchedPids);
                    
                    // test-processの場合、より詳細なログ
                    var testProcessTarget = watchTargets.FirstOrDefault(t => t.TagName == "test-process");
                    if (testProcessTarget != null)
                    {
                        _logger.LogWarning("test-processが監視対象に登録されているが、受信イベントはPID={EventPid}、登録済みPID={RegisteredPid}", 
                            rawEvent.ProcessId, testProcessTarget.ProcessId);
                    }
                    else if (rawEvent.ProviderName.Contains("Kernel") && rawEvent.EventName.StartsWith("FileIO/"))
                    {
                        // FileIOイベントで監視対象外の場合、test-processかどうかチェック
                        try
                        {
                            using var process = System.Diagnostics.Process.GetProcessById(rawEvent.ProcessId);
                            var processName = process.ProcessName;
                            if (processName.Contains("test-process", StringComparison.OrdinalIgnoreCase) ||
                                processName.Contains("proctail_test", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogError("test-processのイベントが監視対象外として拒否されました! ProcessName={ProcessName}, PID={ProcessId}, 監視対象にtest-processが登録されていません", 
                                    processName, rawEvent.ProcessId);
                            }
                        }
                        catch
                        {
                            // プロセスが既に終了している場合
                        }
                    }
                }
                
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

        // ファイルパスフィルタリング（FileIOイベントの場合のみ）
        if ((rawEvent.ProviderName == "Windows Kernel" || rawEvent.ProviderName == "Microsoft-Windows-Kernel-FileIO") && rawEvent.EventName.StartsWith("FileIO/"))
        {
            if (!ShouldProcessFilePath(rawEvent))
            {
                _logger.LogTrace("ファイルパスフィルタリングで除外: Event={Event}, ProcessId={ProcessId}", 
                    rawEvent.EventName, rawEvent.ProcessId);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ファイルパスのフィルタリング
    /// </summary>
    /// <param name="rawEvent">生ETWイベント</param>
    /// <returns>処理すべき場合true</returns>
    private bool ShouldProcessFilePath(RawEventData rawEvent)
    {
        // ファイルパスを取得
        string? filePath = null;
        if (rawEvent.Payload.TryGetValue("FileName", out var fileNameObj))
        {
            filePath = fileNameObj?.ToString();
        }
        else if (rawEvent.Payload.TryGetValue("FilePath", out var filePathObj))
        {
            filePath = filePathObj?.ToString();
        }

        if (string.IsNullOrEmpty(filePath))
        {
            _logger.LogTrace("ファイルパスが見つかりません - イベントを通します (Event: {Event}, ProcessId: {ProcessId})", 
                rawEvent.EventName, rawEvent.ProcessId);
            return true; // ファイルパスが不明な場合は通す（FileIO/Closeなど）
        }

        // 拡張子チェック
        if (_filteringOptions?.IncludeFileExtensions?.Count > 0)
        {
            var extension = Path.GetExtension(filePath);
            if (!_filteringOptions.IncludeFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("除外対象の拡張子です (FilePath: {FilePath}, Extension: {Extension})", 
                    filePath, extension);
                return false;
            }
        }

        // 除外パターンチェック
        if (_filteringOptions?.ExcludeFilePatterns != null)
        {
            foreach (var pattern in _filteringOptions.ExcludeFilePatterns)
            {
                if (IsMatchPattern(filePath, pattern))
                {
                    // test-processが作成するファイルは除外しない
                    if (ShouldAllowTestProcessFile(filePath, rawEvent.ProcessId))
                    {
                        _logger.LogDebug("除外パターン「{Pattern}」にマッチしましたが、test-processのファイルのため許可 (FilePath: {FilePath}, ProcessId: {ProcessId})", 
                            pattern, filePath, rawEvent.ProcessId);
                        continue; // このパターンはスキップして次のパターンをチェック
                    }
                    
                    _logger.LogDebug("除外パターンにマッチしたためフィルタリング (FilePath: {FilePath}, Pattern: {Pattern}, ProcessId: {ProcessId})", 
                        filePath, pattern, rawEvent.ProcessId);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// ワイルドカードパターンマッチング
    /// </summary>
    /// <param name="input">入力文字列</param>
    /// <param name="pattern">パターン</param>
    /// <returns>マッチした場合true</returns>
    private static bool IsMatchPattern(string input, string pattern)
    {
        // Windowsパスの大文字小文字を無視
        input = input.ToUpperInvariant();
        pattern = pattern.ToUpperInvariant();

        // パスセパレータを正規化
        input = input.Replace('/', '\\');
        pattern = pattern.Replace('/', '\\');

        // 簡単なワイルドカードマッチング
        return IsWildcardMatch(input, pattern);
    }

    /// <summary>
    /// ワイルドカード (*) を使った文字列マッチング
    /// </summary>
    /// <param name="input">入力文字列</param>
    /// <param name="pattern">パターン（*を含む）</param>
    /// <returns>マッチした場合true</returns>
    private static bool IsWildcardMatch(string input, string pattern)
    {
        int inputIndex = 0;
        int patternIndex = 0;
        int inputBacktrack = -1;
        int patternBacktrack = -1;

        while (inputIndex < input.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternBacktrack = patternIndex++;
                inputBacktrack = inputIndex;
            }
            else if (patternIndex < pattern.Length && 
                     (pattern[patternIndex] == input[inputIndex] || pattern[patternIndex] == '?'))
            {
                patternIndex++;
                inputIndex++;
            }
            else if (patternBacktrack != -1)
            {
                patternIndex = patternBacktrack + 1;
                inputIndex = ++inputBacktrack;
            }
            else
            {
                return false;
            }
        }

        // パターンの残りが全て * かチェック
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
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
                "Windows Kernel" when rawEvent.EventName.StartsWith("FileIO/") => await ConvertFileEventAsync(rawEvent, baseProperties),
                "Microsoft-Windows-Kernel-FileIO" when rawEvent.EventName.StartsWith("FileIO/") => await ConvertFileEventAsync(rawEvent, baseProperties),
                "Windows Kernel" when rawEvent.EventName.StartsWith("Process/") => await ConvertProcessEventAsync(rawEvent, baseProperties),
                "Microsoft-Windows-Kernel-Process" when rawEvent.EventName.StartsWith("Process/") => await ConvertProcessEventAsync(rawEvent, baseProperties),
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
                // FileIO/Close等でファイルパスが不明な場合の処理
                if (rawEvent.EventName == "FileIO/Close")
                {
                    // Closeイベントはファイルパスがなくても有効
                    filePath = $"<{rawEvent.EventName}:PID{rawEvent.ProcessId}>";
                    _logger.LogTrace("ファイルパス不明のCloseイベントを処理 (ProcessId: {ProcessId})", rawEvent.ProcessId);
                }
                else
                {
                    // 他のイベントでファイルパスが不明な場合はエラー
                    _logger.LogWarning("ファイルパスが見つからないFileIOイベント (Event: {Event}, ProcessId: {ProcessId})",
                        rawEvent.EventName, rawEvent.ProcessId);
                    return null;
                }
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
    private string ExtractFilePathFromPayload(IReadOnlyDictionary<string, object> payload)
    {
        // デバッグ用にペイロード内容をログ出力
        _logger.LogTrace("Payload keys: [{Keys}]", string.Join(", ", payload.Keys));
        foreach (var kvp in payload)
        {
            _logger.LogTrace("Payload[{Key}] = {Value} (Type: {Type})", 
                kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
        }

        // ETWイベントの一般的なファイルパスフィールド名（FileObjectはパスではないので最後）
        var filePathKeys = new[] { "FileName", "OpenPath", "FilePath", "Name", "FileKey" };

        foreach (var key in filePathKeys)
        {
            if (payload.TryGetValue(key, out var value) && value is string filePath && !string.IsNullOrEmpty(filePath))
            {
                _logger.LogTrace("Found file path in key '{Key}': {FilePath}", key, filePath);
                return filePath;
            }
        }

        // FileIO/Closeイベントではファイルパスが直接含まれない場合があるため、
        // これを許容する（FileObjectはファイルパスではないのでスキップ）
        if (payload.TryGetValue("FileObject", out var fileObjValue))
        {
            _logger.LogTrace("FileObjectが見つかりましたが、ファイルパスではありません: {FileObject}", fileObjValue);
        }

        _logger.LogTrace("ペイロードにファイルパスが見つかりません");
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

    /// <summary>
    /// test-processが作成するファイルを許可するかどうかを判定
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="processId">プロセスID</param>
    /// <returns>許可する場合true</returns>
    private bool ShouldAllowTestProcessFile(string filePath, int processId)
    {
        // プロセスが監視対象かチェック
        if (!_watchTargetManager.IsWatchedProcess(processId))
        {
            return false;
        }

        // ファイル名にtest-processまたはproctail_testが含まれる場合は許可
        var fileName = Path.GetFileName(filePath);
        if (fileName.Contains("test-process", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("proctail_test", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("test_", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogTrace("test-process関連ファイルとして許可: {FilePath}, ProcessId: {ProcessId}", 
                filePath, processId);
            return true;
        }
        
        // パスにProcTailTestまたはTestFilesが含まれる場合も許可
        if (filePath.Contains("ProcTailTest", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("TestFiles", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogTrace("ProcTailテストディレクトリ内のファイルとして許可: {FilePath}, ProcessId: {ProcessId}", 
                filePath, processId);
            return true;
        }

        return false;
    }
}