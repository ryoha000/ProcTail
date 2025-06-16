# モック実装とテストヘルパー

## 1. 概要

WSL環境での効率的な開発を支援するため、Windows固有APIのモック実装とテストヘルパーを提供します。これにより、実際のETWやNamed Pipesにアクセスできない環境でも、ビジネスロジックの開発とテストが可能になります。

## 2. モック実装の設計原則

### 2.1 忠実性の段階

```
Real Implementation (実装)
├── High-Fidelity Mock (高忠実度) - 実際の動作に近いシミュレーション
├── Behavior Mock (動作) - 基本的な振る舞いをシミュレート
└── Stub Mock (スタブ) - 最小限の応答のみ
```

### 2.2 使い分け指針

- **開発時**: Behavior Mock を使用して迅速な開発
- **統合テスト**: High-Fidelity Mock で詳細な動作確認
- **単体テスト**: Stub Mock で特定のケースに集中

## 3. ETW モック実装

### 3.1 モックETWイベントプロバイダー

```csharp
// ProcTail.Testing.Mocks/Etw/MockEtwEventProvider.cs
namespace ProcTail.Testing.Mocks.Etw
{
    public class MockEtwEventProvider : IEtwEventProvider
    {
        private readonly MockEventGenerator _eventGenerator;
        private readonly Timer? _eventTimer;
        private readonly CancellationTokenSource _cancellation = new();
        private bool _isMonitoring;

        public event EventHandler<RawEventData>? EventReceived;
        public bool IsMonitoring => _isMonitoring;

        public MockEtwEventProvider(MockEtwConfiguration? config = null)
        {
            var configuration = config ?? MockEtwConfiguration.Default;
            _eventGenerator = new MockEventGenerator(configuration);
        }

        public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_isMonitoring)
                return Task.CompletedTask;

            _isMonitoring = true;
            
            // バックグラウンドでイベント生成
            _ = Task.Run(GenerateEventsAsync, _cancellation.Token);
            
            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        {
            _isMonitoring = false;
            _cancellation.Cancel();
            return Task.CompletedTask;
        }

        private async Task GenerateEventsAsync()
        {
            while (!_cancellation.Token.IsCancellationRequested)
            {
                if (_eventGenerator.ShouldGenerateEvent())
                {
                    var eventData = _eventGenerator.GenerateRandomEvent();
                    EventReceived?.Invoke(this, eventData);
                }

                await Task.Delay(_eventGenerator.GetNextDelay(), _cancellation.Token);
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }

    /// <summary>
    /// モックETW設定
    /// </summary>
    public class MockEtwConfiguration
    {
        public static MockEtwConfiguration Default => new()
        {
            EventGenerationInterval = TimeSpan.FromMilliseconds(100),
            FileEventProbability = 0.6,
            ProcessEventProbability = 0.3,
            GenericEventProbability = 0.1,
            EnableRealisticTimings = true
        };

        public TimeSpan EventGenerationInterval { get; set; } = TimeSpan.FromMilliseconds(100);
        public double FileEventProbability { get; set; } = 0.6;
        public double ProcessEventProbability { get; set; } = 0.3;
        public double GenericEventProbability { get; set; } = 0.1;
        public bool EnableRealisticTimings { get; set; } = true;
        public IReadOnlyList<int> SimulatedProcessIds { get; set; } = new[] { 1234, 5678, 9999 };
    }
}
```

### 3.2 イベント生成エンジン

