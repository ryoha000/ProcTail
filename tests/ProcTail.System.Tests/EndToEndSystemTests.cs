using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Application.Services;
using ProcTail.Core.Interfaces;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;
using ProcTail.System.Tests.Infrastructure;

namespace ProcTail.System.Tests;

/// <summary>
/// 実際のWindows APIを使用したエンドツーエンドシステムテスト
/// </summary>
[TestFixture]
[Category("System")]
[Category("EndToEnd")]
[SupportedOSPlatform("windows")]
public class EndToEndSystemTests
{
    #pragma warning disable NUnit1032
    private IServiceProvider _serviceProvider = null!;
    #pragma warning restore NUnit1032
    private ProcTailService _procTailService = null!;

    [SetUp]
    public void Setup()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Real Windows services configuration
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Real Windows implementations
        services.AddSingleton<IEtwConfiguration, TestEtwConfiguration>();
        services.AddSingleton<INamedPipeConfiguration>(provider => new TestNamedPipeConfiguration
        {
            PipeName = $"ProcTail_E2E_Test_{Guid.NewGuid():N}"[..32]
        });

        services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
        services.AddSingleton<INamedPipeServer, WindowsNamedPipeServer>();

        // Business logic services
        services.AddSingleton<IWatchTargetManager, WatchTargetManager>();
        services.AddSingleton<IEventProcessor, EventProcessor>();
        services.AddSingleton<IEventStorage>(provider => 
        {
            var logger = provider.GetRequiredService<ILogger<EventStorage>>();
            return new EventStorage(logger, maxEventsPerTag: 1000);
        });

        // Main service
        services.AddSingleton<ProcTailService>();

