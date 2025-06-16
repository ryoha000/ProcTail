# 実装ガイドライン

## 1. プロジェクト構成

### 1.1 ソリューション構造

```
ProcTail.sln
├── src/
│   ├── ProcTail.Core/              # ドメインレイヤー
│   │   ├── Interfaces/
│   │   ├── Models/
│   │   ├── Services/
│   │   └── Exceptions/
│   ├── ProcTail.Infrastructure/    # インフラストラクチャレイヤー
│   │   ├── Etw/
│   │   ├── NamedPipes/
│   │   ├── System/
│   │   └── Configuration/
│   ├── ProcTail.Application/       # アプリケーションレイヤー
│   │   ├── Services/
│   │   ├── Handlers/
│   │   └── Dtos/
│   └── ProcTail.Host/             # プレゼンテーションレイヤー
│       ├── Workers/
│       ├── Program.cs
│       └── appsettings.json
├── tests/
│   ├── ProcTail.Core.Tests/
│   ├── ProcTail.Application.Tests/
│   ├── ProcTail.Integration.Tests/
│   └── ProcTail.System.Tests/
└── docs/
```

### 1.2 プロジェクト参照関係

```
Host → Application → Core
   ↓       ↓
Infrastructure
```

## 2. 実装優先順位

### 2.1 フェーズ1: コアドメイン実装

1. **イベントデータモデル**
   ```csharp
   // ProcTail.Core/Models/Events.cs
   public abstract record BaseEventData
   {
       public required DateTime Timestamp { get; init; }
       public required string TagName { get; init; }
       public required int ProcessId { get; init; }
       public required int ThreadId { get; init; }
       public required string ProviderName { get; init; }
       public required string EventName { get; init; }
       public required Guid ActivityId { get; init; }
       public required Guid RelatedActivityId { get; init; }
       public required IReadOnlyDictionary<string, object> Payload { get; init; }
   }

   [JsonDerivedType(typeof(FileEventData), typeDiscriminator: "file")]
   [JsonDerivedType(typeof(ProcessStartEventData), typeDiscriminator: "process_start")]
   [JsonDerivedType(typeof(ProcessEndEventData), typeDiscriminator: "process_end")]
   [JsonDerivedType(typeof(GenericEventData), typeDiscriminator: "generic")]
   [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
   public abstract record BaseEventData { /* ... */ }

   public record FileEventData : BaseEventData
   {
       public required string FilePath { get; init; }
   }

   public record ProcessStartEventData : BaseEventData
   {
       public required int ChildProcessId { get; init; }
       public required string ChildProcessName { get; init; }
   }

   public record ProcessEndEventData : BaseEventData
   {
       public required int ExitCode { get; init; }
   }

   public record GenericEventData : BaseEventData;
   ```

2. **監視対象管理サービス**
   ```csharp
   // ProcTail.Core/Services/WatchTargetManager.cs
   public class WatchTargetManager : IWatchTargetManager
   {
       private readonly ConcurrentDictionary<int, WatchTarget> _targets = new();
       private readonly IProcessValidator _processValidator;
       private readonly ILogger<WatchTargetManager> _logger;

       public async Task<bool> AddTargetAsync(int processId, string tagName)
       {
           // バリデーション
           if (processId <= 0)
           {
               _logger.LogWarning("Invalid PID: {ProcessId}", processId);
               return false;
           }

           if (string.IsNullOrWhiteSpace(tagName))
           {
               _logger.LogWarning("Tag name cannot be empty");
               return false;
           }

           if (!_processValidator.ProcessExists(processId))
           {
               _logger.LogWarning("Process not found: {ProcessId}", processId);
               return false;
           }

           if (_targets.ContainsKey(processId))
           {
               _logger.LogWarning("Process already watched: {ProcessId}", processId);
               return false;
           }

           // 監視対象追加
           var target = new WatchTarget(processId, tagName, DateTime.UtcNow);
           var added = _targets.TryAdd(processId, target);
           
           if (added)
           {
               _logger.LogInformation("Added watch target: PID={ProcessId}, Tag={TagName}", 
                   processId, tagName);
           }

           return added;
       }

       // 他のメソッド実装...
   }
   ```

