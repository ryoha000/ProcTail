using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Application.Services;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Helpers;
using ProcTail.Testing.Common.Mocks.Etw;
using ProcTail.Testing.Common.Mocks.Ipc;

namespace ProcTail.Integration.Tests;

[TestFixture]
[Category("Integration")]
public class EndToEndTests
{
    private IServiceProvider _serviceProvider = null!;
    private IWatchTargetManager _watchTargetManager = null!;
    private IEventProcessor _eventProcessor = null!;
    private IEventStorage _eventStorage = null!;
    private MockEtwEventProvider _mockEtwProvider = null!;
    private MockNamedPipeServer _mockPipeServer = null!;

    [SetUp]
    public void Setup()
    {
        // テスト用のサービスコレクションを作成
        var services = MockServiceFactory.CreateTestServices(
            etwConfig =>
            {
                etwConfig.EventGenerationInterval = TimeSpan.FromMilliseconds(50);
                etwConfig.FileEventProbability = 0.6;
                etwConfig.ProcessEventProbability = 0.3;
                etwConfig.GenericEventProbability = 0.1;
                etwConfig.EnableRealisticTimings = true;
                etwConfig.SimulatedProcessIds = new[] { 1001, 1002, 1003 };
            },
            pipeConfig =>
            {
                pipeConfig.PipeName = "integration-test-pipe";
                pipeConfig.ResponseDelay = TimeSpan.FromMilliseconds(5);
            }
        );
        
        // ビジネスロジックサービスを追加
        services.AddSingleton<IWatchTargetManager, WatchTargetManager>();
        services.AddSingleton<IEventProcessor, EventProcessor>();
        services.AddSingleton<IEventStorage>(provider => 
            new EventStorage(provider.GetRequiredService<ILogger<EventStorage>>(), 100));
        
        _serviceProvider = services.BuildServiceProvider();
        
        // サービスを取得
        _watchTargetManager = _serviceProvider.GetRequiredService<IWatchTargetManager>();
        _eventProcessor = _serviceProvider.GetRequiredService<IEventProcessor>();
        _eventStorage = _serviceProvider.GetRequiredService<IEventStorage>();
        _mockEtwProvider = (MockEtwEventProvider)_serviceProvider.GetRequiredService<IEtwEventProvider>();
        _mockPipeServer = (MockNamedPipeServer)_serviceProvider.GetRequiredService<INamedPipeServer>();
    }

