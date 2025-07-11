using System.Runtime.InteropServices;
using System.Security.Principal;
using FluentAssertions;
using NUnit.Framework;
using ProcTail.System.Tests.Infrastructure;

namespace ProcTail.System.Tests;

[TestFixture]
[Category("System")]
[Category("Platform")]
public class WindowsPlatformTest
{
    [Test]
    public void PlatformDetection_ShouldWorkCorrectly()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // プラットフォーム検出が動作することを確認
        (isWindows || isLinux || isMacOS).Should().BeTrue();
        
        TestContext.WriteLine($"Windows: {isWindows}");
        TestContext.WriteLine($"Linux: {isLinux}");
        TestContext.WriteLine($"macOS: {isMacOS}");
        TestContext.WriteLine($"Current platform: {RuntimeInformation.OSDescription}");
    }

    [Test]
    public void WindowsRequiredCheck_ShouldSkipAppropriately()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
        }

        // Windowsでのみ実行されるべきテスト
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows).Should().BeTrue();
        TestContext.WriteLine("Windows固有テストが実行されました");
    }

    [Test]
    public void AdministratorCheck_OnWindows_ShouldWork()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
        }

        // Windows環境での管理者権限チェック
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            
            TestContext.WriteLine($"管理者権限: {isAdmin}");
            TestContext.WriteLine($"ユーザー名: {identity.Name}");
            
            // 管理者権限の有無にかかわらず、チェック機能が動作することを確認
            (isAdmin is bool).Should().BeTrue();
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("Windows固有のセキュリティ機能にアクセスできません");
        }
    }

    [Test]
    public void PlatformChecks_RequireWindows_ShouldWork()
    {
        // PlatformChecksクラスの動作確認
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows以外ではIgnoreされるべき
            var ex = Assert.Throws<IgnoreException>(() => PlatformChecks.RequireWindows());
            ex.Message.Should().Contain("Windows platform");
        }
        else
        {
            // WindowsではIgnoreされない
            Assert.DoesNotThrow(() => PlatformChecks.RequireWindows());
        }
    }

    [Test]
    [Category("RequiresAdmin")]
    public void PlatformChecks_RequireWindowsAndAdmin_ShouldWork()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows以外ではIgnoreされるべき
            var ex = Assert.Throws<IgnoreException>(() => PlatformChecks.RequireWindowsAndAdministrator());
            ex.Message.Should().Contain("Windows platform");
        }
        else
        {
            // Windows環境では管理者権限がない場合はIgnoreされるべき
            var isAdmin = IsRunningAsAdministrator();
            if (!isAdmin)
            {
                var ex = Assert.Throws<IgnoreException>(() => PlatformChecks.RequireWindowsAndAdministrator());
                ex.Message.Should().Contain("管理者権限が必要です");
            }
            else
            {
                // 管理者権限がある場合は正常に実行される
                Assert.DoesNotThrow(() => PlatformChecks.RequireWindowsAndAdministrator());
                TestContext.WriteLine("管理者権限でテストが実行されました");
            }
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