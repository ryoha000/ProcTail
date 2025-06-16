# インターフェース分離設計

## 1. 設計原則

### 1.1 依存性逆転の原則 (DIP)
- 高レベルモジュール（ビジネスロジック）は低レベルモジュール（Windows API）に依存しない
- 両方とも抽象化（インターフェース）に依存する
- 抽象化は詳細に依存せず、詳細が抽象化に依存する

### 1.2 インターフェース分離の原則 (ISP)
- クライアントは使用しないメソッドに依存を強制されない
- 大きなインターフェースを小さな専門的なインターフェースに分割

## 2. コアインターフェース定義

### 2.1 ETW監視インターフェース

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// ETWイベントの生データ
    /// </summary>
    public record RawEventData(
        DateTime Timestamp,
        string ProviderName,
        string EventName,
        int ProcessId,
        int ThreadId,
        Guid ActivityId,
        Guid RelatedActivityId,
        IReadOnlyDictionary<string, object> Payload
    );

    /// <summary>
    /// ETWイベント監視の抽象化
    /// </summary>
    public interface IEtwEventProvider : IDisposable
    {
        event EventHandler<RawEventData> EventReceived;
        Task StartMonitoringAsync(CancellationToken cancellationToken = default);
        Task StopMonitoringAsync(CancellationToken cancellationToken = default);
        bool IsMonitoring { get; }
    }

    /// <summary>
    /// ETW設定の抽象化
    /// </summary>
    public interface IEtwConfiguration
    {
        IReadOnlyList<string> EnabledProviders { get; }
        IReadOnlyList<string> EnabledEventNames { get; }
        TimeSpan EventBufferTimeout { get; }
    }
}
```

### 2.2 プロセス間通信インターフェース

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// IPC要求イベント引数
    /// </summary>
    public class IpcRequestEventArgs : EventArgs
    {
        public string RequestJson { get; init; } = string.Empty;
        public required Func<string, Task> SendResponseAsync { get; init; }
        public CancellationToken CancellationToken { get; init; }
    }

    /// <summary>
    /// Named Pipe通信の抽象化
    /// </summary>
    public interface INamedPipeServer : IDisposable
    {
        event EventHandler<IpcRequestEventArgs> RequestReceived;
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);
        bool IsRunning { get; }
    }

    /// <summary>
    /// Named Pipe設定の抽象化
    /// </summary>
    public interface IPipeConfiguration
    {
        string PipeName { get; }
        int MaxConcurrentConnections { get; }
        TimeSpan ConnectionTimeout { get; }
        string SecurityDescriptor { get; }
    }
}
```

### 2.3 システム情報インターフェース

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// プロセス情報
    /// </summary>
    public record ProcessInfo(
        int ProcessId,
        string ProcessName,
        string ExecutablePath,
        DateTime StartTime,
        int? ParentProcessId
    );

    /// <summary>
    /// プロセス検証の抽象化
    /// </summary>
    public interface IProcessValidator
    {
        bool ProcessExists(int processId);
        ProcessInfo? GetProcessInfo(int processId);
        IReadOnlyList<ProcessInfo> GetChildProcesses(int parentProcessId);
    }

    /// <summary>
    /// システム権限チェックの抽象化
    /// </summary>
    public interface ISystemPrivilegeChecker
    {
        bool IsRunningAsAdministrator();
        bool CanAccessEtwSessions();
        Task<bool> TryElevatePrivilegesAsync();
    }

    /// <summary>
    /// システム時刻の抽象化（テスト用）
    /// </summary>
    public interface ISystemClock
    {
        DateTime UtcNow { get; }
        DateTime Now { get; }
    }
}
```

## 3. ドメインサービスインターフェース

### 3.1 監視対象管理

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// 監視対象エントリ
    /// </summary>
    public record WatchTarget(
        int ProcessId,
        string TagName,
        DateTime RegisteredAt,
        bool IsChildProcess = false,
        int? ParentProcessId = null
    );

    /// <summary>
    /// 監視対象管理の抽象化
    /// </summary>
    public interface IWatchTargetManager
    {
        /// <summary>
        /// 監視対象を追加
        /// </summary>
        Task<bool> AddTargetAsync(int processId, string tagName);
        
        /// <summary>
        /// 子プロセスを自動追加
        /// </summary>
        Task<bool> AddChildProcessAsync(int childProcessId, int parentProcessId);
        
        /// <summary>
        /// プロセスが監視対象かチェック
        /// </summary>
        bool IsWatchedProcess(int processId);
        
        /// <summary>
        /// プロセスのタグ名を取得
        /// </summary>
        string? GetTagForProcess(int processId);
        
        /// <summary>
        /// 監視対象一覧を取得
        /// </summary>
        IReadOnlyList<WatchTarget> GetWatchTargets();
        
        /// <summary>
        /// 監視対象を削除（プロセス終了時）
        /// </summary>
        bool RemoveTarget(int processId);
    }
}
```