```csharp
// ProcTail.Testing.Mocks/Etw/MockEventGenerator.cs
namespace ProcTail.Testing.Mocks.Etw
{
    public class MockEventGenerator
    {
        private readonly MockEtwConfiguration _config;
        private readonly Random _random = new();
        private readonly string[] _mockFilePaths = {
            @"C:\Users\Test\Documents\test.txt",
            @"C:\Temp\logfile.log",
            @"C:\Windows\System32\test.dll",
            @"C:\Program Files\TestApp\config.json"
        };

        private readonly string[] _mockProcessNames = {
            "notepad.exe", "cmd.exe", "powershell.exe", "testapp.exe"
        };

        public MockEventGenerator(MockEtwConfiguration config)
        {
            _config = config;
        }

        public bool ShouldGenerateEvent()
        {
            // リアルなタイミングをシミュレート
            return _random.NextDouble() < 0.7; // 70%の確率でイベント生成
        }

        public TimeSpan GetNextDelay()
        {
            if (!_config.EnableRealisticTimings)
                return _config.EventGenerationInterval;

            // 現実的な間隔でイベント生成（ランダム性を追加）
            var baseDelay = _config.EventGenerationInterval.TotalMilliseconds;
            var variation = baseDelay * 0.5; // ±50%の変動
            var delay = baseDelay + (_random.NextDouble() - 0.5) * variation;
            
            return TimeSpan.FromMilliseconds(Math.Max(10, delay));
        }

        public RawEventData GenerateRandomEvent()
        {
            var eventType = DetermineEventType();
            var processId = _config.SimulatedProcessIds[_random.Next(_config.SimulatedProcessIds.Count)];

            return eventType switch
            {
                EventType.FileOperation => GenerateFileEvent(processId),
                EventType.ProcessOperation => GenerateProcessEvent(processId),
                _ => GenerateGenericEvent(processId)
            };
        }

        public RawEventData GenerateFileEvent(int processId, string? filePath = null)
        {
            var operations = new[] { "Create", "Write", "Delete", "Rename", "SetInfo" };
            var operation = operations[_random.Next(operations.Length)];
            var targetPath = filePath ?? _mockFilePaths[_random.Next(_mockFilePaths.Length)];

            var payload = new Dictionary<string, object>
            {
                { "FilePath", targetPath },
                { "Operation", operation },
                { "FileSize", _random.Next(0, 1000000) },
                { "CreationTime", DateTime.UtcNow.AddSeconds(-_random.Next(0, 3600)) }
            };

            return new RawEventData(
                DateTime.UtcNow,
                "Microsoft-Windows-Kernel-FileIO",
                $"FileIo/{operation}",
                processId,
                _random.Next(1000, 9999),
                Guid.NewGuid(),
                Guid.Empty,
                payload
            );
        }

        public RawEventData GenerateProcessEvent(int processId, ProcessEventType eventType = ProcessEventType.Random)
        {
            var actualEventType = eventType == ProcessEventType.Random 
                ? (_random.NextDouble() < 0.7 ? ProcessEventType.Start : ProcessEventType.End)
                : eventType;

            var childProcessId = _random.Next(10000, 99999);
            var processName = _mockProcessNames[_random.Next(_mockProcessNames.Length)];

            var payload = new Dictionary<string, object>
            {
                { "ProcessName", processName },
                { "CommandLine", $@"C:\Windows\System32\{processName} /test" },
                { "ParentProcessId", processId }
            };

            if (actualEventType == ProcessEventType.Start)
            {
                payload.Add("ChildProcessId", childProcessId);
                payload.Add("StartTime", DateTime.UtcNow);
            }
            else
            {
                payload.Add("ExitCode", _random.Next(0, 3));
                payload.Add("ExitTime", DateTime.UtcNow);
            }

            return new RawEventData(
                DateTime.UtcNow,
                "Microsoft-Windows-Kernel-Process",
                actualEventType == ProcessEventType.Start ? "Process/Start" : "Process/End",
                processId,
                _random.Next(1000, 9999),
                Guid.NewGuid(),
                Guid.Empty,
                payload
            );
        }

        private RawEventData GenerateGenericEvent(int processId)
        {
            var payload = new Dictionary<string, object>
            {
                { "EventData", $"Generic event data {_random.Next(1000, 9999)}" },
                { "Severity", _random.Next(1, 5) }
            };

            return new RawEventData(
                DateTime.UtcNow,
                "Microsoft-Windows-Test-Provider",
                "Generic/Event",
                processId,
                _random.Next(1000, 9999),
                Guid.NewGuid(),
                Guid.Empty,
                payload
            );
        }

        private EventType DetermineEventType()
        {
            var value = _random.NextDouble();
            
            if (value < _config.FileEventProbability)
                return EventType.FileOperation;
            else if (value < _config.FileEventProbability + _config.ProcessEventProbability)
                return EventType.ProcessOperation;
            else
                return EventType.Generic;
        }

        private enum EventType
        {
            FileOperation,
            ProcessOperation,
            Generic
        }

        public enum ProcessEventType
        {
            Random,
            Start,
            End
        }
    }
}
```

### 3.3 ETWイベントシナリオテスト

