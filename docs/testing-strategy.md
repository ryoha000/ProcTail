# テスト戦略

## 1. テスト戦略の基本方針

### 1.1 テストピラミッド

```
      /\
     /  \
    / E2E \
   /______\
  /        \
 /Integration\
/____________\
/    Unit    \
/____________\
```

- **Unit Tests (70%)**: ドメインロジックの詳細テスト
- **Integration Tests (20%)**: コンポーネント間の統合テスト
- **End-to-End Tests (10%)**: システム全体の動作テスト

### 1.2 テスト分類

| テスト種別 | 対象 | 実行環境 | 依存関係 |
|-----------|------|----------|----------|
| Unit | ドメインロジック | 開発マシン | モック使用 |
| Integration | アプリケーション層 | 開発マシン | 一部実装使用 |
| System | システム全体 | Windows管理者権限 | 実際のWindows API |

## 2. ユニットテスト戦略

### 2.1 テスト対象コンポーネント

#### 2.1.1 WatchTargetManager
```csharp
[TestFixture]
public class WatchTargetManagerTests
{
    private IWatchTargetManager _manager;
    private Mock<IProcessValidator> _mockProcessValidator;
    
    [SetUp]
    public void Setup()
    {
        _mockProcessValidator = new Mock<IProcessValidator>();
        _manager = new WatchTargetManager(_mockProcessValidator.Object);
    }
    
    [Test]
    public async Task AddTarget_ValidPid_ReturnsTrue()
    {
        // Arrange
        _mockProcessValidator.Setup(x => x.ProcessExists(1234)).Returns(true);
        
        // Act
        var result = await _manager.AddTargetAsync(1234, "test-tag");
        
        // Assert
        Assert.IsTrue(result);
        Assert.IsTrue(_manager.IsWatchedProcess(1234));
        Assert.AreEqual("test-tag", _manager.GetTagForProcess(1234));
    }
    
    [Test]
    public async Task AddTarget_InvalidPid_ReturnsFalse()
    {
        // Arrange
        _mockProcessValidator.Setup(x => x.ProcessExists(0)).Returns(false);
        
        // Act
        var result = await _manager.AddTargetAsync(0, "test-tag");
        
        // Assert
        Assert.IsFalse(result);
    }
    
    [Test]
    public async Task AddChildProcess_ValidParent_AutoAssignsTag()
    {
        // Arrange
        await _manager.AddTargetAsync(1234, "parent-tag");
        _mockProcessValidator.Setup(x => x.ProcessExists(5678)).Returns(true);
        
        // Act
        var result = await _manager.AddChildProcessAsync(5678, 1234);
        
        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("parent-tag", _manager.GetTagForProcess(5678));
    }
}
```

#### 2.1.2 EventProcessor
```csharp
[TestFixture]
public class EventProcessorTests
{
    private IEventProcessor _processor;
    private Mock<IWatchTargetManager> _mockTargetManager;
    
    [SetUp]
    public void Setup()
    {
        _mockTargetManager = new Mock<IWatchTargetManager>();
        _processor = new EventProcessor(_mockTargetManager.Object);
    }
    
    [Test]
    public async Task ProcessEvent_FileCreateEvent_ReturnsFileEventData()
    {
        // Arrange
        var rawEvent = new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-FileIO",
            "FileIo/Create",
            1234,
            5678,
            Guid.NewGuid(),
            Guid.Empty,
            new Dictionary<string, object> { { "FilePath", @"C:\test.txt" } }
        );
        
        _mockTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");
        
        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);
        
        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsInstanceOf<FileEventData>(result.EventData);
        var fileEvent = (FileEventData)result.EventData!;
        Assert.AreEqual(@"C:\test.txt", fileEvent.FilePath);
        Assert.AreEqual("test-tag", fileEvent.TagName);
    }
    
    [Test]
    public async Task ProcessEvent_UnwatchedProcess_ReturnsFailure()
    {
        // Arrange
        var rawEvent = new RawEventData(DateTime.UtcNow, "Provider", "Event", 9999, 0, Guid.Empty, Guid.Empty, new Dictionary<string, object>());
        _mockTargetManager.Setup(x => x.IsWatchedProcess(9999)).Returns(false);
        
        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);
        
        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.EventData);
    }
}
```

