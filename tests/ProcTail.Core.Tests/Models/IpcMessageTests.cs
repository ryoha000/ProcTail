using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ProcTail.Core.Models;

namespace ProcTail.Core.Tests.Models;

[TestFixture]
[Category("Unit")]
public class IpcMessageTests
{
    [Test]
    public void RemoveWatchTargetRequest_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new RemoveWatchTargetRequest("test-tag");

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<RemoveWatchTargetRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TagName.Should().Be("test-tag");
    }

    [Test]
    public void RemoveWatchTargetResponse_Success_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new RemoveWatchTargetResponse
        {
            Success = true,
            ErrorMessage = ""
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoveWatchTargetResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.ErrorMessage.Should().BeEmpty();
    }

    [Test]
    public void RemoveWatchTargetResponse_Failure_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new RemoveWatchTargetResponse
        {
            Success = false,
            ErrorMessage = "Tag not found"
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoveWatchTargetResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeFalse();
        deserialized.ErrorMessage.Should().Be("Tag not found");
    }

    [Test]
    public void GetWatchTargetsRequest_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new GetWatchTargetsRequest();

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<GetWatchTargetsRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
    }

    [Test]
    public void GetWatchTargetsResponse_WithData_ShouldSerializeCorrectly()
    {
        // Arrange
        var watchTargets = new List<WatchTargetInfo>
        {
            new WatchTargetInfo(1234, "notepad", @"C:\Windows\notepad.exe", DateTime.UtcNow, "test-tag1"),
            new WatchTargetInfo(5678, "calc", @"C:\Windows\calc.exe", DateTime.UtcNow, "test-tag2")
        };
        var response = new GetWatchTargetsResponse(watchTargets)
        {
            Success = true
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<GetWatchTargetsResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.WatchTargets.Should().HaveCount(2);
        
        var target1 = deserialized.WatchTargets[0];
        target1.ProcessId.Should().Be(1234);
        target1.ProcessName.Should().Be("notepad");
        target1.TagName.Should().Be("test-tag1");
        
        var target2 = deserialized.WatchTargets[1];
        target2.ProcessId.Should().Be(5678);
        target2.ProcessName.Should().Be("calc");
        target2.TagName.Should().Be("test-tag2");
    }

    [Test]
    public void GetWatchTargetsResponse_Empty_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new GetWatchTargetsResponse(new List<WatchTargetInfo>())
        {
            Success = true
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<GetWatchTargetsResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.WatchTargets.Should().BeEmpty();
    }

    [Test]
    public void GetWatchTargetsResponse_DefaultConstructor_ShouldHaveEmptyList()
    {
        // Arrange & Act
        var response = new GetWatchTargetsResponse();

        // Assert
        response.WatchTargets.Should().NotBeNull();
        response.WatchTargets.Should().BeEmpty();
        response.Success.Should().BeTrue(); // BaseResponseのデフォルト
    }

    [Test]
    public void WatchTargetInfo_ShouldContainAllRequiredProperties()
    {
        // Arrange
        var processId = 1234;
        var processName = "test-process";
        var executablePath = @"C:\test\test.exe";
        var startTime = DateTime.UtcNow;
        var tagName = "test-tag";

        // Act
        var watchTargetInfo = new WatchTargetInfo(processId, processName, executablePath, startTime, tagName);

        // Assert
        watchTargetInfo.ProcessId.Should().Be(processId);
        watchTargetInfo.ProcessName.Should().Be(processName);
        watchTargetInfo.ExecutablePath.Should().Be(executablePath);
        watchTargetInfo.StartTime.Should().Be(startTime);
        watchTargetInfo.TagName.Should().Be(tagName);
    }

    [Test]
    public void WatchTargetInfo_JsonSerialization_ShouldPreserveAllProperties()
    {
        // Arrange
        var original = new WatchTargetInfo(
            1234, 
            "test-process", 
            @"C:\test\test.exe", 
            DateTime.UtcNow, 
            "test-tag"
        );

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<WatchTargetInfo>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ProcessId.Should().Be(original.ProcessId);
        deserialized.ProcessName.Should().Be(original.ProcessName);
        deserialized.ExecutablePath.Should().Be(original.ExecutablePath);
        deserialized.StartTime.Should().BeCloseTo(original.StartTime, TimeSpan.FromMilliseconds(1));
        deserialized.TagName.Should().Be(original.TagName);
    }
}