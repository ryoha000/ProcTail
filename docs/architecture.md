# ProcTail アーキテクチャ設計

## 1. 概要

ProcTailは、ビジネスロジックとWindows固有のシステムAPIを分離した設計により、テスタビリティと保守性を重視したアーキテクチャを採用します。

## 2. レイヤード アーキテクチャ

```
┌─────────────────────────────────────┐
│           Presentation Layer        │
│        (Worker Service Host)        │
├─────────────────────────────────────┤
│          Application Layer          │
│      (Service Orchestration)       │
├─────────────────────────────────────┤
│            Domain Layer             │
│        (Business Logic)             │
├─────────────────────────────────────┤
│        Infrastructure Layer        │
│    (Windows APIs & External I/O)   │
└─────────────────────────────────────┘
```

## 3. 主要コンポーネント

### 3.1 Domain Layer (Core Business Logic)

**責務**: ProcTailの純粋なビジネスロジック（Windows APIに依存しない）

- **EventProcessor**: イベント処理とフィルタリングのロジック
- **WatchTargetManager**: 監視対象の管理（PID、タグの関連付け）
- **EventStorage**: イベントデータの記録と取得（FIFOキューの管理）
- **HealthChecker**: システム状態の判定ロジック

### 3.2 Infrastructure Layer (Windows Integration)

**責務**: Windows固有APIとの統合、外部システムとの通信

- **IEtwEventProvider**: ETWイベントの受信（抽象化）
- **INamedPipeServer**: Named Pipes通信の抽象化
- **IProcessValidator**: プロセス存在確認の抽象化
- **ISystemPrivilegeChecker**: 管理者権限チェックの抽象化

### 3.3 Application Layer (Service Coordination)

**責務**: ドメインロジックとインフラストラクチャの調整

- **ProcTailService**: メインサービスオーケストレーター
- **IpcRequestHandler**: IPC要求の処理とルーティング
- **EventMonitoringService**: ETW監視の開始・停止制御

### 3.4 Presentation Layer (Host)

**責務**: .NET Generic Host, Worker Serviceとしての実行

- **Program**: アプリケーションエントリーポイント
- **ProcTailWorker**: BackgroundServiceの実装

## 4. 依存関係の方向

```
Presentation → Application → Domain
                   ↓
              Infrastructure
```

- **Domain Layer**: 他のレイヤーに依存しない（Pure Business Logic）
- **Application Layer**: Domain Layerに依存、Infrastructure Layerのインターフェースを使用
- **Infrastructure Layer**: Domain Layerのインターフェースを実装
- **Presentation Layer**: Application Layerに依存

## 5. インターフェース分離によるテスタビリティ

### 5.1 主要インターフェース

```csharp
// Domain Layer Interfaces
public interface IEventProcessor
{
    Task<ProcessingResult> ProcessEventAsync(RawEventData eventData);
}

public interface IWatchTargetManager
{
    bool AddTarget(int processId, string tagName);
    bool IsTargetProcess(int processId);
    string? GetTagForProcess(int processId);
}

public interface IEventStorage
{
    void StoreEvent(string tagName, BaseEventData eventData);
    IReadOnlyList<BaseEventData> GetEvents(string tagName);
    void ClearEvents(string tagName);
}

// Infrastructure Layer Interfaces  
public interface IEtwEventProvider
{
    event EventHandler<RawEventData> EventReceived;
    Task StartMonitoringAsync();
    Task StopMonitoringAsync();
}

public interface INamedPipeServer
{
    event EventHandler<IpcRequestEventArgs> RequestReceived;
    Task StartAsync();
    Task StopAsync();
}

public interface IProcessValidator
{
    bool ProcessExists(int processId);
    string? GetProcessName(int processId);
}
```

### 5.2 テスト戦略

- **Unit Tests**: Domain Layerを中心とした純粋なロジックのテスト
- **Integration Tests**: MockされたInfrastructure Layerを使用したApplication Layerのテスト
- **System Tests**: 実際のWindowsAPI（管理者権限必要）を使用した統合テスト

## 6. エラーハンドリング戦略

### 6.1 例外の分類

- **DomainException**: ビジネスルール違反
- **InfrastructureException**: Windows API/外部システムエラー
- **ConfigurationException**: 設定関連エラー

### 6.2 復旧可能性による処理分岐

- **致命的エラー**: サービス停止（管理者権限なし、ETW初期化失敗）
- **一時的エラー**: 再試行・ログ出力（個別イベント処理エラー）
- **想定エラー**: 適切な応答・ログ出力（存在しないPID指定）

## 7. 設定管理

### 7.1 設定の階層化

```json
{
  "ProcTail": {
    "EventSettings": {
      "MaxEventsPerTag": 10000,
      "EnabledEventTypes": ["FileIO", "Process"]
    },
    "PipeSettings": {
      "PipeName": "ProcTailIPC",
      "MaxConnections": 10
    },
    "SecuritySettings": {
      "AllowedUsers": "LocalAuthenticatedUsers"
    }
  }
}
```

### 7.2 設定の検証

- 起動時の設定値バリデーション
- 不正値に対するデフォルト値フォールバック
- 実行時設定変更の制限（セキュリティ上の理由）

## 8. ログ戦略

### 8.1 構造化ログ

- **Serilog** + **Microsoft.Extensions.Logging**
- JSON形式での出力（解析・監視ツール連携）
- 機密情報のマスキング（PID以外のプロセス詳細）

### 8.2 ログレベル

- **Critical**: サービス停止を伴う致命的エラー
- **Error**: 機能に影響するエラー（個別処理失敗）
- **Warning**: 想定範囲内の問題（PID重複登録）
- **Information**: 主要な状態変化（監視開始・停止）
- **Debug**: 詳細なトレース情報（開発・トラブルシュート用）