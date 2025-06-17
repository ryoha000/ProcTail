using System.Collections.Concurrent;
using System.Text.Json;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;

namespace ProcTail.Testing.Common.Mocks.Ipc;

/// <summary>
/// モック名前付きパイプサーバー
/// </summary>
public class MockNamedPipeServer : INamedPipeServer
{
    private readonly MockNamedPipeConfiguration _config;
    private readonly ConcurrentQueue<string> _messageLog = new();
    private readonly ConcurrentDictionary<string, object> _responses = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IEventStorage? _eventStorage;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// IPC要求受信時に発火するイベント
    /// </summary>
    public event EventHandler<IpcRequestEventArgs>? RequestReceived;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="config">モック設定</param>
    /// <param name="eventStorage">イベントストレージ（オプション）</param>
    public MockNamedPipeServer(MockNamedPipeConfiguration? config = null, IEventStorage? eventStorage = null)
    {
        _config = config ?? MockNamedPipeConfiguration.Default;
        _eventStorage = eventStorage;
    }

    /// <summary>
    /// サーバーが実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// パイプ名
    /// </summary>
    public string PipeName => _config.PipeName;

    /// <summary>
    /// サーバーを開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MockNamedPipeServer));

        if (_isRunning)
            return Task.CompletedTask;

        _isRunning = true;
        
        // バックグラウンドでクライアント接続を待機
        _ = Task.Run(HandleClientConnectionsAsync, _cancellation.Token);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// サーバーを停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        _cancellation.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 受信したメッセージログを取得
    /// </summary>
    /// <returns>メッセージログ</returns>
    public IReadOnlyList<string> GetMessageLog()
    {
        return _messageLog.ToList();
    }

    /// <summary>
    /// 特定のリクエストに対するレスポンスを設定（テスト用）
    /// </summary>
    /// <param name="requestType">リクエストタイプ</param>
    /// <param name="response">レスポンス</param>
    public void SetResponse(string requestType, object response)
    {
        _responses[requestType] = response;
    }

    /// <summary>
    /// メッセージログをクリア
    /// </summary>
    public void ClearMessageLog()
    {
        _messageLog.Clear();
    }

    /// <summary>
    /// 手動でメッセージを処理（テスト用）
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <returns>レスポンス</returns>
    public async Task<string> ProcessMessageAsync(string message)
    {
        _messageLog.Enqueue(message);
        
        // 設定された遅延をシミュレート
        if (_config.ResponseDelay > TimeSpan.Zero)
        {
            await Task.Delay(_config.ResponseDelay);
        }

        try
        {
            // JSONメッセージをパース
            var jsonDocument = JsonDocument.Parse(message);
            var requestType = jsonDocument.RootElement.GetProperty("RequestType").GetString();
            
            if (requestType != null && _responses.TryGetValue(requestType, out var predefinedResponse))
            {
                return JsonSerializer.Serialize(predefinedResponse);
            }

            // デフォルトの処理
            return requestType switch
            {
                "AddWatchTarget" => ProcessAddWatchTargetRequest(jsonDocument),
                "RemoveWatchTarget" => ProcessRemoveWatchTargetRequest(jsonDocument),
                "GetRecordedEvents" => ProcessGetRecordedEventsRequest(jsonDocument),
                "GetStatus" => ProcessGetStatusRequest(),
                _ => CreateErrorResponse($"Unknown request type: {requestType}")
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error processing message: {ex.Message}");
        }
    }

    /// <summary>
    /// リクエスト受信イベントを手動でトリガー（テスト用）
    /// </summary>
    /// <param name="requestJson">リクエストJSON</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>レスポンス</returns>
    public async Task<string> TriggerRequestReceivedAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        if (RequestReceived == null)
        {
            return await ProcessMessageAsync(requestJson);
        }

        string? response = null;
        var eventArgs = new IpcRequestEventArgs(requestJson, cancellationToken)
        {
            ResponseSender = async (responseJson) =>
            {
                response = responseJson;
                await Task.CompletedTask;
            }
        };

        RequestReceived.Invoke(this, eventArgs);
        
        // レスポンスが設定されるまで待機（テスト用の同期化）
        var timeout = Task.Delay(5000, cancellationToken);
        while (response == null && !timeout.IsCompleted)
        {
            await Task.Delay(10, cancellationToken);
        }

        return response ?? CreateErrorResponse("No response received within timeout");
    }

