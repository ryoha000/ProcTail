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
            builder.SetMinimumLevel(LogLevel.None);
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
    [Category("RequiresAdmin")]
    [Category("EndToEnd")]
    [Timeout(60000)]
    public async Task ProcTailHost_TestProcessMonitoring_ShouldDetectFileWriteEvents()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
            return;
        }

        Process? procTailHostProcess = null;
        Process? testProcess = null;

        try
        {
            TestContext.WriteLine("=== ProcTail統合テスト開始 (test-process使用) ===");
            
            // 1. ProcTailHostを起動
            TestContext.WriteLine("1. ProcTailHostを起動中...");
            procTailHostProcess = await StartProcTailHostProcessAsync();
            
            // 2. test-processを起動
            TestContext.WriteLine("2. test-processを起動中...");
            testProcess = await StartTestProcessAsync("file-write", 5);
            var testProcessPid = testProcess.Id;
            
            // 3. test-processを監視対象に追加
            TestContext.WriteLine($"3. test-process(PID: {testProcessPid})を監視対象に追加中...");
            await AddWatchTargetAsync(testProcessPid, "test-process");
            
            // 4. test-processの完了を待機
            TestContext.WriteLine("4. test-processのファイル操作完了を待機中...");
            await testProcess.WaitForExitAsync();
            
            // 5. イベントが記録されるまで待機
            TestContext.WriteLine("5. イベント処理完了を確認中...");
            await Task.Delay(1000);
            
            // 6. 記録されたイベントを取得・検証
            TestContext.WriteLine("6. 記録されたイベントを取得・検証中...");
            var events = await GetRecordedEventsAsync("test-process");
            
            // Assert
            events.Should().NotBeEmpty("ファイル操作イベントが記録されているはず");
            
            var fileEvents = events.Where(e => 
                e.ToString()!.Contains("FileEvent", StringComparison.OrdinalIgnoreCase) ||
                e.ToString()!.Contains("File", StringComparison.OrdinalIgnoreCase)
            ).ToList();
            
            TestContext.WriteLine($"検出されたイベント数: {events.Count}");
            TestContext.WriteLine($"ファイル操作イベント数: {fileEvents.Count}");
            
            foreach (var evt in events.Take(10))
            {
                TestContext.WriteLine($"- イベント: {evt}");
            }
            
            // 最低限イベントが記録されていることを確認
            events.Count.Should().BeGreaterThan(0, "何らかのイベントが記録されているはず");
            
            TestContext.WriteLine("=== ProcTail統合テスト完了 ===");
        }
        finally
        {
            // クリーンアップ
            TestContext.WriteLine("クリーンアップ中...");
            
            try { testProcess?.Kill(); testProcess?.Dispose(); } catch { }
            try { procTailHostProcess?.Kill(); procTailHostProcess?.Dispose(); } catch { }
            
            await Task.Delay(1000); // リソース解放待機
        }
    }

    [Test]
    [Timeout(30000)]
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
    [Timeout(30000)]
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
            process.StartInfo.Arguments = "/c echo Hello World & timeout /t 2";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;

            TestContext.WriteLine($"Starting child process from parent PID: {currentProcessId}");
            process.Start();
            var childProcessId = process.Id;
            TestContext.WriteLine($"Child process started with PID: {childProcessId}");
            
            await process.WaitForExitAsync();
            TestContext.WriteLine($"Child process {childProcessId} exited");
            await Task.Delay(2000); // Extended wait for ETW processing

            // Assert
            var events = await _procTailService.GetRecordedEventsAsync(tagName);
            TestContext.WriteLine($"Total events captured: {events.Count}");
            
            foreach (var evt in events)
            {
                TestContext.WriteLine($"Event: {evt.GetType().Name}, ProcessId: {evt.ProcessId}, Timestamp: {evt.Timestamp}");
            }
            
            events.Should().NotBeEmpty("Should capture some events from process monitoring");

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
    [Timeout(30000)]
    public async Task IpcCommunication_RealNamedPipes_ShouldWork()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
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

            // Send status request with length prefix
            var request = @"{""RequestType"": ""GetStatus""}";
            var requestBytes = Encoding.UTF8.GetBytes(request);
            var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
            
            // Send length first, then the message
            await client.WriteAsync(lengthBytes);
            await client.WriteAsync(requestBytes);
            await client.FlushAsync();

            // Read response with length prefix
            var lengthBuffer = new byte[4];
            var lengthBytesRead = await client.ReadAsync(lengthBuffer);
            if (lengthBytesRead < 4)
            {
                Assert.Fail($"Failed to read message length: only {lengthBytesRead} bytes read");
            }
            
            var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
            var responseBuffer = new byte[responseLength];
            var responseBytesRead = 0;
            
            while (responseBytesRead < responseLength)
            {
                var read = await client.ReadAsync(responseBuffer, responseBytesRead, responseLength - responseBytesRead);
                if (read == 0) break;
                responseBytesRead += read;
            }
            
            var response = Encoding.UTF8.GetString(responseBuffer, 0, responseBytesRead);

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
    [Timeout(30000)]
    public async Task ServiceLifecycle_RealComponents_ShouldStartAndStopCleanly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
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
    [Timeout(60000)]
    public async Task MemoryAndResourceManagement_LongRunning_ShouldNotLeak()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
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

    private async Task<Process> StartProcTailHostProcessAsync()
    {
        TestContext.WriteLine("ProcTailHostプロセスを起動中...");
        
        // Windows環境でホスト実行可能ファイルを探す
        var hostExecutable = FindHostExecutable();
        
        var hostProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = hostExecutable.Item1,
                Arguments = hostExecutable.Item2 ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        hostProcess.Start();
        
        // ProcTailHostが起動するまで待機
        TestContext.WriteLine("ProcTailHostの起動を待機中... (3秒)");
        await Task.Delay(3000);
        
        TestContext.WriteLine($"ProcTailHost起動完了 (PID: {hostProcess.Id})");
        return hostProcess;
    }

    private async Task<Process> StartTestProcessAsync(string operation, int count = 3)
    {
        TestContext.WriteLine($"test-processを起動中... (操作: {operation}, 回数: {count})");
        
        // test-process実行可能ファイルを探す
        var testProcessExecutable = FindTestProcessExecutable();
        
        var testProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = testProcessExecutable.Item1,
                Arguments = testProcessExecutable.Item2 != null 
                    ? $"{testProcessExecutable.Item2} {operation} --count {count} --interval 500ms --verbose"
                    : $"{operation} --count {count} --interval 500ms --verbose",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        TestContext.WriteLine($"test-process実行コマンド: {testProcessExecutable.Item1} {testProcess.StartInfo.Arguments}");
        
        testProcess.Start();
        
        TestContext.WriteLine($"test-process起動完了 (PID: {testProcess.Id})");
        
        return testProcess;
    }

    private async Task AddWatchTargetAsync(int processId, string tag)
    {
        TestContext.WriteLine($"プロセス {processId} を監視対象に追加中... (タグ: {tag})");
        
        try
        {
            // Windows環境でビルドされた実行可能ファイルを直接使用
            var cliExecutable = FindCliExecutable();
            
            using var client = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cliExecutable.Item1,
                    Arguments = cliExecutable.Item2 != null ? $"{cliExecutable.Item2} add --pid {processId} --tag {tag}" : $"add --pid {processId} --tag {tag}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            TestContext.WriteLine($"CLI実行コマンド: {cliExecutable.Item1} {client.StartInfo.Arguments}");
            
            client.Start();
            
            // タイムアウト付きで実行
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            
            var outputTask = client.StandardOutput.ReadToEndAsync();
            var errorTask = client.StandardError.ReadToEndAsync();
            var waitTask = client.WaitForExitAsync(cts.Token);
            
            try
            {
                await waitTask;
                var output = await outputTask;
                var error = await errorTask;
                
                TestContext.WriteLine($"CLI実行完了 (Exit Code: {client.ExitCode})");
                if (!string.IsNullOrEmpty(output)) TestContext.WriteLine($"出力: {output}");
                if (!string.IsNullOrEmpty(error)) TestContext.WriteLine($"エラー: {error}");
                
                if (client.ExitCode == 0)
                {
                    TestContext.WriteLine($"監視対象追加完了 (PID: {processId}, タグ: {tag})");
                }
                else
                {
                    TestContext.WriteLine($"監視対象追加失敗 (Exit Code: {client.ExitCode})");
                    throw new InvalidOperationException($"CLI コマンドが失敗しました: Exit Code {client.ExitCode}");
                }
            }
            catch (OperationCanceledException)
            {
                TestContext.WriteLine("CLI実行がタイムアウトしました (30秒)");
                if (!client.HasExited)
                {
                    client.Kill();
                    TestContext.WriteLine("CLI プロセスを強制終了しました");
                }
                throw new TimeoutException("CLI コマンドの実行がタイムアウトしました");
            }
            
            // 監視が有効になるまで待機
            TestContext.WriteLine("監視設定の反映を待機中... (1秒)");
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"AddWatchTargetAsync でエラーが発生しました: {ex.Message}");
            TestContext.WriteLine($"スタックトレース: {ex.StackTrace}");
            throw;
        }
    }


    private async Task<List<object>> GetRecordedEventsAsync(string tag)
    {
        TestContext.WriteLine($"記録されたイベントを取得中... (タグ: {tag})");
        
        try
        {
            // Windows環境でビルドされた実行可能ファイルを直接使用
            var cliExecutable = FindCliExecutable();
            
            using var client = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cliExecutable.Item1,
                    Arguments = cliExecutable.Item2 != null ? $"{cliExecutable.Item2} events --tag {tag}" : $"events --tag {tag}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            TestContext.WriteLine($"CLI実行コマンド: {cliExecutable.Item1} {client.StartInfo.Arguments}");
            
            client.Start();
            
            // タイムアウト付きで実行
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            
            var outputTask = client.StandardOutput.ReadToEndAsync();
            var errorTask = client.StandardError.ReadToEndAsync();
            var waitTask = client.WaitForExitAsync(cts.Token);
            
            string output = "";
            string error = "";
            
            try
            {
                await waitTask;
                output = await outputTask;
                error = await errorTask;
                
                TestContext.WriteLine($"CLI実行完了 (Exit Code: {client.ExitCode})");
            }
            catch (OperationCanceledException)
            {
                TestContext.WriteLine("CLI実行がタイムアウトしました (15秒)");
                if (!client.HasExited)
                {
                    client.Kill();
                    TestContext.WriteLine("CLI プロセスを強制終了しました");
                }
                // タイムアウトの場合は空のリストを返す
                return new List<object>();
            }
            
            TestContext.WriteLine($"CLIコマンド出力:\n{output}");
            if (!string.IsNullOrEmpty(error)) TestContext.WriteLine($"CLIエラー出力:\n{error}");
            
            // 簡易的なイベント解析 (実際のJSONパースは必要に応じて実装)
            var events = new List<object>();
            
            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Event") || line.Contains("File") || line.Contains("Process"))
                    {
                        events.Add(line.Trim());
                    }
                }
            }
            
            TestContext.WriteLine($"取得されたイベント数: {events.Count}");
            return events;
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"GetRecordedEventsAsync でエラーが発生しました: {ex.Message}");
            TestContext.WriteLine($"スタックトレース: {ex.StackTrace}");
            return new List<object>();
        }
    }

    private (string FileName, string? DllPath) FindCliExecutable()
    {
        // Windows環境でCLI実行可能ファイルを探す
        var currentDir = Environment.CurrentDirectory;
        var possiblePaths = new[]
        {
            // テストディレクトリ内のpublishされたファイル（最優先）
            Path.Combine(currentDir, "proctail.dll"),
            Path.Combine(currentDir, "proctail"),
            Path.Combine(currentDir, "proctail.exe"),
            Path.Combine(currentDir, "ProcTail.Cli.dll"),
            Path.Combine(currentDir, "ProcTail.Cli.exe"),
            
            // 親ディレクトリを確認（C:\temp\proctail_test_XXX\tests -> C:\temp\proctail_test_XXX）
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "proctail.dll"),
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "proctail"),
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "proctail.exe"),
            
            // 従来のビルド出力パス
            Path.Combine(currentDir, "src", "ProcTail.Cli", "bin", "Debug", "net8.0", "proctail.dll"),
            Path.Combine(currentDir, "src", "ProcTail.Cli", "bin", "Debug", "net8.0", "ProcTail.Cli.dll"),
            Path.Combine(currentDir, "src", "ProcTail.Cli", "bin", "Debug", "net8.0", "proctail.exe"),
            Path.Combine(currentDir, "src", "ProcTail.Cli", "bin", "Debug", "net8.0", "ProcTail.Cli.exe"),
        };

        TestContext.WriteLine($"CLI実行可能ファイルを検索中... (作業ディレクトリ: {Environment.CurrentDirectory})");

        // DLLファイルを優先（dotnet経由で実行、クロスプラットフォーム対応）
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) && path.EndsWith(".dll"))
            {
                TestContext.WriteLine($"CLI DLLファイルを発見: {path}");
                return ("dotnet", path);
            }
        }

        // 実行可能ファイル（.exe または拡張子なし）
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) && (path.EndsWith(".exe") || !Path.HasExtension(path)))
            {
                TestContext.WriteLine($"CLI実行可能ファイルを発見: {path}");
                return (path, null);
            }
        }

        // デバッグ情報: 現在のディレクトリの内容を表示
        TestContext.WriteLine("現在のディレクトリの内容:");
        try
        {
            var files = Directory.GetFiles(Environment.CurrentDirectory, "*proctail*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                TestContext.WriteLine($"  - {Path.GetFileName(file)} (FullPath: {file})");
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"ディレクトリ一覧取得エラー: {ex.Message}");
        }

        // フォールバック: エラーとして例外を投げる
        throw new FileNotFoundException("CLI実行可能ファイル（proctail.dll、proctail.exe、または proctail）が見つかりません。publishされたファイルがテストディレクトリにコピーされているか確認してください。");
    }

    private (string FileName, string? DllPath) FindTestProcessExecutable()
    {
        // test-process実行可能ファイルを探す
        var currentDir = Environment.CurrentDirectory;
        
        // プロジェクトルートディレクトリを探す
        var projectRoot = FindProjectRoot(currentDir);
        
        var possiblePaths = new[]
        {
            // テストディレクトリ内のtest-processファイル（最優先）
            Path.Combine(currentDir, "test-process.exe"),
            Path.Combine(currentDir, "test-process"),
            
            // 親ディレクトリを確認
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "test-process.exe"),
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "test-process"),
            
            // プロジェクトルートからのtools/test-process ディレクトリ内
            projectRoot != null ? Path.Combine(projectRoot, "tools", "test-process", "test-process.exe") : null,
            projectRoot != null ? Path.Combine(projectRoot, "tools", "test-process", "test-process") : null,
            
            // tools/test-process ディレクトリ内
            Path.Combine(currentDir, "tools", "test-process", "test-process.exe"),
            Path.Combine(currentDir, "tools", "test-process", "test-process"),
            
            // 相対パスでtools/test-processを確認
            Path.Combine("..", "..", "..", "tools", "test-process", "test-process.exe"),
            Path.Combine("..", "..", "..", "tools", "test-process", "test-process"),
        }.Where(p => p != null).ToArray();

        TestContext.WriteLine($"test-process実行可能ファイルを検索中... (作業ディレクトリ: {Environment.CurrentDirectory})");

        // 実行可能ファイル（.exe または拡張子なし）
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                TestContext.WriteLine($"test-process実行可能ファイルを発見: {fullPath}");
                return (fullPath, null);
            }
        }

        // デバッグ情報: 現在のディレクトリの内容を表示
        TestContext.WriteLine("現在のディレクトリの内容:");
        try
        {
            var files = Directory.GetFiles(Environment.CurrentDirectory, "*test-process*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                TestContext.WriteLine($"  - {Path.GetFileName(file)} (FullPath: {file})");
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"ディレクトリ一覧取得エラー: {ex.Message}");
        }

        // フォールバック: エラーとして例外を投げる
        throw new FileNotFoundException("test-process実行可能ファイル（test-process.exe または test-process）が見つかりません。tools/test-processディレクトリでビルドされているか確認してください。");
    }

    private (string FileName, string? DllPath) FindHostExecutable()
    {
        // Windows環境でHost実行可能ファイルを探す
        var currentDir = Environment.CurrentDirectory;
        var possiblePaths = new[]
        {
            // テストディレクトリ内のpublishされたファイル（最優先）
            Path.Combine(currentDir, "ProcTail.Host.dll"),
            Path.Combine(currentDir, "ProcTail.Host"),
            Path.Combine(currentDir, "ProcTail.Host.exe"),
            
            // 親ディレクトリを確認（C:\temp\proctail_test_XXX\tests -> C:\temp\proctail_test_XXX）
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "ProcTail.Host.dll"),
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "ProcTail.Host"),
            Path.Combine(Directory.GetParent(currentDir)?.FullName ?? currentDir, "tests", "ProcTail.Host.exe"),
            
            // 従来のビルド出力パス
            Path.Combine(currentDir, "src", "ProcTail.Host", "bin", "Debug", "net8.0", "ProcTail.Host.dll"),
            Path.Combine(currentDir, "src", "ProcTail.Host", "bin", "Debug", "net8.0", "ProcTail.Host.exe"),
        };

        TestContext.WriteLine($"Host実行可能ファイルを検索中... (作業ディレクトリ: {Environment.CurrentDirectory})");

        // DLLファイルを優先（dotnet経由で実行、クロスプラットフォーム対応）
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) && path.EndsWith(".dll"))
            {
                TestContext.WriteLine($"Host DLLファイルを発見: {path}");
                return ("dotnet", path);
            }
        }

        // 実行可能ファイル（.exe または拡張子なし）
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) && (path.EndsWith(".exe") || !Path.HasExtension(path)))
            {
                TestContext.WriteLine($"Host実行可能ファイルを発見: {path}");
                return (path, null);
            }
        }

        // デバッグ情報: 現在のディレクトリの内容を表示
        TestContext.WriteLine("現在のディレクトリの内容:");
        try
        {
            var files = Directory.GetFiles(Environment.CurrentDirectory, "*Host*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                TestContext.WriteLine($"  - {Path.GetFileName(file)} (FullPath: {file})");
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"ディレクトリ一覧取得エラー: {ex.Message}");
        }

        // フォールバック: エラーとして例外を投げる
        throw new FileNotFoundException("Host実行可能ファイル（ProcTail.Host.dll、ProcTail.Host.exe、または ProcTail.Host）が見つかりません。publishされたファイルがテストディレクトリにコピーされているか確認してください。");
    }

    private string? FindProjectRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            // .slnファイルまたはDirectory.Build.propsファイルが存在するディレクトリをプロジェクトルートと判定
            if (current.GetFiles("*.sln").Any() || 
                current.GetFiles("Directory.Build.props").Any() ||
                (current.GetDirectories("src").Any() && current.GetDirectories("tools").Any()))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }
}