#### 2.1.3 EventStorage
```csharp
[TestFixture]
public class EventStorageTests
{
    private IEventStorage _storage;
    private IConfiguration _config;
    
    [SetUp]
    public void Setup()
    {
        var configData = new Dictionary<string, string>
        {
            { "ProcTail:EventSettings:MaxEventsPerTag", "3" }
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        _storage = new EventStorage(_config);
    }
    
    [Test]
    public async Task StoreEvent_WithinLimit_StoresSuccessfully()
    {
        // Arrange
        var eventData = new FileEventData
        {
            TagName = "test-tag",
            FilePath = @"C:\test.txt",
            Timestamp = DateTime.UtcNow,
            ProcessId = 1234
        };
        
        // Act
        await _storage.StoreEventAsync("test-tag", eventData);
        var events = await _storage.GetEventsAsync("test-tag");
        
        // Assert
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(eventData.FilePath, ((FileEventData)events[0]).FilePath);
    }
    
    [Test]
    public async Task StoreEvent_ExceedsLimit_RemovesOldestEvent()
    {
        // Arrange
        var tag = "test-tag";
        for (int i = 1; i <= 4; i++)
        {
            var eventData = new FileEventData
            {
                TagName = tag,
                FilePath = $@"C:\test{i}.txt",
                Timestamp = DateTime.UtcNow.AddSeconds(i),
                ProcessId = 1234
            };
            await _storage.StoreEventAsync(tag, eventData);
        }
        
        // Act
        var events = await _storage.GetEventsAsync(tag);
        
        // Assert  
        Assert.AreEqual(3, events.Count); // 最大件数
        var firstEvent = (FileEventData)events[0];
        Assert.AreEqual(@"C:\test2.txt", firstEvent.FilePath); // 最古が削除されている
    }
}
```

### 2.2 モック戦略

#### 2.2.1 Windows API依存の分離
```csharp
// テスト用のモックファクトリー
public static class TestMockFactory
{
    public static Mock<IEtwEventProvider> CreateEtwProvider()
    {
        var mock = new Mock<IEtwEventProvider>();
        mock.SetupGet(x => x.IsMonitoring).Returns(false);
        return mock;
    }
    
    public static Mock<IProcessValidator> CreateProcessValidator()
    {
        var mock = new Mock<IProcessValidator>();
        
        // デフォルトの動作設定
        mock.Setup(x => x.ProcessExists(It.IsInRange(1, 65535, Range.Inclusive))).Returns(true);
        mock.Setup(x => x.ProcessExists(It.IsInRange(-1000, 0, Range.Inclusive))).Returns(false);
        
        return mock;
    }
    
    public static Mock<ISystemPrivilegeChecker> CreatePrivilegeChecker(bool isAdmin = true)
    {
        var mock = new Mock<ISystemPrivilegeChecker>();
        mock.Setup(x => x.IsRunningAsAdministrator()).Returns(isAdmin);
        mock.Setup(x => x.CanAccessEtwSessions()).Returns(isAdmin);
        return mock;
    }
}
```

## 3. 統合テスト戦略

### 3.1 アプリケーション層のテスト

```csharp
[TestFixture]
public class ProcTailServiceIntegrationTests
{
    private IProcTailService _service;
    private Mock<IEtwEventProvider> _mockEtwProvider;
    private Mock<INamedPipeServer> _mockPipeServer;
    private IEventStorage _realEventStorage;
    private IWatchTargetManager _realTargetManager;
    
    [SetUp]
    public async Task Setup()
    {
        // 一部実装、一部モックの混合構成
        _mockEtwProvider = TestMockFactory.CreateEtwProvider();
        _mockPipeServer = new Mock<INamedPipeServer>();
        
        var config = CreateTestConfiguration();
        _realEventStorage = new EventStorage(config);
        _realTargetManager = new WatchTargetManager(TestMockFactory.CreateProcessValidator().Object);
        
        var serviceProvider = CreateServiceProvider();
        _service = serviceProvider.GetRequiredService<IProcTailService>();
    }
    
    [Test]
    public async Task StartService_ValidConfiguration_StartsSuccessfully()
    {
        // Arrange
        _mockEtwProvider.Setup(x => x.StartMonitoringAsync(It.IsAny<CancellationToken>()))
                       .Returns(Task.CompletedTask);
        _mockPipeServer.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        
        // Act
        await _service.StartAsync();
        
        // Assert
        Assert.AreEqual(ServiceStatus.Running, _service.Status);
        _mockEtwProvider.Verify(x => x.StartMonitoringAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockPipeServer.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    
    [Test]
    public async Task ProcessIpcRequest_AddWatchTarget_ProcessesCorrectly()
    {
        // Arrange
        await _service.StartAsync();
        var request = new AddWatchTargetRequest(1234, "integration-test");
        var requestJson = JsonSerializer.Serialize(request);
        
        // Act
        var handler = _serviceProvider.GetRequiredService<IIpcRequestHandler>();
        var responseJson = await handler.HandleRequestAsync(requestJson);
        
        // Assert
        var response = JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson);
        Assert.IsTrue(response.Success);
        Assert.IsTrue(_realTargetManager.IsWatchedProcess(1234));
    }
}
```

