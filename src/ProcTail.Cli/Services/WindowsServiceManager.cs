using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;

namespace ProcTail.Cli.Services;

/// <summary>
/// Windowsサービス管理インターフェース
/// </summary>
public interface IWindowsServiceManager
{
    Task<bool> StartServiceAsync(string serviceName);
    Task<bool> StopServiceAsync(string serviceName);
    Task<bool> RestartServiceAsync(string serviceName);
    Task<bool> InstallServiceAsync(string serviceName, string binaryPath, string displayName, string description);
    Task<bool> UninstallServiceAsync(string serviceName);
    Task<ServiceStatus> GetServiceStatusAsync(string serviceName);
    Task<bool> IsServiceInstalledAsync(string serviceName);
}

/// <summary>
/// サービス状態
/// </summary>
public enum ServiceStatus
{
    NotFound,
    Stopped,
    Running,
    Paused,
    StartPending,
    StopPending,
    ContinuePending,
    PausePending
}

/// <summary>
/// Windowsサービス管理実装
/// </summary>
public class WindowsServiceManager : IWindowsServiceManager
{
    private readonly ILogger<WindowsServiceManager> _logger;

    public WindowsServiceManager(ILogger<WindowsServiceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// サービスを開始
    /// </summary>
    public async Task<bool> StartServiceAsync(string serviceName)
    {
        try
        {
            _logger.LogInformation("サービス '{ServiceName}' を開始しています...", serviceName);

            using var service = new ServiceController(serviceName);
            
            if (service.Status == ServiceControllerStatus.Running)
            {
                _logger.LogInformation("サービス '{ServiceName}' は既に実行中です", serviceName);
                return true;
            }

            if (service.Status == ServiceControllerStatus.StartPending)
            {
                _logger.LogInformation("サービス '{ServiceName}' は開始処理中です", serviceName);
                await WaitForStatusAsync(service, ServiceControllerStatus.Running, TimeSpan.FromMinutes(2));
                return service.Status == ServiceControllerStatus.Running;
            }

            service.Start();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, TimeSpan.FromMinutes(2));

            if (service.Status == ServiceControllerStatus.Running)
            {
                _logger.LogInformation("サービス '{ServiceName}' が正常に開始されました", serviceName);
                return true;
            }
            else
            {
                _logger.LogError("サービス '{ServiceName}' の開始に失敗しました。現在の状態: {Status}", 
                    serviceName, service.Status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' の開始中にエラーが発生しました", serviceName);
            return false;
        }
    }

    /// <summary>
    /// サービスを停止
    /// </summary>
    public async Task<bool> StopServiceAsync(string serviceName)
    {
        try
        {
            _logger.LogInformation("サービス '{ServiceName}' を停止しています...", serviceName);

            using var service = new ServiceController(serviceName);
            
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                _logger.LogInformation("サービス '{ServiceName}' は既に停止中です", serviceName);
                return true;
            }

            if (service.Status == ServiceControllerStatus.StopPending)
            {
                _logger.LogInformation("サービス '{ServiceName}' は停止処理中です", serviceName);
                await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(2));
                return service.Status == ServiceControllerStatus.Stopped;
            }

            if (service.CanStop)
            {
                service.Stop();
                await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(2));

                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    _logger.LogInformation("サービス '{ServiceName}' が正常に停止されました", serviceName);
                    return true;
                }
                else
                {
                    _logger.LogError("サービス '{ServiceName}' の停止に失敗しました。現在の状態: {Status}", 
                        serviceName, service.Status);
                    return false;
                }
            }
            else
            {
                _logger.LogError("サービス '{ServiceName}' は停止できません", serviceName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' の停止中にエラーが発生しました", serviceName);
            return false;
        }
    }

    /// <summary>
    /// サービスを再起動
    /// </summary>
    public async Task<bool> RestartServiceAsync(string serviceName)
    {
        try
        {
            _logger.LogInformation("サービス '{ServiceName}' を再起動しています...", serviceName);

            var stopped = await StopServiceAsync(serviceName);
            if (!stopped)
            {
                _logger.LogError("サービス停止に失敗したため、再起動を中止します");
                return false;
            }

            // 停止完了まで少し待機
            await Task.Delay(TimeSpan.FromSeconds(2));

            var started = await StartServiceAsync(serviceName);
            if (started)
            {
                _logger.LogInformation("サービス '{ServiceName}' が正常に再起動されました", serviceName);
                return true;
            }
            else
            {
                _logger.LogError("サービス再起動に失敗しました");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' の再起動中にエラーが発生しました", serviceName);
            return false;
        }
    }

    /// <summary>
    /// サービスをインストール
    /// </summary>
    public async Task<bool> InstallServiceAsync(string serviceName, string binaryPath, string displayName, string description)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogError("サービスインストールはWindows環境でのみサポートされています");
                return false;
            }