```csharp
// ProcTail.Testing.Mocks/Etw/EtwScenarioRunner.cs
namespace ProcTail.Testing.Mocks.Etw
{
    /// <summary>
    /// 複雑なETWイベントシナリオのシミュレーション
    /// </summary>
    public class EtwScenarioRunner
    {
        private readonly MockEtwEventProvider _provider;
        private readonly MockEventGenerator _generator;

        public EtwScenarioRunner(MockEtwEventProvider provider)
        {
            _provider = provider;
            _generator = new MockEventGenerator(MockEtwConfiguration.Default);
        }

        /// <summary>
        /// プロセス開始→ファイル操作→プロセス終了のシナリオ
        /// </summary>
        public async Task RunProcessLifecycleScenarioAsync(int parentProcessId, string tagName)
        {
            // 1. 子プロセス開始イベント
            var processStartEvent = _generator.GenerateProcessEvent(
                parentProcessId, 
                MockEventGenerator.ProcessEventType.Start);
            await EmitEventAsync(processStartEvent);

            // 子プロセスIDを取得
            var childProcessId = (int)processStartEvent.Payload["ChildProcessId"];

            // 2. 子プロセスによるファイル操作
            await Task.Delay(100);
            for (int i = 0; i < 5; i++)
            {
                var fileEvent = _generator.GenerateFileEvent(childProcessId, $@"C:\temp\file_{i}.txt");
                await EmitEventAsync(fileEvent);
                await Task.Delay(50);
            }

            // 3. 子プロセス終了イベント
            await Task.Delay(200);
            var processEndEvent = _generator.GenerateProcessEvent(
                childProcessId, 
                MockEventGenerator.ProcessEventType.End);
            await EmitEventAsync(processEndEvent);
        }

        /// <summary>
        /// 大量ファイル操作シナリオ
        /// </summary>
        public async Task RunHighVolumeFileOperationsAsync(int processId, int eventCount = 1000)
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < eventCount; i++)
            {
                var task = Task.Run(async () =>
                {
                    var fileEvent = _generator.GenerateFileEvent(processId);
                    await EmitEventAsync(fileEvent);
                });
                
                tasks.Add(task);
                
                // 過度な同時実行を避ける
                if (tasks.Count >= 10)
                {
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// エラー条件シミュレーション
        /// </summary>
        public async Task RunErrorScenarioAsync()
        {
            // 存在しないプロセスIDでイベント生成
            var invalidProcessEvent = _generator.GenerateFileEvent(-1);
            await EmitEventAsync(invalidProcessEvent);

            // 不正なファイルパス
            var invalidPathEvent = _generator.GenerateFileEvent(1234, "invalid::path");
            await EmitEventAsync(invalidPathEvent);

            // 破損ペイロード
            var corruptedEvent = new RawEventData(
                DateTime.UtcNow,
                "Microsoft-Windows-Kernel-FileIO",
                "FileIo/Create",
                1234,
                5678,
                Guid.NewGuid(),
                Guid.Empty,
                new Dictionary<string, object> { { "CorruptedData", new object() } }
            );
            await EmitEventAsync(corruptedEvent);
        }

        private async Task EmitEventAsync(RawEventData eventData)
        {
            // プロバイダーのEventReceivedイベントを発火
            var eventInfo = typeof(MockEtwEventProvider).GetEvent("EventReceived");
            var handler = eventInfo?.GetAddMethod(true);
            
            if (handler != null)
            {
                var delegateInstance = Delegate.CreateDelegate(
                    typeof(EventHandler<RawEventData>),
                    this,
                    nameof(InvokeEventReceived));
                handler.Invoke(_provider, new[] { delegateInstance });
            }

            await Task.Delay(1); // 非同期処理をシミュレート
        }

        private void InvokeEventReceived(object? sender, RawEventData e)
        {
            // イベント発火のヘルパー（リフレクション経由）
        }
    }
}
```

## 4. Named Pipes モック実装

### 4.1 モックNamed Pipeサーバー