    [TearDown]
    public void TearDown()
    {
        _mockEtwProvider?.StopMonitoringAsync();
        _mockPipeServer?.StopAsync();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task CompleteWorkflow_AddTarget_ProcessEvents_RetrieveData_ShouldWork()
    {
        // Arrange
        const string tagName = "integration-test";
        const int processId = 1234;
        var receivedEvents = new List<BaseEventData>();

        // ETWイベント受信時の処理を設定
        _mockEtwProvider.EventReceived += async (sender, rawEvent) =>
        {
            var result = await _eventProcessor.ProcessEventAsync(rawEvent);
            if (result.Success && result.EventData != null)
            {
                await _eventStorage.StoreEventAsync(tagName, result.EventData);
                receivedEvents.Add(result.EventData);
            }
        };

        // Act & Assert

        // 1. 監視対象を追加
        var addResult = await _watchTargetManager.AddTargetAsync(processId, tagName);
        addResult.Should().BeTrue();
        _watchTargetManager.IsWatchedProcess(processId).Should().BeTrue();
        _watchTargetManager.GetTagForProcess(processId).Should().Be(tagName);

        // 2. ETW監視を開始
        await _mockEtwProvider.StartMonitoringAsync();
        _mockEtwProvider.IsMonitoring.Should().BeTrue();

        // 3. テストイベントを生成・送信
        // Process/Endイベントは子プロセスの監視確認後に送信するため、ここでは送信しない
        var testEvents = new[]
        {
            CreateTestFileEvent(processId, @"C:\test1.txt"),
            CreateTestFileEvent(processId, @"C:\test2.txt"),
            CreateTestProcessStartEvent(processId, 5678, "child.exe")
        };

        foreach (var testEvent in testEvents)
        {
            _mockEtwProvider.TriggerEvent(testEvent);
        }

        // イベント処理の完了を待機（子プロセスの追加は非同期で実行されるため、より長い待機時間が必要）
        await Task.Delay(500);

        // 4. 記録されたイベントを検証
        receivedEvents.Should().HaveCountGreaterThan(0);
        
        var storedEvents = await _eventStorage.GetEventsAsync(tagName);
        storedEvents.Should().NotBeEmpty();
        storedEvents.Should().AllSatisfy(e => e.TagName.Should().Be(tagName));

        // ファイルイベントの検証
        var fileEvents = storedEvents.OfType<FileEventData>().ToList();
        fileEvents.Should().HaveCount(2);
        fileEvents.Should().Contain(e => e.FilePath.Contains("test1.txt"));
        fileEvents.Should().Contain(e => e.FilePath.Contains("test2.txt"));

        // プロセス開始イベントの検証
        var processStartEvents = storedEvents.OfType<ProcessStartEventData>().ToList();
        processStartEvents.Should().HaveCount(1);
        processStartEvents[0].ChildProcessId.Should().Be(5678);
        processStartEvents[0].ChildProcessName.Should().Be("child.exe");

        // 子プロセスが自動的に監視対象に追加されることを検証
        _watchTargetManager.IsWatchedProcess(5678).Should().BeTrue();
        _watchTargetManager.GetTagForProcess(5678).Should().Be(tagName);

        // 5. Process/Endイベントを送信して、プロセス終了も正しく処理されることを確認
        _mockEtwProvider.TriggerEvent(CreateTestProcessEndEvent(5678));
        await Task.Delay(100);

        // プロセス終了イベントの検証
        var allEvents = await _eventStorage.GetEventsAsync(tagName);
        var processEndEvents = allEvents.OfType<ProcessEndEventData>().ToList();
        processEndEvents.Should().HaveCount(1);
        processEndEvents[0].ProcessId.Should().Be(5678);

        // プロセス終了後は監視対象から除去される
        _watchTargetManager.IsWatchedProcess(5678).Should().BeFalse();

        // 6. 統計情報を検証
        var statistics = await _eventStorage.GetStatisticsAsync();
        statistics.TotalTags.Should().BeGreaterThan(0);
        statistics.TotalEvents.Should().BeGreaterThan(0);
        statistics.EventCountByTag.Should().ContainKey(tagName);

        // 7. ETW監視を停止
        await _mockEtwProvider.StopMonitoringAsync();
        _mockEtwProvider.IsMonitoring.Should().BeFalse();
    }

    [Test]
    public async Task IpcWorkflow_AddWatchTarget_GetEvents_ShouldWork()
    {
        // Arrange
        const string tagName = "ipc-test";
        const int processId = 2345;

        // バックグラウンドイベント生成を停止
        await _mockEtwProvider.StopMonitoringAsync();

        // 監視対象を追加
        await _watchTargetManager.AddTargetAsync(processId, tagName);

        // テストイベントを保存
        var testEvents = TestEventFactory.CreateMultipleEvents(5, tagName);
        foreach (var eventData in testEvents)
        {
            await _eventStorage.StoreEventAsync(tagName, eventData);
        }

        // Act & Assert

        // 1. Named Pipeサーバーを開始
        await _mockPipeServer.StartAsync();
        _mockPipeServer.IsRunning.Should().BeTrue();

        // 2. AddWatchTargetリクエストを送信
        var addWatchTargetRequest = new
        {
            RequestType = "AddWatchTarget",
            TargetPath = @"C:\test\process.exe",
            TagName = "new-tag"
        };
        
        var addResponse = await _mockPipeServer.ProcessMessageAsync(
            System.Text.Json.JsonSerializer.Serialize(addWatchTargetRequest));
        
        var addResponseObj = System.Text.Json.JsonSerializer.Deserialize<AddWatchTargetResponse>(addResponse);
        addResponseObj.Should().NotBeNull();
        addResponseObj!.Success.Should().BeTrue();

        // 3. GetRecordedEventsリクエストを送信
        var getEventsRequest = new
        {
            RequestType = "GetRecordedEvents",
            TagName = tagName,
            MaxCount = 10
        };
        
        var eventsResponse = await _mockPipeServer.ProcessMessageAsync(
            System.Text.Json.JsonSerializer.Serialize(getEventsRequest));
        
        var eventsResponseObj = System.Text.Json.JsonSerializer.Deserialize<GetRecordedEventsResponse>(eventsResponse);
        eventsResponseObj.Should().NotBeNull();
        eventsResponseObj!.Success.Should().BeTrue();
        eventsResponseObj.Events.Should().HaveCount(5);

        // 4. GetStatusリクエストを送信
        var statusRequest = new
        {
            RequestType = "GetStatus"
        };
        
        var statusResponse = await _mockPipeServer.ProcessMessageAsync(
            System.Text.Json.JsonSerializer.Serialize(statusRequest));
        
        var statusResponseObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(statusResponse);
        statusResponseObj.GetProperty("Success").GetBoolean().Should().BeTrue();
        statusResponseObj.GetProperty("IsMonitoring").GetBoolean().Should().BeTrue();

        // 5. Named Pipeサーバーを停止
        await _mockPipeServer.StopAsync();
        _mockPipeServer.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task EventFiltering_OnlyWatchedProcesses_ShouldBeProcessed()
    {
        // Arrange
        const string tagName = "filter-test";
        const int watchedProcessId = 3456;
        const int unwatchedProcessId = 7890;

        // 1つのプロセスのみを監視対象に追加
        await _watchTargetManager.AddTargetAsync(watchedProcessId, tagName);

        var processedEvents = new List<BaseEventData>();
        _mockEtwProvider.EventReceived += async (sender, rawEvent) =>
        {
            var result = await _eventProcessor.ProcessEventAsync(rawEvent);
            if (result.Success && result.EventData != null)
            {
                await _eventStorage.StoreEventAsync(tagName, result.EventData);
                processedEvents.Add(result.EventData);
            }
        };

        // Act
        await _mockEtwProvider.StartMonitoringAsync();

        // 監視対象プロセスのイベントを送信
        _mockEtwProvider.TriggerFileEvent(watchedProcessId, @"C:\watched.txt");
        
        // 監視対象外プロセスのイベントを送信
        _mockEtwProvider.TriggerFileEvent(unwatchedProcessId, @"C:\unwatched.txt");

        await Task.Delay(50);

        // Assert
        processedEvents.Should().HaveCount(1);
        processedEvents[0].ProcessId.Should().Be(watchedProcessId);

        var storedEvents = await _eventStorage.GetEventsAsync(tagName);
        storedEvents.Should().HaveCount(1);
        storedEvents[0].ProcessId.Should().Be(watchedProcessId);
        
        var fileEvent = storedEvents[0] as FileEventData;
        fileEvent.Should().NotBeNull();
        fileEvent!.FilePath.Should().Contain("watched.txt");

        await _mockEtwProvider.StopMonitoringAsync();
    }

    [Test]
    public async Task ChildProcessHandling_AutomaticAddition_ShouldWork()
    {
        // Arrange
        const string tagName = "child-test";
        const int parentProcessId = 1111;
        const int childProcessId = 2222;

        // 親プロセスを監視対象に追加
        await _watchTargetManager.AddTargetAsync(parentProcessId, tagName);

        var processedEvents = new List<BaseEventData>();
        _mockEtwProvider.EventReceived += async (sender, rawEvent) =>
        {
            var result = await _eventProcessor.ProcessEventAsync(rawEvent);
            if (result.Success && result.EventData != null)
            {
                await _eventStorage.StoreEventAsync(result.EventData.TagName, result.EventData);
                processedEvents.Add(result.EventData);
            }
        };

        // Act
        await _mockEtwProvider.StartMonitoringAsync();

        // 親プロセスからプロセス開始イベントを送信（子プロセスIDを含む）
        var processStartEvent = CreateTestProcessStartEvent(parentProcessId, childProcessId, "child.exe");
        _mockEtwProvider.TriggerEvent(processStartEvent);

        await Task.Delay(100); // 子プロセス追加の完了を待機

        // 子プロセスからファイルイベントを送信
        _mockEtwProvider.TriggerFileEvent(childProcessId, @"C:\child-file.txt");

        await Task.Delay(50);

        // Assert
        // 子プロセスが自動的に監視対象に追加されている
        _watchTargetManager.IsWatchedProcess(childProcessId).Should().BeTrue();
        _watchTargetManager.GetTagForProcess(childProcessId).Should().Be(tagName);

        // プロセス開始イベントとファイルイベントの両方が処理されている
        processedEvents.Should().HaveCountGreaterOrEqualTo(2);
        
        var processStartEvents = processedEvents.OfType<ProcessStartEventData>().ToList();
        processStartEvents.Should().HaveCount(1);
        processStartEvents[0].ProcessId.Should().Be(parentProcessId);

        var fileEvents = processedEvents.OfType<FileEventData>().ToList();
        fileEvents.Should().HaveCount(1);
        fileEvents[0].ProcessId.Should().Be(childProcessId);
        fileEvents[0].TagName.Should().Be(tagName);

        await _mockEtwProvider.StopMonitoringAsync();
    }

    [Test]
    public async Task EventStorageCapacity_MaxEventsLimit_ShouldTrimOldEvents()
    {
        // Arrange
        const string tagName = "capacity-test";
        const int processId = 4567;
        const int maxEvents = 5;

        // 制限付きEventStorageを作成
        using var limitedStorage = new EventStorage(
            _serviceProvider.GetRequiredService<ILogger<EventStorage>>(), 
            maxEvents);

        await _watchTargetManager.AddTargetAsync(processId, tagName);

        // Act
        // 最大数を超えてイベントを追加
        for (int i = 0; i < 10; i++)
        {
            var eventData = TestEventFactory.CreateFileEvent($@"C:\file{i}.txt", tagName, processId);
            await limitedStorage.StoreEventAsync(tagName, eventData);
        }

        // Assert
        var events = await limitedStorage.GetEventsAsync(tagName);
        events.Should().HaveCount(maxEvents);

        var count = await limitedStorage.GetEventCountAsync(tagName);
        count.Should().Be(maxEvents);

        // 最新のイベントが保持されている（file5-file9）
        var fileEvents = events.OfType<FileEventData>().ToList();
        fileEvents.Should().AllSatisfy(e => 
        {
            var fileNumber = int.Parse(e.FilePath.Substring(e.FilePath.LastIndexOf("file") + 4, 1));
            fileNumber.Should().BeGreaterOrEqualTo(5);
        });
    }

    [Test]
    public async Task TimeRangeQuery_EventRetrieval_ShouldWork()
    {
        // Arrange
        const string tagName = "time-test";
        var baseTime = DateTime.UtcNow;

        // 異なる時刻のイベントを作成
        var events = new[]
        {
            TestEventFactory.CreateFileEvent(@"C:\old.txt", tagName, 1001),
            TestEventFactory.CreateFileEvent(@"C:\recent.txt", tagName, 1002),
            TestEventFactory.CreateFileEvent(@"C:\new.txt", tagName, 1003)
        };

        // タイムスタンプを設定
        events[0] = events[0] with { Timestamp = baseTime.AddMinutes(-30) };
        events[1] = events[1] with { Timestamp = baseTime.AddMinutes(-10) };
        events[2] = events[2] with { Timestamp = baseTime.AddMinutes(-2) };

        // Act
        foreach (var eventData in events)
        {
            await _eventStorage.StoreEventAsync(tagName, eventData);
        }

        // 最近15分間のイベントを取得
        var recentEvents = await _eventStorage.GetEventsByTimeRangeAsync(
            tagName, 
            baseTime.AddMinutes(-15), 
            baseTime.AddMinutes(1));

        // 最新2件のイベントを取得
        var latestEvents = await _eventStorage.GetLatestEventsAsync(tagName, 2);

        // Assert
        recentEvents.Should().HaveCount(2); // recent.txtとnew.txt
        recentEvents.Should().AllSatisfy(e =>
        {
            var fileEvent = e as FileEventData;
            fileEvent!.FilePath.Should().NotContain("old.txt");
        });

        latestEvents.Should().HaveCount(2);
        latestEvents[0].ProcessId.Should().Be(1003); // new.txt（最新）
        latestEvents[1].ProcessId.Should().Be(1002); // recent.txt（2番目）
    }

    private static RawEventData CreateTestFileEvent(int processId, string filePath)
    {
        var payload = new Dictionary<string, object>
        {
            { "FileName", filePath },
            { "IrpPtr", "0x12345678" }
        };

        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-FileIO",
            "FileIo/Create",
            processId,
            1000,
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }

    private static RawEventData CreateTestProcessStartEvent(int parentProcessId, int childProcessId, string childProcessName)
    {
        var payload = new Dictionary<string, object>
        {
            { "ProcessId", childProcessId },
            { "ProcessName", childProcessName },
            { "CommandLine", $"{childProcessName} --test" }
        };

        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-Process",
            "Process/Start",
            parentProcessId,
            1000,
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }

    private static RawEventData CreateTestProcessEndEvent(int processId)
    {
        var payload = new Dictionary<string, object>
        {
            { "ExitStatus", 0 },
            { "HandleCount", 100 }
        };

        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-Process",
            "Process/End",
            processId,
            1000,
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }
}