        _serviceProvider = services.BuildServiceProvider();
        _procTailService = _serviceProvider.GetRequiredService<ProcTailService>();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            _procTailService?.Dispose();
        }
        catch { }
        
        try
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
        catch { }
    }

    [Test]
    public async Task CompleteWorkflow_RealWindowsAPIs_ShouldWorkEndToEnd()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
            return;
        }

        // Arrange
        const string tagName = "e2e-real-test";
        var currentProcessId = Environment.ProcessId;

        try
        {
            // Act 1: Start the service
            await _procTailService.StartAsync();
            _procTailService.IsRunning.Should().BeTrue();

            await Task.Delay(1000); // Wait for ETW to be ready

            // Act 2: Add watch target for current process
            var addResult = await _procTailService.AddWatchTargetAsync(currentProcessId, tagName);
            addResult.Should().BeTrue();

            // Act 3: Perform real file operations that should be captured
            var testFilePath = Path.Combine(Path.GetTempPath(), $"proctail_e2e_test_{Guid.NewGuid()}.txt");
            
            await File.WriteAllTextAsync(testFilePath, "E2E Test Content");
            await Task.Delay(500); // Wait for ETW processing
            
            var fileContent = await File.ReadAllTextAsync(testFilePath);
            fileContent.Should().Be("E2E Test Content");
            
            File.Delete(testFilePath);
            await Task.Delay(500); // Wait for ETW processing

            // Act 4: Get recorded events
            var events = await _procTailService.GetRecordedEventsAsync(tagName);

            // Assert
            events.Should().NotBeEmpty("Should capture file operations from ETW");
            
            var fileEvents = events.Where(e => 
                e.GetType().Name.Contains("FileEvent", StringComparison.OrdinalIgnoreCase))
                .ToList();

            fileEvents.Should().NotBeEmpty("Should capture file operations for our test file");

            TestContext.WriteLine($"Captured {events.Count} total events, {fileEvents.Count} file events");
            foreach (var evt in events.Take(10)) // Log first 10 events
            {
                TestContext.WriteLine($"Event: {evt.GetType().Name}, ProcessId: {evt.ProcessId}, Timestamp: {evt.Timestamp}");
            }
        }
        finally
        {
            // Cleanup
            await _procTailService.StopAsync();
        }
    }

    [Test]
    public async Task ProcessMonitoring_ChildProcessCreation_ShouldCaptureEvents()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
            return;
        }

        // Arrange
        const string tagName = "child-process-test";
        var currentProcessId = Environment.ProcessId;

        try
        {
            await _procTailService.StartAsync();
            await Task.Delay(1000); // Wait for ETW to be ready

            var addResult = await _procTailService.AddWatchTargetAsync(currentProcessId, tagName);
            addResult.Should().BeTrue();

            // Act: Create a child process
            using var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c echo Hello World & timeout /t 1";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;

            process.Start();
            var childProcessId = process.Id;
            
            await process.WaitForExitAsync();
            await Task.Delay(1000); // Wait for ETW processing

            // Assert
            var events = await _procTailService.GetRecordedEventsAsync(tagName);
            events.Should().NotBeEmpty();

            var processEvents = events.Where(e => 
                e.GetType().Name.Contains("Process", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TestContext.WriteLine($"Captured {processEvents.Count} process events");
            foreach (var evt in processEvents)
            {
                TestContext.WriteLine($"Process Event: {evt.GetType().Name}, ProcessId: {evt.ProcessId}");
            }

            // We should have captured some process-related events
            processEvents.Should().NotBeEmpty("Should capture process events");
        }
        finally
        {
            await _procTailService.StopAsync();
        }
    }

    [Test]
    public async Task IpcCommunication_RealNamedPipes_ShouldWork()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        try
        {
            await _procTailService.StartAsync();
            _procTailService.IsRunning.Should().BeTrue();

            var pipeConfig = _serviceProvider.GetRequiredService<INamedPipeConfiguration>();
            
            // Act: Connect via real Named Pipe client
            using var client = new NamedPipeClientStream(".", pipeConfig.PipeName, 
                PipeDirection.InOut);
            
            await client.ConnectAsync(5000);
            client.IsConnected.Should().BeTrue();

            // Send status request
            var request = @"{""RequestType"": ""GetStatus""}";
            var requestBytes = Encoding.UTF8.GetBytes(request);
            await client.WriteAsync(requestBytes);
            await client.FlushAsync();

            // Read response
            var responseBuffer = new byte[4096];
            var bytesRead = await client.ReadAsync(responseBuffer);
            var response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

            // Assert
            response.Should().NotBeNullOrEmpty();
            response.Should().Contain("Success", "Response should indicate success");
            response.Should().Contain("IsRunning", "Response should contain service status");

            TestContext.WriteLine($"IPC Response: {response}");
        }
        finally
        {
            await _procTailService.StopAsync();
        }
    }

    [Test]
    public async Task ServiceLifecycle_RealComponents_ShouldStartAndStopCleanly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Test multiple start/stop cycles with real components
        for (int cycle = 0; cycle < 3; cycle++)
        {
            TestContext.WriteLine($"Lifecycle test cycle {cycle + 1}");

            // Start
            await _procTailService.StartAsync();
            _procTailService.IsRunning.Should().BeTrue($"Service should start in cycle {cycle + 1}");

            // Verify components are running
            var etwProvider = _serviceProvider.GetRequiredService<IEtwEventProvider>();
            var pipeServer = _serviceProvider.GetRequiredService<INamedPipeServer>();
            
            etwProvider.IsMonitoring.Should().BeTrue($"ETW should be monitoring in cycle {cycle + 1}");
            pipeServer.IsRunning.Should().BeTrue($"Pipe server should be running in cycle {cycle + 1}");

            await Task.Delay(100); // Brief operation period

            // Stop
            await _procTailService.StopAsync();
            _procTailService.IsRunning.Should().BeFalse($"Service should stop in cycle {cycle + 1}");
            
            etwProvider.IsMonitoring.Should().BeFalse($"ETW should stop monitoring in cycle {cycle + 1}");
            pipeServer.IsRunning.Should().BeFalse($"Pipe server should stop in cycle {cycle + 1}");

            await Task.Delay(100); // Brief rest period
        }
    }

    [Test]
    public async Task MemoryAndResourceManagement_LongRunning_ShouldNotLeak()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Arrange
        const string tagName = "memory-test";
        var currentProcessId = Environment.ProcessId;

        try
        {
            await _procTailService.StartAsync();
            
            var addResult = await _procTailService.AddWatchTargetAsync(currentProcessId, tagName);
            addResult.Should().BeTrue();

            // Act: Generate sustained activity
            var startTime = DateTime.UtcNow;
            var testDuration = TimeSpan.FromSeconds(10); // Short test for CI

            var initialMemory = GC.GetTotalMemory(true);
            TestContext.WriteLine($"Initial memory: {initialMemory:N0} bytes");

            while (DateTime.UtcNow - startTime < testDuration)
            {
                // Generate some file activity
                var tempFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFile, "test");
                File.Delete(tempFile);

                await Task.Delay(50); // 20 operations per second
            }

            // Allow processing to complete
            await Task.Delay(1000);

            var finalMemory = GC.GetTotalMemory(true);
            TestContext.WriteLine($"Final memory: {finalMemory:N0} bytes");
            TestContext.WriteLine($"Memory difference: {finalMemory - initialMemory:N0} bytes");

            // Assert: Get statistics
            var statistics = await _procTailService.GetStatisticsAsync();
            TestContext.WriteLine($"Total events: {statistics.TotalEvents}");
            TestContext.WriteLine($"Estimated usage: {statistics.EstimatedMemoryUsage:N0} bytes");

            // Memory should not grow excessively (allow for 50MB growth for sustained activity)
            var memoryGrowth = finalMemory - initialMemory;
            memoryGrowth.Should().BeLessThan(50 * 1024 * 1024, 
                "Memory growth should be reasonable during sustained activity");

            statistics.TotalEvents.Should().BeGreaterThan(0, "Should have captured some events");
        }
        finally
        {
            await _procTailService.StopAsync();
        }
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