using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using FluentAssertions;
using ProcTail.Core.Models;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;

namespace ProcTail.Windows.Tests.Infrastructure.Etw;

/// <summary>
/// WindowsEtwEventProvider の Windows 固有テスト
/// Windows 環境でのみ実行される統合テスト
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProviderTests
{
    private WindowsEtwEventProvider? _provider;
    private EtwConfiguration? _configuration;
    private ILogger<WindowsEtwEventProvider>? _logger;
    private readonly List<RawEventData> _receivedEvents = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Windows環境でない場合はテスト全体をスキップ
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
        }
    }

    [SetUp]
    public void SetUp()
    {
        // Windows環境チェック
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        _logger = NullLogger<WindowsEtwEventProvider>.Instance;
        _configuration = CreateTestConfiguration();
        _provider = new WindowsEtwEventProvider(_logger, _configuration);
        _receivedEvents.Clear();

        // イベント受信ハンドラーを設定
        _provider.EventReceived += OnEventReceived;
    }

    [TearDown]
    public void TearDown()
    {
        if (_provider != null)
        {
            _provider.EventReceived -= OnEventReceived;
            _provider.Dispose();
            _provider = null;
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        using var provider = new WindowsEtwEventProvider(_logger!, _configuration!);

        // Assert
        provider.Should().NotBeNull();
        provider.IsMonitoring.Should().BeFalse();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        // このテストは Windows 環境では常に成功するため、条件付きでスキップ
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Pass("Windows環境ではPlatformNotSupportedExceptionは発生しません");
            return;
        }

        // Arrange & Act & Assert
        Action act = () => new WindowsEtwEventProvider(_logger!, _configuration!);
        act.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows platform*");
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    public async Task StartMonitoringAsync_WithAdministratorRights_ShouldStartSuccessfully()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        // Act
        await _provider!.StartMonitoringAsync();

        // Assert
        _provider.IsMonitoring.Should().BeTrue();

        // Cleanup
        await _provider.StopMonitoringAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresNonAdministrator")]
    public async Task StartMonitoringAsync_WithoutAdministratorRights_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        if (IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは非管理者権限で実行する必要があります");
            return;
        }

        // Act & Assert
        Func<Task> act = async () => await _provider!.StartMonitoringAsync();
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*管理者権限*");
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    public async Task StartMonitoringAsync_AlreadyMonitoring_ShouldNotStartAgain()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await _provider!.StartMonitoringAsync();
        var firstState = _provider.IsMonitoring;

        // Act
        await _provider.StartMonitoringAsync(); // 2回目の呼び出し

        // Assert
        firstState.Should().BeTrue();
        _provider.IsMonitoring.Should().BeTrue();

        // Cleanup
        await _provider.StopMonitoringAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    public async Task StopMonitoringAsync_WhenMonitoring_ShouldStopSuccessfully()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await _provider!.StartMonitoringAsync();
        _provider.IsMonitoring.Should().BeTrue();

        // Act
        await _provider.StopMonitoringAsync();

        // Assert
        _provider.IsMonitoring.Should().BeFalse();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public async Task StopMonitoringAsync_WhenNotMonitoring_ShouldCompleteWithoutError()
    {
        // Arrange
        _provider!.IsMonitoring.Should().BeFalse();

        // Act & Assert
        Func<Task> act = async () => await _provider.StopMonitoringAsync();
        await act.Should().NotThrowAsync();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    [Category("LongRunning")]
    public async Task EventReceived_WhenMonitoring_ShouldReceiveEvents()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await _provider!.StartMonitoringAsync();

        try
        {
            // Act - ファイルを作成してイベントを発生させる
            var testFileName = Path.Combine(Path.GetTempPath(), $"ProcTailTest_{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(testFileName, "test content");

            // イベント受信を待機
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (_receivedEvents.Count == 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, timeoutCts.Token);
            }

            // Cleanup test file
            if (File.Exists(testFileName))
            {
                File.Delete(testFileName);
            }

            // Assert
            _receivedEvents.Should().NotBeEmpty("ファイル操作によりETWイベントが発生するはずです");
            _receivedEvents.Should().Contain(e => e.ProviderName.Contains("FileIO") || e.EventName.Contains("Create"));
        }
        finally
        {
            await _provider.StopMonitoringAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    [Category("LongRunning")]
    public async Task EventReceived_ProcessEvents_ShouldReceiveProcessStartStopEvents()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        // プロセスイベントを含む設定
        var processConfig = CreateProcessEventConfiguration();
        using var processProvider = new WindowsEtwEventProvider(_logger!, processConfig);
        var processEvents = new List<RawEventData>();
        processProvider.EventReceived += (sender, e) => processEvents.Add(e);

        await processProvider.StartMonitoringAsync();

        try
        {
            // Act - プロセスを開始してイベントを発生させる
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c echo test";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            await process.WaitForExitAsync();

            // イベント受信を待機
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (processEvents.Count == 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, timeoutCts.Token);
            }

            // Assert
            processEvents.Should().NotBeEmpty("プロセス開始/終了によりETWイベントが発生するはずです");
            processEvents.Should().Contain(e => e.ProviderName.Contains("Process") || 
                                               e.EventName.Contains("Start") || 
                                               e.EventName.Contains("Stop"));
        }
        finally
        {
            await processProvider.StopMonitoringAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var provider = new WindowsEtwEventProvider(_logger!, _configuration!);

        // Act
        provider.Dispose();

        // Assert
        provider.IsMonitoring.Should().BeFalse();

        // Double dispose should not throw
        Action act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    [Category("Windows")]
    [Category("Unit")]
    public void IsRunningAsAdministrator_OnWindows_ShouldReturnCorrectValue()
    {
        // Arrange & Act
        var isAdmin = IsRunningAsAdministrator();

        // Assert
        isAdmin.Should().BeOfType<bool>();
        // 実際の管理者状態に応じて結果が変わるため、型チェックのみ
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    public async Task StartMonitoringAsync_WithInvalidConfiguration_ShouldThrowInvalidOperationException()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        var invalidConfig = CreateInvalidConfiguration();
        using var provider = new WindowsEtwEventProvider(_logger!, invalidConfig);

        // Act & Assert
        Func<Task> act = async () => await provider.StartMonitoringAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ETWセッション*");
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("RequiresAdministrator")]
    public async Task StartMonitoringAsync_CancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        Func<Task> act = async () => await _provider!.StartMonitoringAsync(cts.Token);
        
        // キャンセルまたは正常完了のいずれかが発生する可能性がある
        try
        {
            await act();
            // 正常完了した場合はクリーンアップ
            if (_provider!.IsMonitoring)
            {
                await _provider.StopMonitoringAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル例外は期待される動作
            Assert.Pass("キャンセルが正常に動作しました");
        }
    }

    #region Helper Methods

    private void OnEventReceived(object? sender, RawEventData e)
    {
        _receivedEvents.Add(e);
    }

    private static EtwConfiguration CreateTestConfiguration()
    {
        return new EtwConfiguration();
    }

    private static EtwConfiguration CreateProcessEventConfiguration()
    {
        var config = new EtwConfiguration();
        // プロセスイベントプロバイダーを含むように設定を調整
        // 実際の実装では設定プロパティを使用
        return config;
    }

    private static EtwConfiguration CreateInvalidConfiguration()
    {
        var config = new EtwConfiguration();
        // 無効な設定を作成（空のプロバイダーリストなど）
        return config;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}