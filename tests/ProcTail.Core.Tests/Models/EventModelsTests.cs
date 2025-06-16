using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ProcTail.Core.Models;

namespace ProcTail.Core.Tests.Models;

[TestFixture]
[Category("Unit")]
public class EventModelsTests
{
    [Test]
    public void FileEventData_JsonSerialization_ShouldPreservePolymorphicType()
    {
        // Arrange
        var originalEvent = new FileEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = "test-tag",
            ProcessId = 1234,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-FileIO",
            EventName = "FileIo/Create",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { { "test", "value" } },
            FilePath = @"C:\test.txt"
        };

        // Act
        var json = JsonSerializer.Serialize<BaseEventData>(originalEvent);
        var deserializedEvent = JsonSerializer.Deserialize<BaseEventData>(json);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent.Should().BeOfType<FileEventData>();
        
        var fileEvent = (FileEventData)deserializedEvent!;
        fileEvent.FilePath.Should().Be(originalEvent.FilePath);
        fileEvent.TagName.Should().Be(originalEvent.TagName);
        fileEvent.ProcessId.Should().Be(originalEvent.ProcessId);
    }

    [Test]
    public void ProcessStartEventData_JsonSerialization_ShouldPreservePolymorphicType()
    {
        // Arrange
        var originalEvent = new ProcessStartEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = "test-tag",
            ProcessId = 1234,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-Process",
            EventName = "Process/Start",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { { "CommandLine", "test.exe" } },
            ChildProcessId = 9999,
            ChildProcessName = "child.exe"
        };

        // Act
        var json = JsonSerializer.Serialize<BaseEventData>(originalEvent);
        var deserializedEvent = JsonSerializer.Deserialize<BaseEventData>(json);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent.Should().BeOfType<ProcessStartEventData>();
        
        var processEvent = (ProcessStartEventData)deserializedEvent!;
        processEvent.ChildProcessId.Should().Be(originalEvent.ChildProcessId);
        processEvent.ChildProcessName.Should().Be(originalEvent.ChildProcessName);
    }

    [Test]
    public void ProcessEndEventData_JsonSerialization_ShouldPreservePolymorphicType()
    {
        // Arrange
        var originalEvent = new ProcessEndEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = "test-tag",
            ProcessId = 1234,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-Process",
            EventName = "Process/End",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { { "EndTime", DateTime.UtcNow } },
            ExitCode = 0
        };

        // Act
        var json = JsonSerializer.Serialize<BaseEventData>(originalEvent);
        var deserializedEvent = JsonSerializer.Deserialize<BaseEventData>(json);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent.Should().BeOfType<ProcessEndEventData>();
        
        var processEvent = (ProcessEndEventData)deserializedEvent!;
        processEvent.ExitCode.Should().Be(originalEvent.ExitCode);
    }

    [Test]
    public void GenericEventData_JsonSerialization_ShouldPreservePolymorphicType()
    {
        // Arrange
        var originalEvent = new GenericEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = "test-tag",
            ProcessId = 1234,
            ThreadId = 5678,
            ProviderName = "Custom-Provider",
            EventName = "Custom/Event",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { { "custom", "data" } }
        };

        // Act
        var json = JsonSerializer.Serialize<BaseEventData>(originalEvent);
        var deserializedEvent = JsonSerializer.Deserialize<BaseEventData>(json);

        // Assert
        deserializedEvent.Should().NotBeNull();
        deserializedEvent.Should().BeOfType<GenericEventData>();
        
        var genericEvent = (GenericEventData)deserializedEvent!;
        genericEvent.ProviderName.Should().Be(originalEvent.ProviderName);
        genericEvent.EventName.Should().Be(originalEvent.EventName);
    }

    [Test]
    public void RawEventData_BasicFunctionality_ShouldWork()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var payload = new Dictionary<string, object> { { "test", "value" } };

        // Act
        var rawEvent = new RawEventData(
            timestamp,
            "TestProvider",
            "TestEvent",
            1234,
            5678,
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );

        // Assert
        rawEvent.Timestamp.Should().Be(timestamp);
        rawEvent.ProviderName.Should().Be("TestProvider");
        rawEvent.EventName.Should().Be("TestEvent");
        rawEvent.ProcessId.Should().Be(1234);
        rawEvent.ThreadId.Should().Be(5678);
        rawEvent.Payload.Should().ContainKey("test");
        rawEvent.Payload["test"].Should().Be("value");
    }

    [Test]
    public void ProcessingResult_Success_ShouldContainEventData()
    {
        // Arrange
        var eventData = new GenericEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = "test",
            ProcessId = 1234,
            ThreadId = 5678,
            ProviderName = "Test",
            EventName = "Test/Event",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = new Dictionary<string, object>()
        };

        // Act
        var result = new ProcessingResult(true, eventData);

        // Assert
        result.Success.Should().BeTrue();
        result.EventData.Should().Be(eventData);
        result.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void ProcessingResult_Failure_ShouldContainErrorMessage()
    {
        // Arrange
        var errorMessage = "Processing failed";

        // Act
        var result = new ProcessingResult(false, ErrorMessage: errorMessage);

        // Assert
        result.Success.Should().BeFalse();
        result.EventData.Should().BeNull();
        result.ErrorMessage.Should().Be(errorMessage);
    }
}