using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ProcTail.Cli.Commands;

/// <summary>
/// ETWセッションクリーンアップコマンド
/// </summary>
public static class CleanupCommand
{
    /// <summary>
    /// クリーンアップコマンドを作成
    /// </summary>
    public static Command CreateCommand()
    {
        var command = new Command("cleanup", "ETWセッションとシステムリソースをクリーンアップします");
        
        var forceOption = new Option<bool>(
            "--force",
            "強制的にすべてのETWセッションをクリーンアップします"
        );
        
        var allOption = new Option<bool>(
            "--all",
            "ProcTail関連だけでなく、問題のあるETWセッションもクリーンアップします"
        );
        
        command.AddOption(forceOption);
        command.AddOption(allOption);
        
        command.SetHandler(async (force, all) =>
        {
            await ExecuteCleanupAsync(force, all);
        }, forceOption, allOption);
        
        return command;
    }
    
    /// <summary>
    /// クリーンアップを実行
    /// </summary>
    private static async Task ExecuteCleanupAsync(bool force, bool all)
    {
        Console.WriteLine("=== ProcTail ETWセッション クリーンアップ ===");
        
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("❌ このコマンドはWindowsでのみ利用可能です");
            return;
        }
        
        try
        {
            // 管理者権限チェック
            if (!IsRunningAsAdministrator())
            {
                Console.WriteLine("⚠️  管理者権限が必要です。管理者として実行してください。");
                if (!force)
                {
                    Console.WriteLine("強制実行するには --force オプションを使用してください。");
                    return;
                }
            }
            
            Console.WriteLine("🧹 ETWセッションクリーンアップを開始します...");
            
            // Step 1: ProcTail関連セッションをクリーンアップ
            await CleanupProcTailSessionsAsync();
            
            if (all)
            {
                // Step 2: 問題のあるETWセッションをクリーンアップ
                await CleanupProblematicSessionsAsync();
                
                // Step 3: システムETWリソースの解放
                await ReleaseSystemEtwResourcesAsync();
            }
            
            Console.WriteLine("✅ ETWセッションクリーンアップが完了しました");
            Console.WriteLine("💡 ProcTailサービスを再起動してください");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ クリーンアップ中にエラーが発生しました: {ex.Message}");
        }
    }
    
    /// <summary>
    /// ProcTail関連セッションをクリーンアップ
    /// </summary>
    private static async Task CleanupProcTailSessionsAsync()
    {
        Console.WriteLine("📋 ProcTail関連ETWセッションを検索中...");
        
        var patterns = new[]
        {
            "ProcTail*",
            "PT_*",
            "*ProcTail*"
        };
        
        foreach (var pattern in patterns)
        {
            Console.WriteLine($"🛑 停止中: {pattern}");
            await ExecuteCommandAsync($"logman stop \"{pattern}\" -ets", suppressOutput: true);
        }
    }
    
    /// <summary>
    /// 問題のあるETWセッションをクリーンアップ
    /// </summary>
    private static async Task CleanupProblematicSessionsAsync()
    {
        Console.WriteLine("🔍 問題のあるETWセッションを検索中...");
        
        // 現在のセッション一覧を取得
        var sessions = await GetActiveEtwSessionsAsync();
        var problematicSessions = sessions.Where(session =>
            session.Contains("TraceEventSession") ||
            session.Contains("ProcTail") ||
            session.Contains("PT_")).ToList();
        
        if (problematicSessions.Any())
        {
            Console.WriteLine($"🧹 {problematicSessions.Count} 個の問題セッションを発見しました");
            foreach (var session in problematicSessions)
            {
                Console.WriteLine($"🛑 停止中: {session}");
                await ExecuteCommandAsync($"logman stop \"{session}\" -ets", suppressOutput: true);
            }
        }
        else
        {
            Console.WriteLine("✅ 問題のあるセッションは見つかりませんでした");
        }
    }
    
    /// <summary>
    /// システムETWリソースを解放
    /// </summary>
    private static async Task ReleaseSystemEtwResourcesAsync()
    {
        Console.WriteLine("🔧 システムETWリソースを解放中...");
        
        var commands = new[]
        {
            "wevtutil sl Microsoft-Windows-Kernel-Process/Analytic /e:false",
            "wevtutil sl Microsoft-Windows-Kernel-FileIO/Analytic /e:false"
        };
        
        foreach (var command in commands)
        {
            await ExecuteCommandAsync(command, suppressOutput: true);
        }
        
        Console.WriteLine("✅ システムETWリソースを解放しました");
    }
    
    /// <summary>
    /// 現在のアクティブETWセッション一覧を取得
    /// </summary>
    private static async Task<List<string>> GetActiveEtwSessionsAsync()
    {
        var sessions = new List<string>();
        
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "logman";
            process.StartInfo.Arguments = "query -ets";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && !parts[0].Contains("---") && !parts[0].Contains("データ") && !parts[0].Contains("Data"))
                {
                    sessions.Add(parts[0].Trim());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  セッション一覧の取得に失敗しました: {ex.Message}");
        }
        
        return sessions;
    }
    
    /// <summary>
    /// コマンドを実行
    /// </summary>
    private static async Task ExecuteCommandAsync(string command, bool suppressOutput = false)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command} 2>nul";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            
            if (!suppressOutput)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output);
                }
                
                if (!string.IsNullOrWhiteSpace(error) && !error.Contains("見つかりません"))
                {
                    Console.WriteLine($"⚠️  警告: {error}");
                }
            }
            
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            if (!suppressOutput)
            {
                Console.WriteLine($"⚠️  コマンド実行エラー: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 管理者権限チェック
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