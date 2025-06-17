# ProcTail アーキテクチャドキュメント

このドキュメントでは、ProcTailの設計思想、アーキテクチャパターン、およびコンポーネント間の関係について説明します。

## 📋 目次

- [システム概要](#システム概要)
- [アーキテクチャパターン](#アーキテクチャパターン)
- [レイヤー構成](#レイヤー構成)
- [コンポーネント詳細](#コンポーネント詳細)
- [データフロー](#データフロー)
- [設計原則](#設計原則)
- [技術選択の理由](#技術選択の理由)

## 🎯 システム概要

ProcTailは、Windows環境でのプロセス監視を目的とした分散アーキテクチャを採用しています。

### 主要コンポーネント

```
┌─────────────────┐    IPC (Named Pipes)    ┌──────────────────┐
│   CLI Client    │◄──────────────────────►│  Windows Service │
│   (Frontend)    │                        │   (Backend)      │
└─────────────────┘                        └──────────────────┘
                                                     │
                                                     │ ETW Events
                                                     ▼
                                            ┌─────────────────┐
                                            │ Windows Kernel  │
                                            │ (ETW Providers) │
                                            └─────────────────┘
```

### 設計目標

1. **高性能**: ETWを活用したリアルタイム監視
2. **拡張性**: プラグイン可能なアーキテクチャ
3. **保守性**: 疎結合な設計とテスタビリティ
4. **使いやすさ**: 直感的なCLIインターフェース
5. **セキュリティ**: 最小権限の原則

## 🏗️ アーキテクチャパターン

### 1. Clean Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Presentation Layer                   │
│  ┌─────────────────┐              ┌─────────────────┐   │
│  │   CLI Client    │              │  Windows Host   │   │
│  │  (ProcTail.Cli) │              │ (ProcTail.Host) │   │
│  └─────────────────┘              └─────────────────┘   │
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│                   Application Layer                     │
│  ┌─────────────────────────────────────────────────────┐ │
│  │              ProcTail.Application                   │ │
│  │  • ProcTailService                                  │ │
│  │  • EventProcessor                                   │ │
│  │  • WatchTargetManager                               │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│                     Domain Layer                        │
│  ┌─────────────────────────────────────────────────────┐ │
│  │                ProcTail.Core                        │ │
│  │  • Interfaces (IEtwEventProvider, etc.)            │ │
│  │  • Models (BaseEventData, etc.)                    │ │
│  │  • Domain Services                                 │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│                 Infrastructure Layer                    │
│  ┌─────────────────────────────────────────────────────┐ │
│  │             ProcTail.Infrastructure                 │ │
│  │  • WindowsEtwEventProvider                          │ │
│  │  • WindowsNamedPipeServer                           │ │
│  │  • Configuration                                    │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 2. Event-Driven Architecture

```
┌─────────────────┐    Events    ┌─────────────────┐    Events    ┌─────────────────┐
│      ETW        │─────────────►│  Event Bus      │─────────────►│   Subscribers   │
│   Providers     │              │   (In-Memory)   │              │   (Processors)  │
└─────────────────┘              └─────────────────┘              └─────────────────┘
                                          │
                                          ▼
                                 ┌─────────────────┐
                                 │  Event Storage  │
                                 │ (ConcurrentQueue)│
                                 └─────────────────┘
```

### 3. Microservice Communication

```
┌─────────────────┐                    ┌─────────────────┐
│   CLI Process   │                    │ Service Process │
│                 │                    │                 │
│  ┌───────────┐  │    Named Pipes    │  ┌───────────┐  │
│  │ Commands  │  │◄──────────────────►│  │    IPC    │  │
│  │ Handler   │  │      (JSON)       │  │  Handler  │  │
│  └───────────┘  │                    │  └───────────┘  │
└─────────────────┘                    └─────────────────┘
```

## 📚 レイヤー構成

### Core Layer (Domain)

**責任**: ビジネスルールとエンティティの定義

```csharp
// インターフェースの定義
public interface IEtwEventProvider
{
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
    bool IsMonitoring { get; }
    event EventHandler<RawEventData> EventReceived;
}

// ドメインモデル
public abstract record BaseEventData
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int ProcessId { get; init; }
    public string EventType { get; init; } = "";
}
```

**特徴**:
- 他のレイヤーに依存しない
- ビジネスロジックの中核
- インターフェースとモデルの定義

### Application Layer

**責任**: ユースケースの実装とオーケストレーション

```csharp
public class ProcTailService : IDisposable
{
    private readonly IEtwEventProvider _etwProvider;
    private readonly IEventProcessor _eventProcessor;
    private readonly IWatchTargetManager _watchTargetManager;
    
    public async Task<bool> AddWatchTargetAsync(int processId, string tag)
    {
        // 1. プロセスの存在確認
        // 2. 監視対象として登録
        // 3. ETWイベントの購読設定
        // 4. 結果を返す
    }
}
```

**特徴**:
- ビジネスロジックのオーケストレーション
- 外部依存はインターフェースを通じて抽象化
- トランザクション境界の定義

### Infrastructure Layer

**責任**: 外部システムとの統合

```csharp
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProvider : IEtwEventProvider
{
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        // Windows ETW APIの直接呼び出し
        using var session = new TraceEventSession("ProcTailSession");
        session.EnableProvider("Microsoft-Windows-FileIO");
        // ...
    }
}
```

**特徴**:
- プラットフォーム固有の実装
- 外部ライブラリの使用
- 設定とデータ永続化

### Presentation Layer

**責任**: ユーザーインターフェースとプロトコル変換

```csharp
// CLI: コマンドライン引数の処理
public static async Task<int> Main(string[] args)
{
    var rootCommand = CreateRootCommand();
    return await rootCommand.InvokeAsync(args);
}

// Host: Windows Service として動作
public static async Task Main(string[] args)
{
    var host = CreateHostBuilder(args).Build();
    await host.RunAsync();
}
```

## 🔧 コンポーネント詳細

### 1. ETW Event Provider

```csharp
┌─────────────────────────────────────────────────────────┐
│              WindowsEtwEventProvider                    │
├─────────────────────────────────────────────────────────┤
│ + StartMonitoringAsync(): Task                          │
│ + StopMonitoringAsync(): Task                           │
│ + IsMonitoring: bool                                    │
│ + EventReceived: EventHandler<RawEventData>             │
├─────────────────────────────────────────────────────────┤
│ - _traceEventSession: TraceEventSession                 │
│ - _cancellationTokenSource: CancellationTokenSource    │
│ - ProcessTraceEvent(TraceEvent): void                   │
│ - CreateFileEventData(TraceEvent): FileEventData       │
│ - CreateProcessEventData(TraceEvent): ProcessEventData │
└─────────────────────────────────────────────────────────┘
```

**役割**:
- ETWセッションの管理
- Windowsカーネルイベントの受信
- イベントデータの構造化

### 2. Named Pipe Server

```csharp
┌─────────────────────────────────────────────────────────┐
│              WindowsNamedPipeServer                     │
├─────────────────────────────────────────────────────────┤
│ + StartAsync(): Task                                    │
│ + StopAsync(): Task                                     │
│ + IsRunning: bool                                       │
│ + RequestReceived: EventHandler<IpcRequestEventArgs>    │
├─────────────────────────────────────────────────────────┤
│ - _pipeServer: NamedPipeServerStream                    │
│ - _clientTasks: ConcurrentDictionary<string, Task>     │
│ - HandleClientAsync(NamedPipeServerStream): Task       │
│ - ProcessRequestAsync(string): Task<string>             │
└─────────────────────────────────────────────────────────┘
```

**役割**:
- IPC通信の管理
- 複数クライアントの同時接続
- JSON-RPCプロトコルの実装

### 3. Event Storage

```csharp
┌─────────────────────────────────────────────────────────┐
│                    EventStorage                         │
├─────────────────────────────────────────────────────────┤
│ + AddEvent(string tag, BaseEventData): void            │
│ + GetEvents(string tag, int count): List<BaseEventData>│
│ + ClearEvents(string tag): bool                        │
│ + GetStatistics(): StorageStatistics                   │
├─────────────────────────────────────────────────────────┤
│ - _eventQueues: ConcurrentDict<string, ConcurrentQueue>│
│ - _maxEventsPerTag: int                                 │
│ - _totalEventCount: long                                │
│ - TrimQueueIfNeeded(ConcurrentQueue): void             │
└─────────────────────────────────────────────────────────┘
```

**役割**:
- イベントの一時保存
- タグベースの分類
- メモリ効率的な管理

## 🌊 データフロー

### 1. イベント監視フロー

```
1. ETW Kernel Event
   │
   ▼
2. WindowsEtwEventProvider.ProcessTraceEvent()
   │
   ▼
3. BaseEventData Creation
   │
   ▼
4. EventReceived Event Emission
   │
   ▼
5. EventProcessor.ProcessEvent()
   │
   ▼
6. EventStorage.AddEvent()
   │
   ▼
7. In-Memory Queue Storage
```

### 2. CLI コマンド実行フロー

```
1. CLI Command Input
   │
   ▼
2. Command Line Parsing (System.CommandLine)
   │
   ▼
3. Command Handler Execution
   │
   ▼
4. Named Pipe Client Connection
   │
   ▼
5. JSON Request Serialization
   │
   ▼
6. IPC Communication
   │
   ▼
7. Service Request Processing
   │
   ▼
8. JSON Response Serialization
   │
   ▼
9. CLI Output Formatting
```

### 3. プロセス監視追加フロー

```
1. proctail add --pid 1234 --tag "test"
   │
   ▼
2. AddWatchTargetCommand.ExecuteAsync()
   │
   ▼
3. PipeClient.SendRequestAsync()
   │
   ▼
4. NamedPipeServer.HandleClientAsync()
   │
   ▼
5. ProcTailService.AddWatchTargetAsync()
   │
   ▼
6. WatchTargetManager.AddTarget()
   │
   ▼
7. Process Validation & Registration
   │
   ▼
8. Success Response
```

## 🎨 設計原則

### 1. 単一責任の原則 (SRP)

```csharp
// ✅ 良い例: 各クラスが単一の責任を持つ
public class FileEventProcessor  // ファイルイベントのみ処理
{
    public void ProcessFileEvent(FileEventData eventData) { }
}

public class ProcessEventProcessor  // プロセスイベントのみ処理
{
    public void ProcessProcessEvent(ProcessEventData eventData) { }
}
```

### 2. 依存性逆転の原則 (DIP)

```csharp
// ✅ 良い例: 抽象に依存
public class ProcTailService
{
    private readonly IEtwEventProvider _etwProvider;  // インターフェースに依存
    
    public ProcTailService(IEtwEventProvider etwProvider)
    {
        _etwProvider = etwProvider;
    }
}
```

### 3. 開放閉鎖の原則 (OCP)

```csharp
// ✅ 良い例: 継承による拡張
public abstract record BaseEventData { }

public record FileEventData : BaseEventData { }      // 拡張
public record ProcessEventData : BaseEventData { }   // 拡張
public record NetworkEventData : BaseEventData { }   // 新しい拡張（将来）
```

### 4. インターフェース分離の原則 (ISP)

```csharp
// ✅ 良い例: 小さなインターフェース
public interface IEtwEventProvider
{
    Task StartMonitoringAsync();
    event EventHandler<RawEventData> EventReceived;
}

public interface IEventStorage
{
    void AddEvent(string tag, BaseEventData eventData);
    List<BaseEventData> GetEvents(string tag, int count);
}
```

## 🧩 技術選択の理由

### 1. .NET 8.0

**選択理由**:
- **長期サポート (LTS)**: 安定性と長期メンテナンス
- **パフォーマンス**: AOTとランタイム最適化
- **Windows統合**: 優れたWindows API統合

### 2. ETW (Event Tracing for Windows)

**選択理由**:
- **高性能**: カーネルレベルでの効率的な監視
- **標準技術**: Windows標準の監視技術
- **低オーバーヘッド**: システムへの影響が最小限

### 3. Named Pipes

**選択理由**:
- **セキュリティ**: ローカル通信に限定
- **パフォーマンス**: プロセス間通信の最適化
- **Windows統合**: Windows固有の高速IPC

### 4. System.CommandLine

**選択理由**:
- **公式サポート**: Microsoft公式のCLIフレームワーク
- **タイプセーフ**: 強い型付けとバリデーション
- **拡張性**: サブコマンドとオプションの柔軟な定義

### 5. NUnit + FluentAssertions

**選択理由**:
- **表現力**: 読みやすいテストコード
- **豊富な機能**: 多様なアサーション
- **Visual Studio統合**: 優れたテストランナー統合

### 6. Serilog

**選択理由**:
- **構造化ログ**: JSON形式での詳細ログ
- **シンク多様性**: ファイル、コンソール、イベントログ
- **パフォーマンス**: 非同期ログ出力

## 🔄 将来の拡張性

### 1. プラグインアーキテクチャ

```csharp
// 将来の拡張: プラグインシステム
public interface IEventPlugin
{
    string Name { get; }
    Task<bool> CanHandleAsync(BaseEventData eventData);
    Task ProcessAsync(BaseEventData eventData);
}

public class PluginManager
{
    private readonly List<IEventPlugin> _plugins = new();
    
    public void RegisterPlugin(IEventPlugin plugin)
    {
        _plugins.Add(plugin);
    }
}
```

### 2. 分散システムサポート

```csharp
// 将来の拡張: リモート監視
public interface IRemoteEventProvider : IEtwEventProvider
{
    Task ConnectToRemoteHostAsync(string hostname);
    Task DisconnectAsync();
}
```

### 3. データ永続化

```csharp
// 将来の拡張: データベースサポート
public interface IEventRepository
{
    Task SaveEventAsync(BaseEventData eventData);
    Task<List<BaseEventData>> QueryEventsAsync(EventQuery query);
}
```

## 📊 パフォーマンス考慮事項

### 1. メモリ管理

```csharp
// ConcurrentQueue の使用でスレッドセーフなメモリ管理
private readonly ConcurrentDictionary<string, ConcurrentQueue<BaseEventData>> _eventQueues = new();

// 上限設定によるメモリリーク防止
private void TrimQueueIfNeeded(ConcurrentQueue<BaseEventData> queue)
{
    while (queue.Count > _maxEventsPerTag)
    {
        queue.TryDequeue(out _);
    }
}
```

### 2. 非同期処理

```csharp
// 非ブロッキングなイベント処理
public async Task ProcessEventAsync(BaseEventData eventData)
{
    await Task.Run(() => 
    {
        // CPU集約的な処理を別スレッドで実行
        ProcessEventInternal(eventData);
    });
}
```

### 3. リソース解放

```csharp
// IDisposable パターンの適切な実装
public class WindowsEtwEventProvider : IEtwEventProvider, IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _traceEventSession?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
```

---

**このアーキテクチャにより、ProcTailは高性能で拡張性があり、保守しやすいシステムとして設計されています。**