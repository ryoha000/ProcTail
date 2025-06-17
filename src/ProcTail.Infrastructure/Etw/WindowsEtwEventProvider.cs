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
            await StopMonitoringAsync(cancellationToken);
            throw;
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
            
            // まず既存のセッションをクリーンアップ
            try
            {
                CleanupExistingEtwSessions();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "既存ETWセッションクリーンアップ中に警告が発生しました");
            }
            
            var session = new TraceEventSession(sessionName);
            
            // より限定的なキーワードで開始（リソース消費を削減）
            _logger.LogTrace("軽量カーネルプロバイダーを有効化中...");
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.Process);  // まずProcessのみ
            
            _logger.LogInformation("カーネルプロバイダーを有効にしました (Process)");
            
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
            _logger.LogDebug("既存のProcTail ETWセッションをクリーンアップ中...");
            
            // Process.Startを使ってlogman経由でセッションを停止
            var cleanupCommands = new[]
            {
                "logman stop \"ProcTail*\" -ets",
                "logman stop \"PT_*\" -ets"
            };
            
            foreach (var command in cleanupCommands)
            {
                try
                {
                    using var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = "cmd.exe";
                    process.StartInfo.Arguments = $"/c {command} 2>nul";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit(2000); // 2秒でタイムアウト
                }
                catch
                {
                    // クリーンアップのエラーは無視
                }
            }
            
            // 少し待機してからセッション作成
            Thread.Sleep(1000);
            
            _logger.LogDebug("ETWセッションクリーンアップが完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ETWセッションクリーンアップ中にエラーが発生しました");
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
    /// イベントハンドラーを設定
    /// </summary>
    private void SetupEventHandlers(TraceEventSession session, string providerName)
    {
        // ファイルI/Oイベント
        if (providerName == "Microsoft-Windows-Kernel-FileIO")
        {
            session.Source.Kernel.FileIOCreate += OnFileIOEvent;
            session.Source.Kernel.FileIOWrite += OnFileIOEvent;
            session.Source.Kernel.FileIORead += OnFileIOEvent;
            session.Source.Kernel.FileIODelete += OnFileIOEvent;
            session.Source.Kernel.FileIORename += OnFileIOEvent;
            session.Source.Kernel.FileIOSetInfo += OnFileIOEvent;
            session.Source.Kernel.FileIOClose += OnFileIOEvent;
            _logger.LogDebug("FileIOイベントハンドラーを設定しました (Create, Write, Read, Delete, Rename, SetInfo, Close)");
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
            
            _logger.LogTrace("RAW FileIOイベント受信: EventName={EventName}, ProcessId={ProcessId}, Provider={Provider}", 
                eventName, processId, data.ProviderName);
            
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

            var rawEvent = new RawEventData(
                data.TimeStamp,
                "Microsoft-Windows-Kernel-FileIO",
                $"FileIO/{eventName}",
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

            var rawEvent = new RawEventData(
                data.TimeStamp,
                "Microsoft-Windows-Kernel-Process",
                eventName,
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

            // 設定で有効になっているイベントのみ処理
            if (!_configuration.EnabledEventNames.Contains(data.EventName))
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
}

