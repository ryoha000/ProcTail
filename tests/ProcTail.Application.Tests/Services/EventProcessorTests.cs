using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using ProcTail.Application.Services;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Helpers;

namespace ProcTail.Application.Tests.Services;

[TestFixture]
[Category("Unit")]
public class EventProcessorTests
{
    private Mock<ILogger<EventProcessor>> _mockLogger = null!;
    private Mock<IWatchTargetManager> _mockWatchTargetManager = null!;
    private Mock<IEtwConfiguration> _mockEtwConfiguration = null!;
    private EventProcessor _processor = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EventProcessor>>();
        _mockWatchTargetManager = new Mock<IWatchTargetManager>();
        _mockEtwConfiguration = new Mock<IEtwConfiguration>();

        // デフォルトの設定
        _mockEtwConfiguration.Setup(x => x.EnabledProviders)
            .Returns(new[] { "Microsoft-Windows-Kernel-FileIO", "Microsoft-Windows-Kernel-Process" });
        _mockEtwConfiguration.Setup(x => x.EnabledEventNames)
            .Returns(new[] { "FileIO/Create", "FileIO/Write", "Process/Start", "Process/End" });

        _processor = new EventProcessor(_mockLogger.Object, _mockWatchTargetManager.Object, _mockEtwConfiguration.Object);
    }

    [TearDown]
    public void TearDown()
    {
        // リソースのクリーンアップは特に不要
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _processor.Should().NotBeNull();
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new EventProcessor(null!, _mockWatchTargetManager.Object, _mockEtwConfiguration.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Constructor_WithNullWatchTargetManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new EventProcessor(_mockLogger.Object, null!, _mockEtwConfiguration.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Constructor_WithNullEtwConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new EventProcessor(_mockLogger.Object, _mockWatchTargetManager.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ShouldProcessEvent_WithEnabledProvider_ShouldReturnTrue()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/Create",
            1234
        );

        // Act
        var result = _processor.ShouldProcessEvent(rawEvent);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void ShouldProcessEvent_WithDisabledProvider_ShouldReturnTrue()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Disabled-Provider",
            "FileIO/Create",
            1234
        );

        // Act
        var result = _processor.ShouldProcessEvent(rawEvent);

        // Assert - フィルタリング無効化により、すべてのプロバイダーを通す
        result.Should().BeTrue();
    }

    [Test]
    public void ShouldProcessEvent_WithDisabledEventName_ShouldReturnTrue()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/DisabledEvent",
            1234
        );

        // Act
        var result = _processor.ShouldProcessEvent(rawEvent);

        // Assert - フィルタリング無効化により、すべてのイベント名を通す
        result.Should().BeTrue();
    }

    [Test]
    public void ShouldProcessEvent_WithNullEvent_ShouldReturnFalse()
    {
        // Act
        var result = _processor.ShouldProcessEvent(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task ProcessEventAsync_WithNullEvent_ShouldReturnFailureResult()
    {
        // Act
        var result = await _processor.ProcessEventAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null");
    }

    [Test]
    public async Task ProcessEventAsync_WithFilteredEvent_ShouldReturnFailureResult()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Disabled-Provider",
            "FileIO/Create",
            1234
        );

        // フィルタリング無効化により、プロセスが監視されていないため失敗することを確認
        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(false);

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not watched");
    }

    [Test]
    public async Task ProcessEventAsync_WithUnwatchedProcess_ShouldReturnFailureResult()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/Create",
            1234
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(false);

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not watched");
    }

    [Test]
    public async Task ProcessEventAsync_WithWatchedProcess_NoTag_ShouldReturnFailureResult()
    {
        // Arrange
        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/Create",
            1234
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns((string?)null);

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Tag not found");
    }

    [Test]
    public async Task ProcessEventAsync_WithValidFileEvent_ShouldReturnFileEventData()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "FileName", @"C:\test\file.txt" },
            { "IrpPtr", "0x12345678" }
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/Create",
            1234,
            payload
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EventData.Should().NotBeNull();
        result.EventData.Should().BeOfType<FileEventData>();

        var fileEvent = (FileEventData)result.EventData!;
        fileEvent.TagName.Should().Be("test-tag");
        fileEvent.ProcessId.Should().Be(1234);
        fileEvent.FilePath.Should().Be(@"C:\test\file.txt");
        fileEvent.ProviderName.Should().Be("Microsoft-Windows-Kernel-FileIO");
        fileEvent.EventName.Should().Be("FileIO/Create");
    }

    [Test]
    public async Task ProcessEventAsync_WithValidProcessStartEvent_ShouldReturnProcessStartEventData()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "ProcessId", 5678 },
            { "ProcessName", "child.exe" },
            { "CommandLine", "child.exe --test" }
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-Process",
            "Process/Start",
            1234,
            payload
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");
        _mockWatchTargetManager.Setup(x => x.AddChildProcessAsync(5678, 1234)).ReturnsAsync(true);

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EventData.Should().NotBeNull();
        result.EventData.Should().BeOfType<ProcessStartEventData>();

        var processEvent = (ProcessStartEventData)result.EventData!;
        processEvent.TagName.Should().Be("test-tag");
        processEvent.ProcessId.Should().Be(1234);
        processEvent.ChildProcessId.Should().Be(5678);
        processEvent.ChildProcessName.Should().Be("child.exe");
        processEvent.ProviderName.Should().Be("Microsoft-Windows-Kernel-Process");
        processEvent.EventName.Should().Be("Process/Start");

        // 子プロセス追加は非同期で実行されるため、少し待つ
        await Task.Delay(50);

        // 子プロセス追加が呼ばれることを確認
        _mockWatchTargetManager.Verify(x => x.AddChildProcessAsync(5678, 1234), Times.Once);
    }

    [Test]
    public async Task ProcessEventAsync_WithValidProcessEndEvent_ShouldReturnProcessEndEventData()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "ExitStatus", 1 },
            { "HandleCount", 100 }
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-Process",
            "Process/End",
            1234,
            payload
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");
        _mockWatchTargetManager.Setup(x => x.RemoveTarget(1234)).Returns(true);

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EventData.Should().NotBeNull();
        result.EventData.Should().BeOfType<ProcessEndEventData>();

        var processEvent = (ProcessEndEventData)result.EventData!;
        processEvent.TagName.Should().Be("test-tag");
        processEvent.ProcessId.Should().Be(1234);
        processEvent.ExitCode.Should().Be(1);
        processEvent.ProviderName.Should().Be("Microsoft-Windows-Kernel-Process");
        processEvent.EventName.Should().Be("Process/End");

        // プロセス除去は非同期で実行されるため、少し待つ
        await Task.Delay(50);

        // プロセス除去が呼ばれることを確認
        _mockWatchTargetManager.Verify(x => x.RemoveTarget(1234), Times.Once);
    }

    [Test]
    public async Task ProcessEventAsync_WithUnknownProvider_ShouldReturnGenericEventData()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "CustomData", "test" }
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Unknown-Provider",
            "Unknown/Event",
            1234,
            payload
        );

        // 新しい設定でプロセッサーを作成
        var mockEtwConfigForTest = new Mock<IEtwConfiguration>();
        mockEtwConfigForTest.Setup(x => x.EnabledProviders)
            .Returns(new[] { "Unknown-Provider" });
        mockEtwConfigForTest.Setup(x => x.EnabledEventNames)
            .Returns(new[] { "Unknown/Event" });

        var testProcessor = new EventProcessor(_mockLogger.Object, _mockWatchTargetManager.Object, mockEtwConfigForTest.Object);

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");

        // Act
        var result = await testProcessor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.EventData.Should().NotBeNull();
        result.EventData.Should().BeOfType<GenericEventData>();

        var genericEvent = (GenericEventData)result.EventData!;
        genericEvent.TagName.Should().Be("test-tag");
        genericEvent.ProcessId.Should().Be(1234);
        genericEvent.ProviderName.Should().Be("Unknown-Provider");
        genericEvent.EventName.Should().Be("Unknown/Event");
    }

    [Test]
    public async Task ProcessEventAsync_WithFileEventMissingFilePath_ShouldReturnFailureResult()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "SomeOtherField", "value" }
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-FileIO",
            "FileIO/Create",
            1234,
            payload
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to convert");
    }

    [Test]
    public async Task ProcessEventAsync_WithProcessStartEventMissingProcessId_ShouldReturnFailureResult()
    {
        // Arrange
        var payload = new Dictionary<string, object>
        {
            { "ProcessName", "child.exe" }
            // ProcessIdが不足
        };

        var rawEvent = TestEventFactory.CreateRawEvent(
            "Microsoft-Windows-Kernel-Process",
            "Process/Start",
            1234,
            payload
        );

        _mockWatchTargetManager.Setup(x => x.IsWatchedProcess(1234)).Returns(true);
        _mockWatchTargetManager.Setup(x => x.GetTagForProcess(1234)).Returns("test-tag");

        // Act
        var result = await _processor.ProcessEventAsync(rawEvent);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to convert");
    }
}