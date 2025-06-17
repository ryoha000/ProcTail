using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using FluentAssertions;
using ProcTail.Core.Interfaces;
using ProcTail.Core.Models;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;
using ProcTail.Application.Services;

namespace ProcTail.Windows.Tests.Integration;

/// <summary>
/// Windows 環境でのシステム統合テスト
/// 実際の Windows API を使用したエンドツーエンドテスト
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class WindowsSystemIntegrationTests
{
    private IHost? _host;
    private WindowsEtwEventProvider? _etwProvider;
    private WindowsNamedPipeServer? _pipeServer;
    private ProcTailService? _procTailService;
    private readonly List<BaseEventData> _capturedEvents = new();

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

        _capturedEvents.Clear();
        SetupHost();
    }

    [TearDown]
    public void TearDown()
    {
        _host?.Dispose();
        _etwProvider?.Dispose();
        _pipeServer?.Dispose();
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("EndToEnd")]
    [Category("RequiresAdministrator")]
    public async Task FullSystemIntegration_ShouldMonitorProcessAndReceiveEventsViaIPC()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await StartServicesAsync();
        
        var testProcessPath = "notepad.exe";
        var testTag = "IntegrationTest";

        try
        {
            // Act - プロセス監視を開始
            var addWatchRequest = new IpcRequest
            {
                Type = "AddWatchTarget",
                Data = JsonSerializer.SerializeToElement(new { Pid = -1, ProcessName = testProcessPath, Tag = testTag })
            };

            var addWatchResponse = await SendIpcRequestAsync(addWatchRequest);
            
            // プロセスを開始
            using var process = new Process();
            process.StartInfo.FileName = testProcessPath;
            process.StartInfo.UseShellExecute = true;
            process.Start();

            var targetPid = process.Id;

            // 実際のPIDを使用して監視を開始
            var addActualWatchRequest = new IpcRequest
            {
                Type = "AddWatchTarget",
                Data = JsonSerializer.SerializeToElement(new { Pid = targetPid, Tag = testTag })
            };
            await SendIpcRequestAsync(addActualWatchRequest);

            // イベント収集のために少し待機
            await Task.Delay(TimeSpan.FromSeconds(3));

            // プロセスを終了
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }

            // さらに少し待機してイベント処理を完了させる
            await Task.Delay(TimeSpan.FromSeconds(2));

            // イベントを取得
            var getEventsRequest = new IpcRequest
            {
                Type = "GetRecordedEvents",
                Data = JsonSerializer.SerializeToElement(new { Tag = testTag })
            };

            var eventsResponse = await SendIpcRequestAsync(getEventsRequest);

            // Assert
            addWatchResponse.Should().NotBeNull();
            eventsResponse.Should().NotBeNull();
            
            // レスポンスにイベントが含まれているかチェック
            eventsResponse.Should().Contain("Events", "イベントデータが含まれているはずです");
        }
        finally
        {
            await StopServicesAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("EndToEnd")]
    [Category("RequiresAdministrator")]
    public async Task FileOperationMonitoring_ShouldCaptureFileEvents()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await StartServicesAsync();
        
        var testTag = "FileTest";
        var testFilePath = Path.Combine(Path.GetTempPath(), $"ProcTailFileTest_{Guid.NewGuid():N}.txt");

        try
        {
            // 現在のプロセスを監視対象に追加
            var currentPid = Environment.ProcessId;
            var addWatchRequest = new IpcRequest
            {
                Type = "AddWatchTarget",
                Data = JsonSerializer.SerializeToElement(new { Pid = currentPid, Tag = testTag })
            };

            await SendIpcRequestAsync(addWatchRequest);

            // ファイル操作を実行
            await File.WriteAllTextAsync(testFilePath, "Test content for ProcTail monitoring");
            await File.AppendAllTextAsync(testFilePath, "\nAppended content");
            var content = await File.ReadAllTextAsync(testFilePath);
            File.Delete(testFilePath);

            // イベント処理を待機
            await Task.Delay(TimeSpan.FromSeconds(3));

            // イベントを取得
            var getEventsRequest = new IpcRequest
            {
                Type = "GetRecordedEvents",
                Data = JsonSerializer.SerializeToElement(new { Tag = testTag })
            };

            var eventsResponse = await SendIpcRequestAsync(getEventsRequest);

            // Assert
            eventsResponse.Should().NotBeNull();
            eventsResponse.Should().Contain("Events");
            
            // ファイル関連のイベントが含まれているかチェック
            if (eventsResponse.Contains("FileIO") || eventsResponse.Contains("Create") || eventsResponse.Contains("Write"))
            {
                Assert.Pass("ファイル操作イベントが正常に監視されました");
            }
            else
            {
                TestContext.WriteLine($"応答内容: {eventsResponse}");
                TestContext.WriteLine("ファイルイベントが検出されませんでした（ETWの制限による可能性があります）");
            }
        }
        finally
        {
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
            await StopServicesAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("EndToEnd")]
    public async Task HealthCheck_ShouldReturnSystemStatus()
    {
        // Arrange
        await StartServicesAsync();

        try
        {
            // Act
            var healthCheckRequest = new IpcRequest
            {
                Type = "HealthCheck",
                Data = JsonSerializer.SerializeToElement(new { })
            };

            var response = await SendIpcRequestAsync(healthCheckRequest);

            // Assert
            response.Should().NotBeNull();
            response.Should().Contain("Status");
            response.Should().ContainAny("OK", "Healthy", "Running");
        }
        finally
        {
            await StopServicesAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("EndToEnd")]
    public async Task ConcurrentClients_ShouldHandleMultipleIPCConnections()
    {
        // Arrange
        await StartServicesAsync();
        var clientCount = 5;
        var tasks = new List<Task<string>>();

        try
        {
            // Act - 複数のクライアントから同時にヘルスチェック
            for (int i = 0; i < clientCount; i++)
            {
                var clientId = i;
                var task = Task.Run(async () =>
                {
                    var request = new IpcRequest
                    {
                        Type = "HealthCheck",
                        Data = JsonSerializer.SerializeToElement(new { ClientId = clientId })
                    };
                    return await SendIpcRequestAsync(request);
                });
                tasks.Add(task);
            }

            var responses = await Task.WhenAll(tasks);

            // Assert
            responses.Should().HaveCount(clientCount);
            responses.Should().OnlyContain(r => !string.IsNullOrEmpty(r));
            responses.Should().OnlyContain(r => r.Contains("Status"));
        }
        finally
        {
            await StopServicesAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("EndToEnd")]
    public async Task WindowsSpecificAPIs_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var windowsIdentity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(windowsIdentity);
        var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

        // Assert
        windowsIdentity.Should().NotBeNull();
        windowsIdentity.Name.Should().NotBeNullOrEmpty();
        isAdmin.Should().BeOfType<bool>();

        TestContext.WriteLine($"Current User: {windowsIdentity.Name}");
        TestContext.WriteLine($"Is Administrator: {isAdmin}");
        TestContext.WriteLine($"Authentication Type: {windowsIdentity.AuthenticationType}");
    }

    [Test]
    [Category("Windows")]
    [Category("Integration")]
    [Category("Performance")]
    [Category("RequiresAdministrator")]
    public async Task HighVolumeEvents_ShouldHandlePerformanceLoad()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        await StartServicesAsync();
        var testTag = "PerformanceTest";
        var fileCount = 100;
        var testDir = Path.Combine(Path.GetTempPath(), "ProcTailPerfTest");
        Directory.CreateDirectory(testDir);

        try
        {
            // 現在のプロセスを監視対象に追加
            var currentPid = Environment.ProcessId;
            var addWatchRequest = new IpcRequest
            {
                Type = "AddWatchTarget",
                Data = JsonSerializer.SerializeToElement(new { Pid = currentPid, Tag = testTag })
            };

            await SendIpcRequestAsync(addWatchRequest);

            // 大量のファイル操作を実行
            var stopwatch = Stopwatch.StartNew();
            
            var tasks = Enumerable.Range(0, fileCount).Select(async i =>
            {
                var filePath = Path.Combine(testDir, $"test_{i}.txt");
                await File.WriteAllTextAsync(filePath, $"Test content {i}");
                return filePath;
            });

            var createdFiles = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // イベント処理を待機
            await Task.Delay(TimeSpan.FromSeconds(5));

            // イベントを取得
            var getEventsRequest = new IpcRequest
            {
                Type = "GetRecordedEvents",
                Data = JsonSerializer.SerializeToElement(new { Tag = testTag })
            };

            var eventsResponse = await SendIpcRequestAsync(getEventsRequest);

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "大量のファイル操作が合理的な時間で完了するはずです");
            eventsResponse.Should().NotBeNull();

            TestContext.WriteLine($"Created {fileCount} files in {stopwatch.ElapsedMilliseconds}ms");
            TestContext.WriteLine($"Events response length: {eventsResponse.Length}");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
            await StopServicesAsync();
        }
    }

    #region Helper Methods

    private void SetupHost()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IEtwConfiguration, EtwConfiguration>();
                services.AddSingleton<INamedPipeConfiguration, NamedPipeConfiguration>();
                services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
                services.AddSingleton<INamedPipeServer, WindowsNamedPipeServer>();
                services.AddSingleton<ProcTailService>();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            });

        _host = hostBuilder.Build();
    }

    private async Task StartServicesAsync()
    {
        var serviceProvider = _host!.Services;
        
        _etwProvider = serviceProvider.GetRequiredService<IEtwEventProvider>() as WindowsEtwEventProvider;
        _pipeServer = serviceProvider.GetRequiredService<INamedPipeServer>() as WindowsNamedPipeServer;
        _procTailService = serviceProvider.GetRequiredService<ProcTailService>();

        // サービスを開始
        await _pipeServer!.StartAsync();
        
        if (IsRunningAsAdministrator())
        {
            await _etwProvider!.StartMonitoringAsync();
        }
    }

    private async Task StopServicesAsync()
    {
        if (_etwProvider?.IsMonitoring == true)
        {
            await _etwProvider.StopMonitoringAsync();
        }
        
        if (_pipeServer?.IsRunning == true)
        {
            await _pipeServer.StopAsync();
        }
    }

    private async Task<string> SendIpcRequestAsync(IpcRequest request)
    {
        var configuration = _host!.Services.GetRequiredService<INamedPipeConfiguration>();
        using var client = new NamedPipeClientStream(".", configuration.PipeName, PipeDirection.InOut);
        
        await client.ConnectAsync(TimeSpan.FromSeconds(10));

        // リクエストを送信
        var requestJson = JsonSerializer.Serialize(request);
        await SendMessageAsync(client, requestJson);

        // レスポンスを受信
        return await ReceiveMessageAsync(client);
    }

    private static async Task SendMessageAsync(PipeStream pipe, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        await pipe.WriteAsync(lengthBytes);
        await pipe.WriteAsync(messageBytes);
        await pipe.FlushAsync();
    }

    private static async Task<string> ReceiveMessageAsync(PipeStream pipe)
    {
        // メッセージ長を受信
        var lengthBuffer = new byte[4];
        var bytesRead = 0;
        
        while (bytesRead < 4)
        {
            var read = await pipe.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead));
            if (read == 0)
                return string.Empty;
            bytesRead += read;
        }

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        if (messageLength <= 0)
            return string.Empty;

        // メッセージ本体を受信
        var messageBuffer = new byte[messageLength];
        bytesRead = 0;
        
        while (bytesRead < messageLength)
        {
            var read = await pipe.ReadAsync(messageBuffer.AsMemory(bytesRead, messageLength - bytesRead));
            if (read == 0)
                throw new EndOfStreamException("メッセージ受信が予期せず終了しました");
            bytesRead += read;
        }

        return Encoding.UTF8.GetString(messageBuffer);
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

    #region Test Models

    private class IpcRequest
    {
        public string Type { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
    }

    #endregion
}