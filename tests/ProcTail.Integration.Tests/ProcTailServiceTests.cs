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
public class ProcTailServiceTests
{
    private IServiceProvider _serviceProvider = null!;
    private ProcTailService _procTailService = null!;
    private MockEtwEventProvider _mockEtwProvider = null!;
    private MockNamedPipeServer _mockPipeServer = null!;

    [SetUp]
    public void Setup()
    {
        // モック環境のサービスプロバイダーを作成
        var baseServices = MockServiceFactory.CreateTestServices(
            etwConfig =>
            {
                etwConfig.EventGenerationInterval = TimeSpan.FromMilliseconds(50);
                etwConfig.FileEventProbability = 0.7;
                etwConfig.ProcessEventProbability = 0.3;
            },
            pipeConfig =>
            {
                pipeConfig.PipeName = "test-proctail-service";
                pipeConfig.ResponseDelay = TimeSpan.FromMilliseconds(5);
            }
        );

        // ビジネスロジックサービスを追加
        baseServices.AddSingleton<IWatchTargetManager, WatchTargetManager>();
        baseServices.AddSingleton<IEventProcessor, EventProcessor>();
        baseServices.AddSingleton<IEventStorage>(provider => 
            new EventStorage(provider.GetRequiredService<ILogger<EventStorage>>(), maxEventsPerTag: 100));
        baseServices.AddSingleton<ProcTailService>();

        _serviceProvider = baseServices.BuildServiceProvider();
        
        // サービスを取得
        _procTailService = _serviceProvider.GetRequiredService<ProcTailService>();
        _mockEtwProvider = (MockEtwEventProvider)_serviceProvider.GetRequiredService<IEtwEventProvider>();
        _mockPipeServer = (MockNamedPipeServer)_serviceProvider.GetRequiredService<INamedPipeServer>();
    }

