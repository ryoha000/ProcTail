using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Application.Services;

/// <summary>
/// ProcTailメインサービス - 全体のワークフローを統合
/// </summary>
public class ProcTailService : IProcTailService, IDisposable
{
    private readonly ILogger<ProcTailService> _logger;
    private readonly IEtwEventProvider _etwProvider;
    private readonly IWatchTargetManager _watchTargetManager;
    private readonly IEventProcessor _eventProcessor;
    private readonly IEventStorage _eventStorage;
    private readonly INamedPipeServer _pipeServer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isRunning;
    private bool _disposed;
    private ServiceStatus _status = ServiceStatus.Stopped;

    /// <summary>
    /// サービスが実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// サービス状態
    /// </summary>
    public ServiceStatus Status => _status;

    /// <summary>
    /// 状態変更イベント
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<ServiceStatusChangedEventArgs>? StatusChanged;
#pragma warning restore CS0067

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcTailService(
        ILogger<ProcTailService> logger,
        IEtwEventProvider etwProvider,
        IWatchTargetManager watchTargetManager,
        IEventProcessor eventProcessor,
        IEventStorage eventStorage,
        INamedPipeServer pipeServer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _etwProvider = etwProvider ?? throw new ArgumentNullException(nameof(etwProvider));
        _watchTargetManager = watchTargetManager ?? throw new ArgumentNullException(nameof(watchTargetManager));
        _eventProcessor = eventProcessor ?? throw new ArgumentNullException(nameof(eventProcessor));
        _eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        _pipeServer = pipeServer ?? throw new ArgumentNullException(nameof(pipeServer));
    }