### 3.2 イベント処理

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// イベント処理結果
    /// </summary>
    public record ProcessingResult(
        bool Success,
        BaseEventData? EventData = null,
        string? ErrorMessage = null
    );

    /// <summary>
    /// イベント処理の抽象化
    /// </summary>
    public interface IEventProcessor
    {
        /// <summary>
        /// 生ETWイベントを処理してドメインイベントに変換
        /// </summary>
        Task<ProcessingResult> ProcessEventAsync(RawEventData rawEvent);
        
        /// <summary>
        /// イベントタイプのフィルタリング
        /// </summary>
        bool ShouldProcessEvent(RawEventData rawEvent);
    }

    /// <summary>
    /// イベントストレージの抽象化
    /// </summary>
    public interface IEventStorage
    {
        /// <summary>
        /// イベントを記録
        /// </summary>
        Task StoreEventAsync(string tagName, BaseEventData eventData);
        
        /// <summary>
        /// タグに関連するイベントを取得
        /// </summary>
        Task<IReadOnlyList<BaseEventData>> GetEventsAsync(string tagName);
        
        /// <summary>
        /// タグに関連するイベントをクリア
        /// </summary>
        Task ClearEventsAsync(string tagName);
        
        /// <summary>
        /// 全イベント数を取得
        /// </summary>
        Task<int> GetEventCountAsync(string tagName);
        
        /// <summary>
        /// ストレージ統計情報
        /// </summary>
        Task<StorageStatistics> GetStatisticsAsync();
    }

    /// <summary>
    /// ストレージ統計情報
    /// </summary>
    public record StorageStatistics(
        int TotalTags,
        int TotalEvents,
        IReadOnlyDictionary<string, int> EventCountByTag,
        long EstimatedMemoryUsage
    );
}
```

### 3.3 ヘルスチェック

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// ヘルス状態
    /// </summary>
    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    /// <summary>
    /// ヘルスチェック結果
    /// </summary>
    public record HealthCheckResult(
        HealthStatus Status,
        string Description,
        IReadOnlyDictionary<string, object>? Details = null
    );

    /// <summary>
    /// ヘルスチェックの抽象化
    /// </summary>
    public interface IHealthChecker
    {
        /// <summary>
        /// システム全体のヘルスチェック
        /// </summary>
        Task<HealthCheckResult> CheckHealthAsync();
        
        /// <summary>
        /// 個別コンポーネントのヘルスチェック
        /// </summary>
        Task<HealthCheckResult> CheckComponentHealthAsync(string componentName);
        
        /// <summary>
        /// ヘルスチェック項目の登録
        /// </summary>
        void RegisterHealthCheck(string name, Func<Task<HealthCheckResult>> healthCheck);
    }
}
```

## 4. アプリケーションサービスインターフェース

### 4.1 サービスオーケストレーション

```csharp
namespace ProcTail.Application.Interfaces
{
    /// <summary>
    /// メインサービスの抽象化
    /// </summary>
    public interface IProcTailService
    {
        /// <summary>
        /// サービス開始
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// サービス停止
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// サービス状態
        /// </summary>
        ServiceStatus Status { get; }
        
        /// <summary>
        /// 状態変更イベント
        /// </summary>
        event EventHandler<ServiceStatusChangedEventArgs> StatusChanged;
    }

    public enum ServiceStatus
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    public class ServiceStatusChangedEventArgs : EventArgs
    {
        public ServiceStatus PreviousStatus { get; init; }
        public ServiceStatus CurrentStatus { get; init; }
        public string? Reason { get; init; }
    }
}
```

### 4.2 IPC要求ハンドラー

```csharp
namespace ProcTail.Application.Interfaces
{
    /// <summary>
    /// IPC要求ハンドラーの抽象化
    /// </summary>
    public interface IIpcRequestHandler
    {
        /// <summary>
        /// 要求を処理して応答を返す
        /// </summary>
        Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// サポートされる要求タイプ
        /// </summary>
        IReadOnlyList<Type> SupportedRequestTypes { get; }
    }

    /// <summary>
    /// 特定の要求タイプのハンドラー
    /// </summary>
    public interface IRequestHandler<TRequest, TResponse>
        where TRequest : class
        where TResponse : BaseResponse
    {
        Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
    }
}
```

## 5. 構成管理インターフェース

```csharp
namespace ProcTail.Core.Interfaces
{
    /// <summary>
    /// 設定管理の抽象化
    /// </summary>
    public interface IConfigurationManager
    {
        T GetConfiguration<T>() where T : class, new();
        void ValidateConfiguration();
        bool TryReloadConfiguration();
    }

    /// <summary>
    /// 設定変更通知
    /// </summary>
    public interface IConfigurationChangeNotifier
    {
        event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
    }

    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string SectionName { get; init; } = string.Empty;
        public Type ConfigurationType { get; init; } = typeof(object);
    }
}
```

## 6. テスト用モックファクトリー

```csharp
namespace ProcTail.Testing.Mocks
{
    /// <summary>
    /// モックファクトリー（テスト用）
    /// </summary>
    public static class MockFactory
    {
        public static IEtwEventProvider CreateMockEtwProvider()
        {
            // Mock implementation for testing
        }

        public static INamedPipeServer CreateMockPipeServer()
        {
            // Mock implementation for testing
        }

        public static IProcessValidator CreateMockProcessValidator()
        {
            // Mock implementation for testing
        }

        public static ISystemPrivilegeChecker CreateMockPrivilegeChecker(bool isAdmin = true)
        {
            // Mock implementation for testing
        }
    }
}
```

この設計により、Windows固有のAPIに依存しない純粋なビジネスロジックのテストが可能になり、各コンポーネントを独立してテストできます。