    private async Task HandleClientConnectionsAsync()
    {
        while (!_cancellation.Token.IsCancellationRequested && _isRunning)
        {
            try
            {
                // 実際の実装では名前付きパイプの接続を待機
                // モック実装では何もしない（手動でメッセージを送信）
                await Task.Delay(100, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // エラーログは実際の実装で行う
            }
        }
    }

    private string ProcessAddWatchTargetRequest(JsonDocument request)
    {
        try
        {
            var targetPath = request.RootElement.GetProperty("TargetPath").GetString();
            var tagName = request.RootElement.GetProperty("TagName").GetString();
            
            var response = new AddWatchTargetResponse
            {
                Success = !string.IsNullOrEmpty(targetPath),
                ErrorMessage = string.IsNullOrEmpty(targetPath) ? "Invalid target path" : string.Empty
            };
            
            return JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error processing AddWatchTarget request: {ex.Message}");
        }
    }

    private string ProcessRemoveWatchTargetRequest(JsonDocument request)
    {
        try
        {
            var targetPath = request.RootElement.GetProperty("TargetPath").GetString();
            
            var response = new AddWatchTargetResponse
            {
                Success = !string.IsNullOrEmpty(targetPath),
                ErrorMessage = string.IsNullOrEmpty(targetPath) ? "Invalid target path" : string.Empty
            };
            
            return JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error processing RemoveWatchTarget request: {ex.Message}");
        }
    }

    private string ProcessGetRecordedEventsRequest(JsonDocument request)
    {
        try
        {
            var tagName = request.RootElement.GetProperty("TagName").GetString();
            var maxCount = request.RootElement.GetProperty("MaxCount").GetInt32();
            
            List<BaseEventData> events;
            
            // 実際のEventStorageが利用可能な場合はそれを使用
            if (_eventStorage != null && !string.IsNullOrEmpty(tagName))
            {
                events = _eventStorage.GetEventsAsync(tagName).GetAwaiter().GetResult()
                    .Take(maxCount)
                    .ToList();
            }
            else
            {
                // フォールバック: テスト用のダミーイベントを生成
                events = GenerateTestEvents(tagName, maxCount);
            }
            
            var response = new GetRecordedEventsResponse(events)
            {
                Success = true
            };
            
            return JsonSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error processing GetRecordedEvents request: {ex.Message}");
        }
    }

    private string ProcessGetStatusRequest()
    {
        var response = new
        {
            Success = true,
            IsMonitoring = true,
            ActiveWatchTargets = _config.DefaultWatchTargets.Count,
            TotalEventsRecorded = _config.SimulatedEventCount,
            Message = "Service is running normally"
        };
        
        return JsonSerializer.Serialize(response);
    }

    private List<BaseEventData> GenerateTestEvents(string? tagName, int maxCount)
    {
        var events = new List<BaseEventData>();
        var random = new Random();
        var actualCount = Math.Min(maxCount, _config.SimulatedEventCount);
        
        for (int i = 0; i < actualCount; i++)
        {
            var eventType = random.Next(3);
            BaseEventData eventData = eventType switch
            {
                0 => new FileEventData
                {
                    Timestamp = DateTime.UtcNow.AddSeconds(-random.Next(0, 3600)),
                    TagName = tagName ?? "test-tag",
                    ProcessId = random.Next(1000, 9999),
                    ThreadId = random.Next(1000, 9999),
                    ProviderName = "Microsoft-Windows-Kernel-FileIO",
                    EventName = "FileIo/Create",
                    ActivityId = Guid.NewGuid(),
                    RelatedActivityId = Guid.NewGuid(),
                    Payload = new Dictionary<string, object> { { "test", "value" } },
                    FilePath = $@"C:\test\file{i}.txt"
                },
                1 => new ProcessStartEventData
                {
                    Timestamp = DateTime.UtcNow.AddSeconds(-random.Next(0, 3600)),
                    TagName = tagName ?? "test-tag",
                    ProcessId = random.Next(1000, 9999),
                    ThreadId = random.Next(1000, 9999),
                    ProviderName = "Microsoft-Windows-Kernel-Process",
                    EventName = "Process/Start",
                    ActivityId = Guid.NewGuid(),
                    RelatedActivityId = Guid.NewGuid(),
                    Payload = new Dictionary<string, object> { { "CommandLine", "test.exe" } },
                    ChildProcessId = random.Next(10000, 99999),
                    ChildProcessName = $"test{i}.exe"
                },
                _ => new GenericEventData
                {
                    Timestamp = DateTime.UtcNow.AddSeconds(-random.Next(0, 3600)),
                    TagName = tagName ?? "test-tag",
                    ProcessId = random.Next(1000, 9999),
                    ThreadId = random.Next(1000, 9999),
                    ProviderName = "Test-Provider",
                    EventName = "Test/Event",
                    ActivityId = Guid.NewGuid(),
                    RelatedActivityId = Guid.NewGuid(),
                    Payload = new Dictionary<string, object> { { "data", $"test{i}" } }
                }
            };
            
            events.Add(eventData);
        }
        
        return events;
    }

    private string CreateErrorResponse(string message)
    {
        var response = new AddWatchTargetResponse
        {
            Success = false,
            ErrorMessage = message
        };
        
        return JsonSerializer.Serialize(response);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isRunning = false;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}

/// <summary>
/// モック名前付きパイプ設定
/// </summary>
public class MockNamedPipeConfiguration
{
    /// <summary>
    /// デフォルト設定
    /// </summary>
    public static MockNamedPipeConfiguration Default => new()
    {
        PipeName = "test-proctail-pipe",
        ResponseDelay = TimeSpan.FromMilliseconds(10),
        SimulatedEventCount = 100,
        DefaultWatchTargets = new List<string> { @"C:\test", @"C:\temp" }
    };

    /// <summary>
    /// パイプ名
    /// </summary>
    public string PipeName { get; set; } = "test-proctail-pipe";

    /// <summary>
    /// レスポンス遅延時間
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// シミュレーションするイベント数
    /// </summary>
    public int SimulatedEventCount { get; set; } = 100;

    /// <summary>
    /// デフォルトの監視対象パス
    /// </summary>
    public List<string> DefaultWatchTargets { get; set; } = new();
}