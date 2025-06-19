using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcTail.Cli.Commands;
using ProcTail.Cli.Services;

namespace ProcTail.Cli;

/// <summary>
/// ProcTail CLIツールのエントリーポイント
/// </summary>
public class Program
{
    /// <summary>
    /// メインエントリーポイント
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>終了コード</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Windows環境チェック
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("このアプリケーションはWindows環境でのみ動作します。");
                return 1;
            }

            // 管理者権限チェック（一部のコマンドのみ）
            if (RequiresAdministratorPrivileges(args))
            {
                if (!IsRunningAsAdministrator())
                {
                    Console.WriteLine("このコマンドは管理者権限が必要です。");
                    
                    // UACプロンプトによる権限昇格を試行
                    if (!args.Contains("--no-uac"))
                    {
                        await RequestAdministratorPrivilegesAsync(args);
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine("管理者権限なしでは実行できません。");
                        return 1;
                    }
                }
            }

            var rootCommand = CreateRootCommand();
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            Console.ResetColor();
            
#if DEBUG
            Console.WriteLine($"詳細: {ex}");
#endif
            return 1;
        }
    }

    /// <summary>
    /// ルートコマンドを作成
    /// </summary>
    private static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("ProcTail - プロセス監視ツール")
        {
            Description = "ETWを使用してプロセスのファイル操作と子プロセス作成を監視します。"
        };

        // グローバルオプション
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "詳細な出力を表示");

        var configOption = new Option<string?>(
            aliases: new[] { "--config", "-c" },
            description: "設定ファイルのパス");

        var pipeNameOption = new Option<string>(
            aliases: new[] { "--pipe-name", "-p" },
            getDefaultValue: () => "ProcTail",
            description: "Named Pipeの名前");

        var noUacOption = new Option<bool>(
            aliases: new[] { "--no-uac" },
            description: "UACプロンプトを無効にする（管理者権限なしで実行失敗）");

        rootCommand.AddGlobalOption(verboseOption);
        rootCommand.AddGlobalOption(configOption);
        rootCommand.AddGlobalOption(pipeNameOption);
        rootCommand.AddGlobalOption(noUacOption);

        // サブコマンドを追加
        rootCommand.AddCommand(CreateAddCommand());
        rootCommand.AddCommand(CreateRemoveCommand());
        rootCommand.AddCommand(CreateListCommand());
        rootCommand.AddCommand(CreateEventsCommand());
        rootCommand.AddCommand(CreateStatusCommand());
        rootCommand.AddCommand(CreateClearCommand());
        rootCommand.AddCommand(CreateServiceCommand());
        rootCommand.AddCommand(CreateCleanupCommand());
        rootCommand.AddCommand(CreateVersionCommand());

        return rootCommand;
    }

    /// <summary>
    /// addコマンドを作成
    /// </summary>
    private static Command CreateAddCommand()
    {
        var processIdOption = new Option<int>(
            aliases: new[] { "--process-id", "--pid" },
            description: "監視対象のプロセスID");

        var processNameOption = new Option<string?>(
            aliases: new[] { "--process-name", "--name" },
            description: "監視対象のプロセス名");

        var tagOption = new Option<string>(
            aliases: new[] { "--tag", "-t" },
            description: "監視対象に付けるタグ名")
        {
            IsRequired = true
        };

        var addCommand = new Command("add", "監視対象を追加")
        {
            processIdOption,
            processNameOption,
            tagOption
        };

        addCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new AddWatchTargetCommand(client);
            await command.ExecuteAsync(context);
        });

        return addCommand;
    }

    /// <summary>
    /// removeコマンドを作成
    /// </summary>
    private static Command CreateRemoveCommand()
    {
        var tagOption = new Option<string>(
            aliases: new[] { "--tag", "-t" },
            description: "削除するタグ名")
        {
            IsRequired = true
        };

        var removeCommand = new Command("remove", "監視対象を削除")
        {
            tagOption
        };

        removeCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new RemoveWatchTargetCommand(client);
            await command.ExecuteAsync(context);
        });

        return removeCommand;
    }

    /// <summary>
    /// listコマンドを作成
    /// </summary>
    private static Command CreateListCommand()
    {
        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => "table",
            description: "出力形式 (table, json, csv)");

        var listCommand = new Command("list", "監視対象一覧を表示")
        {
            formatOption
        };

        listCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new ListWatchTargetsCommand(client);
            await command.ExecuteAsync(context);
        });

        return listCommand;
    }

    /// <summary>
    /// eventsコマンドを作成
    /// </summary>
    private static Command CreateEventsCommand()
    {
        var tagOption = new Option<string>(
            aliases: new[] { "--tag", "-t" },
            description: "取得するタグ名")
        {
            IsRequired = true
        };

        var countOption = new Option<int>(
            aliases: new[] { "--count", "-n" },
            getDefaultValue: () => 50,
            description: "取得するイベント数");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => "table",
            description: "出力形式 (table, json, csv)");

        var followOption = new Option<bool>(
            aliases: new[] { "--follow" },
            description: "リアルタイムでイベントを表示");

        var eventsCommand = new Command("events", "記録されたイベントを表示")
        {
            tagOption,
            countOption,
            formatOption,
            followOption
        };

        eventsCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new GetEventsCommand(client);
            await command.ExecuteAsync(context);
        });

        return eventsCommand;
    }

    /// <summary>
    /// statusコマンドを作成
    /// </summary>
    private static Command CreateStatusCommand()
    {
        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => "table",
            description: "出力形式 (table, json)");

        var statusCommand = new Command("status", "サービス状態を表示")
        {
            formatOption
        };

        statusCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new GetStatusCommand(client);
            await command.ExecuteAsync(context);
        });

        return statusCommand;
    }

    /// <summary>
    /// clearコマンドを作成
    /// </summary>
    private static Command CreateClearCommand()
    {
        var tagOption = new Option<string>(
            aliases: new[] { "--tag", "-t" },
            description: "クリアするタグ名")
        {
            IsRequired = true
        };

        var confirmOption = new Option<bool>(
            aliases: new[] { "--yes", "-y" },
            description: "確認をスキップ");

        var clearCommand = new Command("clear", "記録されたイベントをクリア")
        {
            tagOption,
            confirmOption
        };

        clearCommand.SetHandler(async (context) =>
        {
            var client = CreatePipeClient(context);
            var command = new ClearEventsCommand(client);
            await command.ExecuteAsync(context);
        });

        return clearCommand;
    }

    /// <summary>
    /// serviceコマンドを作成
    /// </summary>
    private static Command CreateServiceCommand()
    {
        var startCommand = new Command("start", "サービスを開始");
        var stopCommand = new Command("stop", "サービスを停止");
        var restartCommand = new Command("restart", "サービスを再起動");
        var installCommand = new Command("install", "サービスをインストール");
        var uninstallCommand = new Command("uninstall", "サービスをアンインストール");

        var serviceCommand = new Command("service", "サービス管理")
        {
            startCommand,
            stopCommand,
            restartCommand,
            installCommand,
            uninstallCommand
        };

        startCommand.SetHandler(async (context) =>
        {
            var serviceManager = CreateServiceManager();
            var command = new ServiceStartCommand(serviceManager);
            await command.ExecuteAsync(context);
        });

        stopCommand.SetHandler(async (context) =>
        {
            var serviceManager = CreateServiceManager();
            var command = new ServiceStopCommand(serviceManager);
            await command.ExecuteAsync(context);
        });

        restartCommand.SetHandler(async (context) =>
        {
            var serviceManager = CreateServiceManager();
            var command = new ServiceRestartCommand(serviceManager);
            await command.ExecuteAsync(context);
        });

        installCommand.SetHandler(async (context) =>
        {
            var serviceManager = CreateServiceManager();
            var command = new ServiceInstallCommand(serviceManager);
            await command.ExecuteAsync(context);
        });

        uninstallCommand.SetHandler(async (context) =>
        {
            var serviceManager = CreateServiceManager();
            var command = new ServiceUninstallCommand(serviceManager);
            await command.ExecuteAsync(context);
        });

        return serviceCommand;
    }

    /// <summary>
    /// cleanupコマンドを作成
    /// </summary>
    private static Command CreateCleanupCommand()
    {
        return CleanupCommand.CreateCommand();
    }

    /// <summary>
    /// versionコマンドを作成
    /// </summary>
    private static Command CreateVersionCommand()
    {
        var versionCommand = new Command("version", "バージョン情報を表示");

        versionCommand.SetHandler(() =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "不明";
            
            // Single-file deployment対応: assembly.Locationが空の場合はProcess.GetCurrentProcess().MainModule.FileNameを使用
            var assemblyLocation = assembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation))
            {
                assemblyLocation = System.AppContext.BaseDirectory;
                var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                if (processModule?.FileName != null)
                {
                    assemblyLocation = processModule.FileName;
                }
            }
            
            var fileVersion = "不明";
            var buildDate = DateTime.MinValue;
            
            try
            {
                if (!string.IsNullOrEmpty(assemblyLocation) && System.IO.File.Exists(assemblyLocation))
                {
                    fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(assemblyLocation).FileVersion ?? "不明";
                    buildDate = System.IO.File.GetCreationTime(assemblyLocation);
                }
                else
                {
                    // Fallback: アセンブリの属性から情報を取得
                    var fileVersionAttr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>(assembly);
                    if (fileVersionAttr != null)
                    {
                        fileVersion = fileVersionAttr.Version;
                    }
                    buildDate = DateTime.Now; // Fallback
                }
            }
            catch
            {
                // エラーが発生した場合はデフォルト値を使用
            }

            Console.WriteLine("ProcTail CLI Tool");
            Console.WriteLine($"バージョン: {version}");
            Console.WriteLine($"ファイルバージョン: {fileVersion}");
            Console.WriteLine($"ビルド日時: {buildDate:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"実行環境: .NET {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"アーキテクチャ: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine();
            Console.WriteLine("Copyright (c) 2025 ProcTail Project");
            Console.WriteLine("ETWを使用したプロセス・ファイル操作監視ツール");
        });

        return versionCommand;
    }

    /// <summary>
    /// Named Pipeクライアントを作成
    /// </summary>
    private static IProcTailPipeClient CreatePipeClient(InvocationContext context)
    {
        var pipeName = "ProcTail";
        var verbose = false;
        
        // グローバルオプション値を取得
        foreach (var option in context.ParseResult.RootCommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            switch (option.Name)
            {
                case "pipe-name":
                    pipeName = value as string ?? "ProcTail";
                    break;
                case "verbose":
                    verbose = (bool?)value ?? false;
                    break;
            }
        }

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            if (verbose)
            {
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.Information);
            }
        });

        services.AddSingleton<IProcTailPipeClient>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ProcTailPipeClient>>();
            return new ProcTailPipeClient(logger, pipeName);
        });

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IProcTailPipeClient>();
    }

    /// <summary>
    /// サービス管理を作成
    /// </summary>
    private static Services.IWindowsServiceManager CreateServiceManager()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<Services.IWindowsServiceManager, Services.WindowsServiceManager>();

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<Services.IWindowsServiceManager>();
    }

    /// <summary>
    /// 指定されたコマンドが管理者権限を必要とするかチェック
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>管理者権限が必要な場合はtrue</returns>
    private static bool RequiresAdministratorPrivileges(string[] args)
    {
        if (args.Length == 0) return false;

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "start" => true,        // ETW監視の開始
            "service" => true,      // Windowsサービス管理
            "install" => true,      // サービスインストール
            "uninstall" => true,    // サービスアンインストール
            _ => false
        };
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
                FileName = Environment.ProcessPath ?? "proctail.exe",
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
                await Task.Delay(1000); // 少し待機
            }
            else
            {
                Console.WriteLine("管理者権限でのプロセス起動に失敗しました。");
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
        }
    }
}