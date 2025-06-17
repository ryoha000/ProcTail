using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcTail.Application.Services;
using ProcTail.Core.Interfaces;
using ProcTail.Host.Workers;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Serilog;
using MSConfiguration = Microsoft.Extensions.Configuration;

namespace ProcTail.Host;

/// <summary>
/// ProcTail Windows Serviceのエントリーポイント
/// </summary>
public class Program
{
    /// <summary>
    /// メインエントリーポイント
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>非同期タスク</returns>
    public static async Task Main(string[] args)
    {
        // 早期ログ設定（Serilog初期化前）
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);
        
        var bootstrapLogger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logDirectory, "bootstrap-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        Log.Logger = bootstrapLogger;

        try
        {
            Log.Information("=== ProcTail Host Starting ===");
            Log.Information("Arguments: {Args}", string.Join(" ", args));
            Log.Information("Working Directory: {WorkingDirectory}", Environment.CurrentDirectory);
            Log.Information("Process Path: {ProcessPath}", Environment.ProcessPath);
            Log.Information("UserInteractive: {UserInteractive}", Environment.UserInteractive);

            // Windows環境チェック
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Log.Error("このアプリケーションはWindows環境でのみ動作します");
                Console.WriteLine("このアプリケーションはWindows環境でのみ動作します。");
                Environment.Exit(1);
                return;
            }
            Log.Information("Windows環境が確認されました");

            // 管理者権限チェック
            if (!IsRunningAsAdministrator())
            {
                Log.Warning("管理者権限が必要です");
                Console.WriteLine("このアプリケーションは管理者権限が必要です。");
                
                // UACプロンプトによる権限昇格を試行
                if (args.Length == 0 || !args.Contains("--no-uac"))
                {
                    Log.Information("UACプロンプトによる権限昇格を試行します");
                    await RequestAdministratorPrivilegesAsync(args);
                    return;
                }
                else
                {
                    Log.Error("管理者権限なしでは実行できません");
                    Console.WriteLine("管理者権限なしでは実行できません。");
                    Environment.Exit(1);
                    return;
                }
            }

            Log.Information("管理者権限で実行中です");
            Console.WriteLine("管理者権限で実行中です。");
            
            Log.Information("HostBuilderを作成中...");
            var host = CreateHostBuilder(args).Build();
            