3. **イベント処理サービス**
   ```csharp
   // ProcTail.Core/Services/EventProcessor.cs
   public class EventProcessor : IEventProcessor
   {
       private readonly IWatchTargetManager _targetManager;
       private readonly ILogger<EventProcessor> _logger;

       public async Task<ProcessingResult> ProcessEventAsync(RawEventData rawEvent)
       {
           try
           {
               // 監視対象チェック
               if (!_targetManager.IsWatchedProcess(rawEvent.ProcessId))
               {
                   return new ProcessingResult(false, ErrorMessage: "Process not watched");
               }

               var tagName = _targetManager.GetTagForProcess(rawEvent.ProcessId);
               if (tagName == null)
               {
                   return new ProcessingResult(false, ErrorMessage: "Tag not found");
               }

               // イベント種別に応じたデータ変換
               var eventData = rawEvent.ProviderName switch
               {
                   "Microsoft-Windows-Kernel-FileIO" => CreateFileEventData(rawEvent, tagName),
                   "Microsoft-Windows-Kernel-Process" when rawEvent.EventName.Contains("Start") 
                       => CreateProcessStartEventData(rawEvent, tagName),
                   "Microsoft-Windows-Kernel-Process" when rawEvent.EventName.Contains("End") 
                       => CreateProcessEndEventData(rawEvent, tagName),
                   _ => CreateGenericEventData(rawEvent, tagName)
               };

               return new ProcessingResult(true, eventData);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error processing event: {EventName}", rawEvent.EventName);
               return new ProcessingResult(false, ErrorMessage: ex.Message);
           }
       }

       private FileEventData CreateFileEventData(RawEventData raw, string tagName)
       {
           var filePath = raw.Payload.TryGetValue("FilePath", out var path) 
               ? path?.ToString() ?? string.Empty
               : string.Empty;

           return new FileEventData
           {
               Timestamp = raw.Timestamp,
               TagName = tagName,
               ProcessId = raw.ProcessId,
               ThreadId = raw.ThreadId,
               ProviderName = raw.ProviderName,
               EventName = raw.EventName,
               ActivityId = raw.ActivityId,
               RelatedActivityId = raw.RelatedActivityId,
               Payload = raw.Payload,
               FilePath = filePath
           };
       }

       // 他のファクトリメソッド...
   }
   ```

### 2.2 フェーズ2: インフラストラクチャ実装

1. **ETW イベントプロバイダー**
   ```csharp
   // ProcTail.Infrastructure/Etw/WindowsEtwEventProvider.cs
   public class WindowsEtwEventProvider : IEtwEventProvider
   {
       private TraceEventSession? _session;
       private ETWTraceEventSource? _source;
       private Task? _processingTask;
       private readonly CancellationTokenSource _cancellationTokenSource = new();
       private readonly IEtwConfiguration _config;
       private readonly ILogger<WindowsEtwEventProvider> _logger;

       public event EventHandler<RawEventData>? EventReceived;

       public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
       {
           if (_session != null)
           {
               _logger.LogWarning("ETW monitoring already started");
               return;
           }

           try
           {
               // ETWセッション作成
               _session = new TraceEventSession("ProcTailETWSession");
               _source = new ETWTraceEventSource("ProcTailETWSession", TraceEventSourceType.Session);

               // プロバイダー有効化
               foreach (var provider in _config.EnabledProviders)
               {
                   _session.EnableProvider(provider);
                   _logger.LogDebug("Enabled ETW provider: {Provider}", provider);
               }

               // イベントハンドラー設定
               _source.Dynamic.All += OnEtwEvent;

               // バックグラウンド処理開始
               _processingTask = Task.Run(() => _source.Process(), _cancellationTokenSource.Token);

               _logger.LogInformation("ETW monitoring started");
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Failed to start ETW monitoring");
               await StopMonitoringAsync();
               throw;
           }
       }

       private void OnEtwEvent(TraceEvent obj)
       {
           try
           {
               // イベントフィルタリング
               if (!_config.EnabledEventNames.Contains(obj.EventName))
                   return;

               // ペイロードデータ抽出
               var payload = new Dictionary<string, object>();
               for (int i = 0; i < obj.PayloadNames.Length; i++)
               {
                   var name = obj.PayloadNames[i];
                   var value = obj.PayloadValue(i);
                   payload[name] = value;
               }

               // RawEventData作成
               var rawEvent = new RawEventData(
                   obj.TimeStamp,
                   obj.ProviderName,
                   obj.EventName,
                   obj.ProcessID,
                   obj.ThreadID,
                   obj.ActivityID,
                   obj.RelatedActivityID,
                   payload
               );

               // イベント発火
               EventReceived?.Invoke(this, rawEvent);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error processing ETW event: {EventName}", obj.EventName);
           }
       }

       // Dispose, StopMonitoringAsync実装...
   }
   ```

