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
                throw new UnauthorizedAccessException("ETW監視には管理者権限が必要です");
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
                    session?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ETWセッション停止中に警告が発生しました");
                }
            }
            _sessions.Clear();

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
        var enabledProviders = _configuration.EnabledProviders;
        
        foreach (var providerName in enabledProviders)
        {
            try
            {
                var session = CreateTraceSession(providerName);
                if (session != null)
                {
                    _sessions.Add(session);
                    _logger.LogInformation("ETWプロバイダーセッションを作成しました: {ProviderName}", providerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ETWプロバイダーセッション作成に失敗しました: {ProviderName}", providerName);
                throw;
            }
        }

        if (_sessions.Count == 0)
        {
            throw new InvalidOperationException("有効なETWセッションが作成できませんでした");
        }

        await Task.Delay(100, cancellationToken); // セッション安定化のための待機
    }

    /// <summary>
    /// TraceEventSessionを作成
    /// </summary>
    private TraceEventSession? CreateTraceSession(string providerName)
    {
        var sessionName = $"ProcTail_{providerName}_{Guid.NewGuid():N}"[0..32];
        
        try
        {
            var session = new TraceEventSession(sessionName);
            
            // プロバイダー固有の設定
            switch (providerName)
            {
                case "Microsoft-Windows-Kernel-FileIO":
                    session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit);
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
            
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TraceEventSession作成に失敗しました: {ProviderName}", providerName);
            return null;
        }
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
            var payload = new Dictionary<string, object>();
            
            // TraceEventからペイロード情報を取得
            for (int i = 0; i < data.PayloadNames.Length; i++)
            {
                var name = data.PayloadNames[i];
                var value = data.PayloadValue(i);
                payload[name] = value ?? string.Empty;
            }

            var rawEvent = new RawEventData(
                data.TimeStamp,
                "Microsoft-Windows-Kernel-FileIO",
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
            // 設定で有効になっているイベントのみ処理
            if (!_configuration.EnabledEventNames.Contains(data.EventName))
                return;

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

                // ETWセッションからのイベント処理
                foreach (var session in _sessions.ToList())
                {
                    try
                    {
                        session.Source.Process();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "ETWセッション処理中にエラーが発生しました");
                    }
                }

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