            _logger.LogInformation("サービス '{ServiceName}' をインストールしています...", serviceName);

            // 管理者権限チェック
            if (!IsRunningAsAdministrator())
            {
                _logger.LogError("サービスのインストールには管理者権限が必要です");
                return false;
            }

            // 既にインストールされているかチェック
            if (await IsServiceInstalledAsync(serviceName))
            {
                _logger.LogInformation("サービス '{ServiceName}' は既にインストールされています", serviceName);
                return true;
            }

            // sc.exe コマンドを使用してサービスをインストール
            var arguments = $"create \"{serviceName}\" binpath= \"{binaryPath}\" start= auto DisplayName= \"{displayName}\"";
            var processInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("sc.exe プロセスの開始に失敗しました");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("サービス '{ServiceName}' が正常にインストールされました", serviceName);
                
                // 説明を設定
                if (!string.IsNullOrEmpty(description))
                {
                    await SetServiceDescriptionAsync(serviceName, description);
                }
                
                return true;
            }
            else
            {
                _logger.LogError("サービスインストールに失敗しました。終了コード: {ExitCode}, エラー: {Error}", 
                    process.ExitCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' のインストール中にエラーが発生しました", serviceName);
            return false;
        }
    }

    /// <summary>
    /// サービスをアンインストール
    /// </summary>
    public async Task<bool> UninstallServiceAsync(string serviceName)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogError("サービスアンインストールはWindows環境でのみサポートされています");
                return false;
            }

            _logger.LogInformation("サービス '{ServiceName}' をアンインストールしています...", serviceName);

            // 管理者権限チェック
            if (!IsRunningAsAdministrator())
            {
                _logger.LogError("サービスのアンインストールには管理者権限が必要です");
                return false;
            }

            // サービスが存在するかチェック
            if (!await IsServiceInstalledAsync(serviceName))
            {
                _logger.LogInformation("サービス '{ServiceName}' は存在しません", serviceName);
                return true;
            }

            // サービスが実行中の場合は停止
            var status = await GetServiceStatusAsync(serviceName);
            if (status == ServiceStatus.Running)
            {
                _logger.LogInformation("サービスを停止してからアンインストールします");
                await StopServiceAsync(serviceName);
            }

            // sc.exe コマンドを使用してサービスをアンインストール
            var arguments = $"delete \"{serviceName}\"";
            var processInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("sc.exe プロセスの開始に失敗しました");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("サービス '{ServiceName}' が正常にアンインストールされました", serviceName);
                return true;
            }
            else
            {
                _logger.LogError("サービスアンインストールに失敗しました。終了コード: {ExitCode}, エラー: {Error}", 
                    process.ExitCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' のアンインストール中にエラーが発生しました", serviceName);
            return false;
        }
    }

    /// <summary>
    /// サービス状態を取得
    /// </summary>
    public async Task<ServiceStatus> GetServiceStatusAsync(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            
            return service.Status switch
            {
                ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
                ServiceControllerStatus.Running => ServiceStatus.Running,
                ServiceControllerStatus.Paused => ServiceStatus.Paused,
                ServiceControllerStatus.StartPending => ServiceStatus.StartPending,
                ServiceControllerStatus.StopPending => ServiceStatus.StopPending,
                ServiceControllerStatus.ContinuePending => ServiceStatus.ContinuePending,
                ServiceControllerStatus.PausePending => ServiceStatus.PausePending,
                _ => ServiceStatus.NotFound
            };
        }
        catch (InvalidOperationException)
        {
            // サービスが存在しない
            return ServiceStatus.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス '{ServiceName}' の状態取得中にエラーが発生しました", serviceName);
            return ServiceStatus.NotFound;
        }
    }

    /// <summary>
    /// サービスがインストールされているかチェック
    /// </summary>
    public async Task<bool> IsServiceInstalledAsync(string serviceName)
    {
        try
        {
            var status = await GetServiceStatusAsync(serviceName);
            return status != ServiceStatus.NotFound;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// サービス状態の変更を待機
    /// </summary>
    private async Task WaitForStatusAsync(ServiceController service, ServiceControllerStatus desiredStatus, TimeSpan timeout)
    {
        var timeoutTime = DateTime.Now.Add(timeout);
        
        while (DateTime.Now < timeoutTime)
        {
            service.Refresh();
            if (service.Status == desiredStatus)
            {
                return;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        
        _logger.LogWarning("サービス状態変更のタイムアウトが発生しました。期待値: {DesiredStatus}, 現在値: {CurrentStatus}", 
            desiredStatus, service.Status);
    }

    /// <summary>
    /// サービス説明を設定
    /// </summary>
    private async Task<bool> SetServiceDescriptionAsync(string serviceName, string description)
    {
        try
        {
            var arguments = $"description \"{serviceName}\" \"{description}\"";
            var processInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 管理者権限で実行されているかチェック
    /// </summary>
    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}