            Log.Information("ホストを開始中...");
            // サービスとして実行
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "アプリケーション開始中に致命的エラーが発生しました");
            Console.WriteLine($"アプリケーション開始中にエラーが発生しました: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
            Environment.Exit(1);
        }
        finally
        {
            Log.Information("=== ProcTail Host Stopping ===");
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// ホストビルダーを作成
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>ホストビルダー</returns>
    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "ProcTail";
            });

        // 環境に応じてライフタイム設定を変更
        if (Environment.UserInteractive)
        {
            // 対話モード（開発・デバッグ）
            builder.UseConsoleLifetime();
        }

        return builder
            .UseSerilog((context, services, configuration) =>
            {
                // ログディレクトリを作成
                var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                
                configuration
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithProcessId()
                    .Enrich.WithThreadId()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, "proctail-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .WriteTo.EventLog(
                        source: "ProcTail",
                        logName: "Application",
                        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
            })
            .ConfigureLogging((context, logging) =>
            {
                // Serilogを使用するため、他のプロバイダーをクリア
                logging.ClearProviders();
            })
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services, context.Configuration);
            });
    }

    /// <summary>
    /// DIサービスを設定
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, MSConfiguration.IConfiguration configuration)
    {
        // 設定管理
        services.AddSingleton<Core.Interfaces.IConfigurationManager, Infrastructure.Configuration.ConfigurationManager>();
        services.AddSingleton<IEtwConfiguration>(provider => 
            provider.GetRequiredService<Core.Interfaces.IConfigurationManager>().GetConfiguration<EtwConfiguration>());
        services.AddSingleton<INamedPipeConfiguration>(provider => 
            provider.GetRequiredService<Core.Interfaces.IConfigurationManager>().GetConfiguration<NamedPipeConfiguration>());

        // インフラストラクチャ層
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
            services.AddSingleton<INamedPipeServer, WindowsNamedPipeServer>();
        }
        else
        {
            // 非Windowsプラットフォーム用のスタブ実装
            throw new PlatformNotSupportedException("このアプリケーションはWindows専用です。");
        }

        // アプリケーション層
        services.AddSingleton<IWatchTargetManager, WatchTargetManager>();
        services.AddSingleton<IEventProcessor, EventProcessor>();
        services.AddSingleton<IEventStorage>(provider => 
        {
            var logger = provider.GetRequiredService<ILogger<EventStorage>>();
            var configManager = provider.GetRequiredService<Core.Interfaces.IConfigurationManager>();
            
            // 設定から最大イベント数を取得
            var maxEvents = configuration.GetValue<int>("ProcTail:MaxEventsPerTag", 1000);
            return new EventStorage(logger, maxEvents);
        });

        // メインサービス
        services.AddSingleton<ProcTailService>();

        // ワーカーサービス
        services.AddHostedService<ProcTailWorker>();

        // ヘルスチェック
        services.AddHealthChecks()
            .AddCheck<ProcTailHealthCheck>("proctail_health");

        // 設定値検証
        services.AddOptions<ProcTailOptions>()
            .Bind(configuration.GetSection("ProcTail"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>
    /// 管理者権限で実行されているかチェック
    /// </summary>
    /// <returns>管理者権限で実行されている場合はtrue</returns>
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

    /// <summary>
    /// 管理者権限を要求してアプリケーションを再起動
    /// </summary>
    /// <param name="originalArgs">元のコマンドライン引数</param>
    /// <returns>非同期タスク</returns>
    [SupportedOSPlatform("windows")]
    private static async Task RequestAdministratorPrivilegesAsync(string[] originalArgs)
    {
        try
        {
            Console.WriteLine("UACプロンプトを表示して管理者権限で再起動します...");
            
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "ProcTail.Host.exe",
                Arguments = string.Join(" ", originalArgs),
                Verb = "runas", // UACプロンプトを表示
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            Console.WriteLine($"実行コマンド: {processInfo.FileName} {processInfo.Arguments}");
            
            // 管理者権限で再起動
            var process = Process.Start(processInfo);
            
            if (process != null)
            {
                Console.WriteLine("管理者権限でのプロセス起動に成功しました。");
                // 元のプロセスはそのまま終了させて、管理者権限でのプロセスに引き継ぐ
                await Task.Delay(1000); // 少し待機してからプロセス終了
            }
            else
            {
                Console.WriteLine("管理者権限でのプロセス起動に失敗しました。");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"管理者権限昇格エラー: {ex.Message}");
            
            // ユーザーがUACをキャンセルした場合など
            if (ex is System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 1223)
            {
                Console.WriteLine("ユーザーがUACプロンプトをキャンセルしました。");
            }
            
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// ProcTail設定オプション
/// </summary>
public class ProcTailOptions
{
    /// <summary>
    /// サービス名
    /// </summary>
    public string ServiceName { get; set; } = "ProcTail";

    /// <summary>
    /// 表示名
    /// </summary>
    public string DisplayName { get; set; } = "ProcTail Process Monitor";

    /// <summary>
    /// 説明
    /// </summary>
    public string Description { get; set; } = "Monitors process file operations and child process creation using ETW";

    /// <summary>
    /// 開始モード
    /// </summary>
    public string StartMode { get; set; } = "Automatic";

    /// <summary>
    /// データディレクトリ
    /// </summary>
    public string DataDirectory { get; set; } = "Data";

    /// <summary>
    /// タグあたりの最大イベント数
    /// </summary>
    public int MaxEventsPerTag { get; set; } = 1000;

    /// <summary>
    /// イベント保持日数
    /// </summary>
    public int EventRetentionDays { get; set; } = 7;

    /// <summary>
    /// メトリクス収集を有効にするか
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// メトリクス収集間隔
    /// </summary>
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// ヘルスチェック間隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}