2. **Named Pipe サーバー**
   ```csharp
   // ProcTail.Infrastructure/NamedPipes/WindowsNamedPipeServer.cs
   public class WindowsNamedPipeServer : INamedPipeServer
   {
       private readonly IPipeConfiguration _config;
       private readonly ILogger<WindowsNamedPipeServer> _logger;
       private readonly CancellationTokenSource _cancellationTokenSource = new();
       private readonly SemaphoreSlim _connectionSemaphore;
       private Task? _listenTask;

       public event EventHandler<IpcRequestEventArgs>? RequestReceived;

       public async Task StartAsync(CancellationToken cancellationToken = default)
       {
           if (_listenTask != null)
           {
               _logger.LogWarning("Named pipe server already started");
               return;
           }

           _listenTask = Task.Run(ListenForConnections, _cancellationTokenSource.Token);
           _logger.LogInformation("Named pipe server started on: {PipeName}", _config.PipeName);
       }

       private async Task ListenForConnections()
       {
           while (!_cancellationTokenSource.Token.IsCancellationRequested)
           {
               try
               {
                   await _connectionSemaphore.WaitAsync(_cancellationTokenSource.Token);

                   // 非同期で接続処理
                   _ = Task.Run(async () =>
                   {
                       try
                       {
                           await HandleClientConnection();
                       }
                       finally
                       {
                           _connectionSemaphore.Release();
                       }
                   }, _cancellationTokenSource.Token);
               }
               catch (OperationCanceledException)
               {
                   break;
               }
               catch (Exception ex)
               {
                   _logger.LogError(ex, "Error in connection listener");
                   await Task.Delay(1000, _cancellationTokenSource.Token);
               }
           }
       }

       private async Task HandleClientConnection()
       {
           using var pipeServer = new NamedPipeServerStream(
               _config.PipeName,
               PipeDirection.InOut,
               _config.MaxConcurrentConnections,
               PipeTransmissionMode.Message,
               PipeOptions.Asynchronous,
               bufferSize: 4096,
               bufferSize: 4096,
               CreatePipeSecurity());

           try
           {
               await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);
               _logger.LogDebug("Client connected to named pipe");

               // リクエスト読み取り
               var requestJson = await ReadRequestAsync(pipeServer);
               if (string.IsNullOrEmpty(requestJson))
                   return;

               // イベント発火
               var eventArgs = new IpcRequestEventArgs
               {
                   RequestJson = requestJson,
                   SendResponseAsync = response => WriteResponseAsync(pipeServer, response),
                   CancellationToken = _cancellationTokenSource.Token
               };

               RequestReceived?.Invoke(this, eventArgs);
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error handling client connection");
           }
       }

       private PipeSecurity CreatePipeSecurity()
       {
           var security = new PipeSecurity();
           
           // ローカル認証済みユーザーに読み書き許可
           security.AddAccessRule(new PipeAccessRule(
               new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
               PipeAccessRights.ReadWrite,
               AccessControlType.Allow));

           // 管理者にフルコントロール許可
           security.AddAccessRule(new PipeAccessRule(
               new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
               PipeAccessRights.FullControl,
               AccessControlType.Allow));

           return security;
       }

       // 他のヘルパーメソッド実装...
   }
   ```

### 2.3 フェーズ3: アプリケーション層実装

