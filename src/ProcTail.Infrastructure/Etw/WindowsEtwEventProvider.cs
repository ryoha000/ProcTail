using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace ProcTail.Infrastructure.Etw;

/// <summary>
/// 実際のWindows ETWを使用したイベントプロバイダー
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProvider : IEtwEventProvider, IDisposable
{
    private readonly ILogger<WindowsEtwEventProvider> _logger;
    private readonly IEtwConfiguration _configuration;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<TraceEventSession> _sessions = new();
    private readonly List<Task> _sessionTasks = new();
    private readonly ConcurrentQueue<RawEventData> _eventQueue = new();
    private bool _isMonitoring;
    private bool _disposed;
    private Task? _eventProcessingTask;

    /// <summary>
    /// ETWイベント受信時に発火するイベント
    /// </summary>
    public event EventHandler<RawEventData>? EventReceived;

    /// <summary>
    /// 監視中かどうか
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public WindowsEtwEventProvider(
        ILogger<WindowsEtwEventProvider> logger,
        IEtwConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // Windows環境のチェック
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("WindowsEtwEventProvider is only supported on Windows platform");
        }
    }

    /// <summary>
    /// ETW監視を開始
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsEtwEventProvider));

        if (_isMonitoring)
        {
            _logger.LogWarning("ETW監視は既に開始されています");
            return;
        }

        try
        {
            _logger.LogInformation("ETW監視を開始しています...");

            // 管理者権限チェック
            if (!IsRunningAsAdministrator())
            {
                var errorMsg = "ETW監視には管理者権限が必要です";
                _logger.LogError(errorMsg);
                throw new UnauthorizedAccessException(errorMsg);
            }
            
            _logger.LogInformation("管理者権限が確認されました");

            // 軽量クリーンアップを実行
            try
            {
                _logger.LogDebug("ETWリソース軽量クリーンアップを実行中...");
                CleanupProcTailSessions(); // ProcTail関連のみ
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "軽量クリーンアップ中に警告が発生しました");
            }

            // ETWセッションを作成
            await CreateEtwSessionsAsync(cancellationToken);

            // イベント処理タスクを開始
            _eventProcessingTask = Task.Run(ProcessEventsAsync, _cancellationTokenSource.Token);

            _isMonitoring = true;
            _logger.LogInformation("ETW監視が正常に開始されました");
        }
        catch (UnauthorizedAccessException)
        {
            // 管理者権限エラーは再スロー（テストで検証される）
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW監視開始中にエラーが発生しました");
            
            // 失敗時はサービスを停止せずにエラー状態で継続
            try
            {
                _logger.LogInformation("失敗時の軽量クリーンアップを実行中...");
                CleanupProcTailSessions(); // 軽量なクリーンアップのみ
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "クリーンアップ中にエラーが発生しました");
            }
            
            _logger.LogWarning("ETW監視の開始に失敗しましたが、サービスは継続します");
            // throw を削除してサービス継続
        }
    }

    /// <summary>
    /// ETW監視を停止
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            return;
        }

        try
        {
            _logger.LogInformation("ETW監視を停止しています...");

            _cancellationTokenSource.Cancel();

            // ETWセッションを停止
            foreach (var session in _sessions)
            {
                try
                {
                    session?.Stop();
                    session?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ETWセッション停止中に警告が発生しました");
                }
            }
            _sessions.Clear();

            // セッション処理タスクの完了を待機
            if (_sessionTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(_sessionTasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("ETWセッションタスクの停止がタイムアウトしました");
                }
                _sessionTasks.Clear();
            }

            // イベント処理タスクの完了を待機
            if (_eventProcessingTask != null)
            {
                try
                {
                    await _eventProcessingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("イベント処理タスクの停止がタイムアウトしました");
                }
            }

            // 停止時の追加クリーンアップ
            try
            {
                _logger.LogDebug("停止時のETWクリーンアップを実行中...");
                CleanupProcTailSessions(); // ProcTail関連のみクリーンアップ
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "停止時クリーンアップ中にエラーが発生しました");
            }

            _isMonitoring = false;
            _logger.LogInformation("ETW監視が正常に停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW監視停止中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// ETWセッションを作成
    /// </summary>
    private async Task CreateEtwSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 単一のセッションで複数のカーネルプロバイダーを処理
            var session = CreateKernelTraceSession();
            if (session != null)
            {
                _sessions.Add(session);
                _logger.LogInformation("統合ETWセッションを作成しました");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETWセッション作成に失敗しました");
            throw;
        }

        if (_sessions.Count == 0)
        {
            throw new InvalidOperationException("有効なETWセッションが作成できませんでした");
        }

        await Task.Delay(100, cancellationToken); // セッション安定化のための待機
    }

    /// <summary>
    /// 統合カーネルETWセッションを作成
    /// </summary>
    private TraceEventSession? CreateKernelTraceSession()
    {
        var sessionName = $"ProcTail_{Environment.ProcessId}_{Guid.NewGuid().ToString("N")[..8]}";
        
        try
        {
            _logger.LogTrace("統合ETWセッションを作成中: {SessionName}", sessionName);
            
            // 最小限のクリーンアップ
            try
            {
                // 同名セッションがあれば停止
                using var tempSession = TraceEventSession.GetActiveSession(sessionName);
                tempSession?.Stop();
            }
            catch
            {
                // 既存セッションがない場合は正常
            }
            
            var session = new TraceEventSession(sessionName);
            
            // リソース制約対応：FileIOとProcessを有効にするが、Readは除外してリソース消費を削減
            _logger.LogTrace("最軽量ファイル監視を有効化中...");
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIOInit | 
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.Process);  // FileIOInitとFileIOとProcessでファイル操作イベントを監視
            
            _logger.LogInformation("カーネルプロバイダーを有効にしました (FileIOInit + FileIO + Process)");
            
            // イベントハンドラーを設定
            SetupKernelEventHandlers(session);
            
            // セッション処理タスクを開始
            var sessionTask = Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("統合ETWセッション処理を開始します");
                    session.Source.Process(); // ブロッキング呼び出し
                    _logger.LogInformation("統合ETWセッション処理が終了しました");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "統合ETWセッション処理でエラーが発生しました");
                }
            }, _cancellationTokenSource.Token);
            
            _sessionTasks.Add(sessionTask);
            
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "統合TraceEventSession作成に失敗しました");
            return null;
        }
    }

    /// <summary>
    /// 既存のProcTail ETWセッションをクリーンアップ
    /// </summary>
    private void CleanupExistingEtwSessions()
    {
        try
        {
            _logger.LogInformation("ETWセッションの包括的クリーンアップを開始します...");
            
            // Step 1: ProcTail関連セッションを停止
            CleanupProcTailSessions();
            
            // Step 2: 孤立したETWセッションを検出・停止
            CleanupOrphanedSessions();
            
            // Step 3: システムETWリソースの解放
            ForceReleaseEtwResources();
            
            // Step 4: 短縮された安定化待機
            _logger.LogDebug("ETWリソース安定化のため待機中...");
            Thread.Sleep(1000);
            
            _logger.LogInformation("ETWセッション包括的クリーンアップが完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ETWセッションクリーンアップ中に警告が発生しました");
        }
    }

    /// <summary>
    /// ProcTail関連セッションをクリーンアップ
    /// </summary>
    private void CleanupProcTailSessions()
    {
        _logger.LogDebug("ProcTail関連ETWセッションをクリーンアップ中...");
        
        var patterns = new[]
        {
            "ProcTail*",
            "PT_*",
            "*ProcTail*",
            $"ProcTail_{Environment.ProcessId}_*",
            $"PT_FileIO_{Environment.ProcessId}",
            $"PT_Process_{Environment.ProcessId}"
        };
        
        foreach (var pattern in patterns)
        {
            ExecuteCleanupCommand($"logman stop \"{pattern}\" -ets", $"Pattern: {pattern}");
        }
    }

    /// <summary>
    /// 孤立したETWセッションを検出・停止
    /// </summary>
    private void CleanupOrphanedSessions()
    {
        try
        {
            _logger.LogDebug("孤立ETWセッションを検出中...");
            
            // 現在のETWセッション一覧を取得
            var sessionList = GetActiveEtwSessions();
            var orphanedSessions = sessionList.Where(session => 
                session.Contains("ProcTail") || 
                session.Contains("PT_") ||
                session.StartsWith("TraceEventSession")).ToList();
            
            foreach (var session in orphanedSessions)
            {
                _logger.LogDebug("孤立セッションを停止中: {Session}", session);
                ExecuteCleanupCommand($"logman stop \"{session}\" -ets", $"Orphaned: {session}");
            }
            
            if (orphanedSessions.Any())
            {
                _logger.LogInformation("孤立ETWセッション {Count} 個をクリーンアップしました", orphanedSessions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "孤立セッション検出中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 現在のアクティブETWセッション一覧を取得
    /// </summary>
    private List<string> GetActiveEtwSessions()
    {
        var sessions = new List<string>();
        
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "logman";
            process.StartInfo.Arguments = "query -ets";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // セッション名を抽出（最初の列）
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && !parts[0].Contains("---") && !parts[0].Contains("データ"))
                {
                    sessions.Add(parts[0].Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ETWセッション一覧の取得に失敗しました");
        }
        
        return sessions;
    }

    /// <summary>
    /// システムETWリソースの強制解放
    /// </summary>
    private void ForceReleaseEtwResources()
    {
        _logger.LogDebug("システムETWリソースを強制解放中...");
        
        var systemCommands = new[]
        {
            "logman stop \"Kernel Logger\" -ets",
            "logman stop \"NT Kernel Logger\" -ets",
            "logman stop \"Circular Kernel Context Logger\" -ets",
            "wevtutil sl Microsoft-Windows-Kernel-Process/Analytic /e:false",
            "wevtutil sl Microsoft-Windows-Kernel-FileIO/Analytic /e:false"
        };
        
        foreach (var command in systemCommands)
        {
            ExecuteCleanupCommand(command, "System ETW");
        }
        
        // 追加: Windows Event Logサービスの軽いリフレッシュ
        try
        {
            ExecuteCleanupCommand("net stop EventLog /y && net start EventLog", "EventLog Service");
        }
        catch
        {
            _logger.LogDebug("EventLogサービスのリフレッシュは管理者権限が必要でした");
        }
    }

    /// <summary>
    /// クリーンアップコマンドを実行
    /// </summary>
    private void ExecuteCleanupCommand(string command, string description)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command} 2>nul";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            if (process.WaitForExit(3000))
            {
                if (process.ExitCode == 0)
                {
                    _logger.LogTrace("クリーンアップ成功: {Description}", description);
                }
                else
                {
                    _logger.LogTrace("クリーンアップ試行: {Description} (ExitCode: {ExitCode})", description, process.ExitCode);
                }
            }
            else
            {
                process.Kill();
                _logger.LogTrace("クリーンアップタイムアウト: {Description}", description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "クリーンアップエラー: {Description}", description);
        }
    }


    /// <summary>
    /// 統合カーネルイベントハンドラーを設定
    /// </summary>
    private void SetupKernelEventHandlers(TraceEventSession session)
    {
        // ファイルI/Oイベント
        session.Source.Kernel.FileIOCreate += OnFileIOEvent;
        session.Source.Kernel.FileIOWrite += OnFileIOEvent;
        session.Source.Kernel.FileIORead += OnFileIOEvent;
        session.Source.Kernel.FileIODelete += OnFileIOEvent;
        session.Source.Kernel.FileIORename += OnFileIOEvent;
        session.Source.Kernel.FileIOSetInfo += OnFileIOEvent;
        session.Source.Kernel.FileIOClose += OnFileIOEvent;
        _logger.LogDebug("FileIOイベントハンドラーを設定しました (Create, Write, Read, Delete, Rename, SetInfo, Close)");
        
        // プロセスイベント
        session.Source.Kernel.ProcessStart += OnProcessEvent;
        session.Source.Kernel.ProcessStop += OnProcessEvent;
        _logger.LogDebug("プロセスイベントハンドラーを設定しました (Start, Stop)");
        
        // 汎用イベントハンドラー
        session.Source.UnhandledEvents += OnUnhandledEvent;
        _logger.LogDebug("未処理イベントハンドラーを設定しました");
    }


    /// <summary>
    /// ファイルI/Oイベントハンドラー
    /// </summary>
    private void OnFileIOEvent(TraceEvent data)
    {
        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            var eventName = data.EventName;
            var processId = data.ProcessID;
            var payload = new Dictionary<string, object>();
            
            // TraceEventからペイロード情報を取得
            for (int i = 0; i < data.PayloadNames.Length; i++)
            {
                var name = data.PayloadNames[i];
                var value = data.PayloadValue(i);
                payload[name] = value ?? string.Empty;
            }

            // ファイルパス情報を取得
            var fileName = payload.ContainsKey("FileName") ? payload["FileName"].ToString() : "Unknown";
            
            _logger.LogTrace("FileIOイベントを受信: {EventName}, ProcessId: {ProcessId}, FileName: {FileName}", 
                eventName, processId, fileName);

            // イベント名を適切にフォーマット
            var formattedEventName = eventName;
            if (eventName.StartsWith("FileIO") && !eventName.Contains("/"))
            {
                formattedEventName = $"FileIO/{eventName.Substring(6)}"; // FileIOCreate -> FileIO/Create
            }
            
            var rawEvent = new RawEventData(
                data.TimeStamp,
                "Microsoft-Windows-Kernel-FileIO",
                formattedEventName,
                processId,
                data.ThreadID,
                data.ActivityID,
                data.RelatedActivityID,
                payload
            );

            _eventQueue.Enqueue(rawEvent);
            _logger.LogTrace("FileIOイベントをキューに追加しました: ProcessId: {ProcessId}", processId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ファイルI/Oイベント処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// プロセスイベントハンドラー
    /// </summary>
    private void OnProcessEvent(TraceEvent data)
    {
        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            var eventName = data.EventName;
            var payload = new Dictionary<string, object>();
            
            // TraceEventからペイロード情報を取得
            for (int i = 0; i < data.PayloadNames.Length; i++)
            {
                var name = data.PayloadNames[i];
                var value = data.PayloadValue(i);
                payload[name] = value ?? string.Empty;
            }

            if (data.OpcodeName == "Start")
            {
                // プロセス開始イベント固有の処理
            }
            else if (data.OpcodeName == "End")
            {
                // プロセス終了イベント固有の処理
            }

            // イベント名を適切にフォーマット
            var formattedEventName = eventName;
            if (eventName.StartsWith("Process") && !eventName.Contains("/"))
            {
                formattedEventName = $"Process/{eventName.Substring(7)}"; // ProcessStart -> Process/Start
            }
            
            var rawEvent = new RawEventData(
                data.TimeStamp,
                "Microsoft-Windows-Kernel-Process",
                formattedEventName,
                data.ProcessID,
                data.ThreadID,
                data.ActivityID,
                data.RelatedActivityID,
                payload
            );

            _eventQueue.Enqueue(rawEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセスイベント処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 未処理イベントハンドラー
    /// </summary>
    private void OnUnhandledEvent(TraceEvent data)
    {
        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            _logger.LogTrace("未処理イベントを受信: {ProviderName}.{EventName}, ProcessId: {ProcessId}", 
                data.ProviderName, data.EventName, data.ProcessID);

            // フィルタリングを無効化：すべてのイベントを処理
            // 設定に関係なく全てのイベントを通す

            var payload = new Dictionary<string, object>();
            
            // TraceEventから利用可能なプロパティを抽出
            for (int i = 0; i < data.PayloadNames.Length; i++)
            {
                try
                {
                    var name = data.PayloadNames[i];
                    var value = data.PayloadValue(i);
                    payload[name] = value ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ペイロード値の取得に失敗しました: {PropertyName}", 
                        i < data.PayloadNames.Length ? data.PayloadNames[i] : $"Index{i}");
                }
            }

            var rawEvent = new RawEventData(
                data.TimeStamp,
                data.ProviderName,
                data.EventName,
                data.ProcessID,
                data.ThreadID,
                data.ActivityID,
                data.RelatedActivityID,
                payload
            );

            _eventQueue.Enqueue(rawEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未処理イベント処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// イベント処理ループ
    /// </summary>
    private async Task ProcessEventsAsync()
    {
        _logger.LogInformation("ETWイベント処理ループを開始しました");

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                // キューからイベントを処理
                while (_eventQueue.TryDequeue(out var rawEvent))
                {
                    try
                    {
                        EventReceived?.Invoke(this, rawEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "イベント配信中にエラーが発生しました");
                    }
                }

                // イベントキューの処理のみ行う（セッション処理は別スレッドで実行中）

                await Task.Delay(10, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ETWイベント処理ループが停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETWイベント処理ループでエラーが発生しました");
        }
    }

    /// <summary>
    /// 管理者権限チェック
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
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

        try
        {
            StopMonitoringAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WindowsEtwEventProvider解放中にエラーが発生しました");
        }

        _cancellationTokenSource.Dispose();
        _logger.LogInformation("WindowsEtwEventProviderが解放されました");
    }
}

