# ProcTail 開発者ガイド

このガイドでは、C#に慣れていない開発者向けにProcTailプロジェクトの開発方法を説明します。

## 📋 目次

- [開発環境のセットアップ](#開発環境のセットアップ)
- [プロジェクト構造の理解](#プロジェクト構造の理解)
- [C#基礎知識](#c基礎知識)
- [ProcTailの主要概念](#proctailの主要概念)
- [開発の始め方](#開発の始め方)
- [よくある作業パターン](#よくある作業パターン)
- [テストの書き方](#テストの書き方)
- [デバッグ方法](#デバッグ方法)

## 🛠️ 開発環境のセットアップ

### 1. 必要なソフトウェア

#### **Visual Studio 2022 Community（推奨）**
- 無料で使える統合開発環境
- [ダウンロード](https://visualstudio.microsoft.com/ja/vs/community/)
- インストール時に「.NET デスクトップ開発」ワークロードを選択

#### **または Visual Studio Code**
- 軽量なエディタ
- [ダウンロード](https://code.visualstudio.com/)
- 拡張機能: C# Dev Kit, C# Extensions

#### **.NET 8.0 SDK**
- [ダウンロード](https://dotnet.microsoft.com/download/dotnet/8.0)

### 2. プロジェクトの取得

```bash
# リポジトリをクローン
git clone https://github.com/your-org/proctail.git
cd proctail

# 依存関係を復元
dotnet restore

# ビルドして動作確認
dotnet build
```

### 3. Visual Studio での開き方

1. Visual Studio を開く
2. 「ファイル」→「開く」→「プロジェクトまたはソリューション」
3. `ProcTail.sln` ファイルを選択

## 🏗️ プロジェクト構造の理解

### 📁 フォルダ構成

```
proctail/
├── src/                          # ソースコード
│   ├── ProcTail.Core/            # 共通インターフェース・モデル
│   ├── ProcTail.Infrastructure/  # Windows API実装
│   ├── ProcTail.Application/     # ビジネスロジック
│   ├── ProcTail.Host/           # Windowsサービス
│   └── ProcTail.Cli/            # コマンドラインツール
├── tests/                        # テストコード
│   ├── ProcTail.Core.Tests/
│   ├── ProcTail.Integration.Tests/
│   └── ProcTail.System.Tests/
├── docs/                         # ドキュメント
└── README.md
```

### 🎯 各プロジェクトの役割

#### **ProcTail.Core** - 共通基盤
```csharp
// インターフェースの定義
public interface IEtwEventProvider
{
    Task StartMonitoringAsync();
    event EventHandler<RawEventData> EventReceived;
}

// データモデルの定義
public record FileEventData : BaseEventData
{
    public string FilePath { get; init; } = "";
    public string Operation { get; init; } = "";
}
```

#### **ProcTail.Infrastructure** - Windows API実装
```csharp
// Windows固有の実装
public class WindowsEtwEventProvider : IEtwEventProvider
{
    // ETW（Event Tracing for Windows）の実装
    public async Task StartMonitoringAsync()
    {
        // Windows APIを呼び出してイベント監視を開始
    }
}
```

#### **ProcTail.Application** - ビジネスロジック
```csharp
// アプリケーションのメインロジック
public class ProcTailService
{
    public async Task<bool> AddWatchTargetAsync(int processId, string tag)
    {
        // プロセス監視を開始するロジック
    }
}
```

#### **ProcTail.Host** - Windowsサービス
```csharp
// Windowsサービスとして動作するエントリーポイント
public class Program
{
    public static async Task Main(string[] args)
    {
        // サービスホストを作成・実行
    }
}
```

#### **ProcTail.Cli** - CLIツール
```csharp
// コマンドライン引数を処理
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // コマンドを解析して実行
    }
}
```

## 📚 C#基礎知識

### 1. 基本的な構文

#### **クラスとインターフェース**
```csharp
// インターフェース（契約の定義）
public interface IEventProcessor
{
    void ProcessEvent(BaseEventData eventData);
}

// クラス（実装）
public class EventProcessor : IEventProcessor
{
    public void ProcessEvent(BaseEventData eventData)
    {
        // イベント処理のロジック
        Console.WriteLine($"Processing event: {eventData.EventType}");
    }
}
```

#### **非同期プログラミング**
```csharp
// async/await を使った非同期メソッド
public async Task<List<BaseEventData>> GetEventsAsync(string tag)
{
    // 非同期でデータベースからデータを取得
    var events = await _database.GetEventsAsync(tag);
    return events;
}

// 使用例
var events = await GetEventsAsync("my-tag");
```

#### **LINQ（データ操作）**
```csharp
// コレクションの操作
var fileEvents = events
    .Where(e => e.EventType == "FileOperation")  // フィルタ
    .OrderBy(e => e.Timestamp)                   // 並び替え
    .Take(10)                                    // 最初の10件
    .ToList();                                   // リストに変換
```

### 2. ProcTailでよく使う機能

#### **依存性注入（DI）**
```csharp
// サービスの登録
services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
services.AddSingleton<IEventProcessor, EventProcessor>();

// コンストラクターで依存性を受け取る
public class ProcTailService
{
    private readonly IEtwEventProvider _etwProvider;
    private readonly IEventProcessor _eventProcessor;

    public ProcTailService(
        IEtwEventProvider etwProvider,
        IEventProcessor eventProcessor)
    {
        _etwProvider = etwProvider;
        _eventProcessor = eventProcessor;
    }
}
```

#### **設定管理**
```csharp
// appsettings.jsonから設定を読み込み
public class EtwConfiguration : IEtwConfiguration
{
    public string SessionName { get; set; } = "ProcTailSession";
    public int BufferSizeKB { get; set; } = 1024;
}

// DIコンテナで設定を登録
services.Configure<EtwConfiguration>(
    configuration.GetSection("Etw"));
```

#### **ログ出力**
```csharp
public class EventProcessor
{
    private readonly ILogger<EventProcessor> _logger;

    public EventProcessor(ILogger<EventProcessor> logger)
    {
        _logger = logger;
    }

    public void ProcessEvent(BaseEventData eventData)
    {
        _logger.LogInformation("Processing event {EventType} from process {ProcessId}",
            eventData.EventType, eventData.ProcessId);
    }
}
```

## 🎯 ProcTailの主要概念

### 1. イベントドリブンアーキテクチャ

```csharp
// イベントの定義
public class IpcRequestEventArgs : EventArgs
{
    public string RequestJson { get; init; } = "";
    public Func<string, Task> SendResponseAsync { get; init; } = null!;
}

// イベントの発生
public event EventHandler<IpcRequestEventArgs>? RequestReceived;

// イベントの購読
_namedPipeServer.RequestReceived += async (sender, args) =>
{
    // リクエストを処理してレスポンスを送信
    var response = ProcessRequest(args.RequestJson);
    await args.SendResponseAsync(response);
};
```

### 2. レコード型（データモデル）

```csharp
// 不変のデータ構造
public record BaseEventData
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int ProcessId { get; init; }
    public string EventType { get; init; } = "";
}

// 継承したレコード
public record FileEventData : BaseEventData
{
    public string FilePath { get; init; } = "";
    public string Operation { get; init; } = "";
    public long FileSize { get; init; }
}
```

### 3. プラットフォーム固有コード

```csharp
// Windows専用の機能
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProvider : IEtwEventProvider
{
    public async Task StartMonitoringAsync()
    {
        // ETWはWindows専用機能
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("ETW is Windows-only");
        }
        
        // Windows ETW APIを呼び出し
    }
}
```

## 🚀 開発の始め方

### 1. 初回セットアップ

```bash
# 1. プロジェクトをビルド
dotnet build

# 2. テストを実行（問題がないか確認）
dotnet test

# 3. Hostサービスをデバッグ実行
dotnet run --project src/ProcTail.Host/

# 4. 別のターミナルでCLIをテスト
dotnet run --project src/ProcTail.Cli/ -- version
```

### 2. Visual Studio でのデバッグ

1. **スタートアッププロジェクトの設定**
   - ソリューションエクスプローラーで `ProcTail.Host` を右クリック
   - 「スタートアッププロジェクトに設定」を選択

2. **ブレークポイントの設定**
   ```csharp
   public async Task StartAsync()
   {
       _logger.LogInformation("ProcTailServiceを開始しています...");
       // ← ここにブレークポイントを設定
       await _etwProvider.StartMonitoringAsync();
   }
   ```

3. **F5キーでデバッグ開始**

### 3. コードの変更手順

1. **機能ブランチの作成**
   ```bash
   git checkout -b feature/add-new-command
   ```

2. **コードの変更**
   - 適切なプロジェクトでファイルを編集
   - 必要に応じてテストも追加

3. **動作確認**
   ```bash
   dotnet build
   dotnet test
   ```

4. **コミットとプッシュ**
   ```bash
   git add .
   git commit -m "Add new command feature"
   git push origin feature/add-new-command
   ```

## 🔄 よくある作業パターン

### 1. 新しいコマンドの追加

#### **ステップ1: コマンドクラスの作成**
```csharp
// ProcTail.Cli/Commands/NewCommand.cs
public class NewCommand : BaseCommand
{
    public NewCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        // コマンドの処理を実装
        WriteInfo("新しいコマンドを実行中...");
        
        // サービスとの通信
        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        WriteSuccess("コマンドが正常に完了しました");
    }
}
```

#### **ステップ2: Program.csでコマンドを登録**
```csharp
// ProcTail.Cli/Program.cs
private static RootCommand CreateRootCommand()
{
    var rootCommand = new RootCommand("ProcTail - プロセス監視ツール");
    
    // 既存のコマンド
    rootCommand.AddCommand(CreateAddCommand());
    
    // 新しいコマンドを追加
    rootCommand.AddCommand(CreateNewCommand());
    
    return rootCommand;
}

private static Command CreateNewCommand()
{
    var newCommand = new Command("new", "新しい機能のコマンド");
    
    newCommand.SetHandler(async (context) =>
    {
        var client = CreatePipeClient(context);
        var command = new NewCommand(client);
        await command.ExecuteAsync(context);
    });
    
    return newCommand;
}
```

### 2. 新しいイベントタイプの追加

#### **ステップ1: データモデルの定義**
```csharp
// ProcTail.Core/Models/NewEventData.cs
public record NewEventData : BaseEventData
{
    public string NewProperty { get; init; } = "";
    public int NewValue { get; init; }
    
    public override string ToString()
    {
        return $"NewEvent: {NewProperty} = {NewValue}";
    }
}
```

#### **ステップ2: JSON シリアライゼーションの設定**
```csharp
// ProcTail.Core/Models/BaseEventData.cs に追加
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FileEventData), "FileEvent")]
[JsonDerivedType(typeof(ProcessStartEventData), "ProcessStart")]
[JsonDerivedType(typeof(ProcessEndEventData), "ProcessEnd")]
[JsonDerivedType(typeof(NewEventData), "NewEvent")] // 追加
public abstract record BaseEventData
{
    // 既存のプロパティ
}
```

#### **ステップ3: イベント処理の実装**
```csharp
// ProcTail.Infrastructure/Etw/WindowsEtwEventProvider.cs
private void ProcessTraceEvent(TraceEvent traceEvent)
{
    BaseEventData? eventData = traceEvent.ProviderName switch
    {
        "Microsoft-Windows-FileIO" => CreateFileEventData(traceEvent),
        "Microsoft-Windows-Kernel-Process" => CreateProcessEventData(traceEvent),
        "Microsoft-Windows-NewProvider" => CreateNewEventData(traceEvent), // 追加
        _ => null
    };

    if (eventData != null)
    {
        OnEventReceived(eventData);
    }
}

private NewEventData CreateNewEventData(TraceEvent traceEvent)
{
    return new NewEventData
    {
        ProcessId = traceEvent.ProcessID,
        NewProperty = traceEvent.PayloadString("PropertyName"),
        NewValue = traceEvent.PayloadInt32("ValueName")
    };
}
```

### 3. 設定項目の追加

#### **ステップ1: 設定クラスの更新**
```csharp
// ProcTail.Infrastructure/Configuration/EtwConfiguration.cs
public class EtwConfiguration : IEtwConfiguration
{
    public string SessionName { get; set; } = "ProcTailSession";
    public int BufferSizeKB { get; set; } = 1024;
    public bool EnableNewFeature { get; set; } = false; // 追加
}
```

#### **ステップ2: appsettings.jsonの更新**
```json
{
  "Etw": {
    "SessionName": "ProcTailSession",
    "BufferSizeKB": 1024,
    "EnableNewFeature": true
  }
}
```

#### **ステップ3: 設定を使用する**
```csharp
public class WindowsEtwEventProvider
{
    private readonly IEtwConfiguration _configuration;

    public async Task StartMonitoringAsync()
    {
        if (_configuration.EnableNewFeature)
        {
            // 新機能の処理
            _logger.LogInformation("新機能が有効になっています");
        }
    }
}
```

## 🧪 テストの書き方

### 1. 単体テストの基本

```csharp
// tests/ProcTail.Core.Tests/EventProcessorTests.cs
[TestFixture]
public class EventProcessorTests
{
    private EventProcessor _eventProcessor = null!;
    private Mock<ILogger<EventProcessor>> _mockLogger = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EventProcessor>>();
        _eventProcessor = new EventProcessor(_mockLogger.Object);
    }

    [Test]
    public void ProcessEvent_WithValidEvent_ShouldNotThrow()
    {
        // Arrange
        var eventData = new FileEventData
        {
            ProcessId = 1234,
            FilePath = "C:\\test.txt",
            Operation = "Create"
        };

        // Act & Assert
        Assert.DoesNotThrow(() => _eventProcessor.ProcessEvent(eventData));
    }

    [Test]
    public void ProcessEvent_WithNullEvent_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _eventProcessor.ProcessEvent(null!));
    }
}
```

### 2. 統合テストの例

```csharp
// tests/ProcTail.Integration.Tests/ProcTailServiceTests.cs
[TestFixture]
public class ProcTailServiceIntegrationTests
{
    private ProcTailService _service = null!;

    [SetUp]
    public void Setup()
    {
        // モックを使用した統合テスト用のセットアップ
        var services = new ServiceCollection();
        services.AddSingleton<Mock<IEtwEventProvider>>();
        services.AddSingleton<IEventStorage, EventStorage>();
        
        var serviceProvider = services.BuildServiceProvider();
        _service = serviceProvider.GetRequiredService<ProcTailService>();
    }

    [Test]
    public async Task AddWatchTarget_WithValidProcessId_ShouldReturnTrue()
    {
        // Arrange
        const int processId = 1234;
        const string tag = "test-tag";

        // Act
        var result = await _service.AddWatchTargetAsync(processId, tag);

        // Assert
        result.Should().BeTrue();
    }
}
```

### 3. Windows固有テストの書き方

```csharp
[TestFixture]
[Category("Windows")]
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProviderTests
{
    [Test]
    public void Constructor_OnWindows_ShouldInitializeSuccessfully()
    {
        // Windows環境でのみ実行
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange & Act & Assert
        var action = () => new WindowsEtwEventProvider(_logger, _configuration);
        action.Should().NotThrow();
    }

    [Test]
    public async Task StartMonitoring_WithAdminRights_ShouldSucceed()
    {
        // 管理者権限が必要なテスト
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限が必要です");
            return;
        }

        // テスト実装
    }
}
```

## 🐛 デバッグ方法

### 1. Visual Studio でのデバッグ

#### **ブレークポイントの活用**
```csharp
public async Task ProcessEventAsync(BaseEventData eventData)
{
    // ブレークポイントを設定 → F9キー
    _logger.LogInformation("Processing event: {EventType}", eventData.EventType);
    
    // 条件付きブレークポイント
    // ブレークポイント右クリック → 条件 → eventData.ProcessId == 1234
    if (eventData is FileEventData fileEvent)
    {
        await ProcessFileEventAsync(fileEvent);
    }
}
```

#### **デバッグウィンドウの活用**
- **ローカル**: 現在のスコープの変数
- **ウォッチ**: 監視したい式を追加
- **呼び出し履歴**: メソッドの呼び出し順序
- **出力**: コンソール出力やログ

### 2. ログを使ったデバッグ

```csharp
public class EventProcessor
{
    private readonly ILogger<EventProcessor> _logger;

    public void ProcessEvent(BaseEventData eventData)
    {
        // 情報ログ
        _logger.LogInformation("イベント処理開始: {EventType}, ProcessId: {ProcessId}",
            eventData.EventType, eventData.ProcessId);

        try
        {
            // 処理実装
            DoSomething(eventData);
            
            // 成功ログ
            _logger.LogInformation("イベント処理完了");
        }
        catch (Exception ex)
        {
            // エラーログ
            _logger.LogError(ex, "イベント処理中にエラーが発生しました");
            throw;
        }
    }
}
```

### 3. コンソールでのデバッグ

```bash
# 詳細ログを有効にして実行
dotnet run --project src/ProcTail.Host/ --verbosity normal

# 特定のログレベルのみ表示
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project src/ProcTail.Host/
```

### 4. Named Pipes の動作確認

```csharp
// テスト用のシンプルなクライアント
class Program
{
    static async Task Main()
    {
        using var client = new NamedPipeClientStream(".", "ProcTailIPC", PipeDirection.InOut);
        
        Console.WriteLine("Connecting to ProcTail service...");
        await client.ConnectAsync(5000);
        
        Console.WriteLine("Connected! Sending test request...");
        var request = @"{""RequestType"": ""GetStatus""}";
        var requestBytes = Encoding.UTF8.GetBytes(request);
        await client.WriteAsync(requestBytes);
        await client.FlushAsync();
        
        var responseBuffer = new byte[4096];
        var bytesRead = await client.ReadAsync(responseBuffer);
        var response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
        
        Console.WriteLine($"Response: {response}");
    }
}
```

## 📚 学習リソース

### C# 学習サイト
- [Microsoft Learn C#](https://docs.microsoft.com/ja-jp/learn/paths/csharp-first-steps/)
- [C# プログラミングガイド](https://docs.microsoft.com/ja-jp/dotnet/csharp/programming-guide/)

### .NET 固有の知識
- [依存性注入](https://docs.microsoft.com/ja-jp/dotnet/core/extensions/dependency-injection)
- [非同期プログラミング](https://docs.microsoft.com/ja-jp/dotnet/csharp/programming-guide/concepts/async/)
- [LINQ](https://docs.microsoft.com/ja-jp/dotnet/csharp/programming-guide/concepts/linq/)

### Windows 開発
- [ETW ドキュメント](https://docs.microsoft.com/en-us/windows/win32/etw/)
- [Named Pipes](https://docs.microsoft.com/en-us/windows/win32/ipc/named-pipes)

## 🤝 開発時のベストプラクティス

### 1. コーディング規約
```csharp
// ✅ 良い例
public class EventProcessor
{
    private readonly ILogger<EventProcessor> _logger;
    
    public async Task<bool> ProcessEventAsync(BaseEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        _logger.LogInformation("イベント処理開始: {EventType}", eventData.EventType);
        
        try
        {
            await DoProcessingAsync(eventData);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベント処理に失敗しました");
            return false;
        }
    }
}
```

### 2. エラーハンドリング
```csharp
// 適切な例外処理
public async Task StartMonitoringAsync()
{
    try
    {
        await _etwProvider.StartMonitoringAsync();
        _logger.LogInformation("ETW監視を開始しました");
    }
    catch (UnauthorizedAccessException ex)
    {
        _logger.LogError(ex, "管理者権限が必要です");
        throw new InvalidOperationException("ETW監視には管理者権限が必要です", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "ETW監視の開始に失敗しました");
        throw;
    }
}
```

### 3. テスト駆動開発
```csharp
// 1. まずテストを書く（赤）
[Test]
public void CalculateTotal_WithValidItems_ShouldReturnCorrectSum()
{
    // Arrange
    var items = new[] { 10, 20, 30 };
    var calculator = new Calculator();
    
    // Act
    var result = calculator.CalculateTotal(items);
    
    // Assert
    result.Should().Be(60);
}

// 2. 実装を書く（緑）
public class Calculator
{
    public int CalculateTotal(int[] items)
    {
        return items.Sum();
    }
}

// 3. リファクタリング（青）
```

### 4. Git の使い方
```bash
# 機能ブランチでの開発
git checkout -b feature/new-command
git add .
git commit -m "feat: add new command for user management

- Add UserCommand class
- Add user list functionality
- Add unit tests for UserCommand"

# プルリクエスト前の確認
git checkout main
git pull origin main
git checkout feature/new-command
git rebase main

# 最終確認
dotnet build
dotnet test
```

## 🚨 よくある問題と解決方法

### 1. ビルドエラー
```
error CS0246: The type or namespace name 'ILogger' could not be found
```
**解決方法**: using ディレクティブを追加
```csharp
using Microsoft.Extensions.Logging;
```

### 2. 権限エラー
```
System.UnauthorizedAccessException: Access to the path is denied
```
**解決方法**: Visual Studio を管理者権限で実行

### 3. Named Pipes 接続エラー
```
System.TimeoutException: Timed out waiting for a connection
```
**解決方法**: 
1. ProcTail.Host が起動しているか確認
2. パイプ名が正しいか確認
3. セキュリティ設定を確認

### 4. テストが失敗する
```
Expected: True
But was: False
```
**解決方法**:
1. テストの前提条件を確認
2. モックの設定を確認
3. デバッガーでステップ実行

---

**🎉 これでProcTailの開発を始める準備が整いました！何か分からないことがあれば、遠慮なく質問してください。**