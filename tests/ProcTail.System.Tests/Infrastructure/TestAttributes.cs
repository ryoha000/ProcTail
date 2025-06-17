using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NUnit.Framework;

namespace ProcTail.System.Tests.Infrastructure;

public static class PlatformChecks
{
    public static void RequireWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test requires Windows platform");
        }
    }

    public static void RequireWindowsAndAdministrator()
    {
        RequireWindows();
        
        if (!IsRunningAsAdministrator())
        {
            RequestAdministratorPrivileges();
        }
    }

    /// <summary>
    /// 管理者権限を要求してアプリケーションを再起動
    /// </summary>
    public static void RequestAdministratorPrivileges()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "dotnet",
                Arguments = string.Join(" ", Environment.GetCommandLineArgs()[1..]),
                Verb = "runas", // UACプロンプトを表示
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            TestContext.WriteLine("管理者権限が必要です。UACプロンプトを表示してアプリケーションを再起動します...");
            TestContext.WriteLine($"実行コマンド: {processInfo.FileName} {processInfo.Arguments}");
            
            // 管理者権限で再起動
            var process = Process.Start(processInfo);
            
            if (process != null)
            {
                TestContext.WriteLine("管理者権限でのプロセス起動に成功しました。現在のプロセスを終了します。");
                // 現在のプロセスを終了（管理者権限でのプロセスが実行される）
                Environment.Exit(0);
            }
            else
            {
                Assert.Ignore("管理者権限でのプロセス起動に失敗しました");
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"管理者権限昇格エラー: {ex.Message}");
            
            // ユーザーがUACをキャンセルした場合など
            if (ex is global::System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 1223)
            {
                Assert.Ignore("ユーザーがUACプロンプトをキャンセルしました");
            }
            else
            {
                Assert.Ignore($"管理者権限の取得に失敗しました: {ex.Message}");
            }
        }
    }

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
}