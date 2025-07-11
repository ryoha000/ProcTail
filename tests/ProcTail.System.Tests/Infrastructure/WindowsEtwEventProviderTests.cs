using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.System.Tests.Infrastructure;

namespace ProcTail.System.Tests.Infrastructure;

/// <summary>
/// Windows ETWイベントプロバイダーのシステムテスト
/// 実際のWindows ETW APIを使用したテスト
/// </summary>
[TestFixture]
[Category("System")]
[Category("Windows")]
[SupportedOSPlatform("windows")]
public class WindowsEtwEventProviderTests
{
    private ILogger<WindowsEtwEventProvider> _logger = null!;
    private IEtwConfiguration _configuration = null!;

    [SetUp]
    public void Setup()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<WindowsEtwEventProvider>();
        _configuration = new TestEtwConfiguration();
    }

    [Test]
    public void Constructor_OnWindows_ShouldInitializeSuccessfully()
    {
        // Skip if not on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Act & Assert
        var action = () => new WindowsEtwEventProvider(_logger, _configuration);
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        // Skip if on Windows (we want to test non-Windows behavior)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows以外の環境でのみ実行されます");
            return;
        }

        // Act & Assert
        var action = () => new WindowsEtwEventProvider(_logger, _configuration);
        action.Should().Throw<PlatformNotSupportedException>()
              .WithMessage("*Windows platform*");
    }

    [Test]
    [Category("RequiresAdmin")]
    public async Task StartMonitoringAsync_WithAdministratorRights_ShouldSucceed()
    {
        // Skip if not on Windows or not admin
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限でのみ実行されます");
            return;
        }

        // Arrange
        using var provider = new WindowsEtwEventProvider(_logger, _configuration);

        // Act
        var action = () => provider.StartMonitoringAsync();

        // Assert
        await action.Should().NotThrowAsync();
        provider.IsMonitoring.Should().BeTrue();
    }

    [Test]
    public async Task StartMonitoringAsync_WithoutAdministratorRights_ShouldThrowUnauthorizedAccessException()
    {
        // Skip if not on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        if (IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限なしで実行される必要があります");
            return;
        }

        // Arrange
        using var provider = new WindowsEtwEventProvider(_logger, _configuration);

        // Act & Assert
        var action = () => provider.StartMonitoringAsync();
        await action.Should().ThrowAsync<UnauthorizedAccessException>()
                   .WithMessage("*管理者権限*");
    }

    [Test]
    [Category("RequiresAdmin")]
    public async Task ETWEventCollection_RealFileOperations_ShouldCaptureEvents()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限でのみ実行されます");
            return;
        }

        // Arrange
        using var provider = new WindowsEtwEventProvider(_logger, _configuration);
        var capturedEvents = new List<RawEventData>();
        provider.EventReceived += (sender, eventData) => capturedEvents.Add(eventData);

        var testFilePath = Path.Combine(Path.GetTempPath(), $"proctail_test_{Guid.NewGuid()}.txt");

        try
        {
            // Act
            await provider.StartMonitoringAsync();
            await Task.Delay(500); // Wait for ETW to be ready

            // Trigger file operations that should be captured by ETW
            await File.WriteAllTextAsync(testFilePath, "Test content");
            await Task.Delay(200); // Wait for ETW event processing
            
            File.Delete(testFilePath);
            await Task.Delay(200); // Wait for ETW event processing

            await provider.StopMonitoringAsync();

            // Assert
            capturedEvents.Should().NotBeEmpty("ETW should capture file operations");
            
            var fileEvents = capturedEvents.Where(e => 
                e.ProviderName.Contains("FileIO", StringComparison.OrdinalIgnoreCase) &&
                e.Payload.ContainsKey("FileName") &&
                e.Payload["FileName"].ToString()!.Contains("proctail_test", StringComparison.OrdinalIgnoreCase))
                .ToList();

            fileEvents.Should().NotBeEmpty("Should capture file operations for test file");
        }
        finally
        {
            // Cleanup
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }
    }

    [Test]
    [Category("RequiresAdmin")]
    [RequiresWindowsAndAdministrator] 
    public async Task ETWEventCollection_ProcessEvents_ShouldCaptureProcessCreation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限でのみ実行されます");
            return;
        }

        // Arrange
        using var provider = new WindowsEtwEventProvider(_logger, _configuration);
        var capturedEvents = new List<RawEventData>();
        provider.EventReceived += (sender, eventData) => capturedEvents.Add(eventData);

        // Act
        await provider.StartMonitoringAsync();
        await Task.Delay(500); // Wait for ETW to be ready

        // Create a short-lived process
        using (var process = new Process())
        {
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c echo test";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            await process.WaitForExitAsync();
        }

        await Task.Delay(500); // Wait for ETW event processing
        await provider.StopMonitoringAsync();

        // Assert
        capturedEvents.Should().NotBeEmpty("ETW should capture process events");
        
        var processEvents = capturedEvents.Where(e => 
            e.ProviderName.Contains("Process", StringComparison.OrdinalIgnoreCase))
            .ToList();

        processEvents.Should().NotBeEmpty("Should capture process creation/termination events");
    }

    [Test]
    public async Task StopMonitoringAsync_WhenNotStarted_ShouldNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        using var provider = new WindowsEtwEventProvider(_logger, _configuration);

        // Act & Assert
        var action = () => provider.StopMonitoringAsync();
        await action.Should().NotThrowAsync();
    }

    [Test]
    public void Dispose_ShouldCleanupResources()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        var provider = new WindowsEtwEventProvider(_logger, _configuration);

        // Act & Assert
        var action = () => provider.Dispose();
        action.Should().NotThrow();
        
        // Multiple dispose should not throw
        action.Should().NotThrow();
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

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
}

/// <summary>
/// Windows環境でのみ実行されるテストを示す属性
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequiresWindowsAttribute : CategoryAttribute
{
    public RequiresWindowsAttribute() : base("RequiresWindows") { }
}

/// <summary>
/// Windows環境で管理者権限が必要なテストを示す属性
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequiresWindowsAndAdministratorAttribute : CategoryAttribute
{
    public RequiresWindowsAndAdministratorAttribute() : base("RequiresWindowsAndAdministrator") { }
}