1. **IPC要求ハンドラー**
   ```csharp
   // ProcTail.Application/Handlers/IpcRequestHandler.cs
   public class IpcRequestHandler : IIpcRequestHandler
   {
       private readonly IServiceProvider _serviceProvider;
       private readonly ILogger<IpcRequestHandler> _logger;
       private readonly Dictionary<string, Type> _requestTypeMap;

       public IpcRequestHandler(IServiceProvider serviceProvider, ILogger<IpcRequestHandler> logger)
       {
           _serviceProvider = serviceProvider;
           _logger = logger;
           
           // リクエストタイプマッピング初期化
           _requestTypeMap = new Dictionary<string, Type>
           {
               { nameof(AddWatchTargetRequest), typeof(AddWatchTargetRequest) },
               { nameof(GetRecordedEventsRequest), typeof(GetRecordedEventsRequest) },
               { nameof(ClearEventsRequest), typeof(ClearEventsRequest) },
               { nameof(HealthCheckRequest), typeof(HealthCheckRequest) },
               { nameof(ShutdownRequest), typeof(ShutdownRequest) }
           };
       }

       public async Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken = default)
       {
           try
           {
               // リクエストタイプ判定
               using var document = JsonDocument.Parse(requestJson);
               var requestTypeName = DetermineRequestType(document.RootElement);
               
               if (!_requestTypeMap.TryGetValue(requestTypeName, out var requestType))
               {
                   return SerializeResponse(new BaseResponse { Success = false, ErrorMessage = $"Unknown request type: {requestTypeName}" });
               }

               // 動的ハンドラー呼び出し
               var handlerMethod = GetType().GetMethod(nameof(HandleTypedRequestAsync))!
                   .MakeGenericMethod(requestType);
               
               var task = (Task<string>)handlerMethod.Invoke(this, new object[] { requestJson, cancellationToken })!;
               return await task;
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error handling IPC request");
               return SerializeResponse(new BaseResponse { Success = false, ErrorMessage = "Internal server error" });
           }
       }

       private async Task<string> HandleTypedRequestAsync<TRequest>(string requestJson, CancellationToken cancellationToken)
           where TRequest : class
       {
           var request = JsonSerializer.Deserialize<TRequest>(requestJson);
           var handlerType = typeof(IRequestHandler<,>).MakeGenericType(typeof(TRequest), GetResponseType<TRequest>());
           var handler = _serviceProvider.GetRequiredService(handlerType);
           
           var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<object, BaseResponse>.HandleAsync))!;
           var responseTask = (Task)handleMethod.Invoke(handler, new object[] { request!, cancellationToken })!;
           await responseTask;
           
           var response = ((dynamic)responseTask).Result;
           return SerializeResponse(response);
       }

       // ヘルパーメソッド実装...
   }
   ```

## 3. 設定管理実装

### 3.1 設定クラス定義