    /// <summary>
    /// サービス全体を開始
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcTailService));

        if (_isRunning)
        {
            _logger.LogWarning("ProcTailServiceは既に実行中です");
            return;
        }

        try
        {
            _logger.LogInformation("=== ProcTailServiceを開始しています ===");

            // ETWイベント処理の設定
            _logger.LogInformation("ETWイベントハンドラーを設定中...");
            _etwProvider.EventReceived += OnEtwEventReceived;
            _logger.LogInformation("ETWイベントハンドラーの設定が完了しました");

            // Named Pipeサーバーの要求処理の設定
            _logger.LogInformation("Named Pipeサーバーのイベントハンドラーを設定中...");
            _pipeServer.RequestReceived += OnPipeRequestReceived;
            _logger.LogInformation("Named Pipeサーバーのイベントハンドラーの設定が完了しました");

            // ETW監視を開始
            _logger.LogInformation("ETW監視を開始中...");
            await _etwProvider.StartMonitoringAsync(cancellationToken);
            _logger.LogInformation("ETW監視を開始しました (IsMonitoring: {IsMonitoring})", _etwProvider.IsMonitoring);

            // Named Pipeサーバーを開始
            _logger.LogInformation("Named Pipeサーバーを開始中...");
            await _pipeServer.StartAsync(cancellationToken);
            _logger.LogInformation("Named Pipeサーバーを開始しました (IsRunning: {IsRunning}, PipeName: {PipeName})", 
                _pipeServer.IsRunning, _pipeServer.PipeName);

            _isRunning = true;
            _logger.LogInformation("=== ProcTailServiceが正常に開始されました ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailService開始中にエラーが発生しました");
            await StopAsync();
            throw;
        }
    }

    /// <summary>
    /// サービス全体を停止
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            _logger.LogInformation("ProcTailServiceを停止しています...");

            _cancellationTokenSource.Cancel();

            // ETW監視を停止
            if (_etwProvider.IsMonitoring)
            {
                await _etwProvider.StopMonitoringAsync(cancellationToken);
                _logger.LogInformation("ETW監視を停止しました");
            }

            // Named Pipeサーバーを停止
            if (_pipeServer.IsRunning)
            {
                await _pipeServer.StopAsync(cancellationToken);
                _logger.LogInformation("Named Pipeサーバーを停止しました");
            }

            _isRunning = false;
            _logger.LogInformation("ProcTailServiceが正常に停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailService停止中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 監視対象を追加
    /// </summary>
    public async Task<bool> AddWatchTargetAsync(int processId, string tagName, CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            _logger.LogWarning("サービスが実行中ではありません");
            return false;
        }

        try
        {
            var result = await _watchTargetManager.AddTargetAsync(processId, tagName);
            if (result)
            {
                _logger.LogInformation("監視対象を追加しました (ProcessId: {ProcessId}, Tag: {TagName})", processId, tagName);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "監視対象追加中にエラーが発生しました (ProcessId: {ProcessId}, Tag: {TagName})", processId, tagName);
            return false;
        }
    }

    /// <summary>
    /// 記録されたイベントを取得
    /// </summary>
    public async Task<IReadOnlyList<BaseEventData>> GetRecordedEventsAsync(string tagName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _eventStorage.GetEventsAsync(tagName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント取得中にエラーが発生しました (Tag: {TagName})", tagName);
            return Array.Empty<BaseEventData>();
        }
    }

    /// <summary>
    /// ストレージ統計情報を取得
    /// </summary>
    public async Task<StorageStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _eventStorage.GetStatisticsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "統計情報取得中にエラーが発生しました");
            return new StorageStatistics(0, 0, new Dictionary<string, int>(), 0);
        }
    }

    /// <summary>
    /// ETWイベント受信時の処理
    /// </summary>
    private async void OnEtwEventReceived(object? sender, RawEventData rawEvent)
    {
        try
        {
            _logger.LogDebug("ETWイベントを受信しました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);
            // サービスが停止中または破棄されている場合は処理しない
            if (!IsRunning || _cancellationTokenSource.Token.IsCancellationRequested)
                return;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("ETWイベントを受信しました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);            
            // CancellationTokenSourceが破棄されている場合は処理を停止
            return;
        }

        try
        {
            // イベントを処理してドメインイベントに変換
            var processingResult = await _eventProcessor.ProcessEventAsync(rawEvent);
            
            if (processingResult.Success && processingResult.EventData != null)
            {
                // 変換されたイベントをストレージに保存
                await _eventStorage.StoreEventAsync(processingResult.EventData.TagName, processingResult.EventData);
                
                _logger.LogDebug("イベントを処理・保存しました (Type: {EventType}, ProcessId: {ProcessId}, Tag: {Tag})",
                    processingResult.EventData.GetType().Name, 
                    processingResult.EventData.ProcessId, 
                    processingResult.EventData.TagName);
            }
            else
            {
                _logger.LogDebug("イベントはフィルタリングまたは処理失敗しました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId}, Error: {Error})",
                    rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId, processingResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETWイベント処理中にエラーが発生しました (Provider: {Provider}, Event: {Event}, ProcessId: {ProcessId})",
                rawEvent.ProviderName, rawEvent.EventName, rawEvent.ProcessId);
        }
    }

    /// <summary>
    /// Named Pipe要求受信時の処理
    /// </summary>
    private async void OnPipeRequestReceived(object? sender, IpcRequestEventArgs e)
    {
        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return;

        try
        {
            _logger.LogDebug("IPC要求を受信しました: {RequestJson}", e.RequestJson);

            var response = await ProcessIpcRequestAsync(e.RequestJson, e.CancellationToken);
            await e.SendResponseAsync(response);

            _logger.LogDebug("IPC応答を送信しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPC要求処理中にエラーが発生しました");
            
            var errorResponse = System.Text.Json.JsonSerializer.Serialize(new AddWatchTargetResponse
            {
                Success = false,
                ErrorMessage = $"Internal error: {ex.Message}"
            });
            
            try
            {
                await e.SendResponseAsync(errorResponse);
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "エラー応答送信中にエラーが発生しました");
            }
        }
    }

    /// <summary>
    /// IPC要求を処理
    /// </summary>
    private async Task<string> ProcessIpcRequestAsync(string requestJson, CancellationToken cancellationToken)
    {
        try
        {
            using var jsonDocument = System.Text.Json.JsonDocument.Parse(requestJson);
            var requestType = jsonDocument.RootElement.GetProperty("RequestType").GetString();

            return requestType switch
            {
                "AddWatchTarget" => await ProcessAddWatchTargetRequestAsync(jsonDocument, cancellationToken),
                "RemoveWatchTarget" => await ProcessRemoveWatchTargetRequestAsync(jsonDocument, cancellationToken),
                "GetWatchTargets" => await ProcessGetWatchTargetsRequestAsync(cancellationToken),
                "GetRecordedEvents" => await ProcessGetRecordedEventsRequestAsync(jsonDocument, cancellationToken),
                "GetStatus" => await ProcessGetStatusRequestAsync(cancellationToken),
                "ClearEvents" => await ProcessClearEventsRequestAsync(jsonDocument, cancellationToken),
                "Shutdown" => await ProcessShutdownRequestAsync(cancellationToken),
                _ => CreateErrorResponse($"Unknown request type: {requestType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPC要求解析中にエラーが発生しました");
            return CreateErrorResponse($"Request parsing error: {ex.Message}");
        }
    }

    private async Task<string> ProcessAddWatchTargetRequestAsync(System.Text.Json.JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var processId = request.RootElement.GetProperty("ProcessId").GetInt32();
            var tagName = request.RootElement.GetProperty("TagName").GetString() ?? string.Empty;

            var success = await _watchTargetManager.AddTargetAsync(processId, tagName);

            // 追加直後に確認
            if (success)
            {
                var isWatched = _watchTargetManager.IsWatchedProcess(processId);
                _logger.LogInformation("監視対象追加後の確認: ProcessId={ProcessId}, IsWatched={IsWatched}", processId, isWatched);
                
                if (!isWatched)
                {
                    _logger.LogError("監視対象追加に成功したが、直後の確認で見つかりません: ProcessId={ProcessId}", processId);
                }
            }

            var response = new AddWatchTargetResponse
            {
                Success = success,
                ErrorMessage = success ? string.Empty : "Failed to add watch target"
            };

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"AddWatchTarget error: {ex.Message}");
        }
    }

    private async Task<string> ProcessRemoveWatchTargetRequestAsync(System.Text.Json.JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var tagName = request.RootElement.GetProperty("TagName").GetString() ?? string.Empty;

            var removedCount = await _watchTargetManager.RemoveWatchTargetsByTagAsync(tagName, cancellationToken);

            var response = new RemoveWatchTargetResponse
            {
                Success = removedCount > 0,
                ErrorMessage = removedCount > 0 ? string.Empty : $"No watch targets found for tag: {tagName}"
            };

            _logger.LogInformation("監視対象を削除しました (Tag: {TagName}, RemovedCount: {RemovedCount})", tagName, removedCount);

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"RemoveWatchTarget error: {ex.Message}");
        }
    }

    private async Task<string> ProcessGetWatchTargetsRequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var watchTargets = await _watchTargetManager.GetWatchTargetInfosAsync();

            var response = new GetWatchTargetsResponse(watchTargets)
            {
                Success = true
            };

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"GetWatchTargets error: {ex.Message}");
        }
    }

    private async Task<string> ProcessGetRecordedEventsRequestAsync(System.Text.Json.JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var tagName = request.RootElement.GetProperty("TagName").GetString() ?? string.Empty;
            var events = await GetRecordedEventsAsync(tagName, cancellationToken);

            var response = new GetRecordedEventsResponse(events.ToList())
            {
                Success = true
            };

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"GetRecordedEvents error: {ex.Message}");
        }
    }

    private async Task<string> ProcessGetStatusRequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var statistics = await GetStatisticsAsync(cancellationToken);
            var watchTargets = _watchTargetManager.GetWatchTargets();

            var response = new
            {
                Success = true,
                IsRunning = _isRunning,
                IsEtwMonitoring = _etwProvider.IsMonitoring,
                IsPipeServerRunning = _pipeServer.IsRunning,
                ActiveWatchTargets = watchTargets.Count,
                TotalTags = statistics.TotalTags,
                TotalEvents = statistics.TotalEvents,
                EstimatedMemoryUsageMB = statistics.EstimatedMemoryUsage / 1024 / 1024,
                Message = "ProcTail service is running normally"
            };

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"GetStatus error: {ex.Message}");
        }
    }

    private async Task<string> ProcessClearEventsRequestAsync(System.Text.Json.JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var tagName = request.RootElement.GetProperty("TagName").GetString() ?? string.Empty;
            await _eventStorage.ClearEventsAsync(tagName);

            var response = new ClearEventsResponse
            {
                Success = true
            };

            return System.Text.Json.JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"ClearEvents error: {ex.Message}");
        }
    }

    private Task<string> ProcessShutdownRequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 非同期でシャットダウンを実行
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken); // 応答送信の時間を確保
                await StopAsync(cancellationToken);
            }, cancellationToken);

            var response = new ShutdownResponse
            {
                Success = true
            };

            return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateErrorResponse($"Shutdown error: {ex.Message}"));
        }
    }

    private static string CreateErrorResponse(string message)
    {
        var response = new AddWatchTargetResponse
        {
            Success = false,
            ErrorMessage = message
        };

        return System.Text.Json.JsonSerializer.Serialize(response);
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
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailService解放中にエラーが発生しました");
        }

        _cancellationTokenSource.Dispose();
        _logger.LogInformation("ProcTailServiceが解放されました");
    }
}