### 3.2 イベントフロー統合テスト

```csharp
[TestFixture]
public class EventFlowIntegrationTests
{
    [Test]
    public async Task FullEventFlow_EtwToStorage_WorksCorrectly()
    {
        // Arrange
        var mockEtwProvider = new Mock<IEtwEventProvider>();
        var realEventProcessor = new EventProcessor(_realTargetManager);
        var realEventStorage = new EventStorage(_config);
        
        await _realTargetManager.AddTargetAsync(1234, "flow-test");
        
        // Act: ETWイベントをシミュレート
        var rawEvent = new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-FileIO",
            "FileIo/Create",
            1234,
            5678,
            Guid.NewGuid(),
            Guid.Empty,
            new Dictionary<string, object> { { "FilePath", @"C:\test.txt" } }
        );
        
        var processingResult = await realEventProcessor.ProcessEventAsync(rawEvent);
        if (processingResult.Success)
        {
            await realEventStorage.StoreEventAsync("flow-test", processingResult.EventData!);
        }
        
        // Assert
        var storedEvents = await realEventStorage.GetEventsAsync("flow-test");
        Assert.AreEqual(1, storedEvents.Count);
        Assert.IsInstanceOf<FileEventData>(storedEvents[0]);
    }
}
```

## 4. システムテスト戦略

### 4.1 Windows管理者権限テスト

```csharp
[TestFixture]
[Platform("Win")]
[Category("RequiresAdmin")]
public class SystemTests
{
    [SetUp]
    public void Setup()
    {
        // 管理者権限チェック
        Assume.That(WindowsIdentity.GetCurrent().Groups
            .Any(g => g.Value == "S-1-5-32-544")); // Administrators group
    }
    
    [Test]
    public async Task RealEtwMonitoring_FileCreation_CapturesEvent()
    {
        // Arrange
        var config = new EtwConfiguration
        {
            EnabledProviders = new[] { "Microsoft-Windows-Kernel-FileIO" },
            EnabledEventNames = new[] { "FileIo/Create" }
        };
        
        using var etwProvider = new WindowsEtwEventProvider(config);
        var eventReceived = false;
        RawEventData? capturedEvent = null;
        
        etwProvider.EventReceived += (sender, eventData) =>
        {
            if (eventData.EventName == "FileIo/Create")
            {
                eventReceived = true;
                capturedEvent = eventData;
            }
        };
        
        // Act
        await etwProvider.StartMonitoringAsync();
        
        // ファイルを実際に作成
        var testFile = Path.Combine(Path.GetTempPath(), $"proctail_test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(testFile, "test content");
        
        // Wait for ETW event
        await Task.Delay(1000);
        
        // Assert
        Assert.IsTrue(eventReceived);
        Assert.IsNotNull(capturedEvent);
        
        // Cleanup
        File.Delete(testFile);
        await etwProvider.StopMonitoringAsync();
    }
    
    [Test]
    public async Task RealNamedPipes_Communication_WorksCorrectly()
    {
        // Arrange
        var pipeName = $"ProcTailTest_{Guid.NewGuid()}";
        var config = new PipeConfiguration
        {
            PipeName = pipeName,
            MaxConcurrentConnections = 1
        };
        
        using var pipeServer = new WindowsNamedPipeServer(config);
        var requestReceived = false;
        string? receivedRequest = null;
        
        pipeServer.RequestReceived += async (sender, args) =>
        {
            requestReceived = true;
            receivedRequest = args.RequestJson;
            await args.SendResponseAsync("{\"Success\":true}");
        };
        
        // Act
        await pipeServer.StartAsync();
        
        // クライアントから接続
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(5000);
        
        var request = "{\"ProcessId\":1234,\"TagName\":\"test\"}";
        var requestBytes = Encoding.UTF8.GetBytes(request);
        await client.WriteAsync(requestBytes);
        
        var responseBuffer = new byte[1024];
        var bytesRead = await client.ReadAsync(responseBuffer);
        var response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
        
        // Assert
        Assert.IsTrue(requestReceived);
        Assert.AreEqual(request, receivedRequest);
        Assert.AreEqual("{\"Success\":true}", response);
        
        // Cleanup
        await pipeServer.StopAsync();
    }
}
```