```csharp
// ProcTail.Infrastructure/Configuration/ProcTailSettings.cs
public class ProcTailSettings
{
    public const string SectionName = "ProcTail";
    
    public EventSettings EventSettings { get; set; } = new();
    public PipeSettings PipeSettings { get; set; } = new();
    public SecuritySettings SecuritySettings { get; set; } = new();
    public LoggingSettings LoggingSettings { get; set; } = new();
}

public class EventSettings
{
    public int MaxEventsPerTag { get; set; } = 10000;
    public List<string> EnabledEventTypes { get; set; } = new() { "FileIO", "Process" };
    public TimeSpan EventBufferTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}

public class PipeSettings
{
    public string PipeName { get; set; } = "ProcTailIPC";
    public int MaxConcurrentConnections { get; set; } = 10;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

### 3.2 設定バリデーション

```csharp
// ProcTail.Infrastructure/Configuration/ProcTailSettingsValidator.cs
public class ProcTailSettingsValidator : IValidateOptions<ProcTailSettings>
{
    public ValidateOptionsResult Validate(string name, ProcTailSettings options)
    {
        var errors = new List<string>();

        if (options.EventSettings.MaxEventsPerTag <= 0)
            errors.Add("MaxEventsPerTag must be greater than 0");

        if (string.IsNullOrWhiteSpace(options.PipeSettings.PipeName))
            errors.Add("PipeName cannot be empty");

        if (options.PipeSettings.MaxConcurrentConnections <= 0)
            errors.Add("MaxConcurrentConnections must be greater than 0");

        return errors.Any() 
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
```

## 4. 依存性注入設定

### 4.1 サービス登録

```csharp
// ProcTail.Host/Program.cs
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        
        // 管理者権限チェック
        var privilegeChecker = host.Services.GetRequiredService<ISystemPrivilegeChecker>();
        if (!privilegeChecker.IsRunningAsAdministrator())
        {
            Console.WriteLine("Administrator privileges required. Please run as administrator.");
            Environment.Exit(1);
        }

        await host.RunAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices((context, services) =>
            {
                // 設定
                services.Configure<ProcTailSettings>(context.Configuration.GetSection(ProcTailSettings.SectionName));
                services.AddSingleton<IValidateOptions<ProcTailSettings>, ProcTailSettingsValidator>();

                // コアサービス
                services.AddSingleton<IWatchTargetManager, WatchTargetManager>();
                services.AddSingleton<IEventProcessor, EventProcessor>();
                services.AddSingleton<IEventStorage, EventStorage>();
                services.AddSingleton<IHealthChecker, HealthChecker>();

                // インフラストラクチャサービス
                services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
                services.AddSingleton<INamedPipeServer, WindowsNamedPipeServer>();
                services.AddSingleton<IProcessValidator, WindowsProcessValidator>();
                services.AddSingleton<ISystemPrivilegeChecker, WindowsSystemPrivilegeChecker>();
                services.AddSingleton<ISystemClock, SystemClock>();

                // アプリケーションサービス
                services.AddSingleton<IProcTailService, ProcTailService>();
                services.AddSingleton<IIpcRequestHandler, IpcRequestHandler>();

                // リクエストハンドラー
                services.AddScoped<IRequestHandler<AddWatchTargetRequest, AddWatchTargetResponse>, AddWatchTargetHandler>();
                services.AddScoped<IRequestHandler<GetRecordedEventsRequest, GetRecordedEventsResponse>, GetRecordedEventsHandler>();
                services.AddScoped<IRequestHandler<ClearEventsRequest, ClearEventsResponse>, ClearEventsHandler>();
                services.AddScoped<IRequestHandler<HealthCheckRequest, HealthCheckResponse>, HealthCheckHandler>();
                services.AddScoped<IRequestHandler<ShutdownRequest, ShutdownResponse>, ShutdownHandler>();

                // ワーカーサービス
                services.AddHostedService<ProcTailWorker>();
            })
            .UseSerilog((context, config) =>
            {
                config.ReadFrom.Configuration(context.Configuration);
            });
}
```

## 5. エラーハンドリング戦略

### 5.1 カスタム例外定義

```csharp
// ProcTail.Core/Exceptions/ProcTailExceptions.cs
namespace ProcTail.Core.Exceptions
{
    public abstract class ProcTailException : Exception
    {
        protected ProcTailException(string message) : base(message) { }
        protected ProcTailException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class DomainException : ProcTailException
    {
        public DomainException(string message) : base(message) { }
    }

    public class InfrastructureException : ProcTailException
    {
        public InfrastructureException(string message) : base(message) { }
        public InfrastructureException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ConfigurationException : ProcTailException
    {
        public ConfigurationException(string message) : base(message) { }
    }
}
```

### 5.2 グローバル例外ハンドリング

```csharp
// ProcTail.Host/Middleware/GlobalExceptionHandler.cs
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public BaseResponse HandleException(Exception exception)
    {
        return exception switch
        {
            DomainException domainEx => new BaseResponse 
            { 
                Success = false, 
                ErrorMessage = domainEx.Message 
            },
            InfrastructureException infraEx => new BaseResponse 
            { 
                Success = false, 
                ErrorMessage = "System error occurred" 
            },
            ConfigurationException configEx => new BaseResponse 
            { 
                Success = false, 
                ErrorMessage = "Configuration error" 
            },
            _ => new BaseResponse 
            { 
                Success = false, 
                ErrorMessage = "Unexpected error occurred" 
            }
        };
    }
}
```

この実装ガイドラインに従うことで、テスタブルで保守性の高いProcTailシステムを構築できます。