using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using ProcTail.Application.Services;
using ProcTail.Core.Models;

namespace ProcTail.Application.Tests.Services;

[TestFixture]
[Category("Application")]
public class WatchTargetManagerTests
{
    private Mock<ILogger<WatchTargetManager>> _mockLogger = null!;
    private WatchTargetManager _watchTargetManager = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<WatchTargetManager>>();
        _watchTargetManager = new WatchTargetManager(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _watchTargetManager?.Dispose();
    }

    [Test]
    public async Task RemoveWatchTargetsByTagAsync_WithExistingTag_ShouldRemoveTargets()
    {
        // Arrange
        const string tagName = "test-tag";
        const int processId1 = 1234;
        const int processId2 = 5678;

        await _watchTargetManager.AddTargetAsync(processId1, tagName);
        await _watchTargetManager.AddTargetAsync(processId2, tagName);

        // Act
        var removedCount = await _watchTargetManager.RemoveWatchTargetsByTagAsync(tagName);

        // Assert
        removedCount.Should().Be(2);
        _watchTargetManager.IsWatchedProcess(processId1).Should().BeFalse();
        _watchTargetManager.IsWatchedProcess(processId2).Should().BeFalse();
        _watchTargetManager.ActiveTargetCount.Should().Be(0);
    }

    [Test]
    public async Task RemoveWatchTargetsByTagAsync_WithNonExistingTag_ShouldReturnZero()
    {
        // Arrange
        const string nonExistingTag = "non-existing-tag";

        // Act
        var removedCount = await _watchTargetManager.RemoveWatchTargetsByTagAsync(nonExistingTag);

        // Assert
        removedCount.Should().Be(0);
    }

    [Test]
    public async Task RemoveWatchTargetsByTagAsync_WithEmptyTag_ShouldReturnZero()
    {
        // Arrange & Act
        var removedCount = await _watchTargetManager.RemoveWatchTargetsByTagAsync("");

        // Assert
        removedCount.Should().Be(0);
    }

    [Test]
    public async Task GetWatchTargetInfosAsync_WithWatchTargets_ShouldReturnDetailedInfo()
    {
        // Arrange
        const string tagName = "test-tag";
        const int processId = 1234;

        await _watchTargetManager.AddTargetAsync(processId, tagName);

        // Act
        var watchTargetInfos = await _watchTargetManager.GetWatchTargetInfosAsync();

        // Assert
        watchTargetInfos.Should().HaveCount(1);
        var targetInfo = watchTargetInfos[0];
        targetInfo.ProcessId.Should().Be(processId);
        targetInfo.TagName.Should().Be(tagName);
        targetInfo.StartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        
        // プロセス情報は実際のプロセスが存在しない場合の処理を確認
        // モックプロセスでは "[Terminated]" が返される
        targetInfo.ProcessName.Should().NotBeEmpty();
        targetInfo.ExecutablePath.Should().NotBeEmpty();
    }

    [Test]
    public async Task GetWatchTargetInfosAsync_WithNoWatchTargets_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var watchTargetInfos = await _watchTargetManager.GetWatchTargetInfosAsync();

        // Assert
        watchTargetInfos.Should().BeEmpty();
    }

    [Test]
    public async Task GetWatchTargetInfosAsync_WithMultipleTags_ShouldReturnAllTargets()
    {
        // Arrange
        const string tag1 = "tag1";
        const string tag2 = "tag2";
        const int processId1 = 1111;
        const int processId2 = 2222;
        const int processId3 = 3333;

        await _watchTargetManager.AddTargetAsync(processId1, tag1);
        await _watchTargetManager.AddTargetAsync(processId2, tag1);
        await _watchTargetManager.AddTargetAsync(processId3, tag2);

        // Act
        var watchTargetInfos = await _watchTargetManager.GetWatchTargetInfosAsync();

        // Assert
        watchTargetInfos.Should().HaveCount(3);
        watchTargetInfos.Should().Contain(t => t.ProcessId == processId1 && t.TagName == tag1);
        watchTargetInfos.Should().Contain(t => t.ProcessId == processId2 && t.TagName == tag1);
        watchTargetInfos.Should().Contain(t => t.ProcessId == processId3 && t.TagName == tag2);
    }

    [Test]
    public async Task AddChildProcessAsync_WithValidParent_ShouldAddChild()
    {
        // Arrange
        const string tagName = "parent-tag";
        const int parentProcessId = 1000;
        const int childProcessId = 2000;

        await _watchTargetManager.AddTargetAsync(parentProcessId, tagName);

        // Act
        var result = await _watchTargetManager.AddChildProcessAsync(childProcessId, parentProcessId);

        // Assert
        result.Should().BeTrue();
        _watchTargetManager.IsWatchedProcess(childProcessId).Should().BeTrue();
        _watchTargetManager.GetTagForProcess(childProcessId).Should().Be(tagName);
        _watchTargetManager.ActiveTargetCount.Should().Be(2);
    }

    [Test]
    public async Task AddChildProcessAsync_WithNonWatchedParent_ShouldFail()
    {
        // Arrange
        const int parentProcessId = 1000;
        const int childProcessId = 2000;

        // Act
        var result = await _watchTargetManager.AddChildProcessAsync(childProcessId, parentProcessId);

        // Assert
        result.Should().BeFalse();
        _watchTargetManager.IsWatchedProcess(childProcessId).Should().BeFalse();
        _watchTargetManager.ActiveTargetCount.Should().Be(0);
    }

    [Test]
    public async Task RemoveTarget_WithChildProcess_ShouldRemoveFromTagMapping()
    {
        // Arrange
        const string tagName = "family-tag";
        const int parentProcessId = 1000;
        const int childProcessId = 2000;

        await _watchTargetManager.AddTargetAsync(parentProcessId, tagName);
        await _watchTargetManager.AddChildProcessAsync(childProcessId, parentProcessId);

        // Act
        var result = _watchTargetManager.RemoveTarget(childProcessId);

        // Assert
        result.Should().BeTrue();
        _watchTargetManager.IsWatchedProcess(childProcessId).Should().BeFalse();
        _watchTargetManager.IsWatchedProcess(parentProcessId).Should().BeTrue(); // 親は残る
        _watchTargetManager.ActiveTargetCount.Should().Be(1);
    }
}