## 5. パフォーマンステスト

### 5.1 負荷テスト

```csharp
[TestFixture]
[Category("Performance")]
public class PerformanceTests
{
    [Test]
    public async Task EventStorage_HighVolumeEvents_MaintainsPerformance()
    {
        // Arrange
        var storage = new EventStorage(_config);
        var stopwatch = Stopwatch.StartNew();
        const int eventCount = 10000;
        
        // Act
        var tasks = Enumerable.Range(0, eventCount)
            .Select(i => Task.Run(async () =>
            {
                var eventData = new FileEventData
                {
                    TagName = "perf-test",
                    FilePath = $@"C:\test{i}.txt",
                    Timestamp = DateTime.UtcNow,
                    ProcessId = 1234
                };
                await storage.StoreEventAsync("perf-test", eventData);
            }));
        
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert
        var events = await storage.GetEventsAsync("perf-test");
        Assert.LessOrEqual(stopwatch.ElapsedMilliseconds, 5000); // 5秒以内
        TestContext.WriteLine($"Processed {eventCount} events in {stopwatch.ElapsedMilliseconds}ms");
    }
    
    [Test]
    public async Task WatchTargetManager_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var manager = new WatchTargetManager(TestMockFactory.CreateProcessValidator().Object);
        const int concurrentOperations = 100;
        
        // Act
        var addTasks = Enumerable.Range(1000, concurrentOperations)
            .Select(pid => Task.Run(() => manager.AddTargetAsync(pid, $"tag-{pid}")));
            
        var checkTasks = Enumerable.Range(1000, concurrentOperations)
            .Select(pid => Task.Run(() => manager.IsWatchedProcess(pid)));
        
        await Task.WhenAll(addTasks.Concat(checkTasks));
        
        // Assert
        var targets = manager.GetWatchTargets();
        Assert.AreEqual(concurrentOperations, targets.Count);
    }
}
```

## 6. テスト実行戦略

### 6.1 CI/CD パイプライン

```yaml
# テスト実行段階
test-stages:
  - name: unit-tests
    command: dotnet test --filter Category!=RequiresAdmin
    environment: any
    
  - name: integration-tests  
    command: dotnet test --filter Category=Integration
    environment: windows
    
  - name: system-tests
    command: dotnet test --filter Category=RequiresAdmin
    environment: windows-admin
    requirements:
      - elevated-privileges
      - windows-server-2019+
```

### 6.2 テストデータ管理

```csharp
public static class TestDataBuilder
{
    public static RawEventData FileCreateEvent(int processId = 1234, string filePath = @"C:\test.txt")
    {
        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-FileIO", 
            "FileIo/Create",
            processId,
            5678,
            Guid.NewGuid(),
            Guid.Empty,
            new Dictionary<string, object> { { "FilePath", filePath } }
        );
    }
    
    public static ProcessInfo TestProcess(int pid = 1234, string name = "test.exe")
    {
        return new ProcessInfo(pid, name, @"C:\test\test.exe", DateTime.UtcNow, null);
    }
}
```

この戦略により、Windows固有のAPIに依存せずに大部分のロジックをテストでき、品質を保ちながら開発効率を向上させることができます。