```csharp
// ProcTail.Testing.Mocks/NamedPipes/MockNamedPipeServer.cs
namespace ProcTail.Testing.Mocks.NamedPipes
{
    public class MockNamedPipeServer : INamedPipeServer
    {
        private readonly MockPipeConfiguration _config;
        private readonly ConcurrentQueue<MockPipeConnection> _connections = new();
        private readonly CancellationTokenSource _cancellation = new();
        private bool _isRunning;

        public event EventHandler<IpcRequestEventArgs>? RequestReceived;
        public bool IsRunning => _isRunning;

        public MockNamedPipeServer(MockPipeConfiguration? config = null)
        {
            _config = config ?? MockPipeConfiguration.Default;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                return Task.CompletedTask;

            _isRunning = true;
            
            // リスナータスクを開始
            _ = Task.Run(SimulateListening, _cancellation.Token);
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _isRunning = false;
            _cancellation.Cancel();
            
            // すべての接続を閉じる
            while (_connections.TryDequeue(out var connection))
            {
                connection.Dispose();
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// テスト用: クライアント接続をシミュレート
        /// </summary>
        public async Task<string> SimulateClientRequestAsync(string requestJson)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Server is not running");

            var connection = new MockPipeConnection();
            _connections.Enqueue(connection);

            var responseReceived = false;
            string? response = null;

            var eventArgs = new IpcRequestEventArgs
            {
                RequestJson = requestJson,
                SendResponseAsync = async (resp) =>
                {
                    response = resp;
                    responseReceived = true;
                    await Task.CompletedTask;
                },
                CancellationToken = _cancellation.Token
            };

            // イベント発火
            RequestReceived?.Invoke(this, eventArgs);

            // 応答を待機（タイムアウト付き）
            var timeout = DateTime.UtcNow.Add(_config.RequestTimeout);
            while (!responseReceived && DateTime.UtcNow < timeout)
            {
                await Task.Delay(10);
            }

            if (!responseReceived)
                throw new TimeoutException("Response not received within timeout period");

            return response ?? string.Empty;
        }

        /// <summary>
        /// 複数同時接続をシミュレート
        /// </summary>
        public async Task<IReadOnlyList<string>> SimulateConcurrentRequestsAsync(
            IReadOnlyList<string> requests, 
            int maxConcurrency = 5)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = requests.Select(async request =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await SimulateClientRequestAsync(request);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            return await Task.WhenAll(tasks);
        }

        private async Task SimulateListening()
        {
            while (!_cancellation.Token.IsCancellationRequested)
            {
                // 接続待機をシミュレート
                await Task.Delay(_config.ListenInterval, _cancellation.Token);
                
                // 設定に基づいてランダムな接続エラーをシミュレート
                if (_config.SimulateConnectionErrors && Random.Shared.NextDouble() < 0.05)
                {
                    // 5%の確率で接続エラー
                    continue;
                }
            }
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellation.Dispose();
        }

        private class MockPipeConnection : IDisposable
        {
            public DateTime ConnectedAt { get; } = DateTime.UtcNow;
            public void Dispose() { }
        }
    }

    public class MockPipeConfiguration
    {
        public static MockPipeConfiguration Default => new()
        {
            ListenInterval = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(30),
            SimulateConnectionErrors = false,
            MaxConcurrentConnections = 10
        };

        public TimeSpan ListenInterval { get; set; } = TimeSpan.FromMilliseconds(50);
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool SimulateConnectionErrors { get; set; } = false;
        public int MaxConcurrentConnections { get; set; } = 10;
    }
}
```

## 5. テストヘルパーとファクトリー

### 5.1 統合テストヘルパー

