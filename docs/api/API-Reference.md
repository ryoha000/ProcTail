# ProcTail API リファレンス

このドキュメントでは、ProcTailのNamedPipes IPC APIと内部APIについて説明します。

## 📋 目次

- [IPC API (Named Pipes)](#ipc-api-named-pipes)
- [内部API](#内部api)
- [データモデル](#データモデル)
- [エラーハンドリング](#エラーハンドリング)
- [使用例](#使用例)

## 🔗 IPC API (Named Pipes)

ProcTail CLIとHostサービス間の通信は、Named Pipesを使用したJSON-RPCプロトコルで行われます。

### 接続情報

| 項目 | 値 |
|------|-----|
| **パイプ名** | `ProcTailIPC` (設定変更可能) |
| **サーバー名** | `.` (ローカルマシン) |
| **プロトコル** | JSON over Named Pipes |
| **方向** | 双方向 (InOut) |

### 基本リクエスト形式

```json
{
  "RequestType": "CommandName",
  "Parameters": {
    // コマンド固有のパラメータ
  },
  "RequestId": "optional-request-id"
}
```

### 基本レスポンス形式

```json
{
  "Success": true,
  "Message": "Operation completed successfully",
  "Data": {
    // レスポンスデータ
  },
  "RequestId": "optional-request-id",
  "Timestamp": "2025-01-01T12:00:00.000Z"
}
```

## 📡 IPC コマンド一覧

### 1. AddWatchTarget - 監視対象追加

プロセスを監視対象に追加します。

#### リクエスト
```json
{
  "RequestType": "AddWatchTarget",
  "Parameters": {
    "ProcessId": 1234,
    "Tag": "my-application"
  }
}
```

#### パラメータ
| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `ProcessId` | int | ✅ | 監視対象のプロセスID |
| `Tag` | string | ✅ | プロセスに付けるタグ名 |

#### レスポンス
```json
{
  "Success": true,
  "Message": "Watch target added successfully",
  "Data": {
    "ProcessId": 1234,
    "Tag": "my-application",
    "ProcessName": "notepad.exe",
    "StartTime": "2025-01-01T12:00:00.000Z"
  }
}
```

#### エラーレスポンス
```json
{
  "Success": false,
  "Message": "Process not found",
  "ErrorCode": "PROCESS_NOT_FOUND",
  "Data": null
}
```

### 2. RemoveWatchTarget - 監視対象削除

指定したタグの監視を停止します。

#### リクエスト
```json
{
  "RequestType": "RemoveWatchTarget",
  "Parameters": {
    "Tag": "my-application"
  }
}
```

#### パラメータ
| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `Tag` | string | ✅ | 削除するタグ名 |

#### レスポンス
```json
{
  "Success": true,
  "Message": "Watch target removed successfully",
  "Data": {
    "Tag": "my-application",
    "RemovedProcessCount": 2
  }
}
```

### 3. GetWatchTargets - 監視対象一覧取得

現在の監視対象一覧を取得します。

#### リクエスト
```json
{
  "RequestType": "GetWatchTargets",
  "Parameters": {}
}
```

#### レスポンス
```json
{
  "Success": true,
  "Message": "Watch targets retrieved successfully",
  "Data": {
    "Targets": [
      {
        "Tag": "my-application",
        "ProcessId": 1234,
        "ProcessName": "notepad.exe",
        "StartTime": "2025-01-01T12:00:00.000Z",
        "IsRunning": true
      },
      {
        "Tag": "browser",
        "ProcessId": 5678,
        "ProcessName": "chrome.exe",
        "StartTime": "2025-01-01T11:30:00.000Z",
        "IsRunning": true
      }
    ],
    "TotalCount": 2
  }
}
```

### 4. GetRecordedEvents - イベント取得

記録されたイベントを取得します。

#### リクエスト
```json
{
  "RequestType": "GetRecordedEvents",
  "Parameters": {
    "Tag": "my-application",
    "Count": 50,
    "EventType": "FileOperation"
  }
}
```

#### パラメータ
| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `Tag` | string | ✅ | 取得するタグ名 |
| `Count` | int | ✗ | 取得するイベント数（デフォルト: 50） |
| `EventType` | string | ✗ | フィルタするイベント種別 |

#### レスポンス
```json
{
  "Success": true,
  "Message": "Events retrieved successfully",
  "Data": {
    "Tag": "my-application",
    "Events": [
      {
        "$type": "FileEvent",
        "Timestamp": "2025-01-01T12:34:56.789Z",
        "ProcessId": 1234,
        "EventType": "FileOperation",
        "FilePath": "C:\\temp\\test.txt",
        "Operation": "Create",
        "FileSize": 0
      },
      {
        "$type": "ProcessStart",
        "Timestamp": "2025-01-01T12:35:00.123Z",
        "ProcessId": 5678,
        "EventType": "ProcessStart",
        "ProcessName": "notepad.exe",
        "ParentProcessId": 1234,
        "CommandLine": "notepad.exe C:\\temp\\test.txt"
      }
    ],
    "TotalCount": 2,
    "HasMore": false
  }
}
```

### 5. ClearEvents - イベントクリア

指定したタグのイベント履歴をクリアします。

#### リクエスト
```json
{
  "RequestType": "ClearEvents",
  "Parameters": {
    "Tag": "my-application"
  }
}
```

#### パラメータ
| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `Tag` | string | ✅ | クリアするタグ名 |

#### レスポンス
```json
{
  "Success": true,
  "Message": "Events cleared successfully",
  "Data": {
    "Tag": "my-application",
    "ClearedEventCount": 142
  }
}
```

### 6. GetStatus - サービス状態取得

ProcTailサービスの状態を取得します。

#### リクエスト
```json
{
  "RequestType": "GetStatus",
  "Parameters": {}
}
```

#### レスポンス
```json
{
  "Success": true,
  "Message": "Service status retrieved successfully",
  "Data": {
    "Service": {
      "Status": "Running",
      "Version": "1.0.0",
      "StartTime": "2025-01-01T10:00:00.000Z",
      "Uptime": "02:15:30"
    },
    "Monitoring": {
      "EtwSessionActive": true,
      "ActiveTags": 3,
      "ActiveProcesses": 5,
      "TotalEvents": 1247
    },
    "Resources": {
      "MemoryUsageMB": 45.2,
      "CpuUsagePercent": 2.1,
      "EstimatedMemoryUsage": 47185920
    },
    "Ipc": {
      "NamedPipeActive": true,
      "ConnectedClients": 2,
      "TotalRequests": 156
    }
  }
}
```

### 7. HealthCheck - ヘルスチェック

サービスの健全性を確認します。

#### リクエスト
```json
{
  "RequestType": "HealthCheck",
  "Parameters": {}
}
```

#### レスポンス
```json
{
  "Success": true,
  "Message": "Service is healthy",
  "Data": {
    "Status": "Healthy",
    "CheckTime": "2025-01-01T12:00:00.000Z",
    "Details": {
      "EtwProvider": "Healthy",
      "NamedPipeServer": "Healthy",
      "EventStorage": "Healthy",
      "MemoryUsage": "Normal"
    }
  }
}
```

### 8. Shutdown - サービス終了

ProcTailサービスを終了します。

#### リクエスト
```json
{
  "RequestType": "Shutdown",
  "Parameters": {
    "Force": false
  }
}
```

#### パラメータ
| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `Force` | bool | ✗ | 強制終了フラグ（デフォルト: false） |

#### レスポンス
```json
{
  "Success": true,
  "Message": "Service shutdown initiated",
  "Data": {
    "ShutdownTime": "2025-01-01T12:00:00.000Z",
    "GracefulShutdown": true
  }
}
```

## 🏗️ 内部API

### IEtwEventProvider インターフェース

ETWイベントプロバイダーのインターフェース。

```csharp
public interface IEtwEventProvider : IDisposable
{
    /// <summary>
    /// ETW監視を開始
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ETW監視を停止
    /// </summary>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 監視状態
    /// </summary>
    bool IsMonitoring { get; }
    
    /// <summary>
    /// イベント受信時に発生
    /// </summary>
    event EventHandler<RawEventData> EventReceived;
}
```

### INamedPipeServer インターフェース

Named Pipeサーバーのインターフェース。

```csharp
public interface INamedPipeServer : IDisposable
{
    /// <summary>
    /// サーバーを開始
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// サーバーを停止
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// サーバーの動作状態
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// リクエスト受信時に発生
    /// </summary>
    event EventHandler<IpcRequestEventArgs> RequestReceived;
}
```

### IEventStorage インターフェース

イベントストレージのインターフェース。

```csharp
public interface IEventStorage : IDisposable
{
    /// <summary>
    /// イベントを追加
    /// </summary>
    void AddEvent(string tag, BaseEventData eventData);
    
    /// <summary>
    /// イベントを取得
    /// </summary>
    List<BaseEventData> GetEvents(string tag, int count = 50);
    
    /// <summary>
    /// イベントをクリア
    /// </summary>
    bool ClearEvents(string tag);
    
    /// <summary>
    /// 統計情報を取得
    /// </summary>
    StorageStatistics GetStatistics();
}
```

### IProcTailService インターフェース

メインサービスのインターフェース。

```csharp
public interface IProcTailService : IDisposable
{
    /// <summary>
    /// サービスを開始
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// サービスを停止
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 監視対象を追加
    /// </summary>
    Task<bool> AddWatchTargetAsync(int processId, string tag);
    
    /// <summary>
    /// 監視対象を削除
    /// </summary>
    Task<bool> RemoveWatchTargetAsync(string tag);
    
    /// <summary>
    /// 記録されたイベントを取得
    /// </summary>
    Task<List<BaseEventData>> GetRecordedEventsAsync(string tag, int count = 50);
    
    /// <summary>
    /// サービスの統計情報を取得
    /// </summary>
    Task<ServiceStatistics> GetStatisticsAsync();
    
    /// <summary>
    /// サービスが実行中かどうか
    /// </summary>
    bool IsRunning { get; }
}
```

## 📊 データモデル

### BaseEventData (抽象基底クラス)

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FileEventData), "FileEvent")]
[JsonDerivedType(typeof(ProcessStartEventData), "ProcessStart")]
[JsonDerivedType(typeof(ProcessEndEventData), "ProcessEnd")]
public abstract record BaseEventData
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int ProcessId { get; init; }
    public string EventType { get; init; } = "";
}
```

### FileEventData

```csharp
public record FileEventData : BaseEventData
{
    public string FilePath { get; init; } = "";
    public string Operation { get; init; } = "";
    public long FileSize { get; init; }
    public string FileAttributes { get; init; } = "";
}
```

**FileOperation値:**
- `Create` - ファイル作成
- `Write` - ファイル書き込み
- `Delete` - ファイル削除
- `Rename` - ファイル名変更
- `SetInfo` - ファイル情報変更

### ProcessStartEventData

```csharp
public record ProcessStartEventData : BaseEventData
{
    public string ProcessName { get; init; } = "";
    public int ParentProcessId { get; init; }
    public string CommandLine { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
}
```

### ProcessEndEventData

```csharp
public record ProcessEndEventData : BaseEventData
{
    public string ProcessName { get; init; } = "";
    public int ExitCode { get; init; }
    public TimeSpan ExecutionTime { get; init; }
}
```

### RawEventData

```csharp
public record RawEventData
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ProviderName { get; init; } = "";
    public string EventName { get; init; } = "";
    public int ProcessId { get; init; }
    public Dictionary<string, object> Payload { get; init; } = new();
}
```

### ServiceStatistics

```csharp
public record ServiceStatistics
{
    public string Status { get; init; } = "";
    public DateTime StartTime { get; init; }
    public TimeSpan Uptime { get; init; }
    public int ActiveTags { get; init; }
    public int ActiveProcesses { get; init; }
    public long TotalEvents { get; init; }
    public double MemoryUsageMB { get; init; }
    public double CpuUsagePercent { get; init; }
    public long EstimatedMemoryUsage { get; init; }
    public bool EtwSessionActive { get; init; }
    public bool NamedPipeActive { get; init; }
    public int ConnectedClients { get; init; }
}
```

### StorageStatistics

```csharp
public record StorageStatistics
{
    public int TotalTags { get; init; }
    public long TotalEvents { get; init; }
    public long EstimatedMemoryUsage { get; init; }
    public Dictionary<string, int> EventCountByTag { get; init; } = new();
    public DateTime LastEventTime { get; init; }
}
```

## ❌ エラーハンドリング

### エラーコード一覧

| エラーコード | 説明 |
|-------------|------|
| `PROCESS_NOT_FOUND` | 指定されたプロセスが見つからない |
| `TAG_NOT_FOUND` | 指定されたタグが見つからない |
| `TAG_ALREADY_EXISTS` | タグが既に存在する |
| `INSUFFICIENT_PERMISSIONS` | 権限不足 |
| `ETW_SESSION_ERROR` | ETWセッション エラー |
| `PIPE_SERVER_ERROR` | Named Pipeサーバー エラー |
| `INVALID_REQUEST` | 無効なリクエスト |
| `SERVICE_NOT_RUNNING` | サービスが実行されていない |

### エラーレスポンス形式

```json
{
  "Success": false,
  "Message": "Human readable error message",
  "ErrorCode": "ERROR_CODE_CONSTANT",
  "Data": {
    "Details": "Additional error details",
    "StackTrace": "Stack trace (debug mode only)"
  },
  "Timestamp": "2025-01-01T12:00:00.000Z"
}
```

### 例外マッピング

| C# 例外 | エラーコード | HTTPステータス相当 |
|---------|-------------|-------------------|
| `ArgumentException` | `INVALID_REQUEST` | 400 Bad Request |
| `ArgumentNullException` | `INVALID_REQUEST` | 400 Bad Request |
| `UnauthorizedAccessException` | `INSUFFICIENT_PERMISSIONS` | 403 Forbidden |
| `InvalidOperationException` | `SERVICE_NOT_RUNNING` | 409 Conflict |
| `TimeoutException` | `PIPE_SERVER_ERROR` | 408 Request Timeout |
| `Exception` | `INTERNAL_ERROR` | 500 Internal Server Error |

## 💡 使用例

### C# クライアントの実装例

```csharp
public class ProcTailApiClient : IDisposable
{
    private readonly NamedPipeClientStream _pipeClient;
    
    public ProcTailApiClient(string pipeName = "ProcTailIPC")
    {
        _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
    }
    
    public async Task ConnectAsync(int timeoutMs = 5000)
    {
        await _pipeClient.ConnectAsync(timeoutMs);
    }
    
    public async Task<ApiResponse<T>> SendRequestAsync<T>(string requestType, object parameters = null!)
    {
        var request = new
        {
            RequestType = requestType,
            Parameters = parameters ?? new { },
            RequestId = Guid.NewGuid().ToString()
        };
        
        var requestJson = JsonSerializer.Serialize(request);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        
        await _pipeClient.WriteAsync(requestBytes);
        await _pipeClient.FlushAsync();
        
        var responseBuffer = new byte[65536];
        var bytesRead = await _pipeClient.ReadAsync(responseBuffer);
        var responseJson = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
        
        return JsonSerializer.Deserialize<ApiResponse<T>>(responseJson)!;
    }
    
    public void Dispose()
    {
        _pipeClient?.Dispose();
    }
}

// 使用例
using var client = new ProcTailApiClient();
await client.ConnectAsync();

// 監視対象を追加
var addResult = await client.SendRequestAsync<object>("AddWatchTarget", new
{
    ProcessId = 1234,
    Tag = "my-app"
});

// イベントを取得
var eventsResult = await client.SendRequestAsync<EventsResponse>("GetRecordedEvents", new
{
    Tag = "my-app",
    Count = 100
});
```

### PowerShell クライアントの実装例

```powershell
function Invoke-ProcTailApi {
    param(
        [string]$RequestType,
        [hashtable]$Parameters = @{},
        [string]$PipeName = "ProcTailIPC"
    )
    
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, "InOut")
    
    try {
        $pipe.Connect(5000)
        
        $request = @{
            RequestType = $RequestType
            Parameters = $Parameters
            RequestId = [Guid]::NewGuid().ToString()
        } | ConvertTo-Json -Depth 10
        
        $requestBytes = [System.Text.Encoding]::UTF8.GetBytes($request)
        $pipe.Write($requestBytes, 0, $requestBytes.Length)
        $pipe.Flush()
        
        $responseBuffer = New-Object byte[] 65536
        $bytesRead = $pipe.Read($responseBuffer, 0, $responseBuffer.Length)
        $responseJson = [System.Text.Encoding]::UTF8.GetString($responseBuffer, 0, $bytesRead)
        
        return $responseJson | ConvertFrom-Json
    }
    finally {
        $pipe.Dispose()
    }
}

# 使用例
$result = Invoke-ProcTailApi -RequestType "AddWatchTarget" -Parameters @{
    ProcessId = 1234
    Tag = "powershell-test"
}

Write-Output "Result: $($result.Success)"
```

---

**このAPIリファレンスにより、ProcTailとの統合や独自クライアントの開発が可能になります。**