    [TearDown]
    public void TearDown()
    {
        _procTailService?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task StartAsync_ShouldInitializeAllComponents()
    {
        // Act
        await _procTailService.StartAsync();

        // Assert
        _procTailService.IsRunning.Should().BeTrue();
        _mockEtwProvider.IsMonitoring.Should().BeTrue();
        _mockPipeServer.IsRunning.Should().BeTrue();
    }

    [Test]
    public async Task StopAsync_ShouldShutdownAllComponents()
    {
        // Arrange
        await _procTailService.StartAsync();

        // Act
        await _procTailService.StopAsync();

        // Assert
        _procTailService.IsRunning.Should().BeFalse();
        _mockEtwProvider.IsMonitoring.Should().BeFalse();
        _mockPipeServer.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task FullWorkflow_ProcessMonitoring_ShouldWorkEndToEnd()
    {
        // Arrange
        const string tagName = "service-test";
        const int processId = 1234;
        
        await _procTailService.StartAsync();

        // バックグラウンドイベント生成を停止して制御されたテストを実行
        await _mockEtwProvider.StopMonitoringAsync();

        // Act
        // 1. 監視対象を追加
        var addResult = await _procTailService.AddWatchTargetAsync(processId, tagName);
        addResult.Should().BeTrue();

        // 2. テストイベントを生成（手動トリガーは監視状態に関係なく動作）
        _mockEtwProvider.TriggerFileEvent(processId, @"C:\service-test.txt", "Create");
        _mockEtwProvider.TriggerFileEvent(processId, @"C:\service-test2.txt", "Write");
        _mockEtwProvider.TriggerProcessEvent(processId, MockEventGenerator.ProcessEventType.Start);

        // イベント処理を待機
        await Task.Delay(200);

        // 3. 記録されたイベントを取得
        var events = await _procTailService.GetRecordedEventsAsync(tagName);

        // Assert
        events.Should().NotBeEmpty();
        events.Should().AllSatisfy(e => e.TagName.Should().Be(tagName));
        events.Should().AllSatisfy(e => e.ProcessId.Should().Be(processId));

        var fileEvents = events.OfType<FileEventData>().ToList();
        fileEvents.Should().HaveCount(2);
        fileEvents.Should().Contain(e => e.FilePath.Contains("service-test.txt"));
        fileEvents.Should().Contain(e => e.FilePath.Contains("service-test2.txt"));

        var processEvents = events.OfType<ProcessStartEventData>().ToList();
        processEvents.Should().HaveCount(1);

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task IpcIntegration_AddWatchTargetRequest_ShouldWork()
    {
        // Arrange
        await _procTailService.StartAsync();

        var request = new
        {
            RequestType = "AddWatchTarget",
            ProcessId = 5678,
            TagName = "ipc-test"
        };

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(request));

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<AddWatchTargetResponse>(response);
        responseObj.Should().NotBeNull();
        responseObj!.Success.Should().BeTrue();
        responseObj.ErrorMessage.Should().BeEmpty();

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task IpcIntegration_GetRecordedEventsRequest_ShouldWork()
    {
        // Arrange
        const string tagName = "ipc-events-test";
        const int processId = 9999;

        await _procTailService.StartAsync();
        
        // バックグラウンドイベント生成を停止して制御されたテストを実行
        await _mockEtwProvider.StopMonitoringAsync();
        
        await _procTailService.AddWatchTargetAsync(processId, tagName);

        // イベントを生成（手動トリガーは監視状態に関係なく動作）
        _mockEtwProvider.TriggerFileEvent(processId, @"C:\ipc-test1.txt");
        _mockEtwProvider.TriggerFileEvent(processId, @"C:\ipc-test2.txt");
        await Task.Delay(100);

        var request = new
        {
            RequestType = "GetRecordedEvents",
            TagName = tagName,
            MaxCount = 10
        };

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(request));

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<GetRecordedEventsResponse>(response);
        responseObj.Should().NotBeNull();
        responseObj!.Success.Should().BeTrue();
        responseObj.Events.Should().HaveCount(2);
        responseObj.Events.Should().AllSatisfy(e => e.TagName.Should().Be(tagName));

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task IpcIntegration_GetStatusRequest_ShouldWork()
    {
        // Arrange
        await _procTailService.StartAsync();
        await _procTailService.AddWatchTargetAsync(1111, "status-test");

        var request = new
        {
            RequestType = "GetStatus"
        };

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(request));

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);
        responseObj.GetProperty("Success").GetBoolean().Should().BeTrue();
        responseObj.GetProperty("IsRunning").GetBoolean().Should().BeTrue();
        responseObj.GetProperty("IsEtwMonitoring").GetBoolean().Should().BeTrue();
        responseObj.GetProperty("IsPipeServerRunning").GetBoolean().Should().BeTrue();
        responseObj.GetProperty("ActiveWatchTargets").GetInt32().Should().Be(1);

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task IpcIntegration_ClearEventsRequest_ShouldWork()
    {
        // Arrange
        const string tagName = "clear-test";
        const int processId = 2222;

        await _procTailService.StartAsync();
        await _procTailService.AddWatchTargetAsync(processId, tagName);

        // イベントを生成
        _mockEtwProvider.TriggerFileEvent(processId, @"C:\clear-test.txt");
        await Task.Delay(100);

        var clearRequest = new
        {
            RequestType = "ClearEvents",
            TagName = tagName
        };

        // Act
        var clearResponse = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(clearRequest));

        // イベントが削除されたことを確認
        var events = await _procTailService.GetRecordedEventsAsync(tagName);

        // Assert
        var clearResponseObj = System.Text.Json.JsonSerializer.Deserialize<ClearEventsResponse>(clearResponse);
        clearResponseObj.Should().NotBeNull();
        clearResponseObj!.Success.Should().BeTrue();

        events.Should().BeEmpty();

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task IpcIntegration_ShutdownRequest_ShouldWork()
    {
        // Arrange
        await _procTailService.StartAsync();

        var request = new
        {
            RequestType = "Shutdown"
        };

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(request));

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<ShutdownResponse>(response);
        responseObj.Should().NotBeNull();
        responseObj!.Success.Should().BeTrue();

        // シャットダウンが実行されるまで少し待機
        await Task.Delay(200);
        _procTailService.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task ErrorHandling_InvalidIpcRequest_ShouldReturnError()
    {
        // Arrange
        await _procTailService.StartAsync();

        var invalidRequest = "{ invalid json }";

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(invalidRequest);

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<AddWatchTargetResponse>(response);
        responseObj.Should().NotBeNull();
        responseObj!.Success.Should().BeFalse();
        responseObj.ErrorMessage.Should().Contain("error");

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task ErrorHandling_UnknownRequestType_ShouldReturnError()
    {
        // Arrange
        await _procTailService.StartAsync();

        var unknownRequest = new
        {
            RequestType = "UnknownRequest",
            Data = "test"
        };

        // Act
        var response = await _mockPipeServer.TriggerRequestReceivedAsync(
            System.Text.Json.JsonSerializer.Serialize(unknownRequest));

        // Assert
        var responseObj = System.Text.Json.JsonSerializer.Deserialize<AddWatchTargetResponse>(response);
        responseObj.Should().NotBeNull();
        responseObj!.Success.Should().BeFalse();
        responseObj.ErrorMessage.Should().Contain("Unknown request type");

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task ConcurrentOperations_MultipleTargetsAndEvents_ShouldWork()
    {
        // Arrange
        await _procTailService.StartAsync();

        var tasks = new List<Task>();
        var tagNames = new[] { "concurrent1", "concurrent2", "concurrent3" };
        var processIds = new[] { 3001, 3002, 3003 };

        // Act
        // 複数の監視対象を並行して追加
        for (int i = 0; i < 3; i++)
        {
            var processId = processIds[i];
            var tagName = tagNames[i];
            
            tasks.Add(_procTailService.AddWatchTargetAsync(processId, tagName));
        }

        await Task.WhenAll(tasks);

        // 各プロセスでイベントを並行生成
        var eventTasks = new List<Task>();
        for (int i = 0; i < 3; i++)
        {
            var processId = processIds[i];
            eventTasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 5; j++)
                {
                    _mockEtwProvider.TriggerFileEvent(processId, $@"C:\concurrent{i}_{j}.txt");
                }
            }));
        }

        await Task.WhenAll(eventTasks);
        await Task.Delay(200); // イベント処理の完了を待機

        // Assert
        for (int i = 0; i < 3; i++)
        {
            var events = await _procTailService.GetRecordedEventsAsync(tagNames[i]);
            events.Should().HaveCount(5);
            events.Should().AllSatisfy(e => e.TagName.Should().Be(tagNames[i]));
            events.Should().AllSatisfy(e => e.ProcessId.Should().Be(processIds[i]));
        }

        var statistics = await _procTailService.GetStatisticsAsync();
        statistics.TotalTags.Should().Be(3);
        statistics.TotalEvents.Should().Be(15);

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task MemoryManagement_LargeNumberOfEvents_ShouldNotExceedLimits()
    {
        // Arrange
        const string tagName = "memory-test";
        const int processId = 4000;
        const int eventCount = 200; // 制限(100)を超える数

        await _procTailService.StartAsync();
        await _procTailService.AddWatchTargetAsync(processId, tagName);

        // Act
        // 制限を超える数のイベントを生成
        for (int i = 0; i < eventCount; i++)
        {
            _mockEtwProvider.TriggerFileEvent(processId, $@"C:\memory-test-{i}.txt");
        }

        await Task.Delay(300); // イベント処理の完了を待機

        // Assert
        var events = await _procTailService.GetRecordedEventsAsync(tagName);
        events.Should().HaveCountLessOrEqualTo(100); // 制限内に収まっている

        var statistics = await _procTailService.GetStatisticsAsync();
        statistics.EventCountByTag[tagName].Should().BeLessOrEqualTo(100);

        await _procTailService.StopAsync();
    }

    [Test]
    public async Task ServiceLifecycle_MultipleStartStop_ShouldWork()
    {
        // Act & Assert
        // 最初の開始・停止
        await _procTailService.StartAsync();
        _procTailService.IsRunning.Should().BeTrue();
        
        await _procTailService.StopAsync();
        _procTailService.IsRunning.Should().BeFalse();

        // 再開始・停止
        await _procTailService.StartAsync();
        _procTailService.IsRunning.Should().BeTrue();
        
        await _procTailService.StopAsync();
        _procTailService.IsRunning.Should().BeFalse();

        // 重複開始の確認
        await _procTailService.StartAsync();
        await _procTailService.StartAsync(); // 重複開始は無視される
        _procTailService.IsRunning.Should().BeTrue();
        
        await _procTailService.StopAsync();
    }
}