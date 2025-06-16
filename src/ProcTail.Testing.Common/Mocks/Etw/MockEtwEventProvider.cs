using System.Collections.Concurrent;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Testing.Common.Mocks.Etw;

/// <summary>
/// モックETWイベントプロバイダー
/// </summary>
public class MockEtwEventProvider : IEtwEventProvider
{
    private readonly MockEtwConfiguration _config;
    private readonly MockEventGenerator _eventGenerator;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isMonitoring;
    private bool _disposed;

    /// <summary>
    /// ETWイベント受信イベント
    /// </summary>
    public event EventHandler<RawEventData>? EventReceived;

    /// <summary>
    /// 監視中かどうか
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="config">モック設定</param>
    public MockEtwEventProvider(MockEtwConfiguration? config = null)
    {
        _config = config ?? MockEtwConfiguration.Default;
        _eventGenerator = new MockEventGenerator(_config);
    }

    /// <summary>
    /// ETW監視を開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MockEtwEventProvider));

        if (_isMonitoring)
            return Task.CompletedTask;

        _isMonitoring = true;
        
        // バックグラウンドでイベント生成を開始
        _ = Task.Run(GenerateEventsAsync, _cancellation.Token);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// ETW監視を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _isMonitoring = false;
        _cancellation.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 手動でイベントを発火（テスト用）
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    public void TriggerEvent(RawEventData eventData)
    {
        if (_isMonitoring)
        {
            EventReceived?.Invoke(this, eventData);
        }
    }

    /// <summary>
    /// 特定のプロセスIDでファイルイベントを生成（テスト用）
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="operation">操作タイプ</param>
    public void TriggerFileEvent(int processId, string filePath, string operation = "Create")
    {
        var eventData = _eventGenerator.GenerateFileEvent(processId, filePath, operation);
        TriggerEvent(eventData);
    }

    /// <summary>
    /// 特定のプロセスIDでプロセスイベントを生成（テスト用）
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="eventType">イベントタイプ</param>
    public void TriggerProcessEvent(int processId, MockEventGenerator.ProcessEventType eventType)
    {
        var eventData = _eventGenerator.GenerateProcessEvent(processId, eventType);
        TriggerEvent(eventData);
    }

    /// <summary>
    /// バックグラウンドでのイベント生成
    /// </summary>
    private async Task GenerateEventsAsync()
    {
        while (!_cancellation.Token.IsCancellationRequested && _isMonitoring)
        {
            try
            {
                if (_eventGenerator.ShouldGenerateEvent())
                {
                    var eventData = _eventGenerator.GenerateRandomEvent();
                    EventReceived?.Invoke(this, eventData);
                }

                var delay = _eventGenerator.GetNextDelay();
                await Task.Delay(delay, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // ログ出力は実際の実装で行う
                // テスト環境では例外を無視
            }
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
        _isMonitoring = false;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}

/// <summary>
/// モックETW設定
/// </summary>
public class MockEtwConfiguration : IEtwConfiguration
{
    /// <summary>
    /// デフォルト設定
    /// </summary>
    public static MockEtwConfiguration Default => new()
    {
        EventGenerationInterval = TimeSpan.FromMilliseconds(100),
        FileEventProbability = 0.6,
        ProcessEventProbability = 0.3,
        GenericEventProbability = 0.1,
        EnableRealisticTimings = true,
        SimulatedProcessIds = new[] { 1234, 5678, 9999 }
    };

    /// <summary>
    /// イベント生成間隔
    /// </summary>
    public TimeSpan EventGenerationInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// ファイルイベント生成確率
    /// </summary>
    public double FileEventProbability { get; set; } = 0.6;

    /// <summary>
    /// プロセスイベント生成確率
    /// </summary>
    public double ProcessEventProbability { get; set; } = 0.3;

    /// <summary>
    /// 汎用イベント生成確率
    /// </summary>
    public double GenericEventProbability { get; set; } = 0.1;

    /// <summary>
    /// リアルなタイミングを有効にするか
    /// </summary>
    public bool EnableRealisticTimings { get; set; } = true;

    /// <summary>
    /// シミュレーションで使用するプロセスID一覧
    /// </summary>
    public IReadOnlyList<int> SimulatedProcessIds { get; set; } = new[] { 1234, 5678, 9999 };

    // IEtwConfiguration 実装
    public IReadOnlyList<string> EnabledProviders => new[]
    {
        "Microsoft-Windows-Kernel-FileIO",
        "Microsoft-Windows-Kernel-Process"
    };

    public IReadOnlyList<string> EnabledEventNames => new[]
    {
        "FileIo/Create",
        "FileIo/Write",
        "FileIo/Delete",
        "FileIo/Rename",
        "FileIo/SetInfo",
        "Process/Start",
        "Process/End"
    };

    public TimeSpan EventBufferTimeout => TimeSpan.FromMilliseconds(100);
}