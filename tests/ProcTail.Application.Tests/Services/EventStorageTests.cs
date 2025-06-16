using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using ProcTail.Application.Services;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Helpers;

namespace ProcTail.Application.Tests.Services;

[TestFixture]
[Category("Unit")]
public class EventStorageTests
{
    private Mock<ILogger<EventStorage>> _mockLogger = null!;
    private EventStorage _storage = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EventStorage>>();
        _storage = new EventStorage(_mockLogger.Object, maxEventsPerTag: 100);
    }

    [TearDown]
    public void TearDown()
    {
        _storage?.Dispose();
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        using var storage = new EventStorage(_mockLogger.Object);

        // Assert
        storage.Should().NotBeNull();
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new EventStorage(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public async Task StoreEventAsync_WithValidEvent_ShouldStoreSuccessfully()
    {
        // Arrange
        const string tagName = "test-tag";
        var eventData = TestEventFactory.CreateFileEvent(tagName: tagName);

        // Act
        await _storage.StoreEventAsync(tagName, eventData);

        // Assert
        var events = await _storage.GetEventsAsync(tagName);
        events.Should().HaveCount(1);
        events[0].Should().Be(eventData);

        var count = await _storage.GetEventCountAsync(tagName);
        count.Should().Be(1);
    }

    [Test]
    public async Task StoreEventAsync_WithMultipleEvents_ShouldMaintainOrder()
    {
        // Arrange
        const string tagName = "test-tag";
        var events = TestEventFactory.CreateTimeOrderedEvents(5, 1, tagName);

        // Act
        foreach (var eventData in events)
        {
            await _storage.StoreEventAsync(tagName, eventData);
        }

        // Assert
        var storedEvents = await _storage.GetEventsAsync(tagName);
        storedEvents.Should().HaveCount(5);
        
        // FIFOなので順序が保持される
        for (int i = 0; i < events.Count; i++)
        {
            storedEvents[i].Should().Be(events[i]);
        }
    }

    [Test]
    public async Task StoreEventAsync_WithEmptyTagName_ShouldNotStore()
    {
        // Arrange
        var eventData = TestEventFactory.CreateFileEvent();

        // Act
        await _storage.StoreEventAsync("", eventData);
        await _storage.StoreEventAsync(" ", eventData);
        await _storage.StoreEventAsync(null!, eventData);

        // Assert
        var statistics = await _storage.GetStatisticsAsync();
        statistics.TotalEvents.Should().Be(0);
        statistics.TotalTags.Should().Be(0);
    }

    [Test]
    public async Task StoreEventAsync_WithNullEvent_ShouldNotStore()
    {
        // Arrange
        const string tagName = "test-tag";

        // Act
        await _storage.StoreEventAsync(tagName, null!);

        // Assert
        var events = await _storage.GetEventsAsync(tagName);
        events.Should().BeEmpty();

        var count = await _storage.GetEventCountAsync(tagName);
        count.Should().Be(0);
    }

    [Test]
    public async Task StoreEventAsync_ExceedingMaxEvents_ShouldTrimOldEvents()
    {
        // Arrange
        const string tagName = "test-tag";
        const int maxEvents = 3;
        using var limitedStorage = new EventStorage(_mockLogger.Object, maxEvents);

        // Act - 最大数を超えてイベントを追加
        for (int i = 0; i < 5; i++)
        {
            var eventData = TestEventFactory.CreateFileEvent($@"C:\file{i}.txt", tagName, 1000 + i);
            await limitedStorage.StoreEventAsync(tagName, eventData);
        }

        // Assert
        var events = await limitedStorage.GetEventsAsync(tagName);
        events.Should().HaveCount(maxEvents);
        
        // 古いイベント（file0, file1）は削除され、新しいイベント（file2, file3, file4）が残る
        events[0].ProcessId.Should().Be(1002); // file2
        events[1].ProcessId.Should().Be(1003); // file3
        events[2].ProcessId.Should().Be(1004); // file4

        var count = await limitedStorage.GetEventCountAsync(tagName);
        count.Should().Be(maxEvents);
    }

    [Test]
    public async Task GetEventsAsync_WithNonExistentTag_ShouldReturnEmpty()
    {
        // Act
        var events = await _storage.GetEventsAsync("non-existent-tag");

        // Assert
        events.Should().BeEmpty();
    }

    [Test]
    public async Task GetEventsAsync_WithEmptyTagName_ShouldReturnEmpty()
    {
        // Act
        var events1 = await _storage.GetEventsAsync("");
        var events2 = await _storage.GetEventsAsync(" ");
        var events3 = await _storage.GetEventsAsync(null!);

        // Assert
        events1.Should().BeEmpty();
        events2.Should().BeEmpty();
        events3.Should().BeEmpty();
    }

    [Test]
    public async Task ClearEventsAsync_WithExistingEvents_ShouldRemoveAllEvents()
    {
        // Arrange
        const string tagName = "test-tag";
        var events = TestEventFactory.CreateMultipleEvents(5, tagName);
        
        foreach (var eventData in events)
        {
            await _storage.StoreEventAsync(tagName, eventData);
        }

        // Act
        await _storage.ClearEventsAsync(tagName);

        // Assert
        var remainingEvents = await _storage.GetEventsAsync(tagName);
        remainingEvents.Should().BeEmpty();

        var count = await _storage.GetEventCountAsync(tagName);
        count.Should().Be(0);
    }

    [Test]
    public async Task ClearEventsAsync_WithNonExistentTag_ShouldNotThrow()
    {
        // Act & Assert
        await _storage.ClearEventsAsync("non-existent-tag");
        
        // 例外が発生しないことを確認
        var statistics = await _storage.GetStatisticsAsync();
        statistics.Should().NotBeNull();
    }

    [Test]
    public async Task GetEventCountAsync_WithExistingTag_ShouldReturnCorrectCount()
    {
        // Arrange
        const string tagName = "test-tag";
        var events = TestEventFactory.CreateMultipleEvents(7, tagName);
        
        foreach (var eventData in events)
        {
            await _storage.StoreEventAsync(tagName, eventData);
        }

        // Act
        var count = await _storage.GetEventCountAsync(tagName);

        // Assert
        count.Should().Be(7);
    }

    [Test]
    public async Task GetEventCountAsync_WithNonExistentTag_ShouldReturnZero()
    {
        // Act
        var count = await _storage.GetEventCountAsync("non-existent-tag");

        // Assert
        count.Should().Be(0);
    }

    [Test]
    public async Task GetStatisticsAsync_WithMultipleTags_ShouldReturnCorrectStatistics()
    {
        // Arrange
        const string tag1 = "tag1";
        const string tag2 = "tag2";
        
        // tag1に3個、tag2に5個のイベントを追加
        for (int i = 0; i < 3; i++)
        {
            await _storage.StoreEventAsync(tag1, TestEventFactory.CreateFileEvent(tagName: tag1));
        }
        
        for (int i = 0; i < 5; i++)
        {
            await _storage.StoreEventAsync(tag2, TestEventFactory.CreateFileEvent(tagName: tag2));
        }

        // Act
        var statistics = await _storage.GetStatisticsAsync();

        // Assert
        statistics.TotalTags.Should().Be(2);
        statistics.TotalEvents.Should().Be(8);
        statistics.EventCountByTag[tag1].Should().Be(3);
        statistics.EventCountByTag[tag2].Should().Be(5);
        statistics.EstimatedMemoryUsage.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GetAllTagsAsync_WithMultipleTags_ShouldReturnAllTags()
    {
        // Arrange
        var tags = new[] { "tag1", "tag2", "tag3" };
        
        foreach (var tag in tags)
        {
            await _storage.StoreEventAsync(tag, TestEventFactory.CreateFileEvent(tagName: tag));
        }

        // Act
        var allTags = await _storage.GetAllTagsAsync();

        // Assert
        allTags.Should().HaveCount(3);
        allTags.Should().Contain(tags);
    }

    [Test]
    public async Task GetEventsByTimeRangeAsync_WithValidRange_ShouldReturnFilteredEvents()
    {
        // Arrange
        const string tagName = "test-tag";
        var baseTime = DateTime.UtcNow;
        
        // 異なる時刻のイベントを作成
        var events = new[]
        {
            TestEventFactory.CreateFileEvent(@"C:\file1.txt", tagName, 1001),
            TestEventFactory.CreateFileEvent(@"C:\file2.txt", tagName, 1002),
            TestEventFactory.CreateFileEvent(@"C:\file3.txt", tagName, 1003)
        };

        // タイムスタンプを手動で設定
        events[0] = events[0] with { Timestamp = baseTime.AddMinutes(-10) };
        events[1] = events[1] with { Timestamp = baseTime.AddMinutes(-5) };
        events[2] = events[2] with { Timestamp = baseTime.AddMinutes(-1) };

        foreach (var eventData in events)
        {
            await _storage.StoreEventAsync(tagName, eventData);
        }

        // Act - 最近7分間のイベントを取得
        var filteredEvents = await _storage.GetEventsByTimeRangeAsync(
            tagName, 
            baseTime.AddMinutes(-7), 
            baseTime.AddMinutes(1)
        );

        // Assert
        filteredEvents.Should().HaveCount(2); // file2とfile3のみ
        filteredEvents.Should().Contain(e => e.ProcessId == 1002);
        filteredEvents.Should().Contain(e => e.ProcessId == 1003);
        filteredEvents.Should().NotContain(e => e.ProcessId == 1001);
    }

    [Test]
    public async Task GetLatestEventsAsync_WithValidCount_ShouldReturnLatestEvents()
    {
        // Arrange
        const string tagName = "test-tag";
        var baseTime = DateTime.UtcNow;
        
        var events = new[]
        {
            TestEventFactory.CreateFileEvent(@"C:\file1.txt", tagName, 1001),
            TestEventFactory.CreateFileEvent(@"C:\file2.txt", tagName, 1002),
            TestEventFactory.CreateFileEvent(@"C:\file3.txt", tagName, 1003),
            TestEventFactory.CreateFileEvent(@"C:\file4.txt", tagName, 1004)
        };

        // タイムスタンプを設定（昇順）
        for (int i = 0; i < events.Length; i++)
        {
            events[i] = events[i] with { Timestamp = baseTime.AddMinutes(i) };
            await _storage.StoreEventAsync(tagName, events[i]);
        }

        // Act - 最新の2件を取得
        var latestEvents = await _storage.GetLatestEventsAsync(tagName, 2);

        // Assert
        latestEvents.Should().HaveCount(2);
        latestEvents[0].ProcessId.Should().Be(1004); // 最新
        latestEvents[1].ProcessId.Should().Be(1003); // 2番目に新しい
    }

    [Test]
    public async Task GetLatestEventsAsync_WithZeroCount_ShouldReturnEmpty()
    {
        // Arrange
        const string tagName = "test-tag";
        await _storage.StoreEventAsync(tagName, TestEventFactory.CreateFileEvent(tagName: tagName));

        // Act
        var events = await _storage.GetLatestEventsAsync(tagName, 0);

        // Assert
        events.Should().BeEmpty();
    }

    [Test]
    public async Task MultipleTagsOperations_ShouldWorkIndependently()
    {
        // Arrange
        const string tag1 = "tag1";
        const string tag2 = "tag2";
        
        var events1 = TestEventFactory.CreateMultipleEvents(3, tag1);
        var events2 = TestEventFactory.CreateMultipleEvents(5, tag2);

        // Act
        foreach (var eventData in events1)
        {
            await _storage.StoreEventAsync(tag1, eventData);
        }
        
        foreach (var eventData in events2)
        {
            await _storage.StoreEventAsync(tag2, eventData);
        }

        // Assert
        var tag1Events = await _storage.GetEventsAsync(tag1);
        var tag2Events = await _storage.GetEventsAsync(tag2);
        
        tag1Events.Should().HaveCount(3);
        tag2Events.Should().HaveCount(5);
        
        tag1Events.Should().AllSatisfy(e => e.TagName.Should().Be(tag1));
        tag2Events.Should().AllSatisfy(e => e.TagName.Should().Be(tag2));

        // tag1をクリアしてもtag2は影響を受けない
        await _storage.ClearEventsAsync(tag1);
        
        tag1Events = await _storage.GetEventsAsync(tag1);
        tag2Events = await _storage.GetEventsAsync(tag2);
        
        tag1Events.Should().BeEmpty();
        tag2Events.Should().HaveCount(5);
    }
}