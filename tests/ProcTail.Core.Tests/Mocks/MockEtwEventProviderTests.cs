using FluentAssertions;
using NUnit.Framework;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Mocks.Etw;

namespace ProcTail.Core.Tests.Mocks;

[TestFixture]
[Category("Unit")]
public class MockEtwEventProviderTests
{
    private MockEtwEventProvider _provider = null!;
    private MockEtwConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        _config = new MockEtwConfiguration
        {
            EventGenerationInterval = TimeSpan.FromMilliseconds(10),
            FileEventProbability = 0.8,
            ProcessEventProbability = 0.15,
            GenericEventProbability = 0.05,
            EnableRealisticTimings = false,
            SimulatedProcessIds = new[] { 1111, 2222 }
        };
        _provider = new MockEtwEventProvider(_config);
    }

    [TearDown]
    public void TearDown()
    {
        _provider?.Dispose();
    }

    [Test]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        using var provider = new MockEtwEventProvider();

        // Assert
        provider.Should().NotBeNull();
        provider.IsMonitoring.Should().BeFalse();
    }

    [Test]
    public async Task StartMonitoringAsync_ShouldSetIsMonitoringToTrue()
    {
        // Act
        await _provider.StartMonitoringAsync();

        // Assert
        _provider.IsMonitoring.Should().BeTrue();
    }

    [Test]
    public async Task StopMonitoringAsync_ShouldSetIsMonitoringToFalse()
    {
        // Arrange
        await _provider.StartMonitoringAsync();

        // Act
        await _provider.StopMonitoringAsync();

        // Assert
        _provider.IsMonitoring.Should().BeFalse();
    }

    [Test]
    public void TriggerEvent_WhenMonitoring_ShouldRaiseEvent()
    {
        // Arrange
        RawEventData? receivedEvent = null;
        _provider.EventReceived += (sender, eventData) => receivedEvent = eventData;
        
        var testEvent = new RawEventData(
            DateTime.UtcNow,
            "Test-Provider",
            "Test/Event",
            1234,
            5678,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Dictionary<string, object> { { "test", "value" } }
        );

        // Act
        _ = _provider.StartMonitoringAsync();
        _provider.TriggerEvent(testEvent);

        // Assert
        receivedEvent.Should().NotBeNull();
        receivedEvent.Should().Be(testEvent);
    }

    [Test]
    public void TriggerEvent_WhenNotMonitoring_ShouldStillRaiseEvent()
    {
        // Arrange
        var eventRaised = false;
        _provider.EventReceived += (sender, eventData) => eventRaised = true;
        
        var testEvent = new RawEventData(
            DateTime.UtcNow,
            "Test-Provider",
            "Test/Event",
            1234,
            5678,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Dictionary<string, object> { { "test", "value" } }
        );

        // Act
        _provider.TriggerEvent(testEvent);

        // Assert
        eventRaised.Should().BeTrue("手動トリガーは監視状態に関係なく常に実行される");
    }

    [Test]
    public void TriggerFileEvent_ShouldGenerateFileEvent()
    {
        // Arrange
        RawEventData? receivedEvent = null;
        bool targetEventReceived = false;
        _provider.EventReceived += (sender, eventData) => 
        {
            // 期待するProcessIdとイベントタイプのイベントのみをキャプチャ
            if (eventData.ProcessId == 1234 && eventData.EventName == "FileIo/Create")
            {
                receivedEvent = eventData;
                targetEventReceived = true;
            }
        };

        // Act
        _ = _provider.StartMonitoringAsync();
        _provider.TriggerFileEvent(1234, @"C:\test.txt", "Create");

        // バックグラウンドイベントの影響を回避するため少し待機
        Thread.Sleep(50);

        // Assert
        targetEventReceived.Should().BeTrue("期待するProcessIdとイベントタイプのイベントが受信されるべき");
        receivedEvent.Should().NotBeNull();
        receivedEvent!.ProviderName.Should().Be("Microsoft-Windows-Kernel-FileIO");
        receivedEvent.EventName.Should().Be("FileIo/Create");
        receivedEvent.ProcessId.Should().Be(1234);
        receivedEvent.Payload.Should().ContainKey("FileName");
        receivedEvent.Payload["FileName"].Should().Be(@"C:\test.txt");
    }

    [Test]
    public void TriggerProcessEvent_ShouldGenerateProcessEvent()
    {
        // Arrange
        RawEventData? receivedEvent = null;
        bool targetEventReceived = false;
        _provider.EventReceived += (sender, eventData) => 
        {
            // 期待するProcessIdのイベントのみをキャプチャ
            if (eventData.ProcessId == 1234 && eventData.EventName == "Process/Start")
            {
                receivedEvent = eventData;
                targetEventReceived = true;
            }
        };

        // Act
        _ = _provider.StartMonitoringAsync();
        _provider.TriggerProcessEvent(1234, MockEventGenerator.ProcessEventType.Start);

        // バックグラウンドイベントの影響を回避するため少し待機
        Thread.Sleep(50);

        // Assert
        targetEventReceived.Should().BeTrue("期待するProcessIdのイベントが受信されるべき");
        receivedEvent.Should().NotBeNull();
        receivedEvent!.ProviderName.Should().Be("Microsoft-Windows-Kernel-Process");
        receivedEvent.EventName.Should().Be("Process/Start");
        receivedEvent.ProcessId.Should().Be(1234);
        receivedEvent.Payload.Should().ContainKey("ProcessName");
    }

    [Test]
    public async Task BackgroundEventGeneration_ShouldGenerateEvents()
    {
        // Arrange
        var receivedEvents = new List<RawEventData>();
        _provider.EventReceived += (sender, eventData) => receivedEvents.Add(eventData);

        // Act
        await _provider.StartMonitoringAsync();
        await Task.Delay(100); // Wait for some events to be generated
        await _provider.StopMonitoringAsync();

        // Assert
        receivedEvents.Should().NotBeEmpty();
        receivedEvents.Should().AllSatisfy(e =>
        {
            e.ProviderName.Should().NotBeNullOrEmpty();
            e.EventName.Should().NotBeNullOrEmpty();
            e.ProcessId.Should().BeOneOf(_config.SimulatedProcessIds.ToArray());
        });
    }
}