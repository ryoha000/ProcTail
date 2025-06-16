using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Mocks.Ipc;

namespace ProcTail.Core.Tests.Mocks;

[TestFixture]
[Category("Unit")]
public class MockNamedPipeServerTests
{
    private MockNamedPipeServer _server = null!;
    private MockNamedPipeConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        _config = new MockNamedPipeConfiguration
        {
            PipeName = "test-pipe",
            ResponseDelay = TimeSpan.Zero,
            SimulatedEventCount = 10,
            DefaultWatchTargets = new List<string> { @"C:\test" }
        };
        _server = new MockNamedPipeServer(_config);
    }

    [TearDown]
    public void TearDown()
    {
        _server?.Dispose();
    }

    [Test]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        using var server = new MockNamedPipeServer();

        // Assert
        server.Should().NotBeNull();
        server.IsRunning.Should().BeFalse();
        server.PipeName.Should().Be("test-proctail-pipe");
    }

    [Test]
    public async Task StartAsync_ShouldSetIsRunningToTrue()
    {
        // Act
        await _server.StartAsync();

        // Assert
        _server.IsRunning.Should().BeTrue();
    }

    [Test]
    public async Task StopAsync_ShouldSetIsRunningToFalse()
    {
        // Arrange
        await _server.StartAsync();

        // Act
        await _server.StopAsync();

        // Assert
        _server.IsRunning.Should().BeFalse();
    }

    [Test]
    public async Task ProcessMessageAsync_AddWatchTarget_ShouldReturnSuccessResponse()
    {
        // Arrange
        var request = new
        {
            RequestType = "AddWatchTarget",
            TargetPath = @"C:\test",
            TagName = "test-tag"
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = await _server.ProcessMessageAsync(requestJson);
        var response = JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.ErrorMessage.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessMessageAsync_AddWatchTarget_WithEmptyPath_ShouldReturnErrorResponse()
    {
        // Arrange
        var request = new
        {
            RequestType = "AddWatchTarget",
            TargetPath = "",
            TagName = "test-tag"
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = await _server.ProcessMessageAsync(requestJson);
        var response = JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("Invalid target path");
    }

    [Test]
    public async Task ProcessMessageAsync_GetRecordedEvents_ShouldReturnEvents()
    {
        // Arrange
        var request = new
        {
            RequestType = "GetRecordedEvents",
            TagName = "test-tag",
            MaxCount = 5
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = await _server.ProcessMessageAsync(requestJson);
        var response = JsonSerializer.Deserialize<GetRecordedEventsResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.Events.Should().NotBeEmpty();
        response.Events.Should().HaveCount(5);
    }

    [Test]
    public async Task ProcessMessageAsync_GetStatus_ShouldReturnStatusInfo()
    {
        // Arrange
        var request = new
        {
            RequestType = "GetStatus"
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = await _server.ProcessMessageAsync(requestJson);
        var response = JsonSerializer.Deserialize<JsonElement>(responseJson);

        // Assert
        response.GetProperty("Success").GetBoolean().Should().BeTrue();
        response.GetProperty("IsMonitoring").GetBoolean().Should().BeTrue();
        response.GetProperty("ActiveWatchTargets").GetInt32().Should().Be(1);
        response.GetProperty("TotalEventsRecorded").GetInt32().Should().Be(10);
    }

    [Test]
    public async Task ProcessMessageAsync_UnknownRequestType_ShouldReturnError()
    {
        // Arrange
        var request = new
        {
            RequestType = "UnknownRequest"
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = await _server.ProcessMessageAsync(requestJson);
        var response = JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("Unknown request type");
    }

    [Test]
    public void SetResponse_ShouldOverrideDefaultResponse()
    {
        // Arrange
        var customResponse = new AddWatchTargetResponse
        {
            Success = false,
            ErrorMessage = "Custom error"
        };
        _server.SetResponse("AddWatchTarget", customResponse);

        var request = new
        {
            RequestType = "AddWatchTarget",
            TargetPath = @"C:\test",
            TagName = "test-tag"
        };
        var requestJson = JsonSerializer.Serialize(request);

        // Act
        var responseJson = _server.ProcessMessageAsync(requestJson).Result;
        var response = JsonSerializer.Deserialize<AddWatchTargetResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("Custom error");
    }

    [Test]
    public void MessageLog_ShouldTrackProcessedMessages()
    {
        // Arrange
        var request1 = JsonSerializer.Serialize(new { RequestType = "GetStatus" });
        var request2 = JsonSerializer.Serialize(new { RequestType = "AddWatchTarget", TargetPath = @"C:\test", TagName = "test" });

        // Act
        _ = _server.ProcessMessageAsync(request1);
        _ = _server.ProcessMessageAsync(request2);

        // Assert
        var messageLog = _server.GetMessageLog();
        messageLog.Should().HaveCount(2);
        messageLog[0].Should().Contain("GetStatus");
        messageLog[1].Should().Contain("AddWatchTarget");
    }

    [Test]
    public void ClearMessageLog_ShouldEmptyMessageLog()
    {
        // Arrange
        var request = JsonSerializer.Serialize(new { RequestType = "GetStatus" });
        _ = _server.ProcessMessageAsync(request);

        // Act
        _server.ClearMessageLog();

        // Assert
        _server.GetMessageLog().Should().BeEmpty();
    }
}