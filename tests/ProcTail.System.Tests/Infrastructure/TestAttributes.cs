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
            Assert.Ignore("このテストはWindows環境で管理者権限が必要です");
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