```csharp
// ProcTail.Testing.Common/Helpers/TestServiceFactory.cs
namespace ProcTail.Testing.Common.Helpers
{
    /// <summary>
    /// テスト用のサービスファクトリー
    /// </summary>
    public static class TestServiceFactory
    {
        /// <summary>
        /// WSL/Linux環境用の完全モック構成
        /// </summary>
        public static IServiceCollection CreateMockServices(IConfiguration? configuration = null)
        {
            var services = new ServiceCollection();
            var config = configuration ?? CreateTestConfiguration();

            // 基本設定
            services.AddSingleton(config);
            services.Configure<ProcTailSettings>(config.GetSection(ProcTailSettings.SectionName));

            // モック実装を登録
            services.AddSingleton<IEtwEventProvider, MockEtwEventProvider>();
            services.AddSingleton<INamedPipeServer, MockNamedPipeServer>();
            services.AddSingleton<IProcessValidator, MockProcessValidator>();
            services.AddSingleton<ISystemPrivilegeChecker, MockSystemPrivilegeChecker>();
            services.AddSingleton<ISystemClock, MockSystemClock>();

            // 実際のビジネスロジック
            services.AddSingleton<IWatchTargetManager, WatchTargetManager>();
            services.AddSingleton<IEventProcessor, EventProcessor>();
            services.AddSingleton<IEventStorage, EventStorage>();
            services.AddSingleton<IHealthChecker, HealthChecker>();

            // アプリケーションサービス
            services.AddSingleton<IProcTailService, ProcTailService>();
            services.AddSingleton<IIpcRequestHandler, IpcRequestHandler>();

            // ログ設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            return services;
        }

        /// <summary>
        /// ハイブリッド構成（一部実装、一部モック）
        /// </summary>
        public static IServiceCollection CreateHybridServices(
            bool useRealEtw = false,
            bool useRealNamedPipes = false)
        {
            var services = CreateMockServices();

            if (useRealEtw && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                services.Replace(ServiceDescriptor.Singleton<IEtwEventProvider, WindowsEtwEventProvider>());
            }

            if (useRealNamedPipes && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                services.Replace(ServiceDescriptor.Singleton<INamedPipeServer, WindowsNamedPipeServer>());
            }

            return services;
        }

        private static IConfiguration CreateTestConfiguration()
        {
            var configData = new Dictionary<string, string>
            {
                { "ProcTail:EventSettings:MaxEventsPerTag", "1000" },
                { "ProcTail:PipeSettings:PipeName", "ProcTailTest" },
                { "ProcTail:PipeSettings:MaxConcurrentConnections", "5" },
                { "Logging:LogLevel:Default", "Debug" },
                { "Logging:LogLevel:ProcTail", "Trace" }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();
        }
    }

    /// <summary>
    /// テストデータビルダー
    /// </summary>
    public static class TestDataBuilder
    {
        public static AddWatchTargetRequest CreateValidAddWatchTargetRequest(
            int processId = 1234, 
            string tagName = "test-tag")
        {
            return new AddWatchTargetRequest(processId, tagName);
        }

        public static GetRecordedEventsRequest CreateGetEventsRequest(string tagName = "test-tag")
        {
            return new GetRecordedEventsRequest(tagName);
        }

        public static RawEventData CreateFileEvent(
            int processId = 1234,
            string filePath = @"C:\test.txt",
            string operation = "Create")
        {
            return new RawEventData(
                DateTime.UtcNow,
                "Microsoft-Windows-Kernel-FileIO",
                $"FileIo/{operation}",
                processId,
                5678,
                Guid.NewGuid(),
                Guid.Empty,
                new Dictionary<string, object> { { "FilePath", filePath } }
            );
        }

        public static List<RawEventData> CreateEventSequence(
            int processId = 1234,
            int eventCount = 10)
        {
            var events = new List<RawEventData>();
            var random = new Random();

            for (int i = 0; i < eventCount; i++)
            {
                events.Add(CreateFileEvent(
                    processId, 
                    $@"C:\temp\file_{i}.txt",
                    random.NextDouble() < 0.5 ? "Create" : "Write"));
            }

            return events;
        }
    }
}
```

### 5.2 テスト拡張メソッド

```csharp
// ProcTail.Testing.Common/Extensions/TestExtensions.cs
namespace ProcTail.Testing.Common.Extensions
{
    public static class TestExtensions
    {
        /// <summary>
        /// イベントが発火されるまで待機
        /// </summary>
        public static async Task<T> WaitForEventAsync<T>(
            this object source,
            string eventName,
            TimeSpan timeout = default) where T : EventArgs
        {
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(5);

            var tcs = new TaskCompletionSource<T>();
            var eventInfo = source.GetType().GetEvent(eventName);

            if (eventInfo == null)
                throw new ArgumentException($"Event '{eventName}' not found on type {source.GetType().Name}");

            EventHandler<T>? handler = null;
            handler = (sender, args) =>
            {
                eventInfo.RemoveEventHandler(source, handler);
                tcs.SetResult(args);
            };

            eventInfo.AddEventHandler(source, handler);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }

        /// <summary>
        /// 非同期操作の完了を待機
        /// </summary>
        public static async Task<bool> WaitForConditionAsync(
            Func<bool> condition,
            TimeSpan timeout = default,
            TimeSpan pollInterval = default)
        {
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(5);
            if (pollInterval == default)
                pollInterval = TimeSpan.FromMilliseconds(100);

            var endTime = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < endTime)
            {
                if (condition())
                    return true;

                await Task.Delay(pollInterval);
            }

            return false;
        }

        /// <summary>
        /// サービスコレクションからモックサービスを取得
        /// </summary>
        public static Mock<T> GetMock<T>(this IServiceProvider serviceProvider) where T : class
        {
            var service = serviceProvider.GetRequiredService<T>();
            if (service is not Mock<T> mock)
                throw new InvalidOperationException($"Service of type {typeof(T).Name} is not a mock");
            return mock;
        }
    }
}
```

これらのモック実装により、WSL環境でもProcTailの主要機能を効率的にテストできます。