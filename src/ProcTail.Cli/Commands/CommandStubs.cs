using System.CommandLine.Invocation;
using ProcTail.Cli.Services;

namespace ProcTail.Cli.Commands;

// 他のコマンドクラスの基本実装

public class RemoveWatchTargetCommand : BaseCommand
{
    public RemoveWatchTargetCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        var tagName = "";
        var skipConfirm = false;
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            switch (option.Name)
            {
                case "tag":
                    tagName = value as string ?? "";
                    break;
                case "yes":
                    skipConfirm = (bool?)value ?? false;
                    break;
            }
        }
        
        if (string.IsNullOrWhiteSpace(tagName))
        {
            WriteError("タグ名が指定されていません。--tag オプションでタグ名を指定してください。");
            context.ExitCode = 1;
            return;
        }
        
        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        if (!skipConfirm && !PromptConfirmation($"タグ '{tagName}' の監視対象を削除しますか？"))
        {
            WriteInfo("操作をキャンセルしました。");
            return;
        }

        try
        {
            var response = await _pipeClient.RemoveWatchTargetAsync(tagName, context.GetCancellationToken());

            if (response.Success)
            {
                WriteSuccess($"タグ '{tagName}' の監視対象を削除しました。");
            }
            else
            {
                WriteError($"監視対象削除に失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"監視対象削除中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

public class ListWatchTargetsCommand : BaseCommand
{
    public ListWatchTargetsCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        var format = "table";
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            if (option.Name == "format")
            {
                format = value as string ?? "table";
                break;
            }
        }
        
        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        try
        {
            var response = await _pipeClient.GetWatchTargetsAsync(context.GetCancellationToken());
            
            if (response.Success)
            {
                if (response.WatchTargets.Count == 0)
                {
                    WriteInfo("監視対象はありません。");
                    return;
                }

                switch (format.ToLowerInvariant())
                {
                    case "json":
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response.WatchTargets, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        break;
                    case "csv":
                        OutputCsvFormat(response.WatchTargets);
                        break;
                    default: // table
                        OutputTableFormat(response.WatchTargets);
                        break;
                }
            }
            else
            {
                WriteError($"監視対象一覧取得に失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"監視対象一覧取得中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }

    private void OutputTableFormat(List<ProcTail.Core.Models.WatchTargetInfo> watchTargets)
    {
        const int pidWidth = 8;
        const int tagWidth = 15;
        const int nameWidth = 20;
        const int startTimeWidth = 20;
        const int pathWidth = 50;

        // ヘッダー
        Console.WriteLine($"{"PID".PadRight(pidWidth)} {"Tag".PadRight(tagWidth)} {"Process Name".PadRight(nameWidth)} {"Start Time".PadRight(startTimeWidth)} {"Executable Path".PadRight(pathWidth)}");
        Console.WriteLine(new string('-', pidWidth + tagWidth + nameWidth + startTimeWidth + pathWidth + 4));

        foreach (var target in watchTargets.OrderBy(t => t.TagName).ThenBy(t => t.ProcessId))
        {
            var pid = target.ProcessId.ToString().PadRight(pidWidth);
            var tag = TruncateString(target.TagName, tagWidth);
            var name = TruncateString(target.ProcessName, nameWidth);
            var startTime = target.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm").PadRight(startTimeWidth);
            var path = TruncateString(target.ExecutablePath, pathWidth);

            Console.WriteLine($"{pid} {tag} {name} {startTime} {path}");
        }
    }

    private void OutputCsvFormat(List<ProcTail.Core.Models.WatchTargetInfo> watchTargets)
    {
        Console.WriteLine("PID,Tag,ProcessName,StartTime,ExecutablePath");
        foreach (var target in watchTargets.OrderBy(t => t.TagName).ThenBy(t => t.ProcessId))
        {
            Console.WriteLine($"{target.ProcessId},\"{EscapeCsv(target.TagName)}\",\"{EscapeCsv(target.ProcessName)}\",\"{target.StartTime:yyyy-MM-dd HH:mm:ss}\",\"{EscapeCsv(target.ExecutablePath)}\"");
        }
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return "".PadRight(maxLength);
        
        return value.Length > maxLength ? (value.Substring(0, maxLength - 3) + "...").PadRight(maxLength) : value.PadRight(maxLength);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        
        return value.Replace("\"", "\"\"");
    }
}

public class GetStatusCommand : BaseCommand
{
    public GetStatusCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        var format = "table";
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            if (option.Name == "format")
            {
                format = value as string ?? "table";
                break;
            }
        }

        try
        {
            var response = await _pipeClient.GetStatusAsync(context.GetCancellationToken());

            if (response.Success)
            {
                if (format.ToLowerInvariant() == "json")
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    WriteSuccess($"ProcTailサービス状態: {(response.IsRunning ? "実行中" : "停止中")}");
                    Console.WriteLine($"ETW監視: {(response.IsEtwMonitoring ? "有効" : "無効")}");
                    Console.WriteLine($"Named Pipeサーバー: {(response.IsPipeServerRunning ? "実行中" : "停止中")}");
                    Console.WriteLine($"アクティブな監視対象: {response.ActiveWatchTargets}");
                    Console.WriteLine($"総タグ数: {response.TotalTags}");
                    Console.WriteLine($"総イベント数: {response.TotalEvents}");
                    Console.WriteLine($"推定メモリ使用量: {response.EstimatedMemoryUsageMB}MB");
                }
            }
            else
            {
                WriteError($"状態取得に失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービスに接続できませんでした: {ex.Message}");
            WriteInfo("サービスが起動していることを確認してください");
            context.ExitCode = 1;
        }
    }
}

public class ClearEventsCommand : BaseCommand
{
    public ClearEventsCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        var tagName = "";
        var skipConfirm = false;
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            switch (option.Name)
            {
                case "tag":
                    tagName = value as string ?? "";
                    break;
                case "yes":
                    skipConfirm = (bool?)value ?? false;
                    break;
            }
        }

        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        if (!skipConfirm && !PromptConfirmation($"タグ '{tagName}' のイベントをクリアしますか？"))
        {
            WriteInfo("操作をキャンセルしました。");
            return;
        }

        try
        {
            var response = await _pipeClient.ClearEventsAsync(tagName, context.GetCancellationToken());

            if (response.Success)
            {
                WriteSuccess($"タグ '{tagName}' のイベントをクリアしました。");
            }
            else
            {
                WriteError($"イベントクリアに失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"イベントクリア中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

// サービス管理コマンド群
public class ServiceStartCommand
{
    private readonly Services.IWindowsServiceManager _serviceManager;

    public ServiceStartCommand(Services.IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }
    
    protected void WriteInfo(string message) => Console.WriteLine($"[INFO] {message}");
    protected void WriteSuccess(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    protected void WriteError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public async Task ExecuteAsync(InvocationContext context)
    {
        const string serviceName = "ProcTail";
        
        try
        {
            WriteInfo($"ProcTailサービス '{serviceName}' を開始しています...");
            
            var status = await _serviceManager.GetServiceStatusAsync(serviceName);
            if (status == Services.ServiceStatus.NotFound)
            {
                WriteError($"サービス '{serviceName}' が見つかりません。");
                WriteInfo("'proctail service install' でサービスをインストールしてください。");
                context.ExitCode = 1;
                return;
            }
            
            if (status == Services.ServiceStatus.Running)
            {
                WriteInfo("サービスは既に実行中です。");
                return;
            }
            
            var result = await _serviceManager.StartServiceAsync(serviceName);
            if (result)
            {
                WriteSuccess("サービスが正常に開始されました。");
            }
            else
            {
                WriteError("サービスの開始に失敗しました。");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービス開始中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

public class ServiceStopCommand
{
    private readonly Services.IWindowsServiceManager _serviceManager;

    public ServiceStopCommand(Services.IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }
    
    protected void WriteInfo(string message) => Console.WriteLine($"[INFO] {message}");
    protected void WriteSuccess(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    protected void WriteError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public async Task ExecuteAsync(InvocationContext context)
    {
        const string serviceName = "ProcTail";
        
        try
        {
            WriteInfo($"ProcTailサービス '{serviceName}' を停止しています...");
            
            var status = await _serviceManager.GetServiceStatusAsync(serviceName);
            if (status == Services.ServiceStatus.NotFound)
            {
                WriteError($"サービス '{serviceName}' が見つかりません。");
                context.ExitCode = 1;
                return;
            }
            
            if (status == Services.ServiceStatus.Stopped)
            {
                WriteInfo("サービスは既に停止しています。");
                return;
            }
            
            var result = await _serviceManager.StopServiceAsync(serviceName);
            if (result)
            {
                WriteSuccess("サービスが正常に停止されました。");
            }
            else
            {
                WriteError("サービスの停止に失敗しました。");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービス停止中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

public class ServiceRestartCommand
{
    private readonly Services.IWindowsServiceManager _serviceManager;

    public ServiceRestartCommand(Services.IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }
    
    protected void WriteInfo(string message) => Console.WriteLine($"[INFO] {message}");
    protected void WriteSuccess(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    protected void WriteError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public async Task ExecuteAsync(InvocationContext context)
    {
        const string serviceName = "ProcTail";
        
        try
        {
            WriteInfo($"ProcTailサービス '{serviceName}' を再起動しています...");
            
            var status = await _serviceManager.GetServiceStatusAsync(serviceName);
            if (status == Services.ServiceStatus.NotFound)
            {
                WriteError($"サービス '{serviceName}' が見つかりません。");
                WriteInfo("'proctail service install' でサービスをインストールしてください。");
                context.ExitCode = 1;
                return;
            }
            
            var result = await _serviceManager.RestartServiceAsync(serviceName);
            if (result)
            {
                WriteSuccess("サービスが正常に再起動されました。");
            }
            else
            {
                WriteError("サービスの再起動に失敗しました。");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービス再起動中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

public class ServiceInstallCommand
{
    private readonly Services.IWindowsServiceManager _serviceManager;

    public ServiceInstallCommand(Services.IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }
    
    protected void WriteInfo(string message) => Console.WriteLine($"[INFO] {message}");
    protected void WriteSuccess(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    protected void WriteError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public async Task ExecuteAsync(InvocationContext context)
    {
        const string serviceName = "ProcTail";
        const string displayName = "ProcTail Process Monitor";
        const string description = "Monitors process file operations and child process creation using ETW";
        
        try
        {
            WriteInfo($"ProcTailサービス '{serviceName}' をインストールしています...");
            
            // 実行可能ファイルのパスを取得
            var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var hostExePath = Path.Combine(currentDirectory, "ProcTail.Host.exe");
            
            // Host.exeが存在するかチェック
            if (!File.Exists(hostExePath))
            {
                // 相対パスで検索
                hostExePath = Path.Combine(currentDirectory, "..", "ProcTail.Host", "ProcTail.Host.exe");
                if (!File.Exists(hostExePath))
                {
                    WriteError("ProcTail.Host.exe が見つかりません。");
                    WriteInfo("ビルドが完了していることを確認してください。");
                    context.ExitCode = 1;
                    return;
                }
            }
            
            var absolutePath = Path.GetFullPath(hostExePath);
            WriteInfo($"サービスバイナリ: {absolutePath}");
            
            // 既にインストールされているかチェック
            if (await _serviceManager.IsServiceInstalledAsync(serviceName))
            {
                WriteInfo("サービスは既にインストールされています。");
                return;
            }
            
            var result = await _serviceManager.InstallServiceAsync(serviceName, absolutePath, displayName, description);
            if (result)
            {
                WriteSuccess($"サービス '{serviceName}' が正常にインストールされました。");
                WriteInfo("'proctail service start' でサービスを開始してください。");
            }
            else
            {
                WriteError("サービスのインストールに失敗しました。");
                WriteInfo("管理者権限で実行していることを確認してください。");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービスインストール中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

public class ServiceUninstallCommand
{
    private readonly Services.IWindowsServiceManager _serviceManager;

    public ServiceUninstallCommand(Services.IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }
    
    protected void WriteInfo(string message) => Console.WriteLine($"[INFO] {message}");
    protected void WriteSuccess(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    protected void WriteError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public async Task ExecuteAsync(InvocationContext context)
    {
        const string serviceName = "ProcTail";
        
        try
        {
            WriteInfo($"ProcTailサービス '{serviceName}' をアンインストールしています...");
            
            // サービスが存在するかチェック
            if (!await _serviceManager.IsServiceInstalledAsync(serviceName))
            {
                WriteInfo("サービスは既にアンインストールされています。");
                return;
            }
            
            var result = await _serviceManager.UninstallServiceAsync(serviceName);
            if (result)
            {
                WriteSuccess($"サービス '{serviceName}' が正常にアンインストールされました。");
            }
            else
            {
                WriteError("サービスのアンインストールに失敗しました。");
                WriteInfo("管理者権限で実行していることを確認してください。");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"サービスアンインストール中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}

