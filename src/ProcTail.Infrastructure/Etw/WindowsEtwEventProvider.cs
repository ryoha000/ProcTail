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
                _logger.LogInformation("統合ETWセッションを作成しました - セッション名: {SessionName}", sessionName);
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
            
            // FileIOとProcessを有効にしてtest-processのファイル操作を監視
            _logger.LogTrace("ファイルI/O監視を有効化中...");
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIO | 
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Process);  // FileIO、FileIOInit、Processを有効化
            
            _logger.LogInformation("カーネルプロバイダーを有効にしました (FileIO + Process)");
            
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
    /// TraceEventSessionを作成
    /// </summary>
    private TraceEventSession? CreateTraceSession(string providerName)
    {
        var sessionName = $"PT_{providerName.Split('-').Last()}_{Environment.ProcessId}";
        
        try
        {
            _logger.LogTrace("ETWセッションを作成中: {SessionName} (Provider: {Provider})", sessionName, providerName);
            var session = new TraceEventSession(sessionName);
            
            // プロバイダー固有の設定
            switch (providerName)
            {
                case "Microsoft-Windows-Kernel-FileIO":
                    // FileIOInitだけでなく、実際のFileIO操作イベントも有効にする
                    _logger.LogTrace("FileIOキーワードを有効化中...");
                    session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.FileIOInit | 
                        KernelTraceEventParser.Keywords.FileIO);
                    _logger.LogInformation("FileIOプロバイダーを有効にしました (Keywords: FileIOInit | FileIO)");
                    break;
                    
                case "Microsoft-Windows-Kernel-Process":
                    session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
                    break;
                    
                default:
                    // カスタムプロバイダーの場合
                    if (Guid.TryParse(providerName, out var providerGuid))
                    {
                        session.EnableProvider(providerGuid);
                    }
                    else
                    {
                        session.EnableProvider(providerName);
                    }
                    break;
            }

            // イベントハンドラーを設定
            SetupEventHandlers(session, providerName);
            
            // 各セッションを別スレッドで処理開始
            var sessionTask = Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("ETWセッション処理を開始します: {ProviderName}", providerName);
                    session.Source.Process(); // ブロッキング呼び出し
                    _logger.LogInformation("ETWセッション処理が終了しました: {ProviderName}", providerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ETWセッション処理でエラーが発生しました: {ProviderName}", providerName);
                }
            }, _cancellationTokenSource.Token);
            
            _sessionTasks.Add(sessionTask);
            
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TraceEventSession作成に失敗しました: {ProviderName}", providerName);
            return null;
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
        // session.Source.Kernel.FileIORead += OnFileIOEvent; // 要件によりReadは無効化
        session.Source.Kernel.FileIODelete += OnFileIOEvent;
        session.Source.Kernel.FileIORename += OnFileIOEvent;
        session.Source.Kernel.FileIOSetInfo += OnFileIOEvent;
        session.Source.Kernel.FileIOClose += OnFileIOEvent;
        
        // すべてのFileIOイベントをキャッチするための汎用ハンドラーも追加
        session.Source.Kernel.All += (data) => {
            if (data.EventName.StartsWith("FileIO/"))
            {
                _logger.LogError("Kernel.AllでFileIOイベントをキャッチ: {EventName}, ProcessId: {ProcessId}", data.EventName, data.ProcessID);
                OnFileIOEvent(data);
            }
        };
        
        _logger.LogInformation("FileIOイベントハンドラーを設定しました (Create, Write, Delete, Rename, SetInfo, Close + All)");
        
        // プロセスイベント
        session.Source.Kernel.ProcessStart += OnProcessEvent;
        session.Source.Kernel.ProcessStop += OnProcessEvent;
        _logger.LogDebug("プロセスイベントハンドラーを設定しました (Start, Stop)");
        
        // 汎用イベントハンドラー（デバッグ強化）
        session.Source.UnhandledEvents += OnUnhandledEvent;
        
        // すべてのイベントをキャッチするハンドラー（デバッグ用）
        session.Source.Dynamic.All += (data) => {
            if (IsTestProcess(data.ProcessID))
            {
                _logger.LogError("Dynamic.Allでtest-processイベントをキャッチ: {EventName}, ProcessId: {ProcessId}, Provider: {Provider}", 
                    data.EventName, data.ProcessID, data.ProviderName);
            }
        };
        
        _logger.LogInformation("未処理イベントハンドラーを設定しました（デバッグ強化版）");
    }

    /// <summary>
    /// イベントハンドラーを設定
    /// </summary>
    private void SetupEventHandlers(TraceEventSession session, string providerName)
    {
        // ファイルI/Oイベント
        if (providerName == "Microsoft-Windows-Kernel-FileIO")
        {
            session.Source.Kernel.FileIOCreate += OnFileIOEvent;
            session.Source.Kernel.FileIOWrite += OnFileIOEvent;
            // session.Source.Kernel.FileIORead += OnFileIOEvent; // 要件によりReadは無効化
            session.Source.Kernel.FileIODelete += OnFileIOEvent;
            session.Source.Kernel.FileIORename += OnFileIOEvent;
            session.Source.Kernel.FileIOSetInfo += OnFileIOEvent;
            session.Source.Kernel.FileIOClose += OnFileIOEvent;
            _logger.LogDebug("FileIOイベントハンドラーを設定しました (Create, Write, Delete, Rename, SetInfo, Close)");
        }
        
        // プロセスイベント
        if (providerName == "Microsoft-Windows-Kernel-Process")
        {
            session.Source.Kernel.ProcessStart += OnProcessEvent;
            session.Source.Kernel.ProcessStop += OnProcessEvent;
        }
        
        // 汎用イベントハンドラー
        session.Source.UnhandledEvents += OnUnhandledEvent;
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
            
            _logger.LogDebug("RAW FileIOイベント受信: EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}, OpcodeName={OpcodeName}", 
                eventName, processId, data.ProviderName, data.OpcodeName);
                
            // 追加デバッグ: test-processのFileIOイベントを詳細にログ出力
            if (IsTestProcess(processId))
            {
                _logger.LogInformation("DEBUG FileIOイベント詳細: EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}, OpcodeName={OpcodeName}, TaskName={TaskName}, Level={Level}", 
                    eventName, processId, data.ProviderName, data.OpcodeName, data.TaskName, data.Level);
            }
            
            // test-processからのイベントかチェック（プロセス名で判定）
            if (IsTestProcess(processId))
            {
                _logger.LogError("TEST-PROCESS FileIOイベント受信!!! EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}, OpcodeName={OpcodeName}, TaskName={TaskName}, TimeStamp={TimeStamp}", 
                    eventName, processId, data.ProviderName, data.OpcodeName, data.TaskName, data.TimeStamp);
            }
            
            // TraceEventからペイロード情報を取得
            for (int i = 0; i < data.PayloadNames.Length; i++)
            {
                var name = data.PayloadNames[i];
                var value = data.PayloadValue(i);
                payload[name] = value ?? string.Empty;
            }

            // ファイルパス情報を取得（デバッグ強化）
            var fileName = payload.ContainsKey("FileName") ? payload["FileName"].ToString() : "Unknown";
            
            // test-processの場合、ペイロード詳細をログ出力
            if (IsTestProcess(processId))
            {
                var payloadStr = string.Join(", ", payload.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                _logger.LogError("test-process FileIOイベントペイロード: ProcessId={ProcessId}, FileName={FileName}, Payload=[{Payload}]", 
                    processId, fileName, payloadStr);
            }
            
            _logger.LogTrace("FileIOイベントを受信: {EventName}, ProcessId: {ProcessId}, FileName: {FileName}", 
                eventName, processId, fileName);

            // イベント名を適切にフォーマット
            var formattedEventName = FormatEventName(eventName, "FileIO");
            
            var rawEvent = new RawEventData(
                data.TimeStamp,
                data.ProviderName, // 実際のプロバイダー名を使用
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
            var processId = data.ProcessID;
            var payload = new Dictionary<string, object>();
            
            // 全プロセスイベントをログ出力（デバッグ用）
            _logger.LogDebug("RAW Processイベント受信: EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}, OpcodeName={OpcodeName}", 
                eventName, processId, data.ProviderName, data.OpcodeName);
                
            // test-processからのイベントかチェック
            if (IsTestProcess(processId))
            {
                _logger.LogError("TEST-PROCESS Processイベント受信!!! EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}, OpcodeName={OpcodeName}, TimeStamp={TimeStamp}", 
                    eventName, processId, data.ProviderName, data.OpcodeName, data.TimeStamp);
            }
            
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
            var formattedEventName = FormatEventName(eventName, "Process");
            
            var rawEvent = new RawEventData(
                data.TimeStamp,
                data.ProviderName, // 実際のプロバイダー名を使用
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

            // 設定で有効になっているイベントのみ処理（設定が空の場合はすべて通す）
            if (_configuration.EnabledEventNames.Count > 0 && !_configuration.EnabledEventNames.Contains(data.EventName))
            {
                _logger.LogTrace("イベントはフィルタリングされました: {EventName} (有効なイベント一覧にない)", data.EventName);
                return;
            }

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

    /// <summary>
    /// ETWイベント名を一貫した形式にフォーマット
    /// </summary>
    /// <param name="eventName">元のイベント名</param>
    /// <param name="prefix">プレフィックス（FileIO, Process等）</param>
    /// <returns>フォーマットされたイベント名</returns>
    private static string FormatEventName(string eventName, string prefix)
    {
        // 既に正しい形式の場合はそのまま返す
        if (eventName.Contains("/"))
        {
            return eventName;
        }

        // プレフィックスで始まる場合はスラッシュ形式に変換
        if (eventName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = eventName.Substring(prefix.Length);
            return $"{prefix}/{suffix}";
        }

        // プレフィックスがない場合はそのまま返す
        return eventName;
    }

    /// <summary>
    /// test-processかどうかを判定するヘルパーメソッド
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>test-processの場合true</returns>
    private bool IsTestProcess(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var mainModuleName = process.MainModule?.ModuleName ?? "Unknown";
            
            var isTestProcess = processName.Contains("test-process", StringComparison.OrdinalIgnoreCase) ||
                               processName.Contains("proctail_test", StringComparison.OrdinalIgnoreCase) ||
                               mainModuleName.Contains("test-process", StringComparison.OrdinalIgnoreCase);
                               
            if (isTestProcess)
            {
                _logger.LogError("IsTestProcess: PID={ProcessId}, ProcessName={ProcessName}, MainModule={MainModule} -> TRUE", 
                    processId, processName, mainModuleName);
            }
            
            return isTestProcess;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("IsTestProcess: PID={ProcessId} -> プロセス情報取得失敗: {Error}", processId, ex.Message);
            return false;
        }
    }
}

