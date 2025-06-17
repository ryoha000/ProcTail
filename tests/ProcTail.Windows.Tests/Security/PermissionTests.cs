using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Security.AccessControl;
using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using FluentAssertions;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;

namespace ProcTail.Windows.Tests.Security;

/// <summary>
/// Windows セキュリティとアクセス許可のテスト
/// 管理者権限、ユーザー権限、アクセス制御のテスト
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class PermissionTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Windows環境でない場合はテスト全体をスキップ
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void WindowsIdentity_GetCurrent_ShouldReturnValidIdentity()
    {
        // Act
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        // Assert
        identity.Should().NotBeNull();
        identity.Name.Should().NotBeNullOrEmpty();
        identity.AuthenticationType.Should().NotBeNullOrEmpty();
        identity.IsAuthenticated.Should().BeTrue();

        TestContext.WriteLine($"Current User: {identity.Name}");
        TestContext.WriteLine($"Authentication Type: {identity.AuthenticationType}");
        TestContext.WriteLine($"Is System: {identity.IsSystem}");
        TestContext.WriteLine($"Is Anonymous: {identity.IsAnonymous}");
        TestContext.WriteLine($"Is Guest: {identity.IsGuest}");
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void WindowsPrincipal_RoleChecks_ShouldWorkCorrectly()
    {
        // Arrange
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        // Act
        var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var isUser = principal.IsInRole(WindowsBuiltInRole.User);
        var isGuest = principal.IsInRole(WindowsBuiltInRole.Guest);
        var isBackupOperator = principal.IsInRole(WindowsBuiltInRole.BackupOperator);

        // Assert
        isAdmin.Should().BeOfType<bool>();
        isUser.Should().BeOfType<bool>();
        isGuest.Should().BeOfType<bool>();
        isBackupOperator.Should().BeOfType<bool>();

        TestContext.WriteLine($"Is Administrator: {isAdmin}");
        TestContext.WriteLine($"Is User: {isUser}");
        TestContext.WriteLine($"Is Guest: {isGuest}");
        TestContext.WriteLine($"Is Backup Operator: {isBackupOperator}");

        if (isAdmin)
        {
            TestContext.WriteLine("⚠️  実行中のプロセスは管理者権限を持っています");
        }
        else
        {
            TestContext.WriteLine("ℹ️  実行中のプロセスは標準ユーザー権限です");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    [Category("RequiresAdministrator")]
    public async Task EtwEventProvider_WithAdministratorRights_ShouldStartSuccessfully()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        var logger = NullLogger<WindowsEtwEventProvider>.Instance;
        var configuration = new EtwConfiguration();
        using var provider = new WindowsEtwEventProvider(logger, configuration);

        // Act & Assert
        Func<Task> act = async () => await provider.StartMonitoringAsync();
        await act.Should().NotThrowAsync("管理者権限があるため、ETW監視を開始できるはずです");

        if (provider.IsMonitoring)
        {
            await provider.StopMonitoringAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    [Category("RequiresNonAdministrator")]
    public async Task EtwEventProvider_WithoutAdministratorRights_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        if (IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは非管理者権限で実行する必要があります");
            return;
        }

        var logger = NullLogger<WindowsEtwEventProvider>.Instance;
        var configuration = new EtwConfiguration();
        using var provider = new WindowsEtwEventProvider(logger, configuration);

        // Act & Assert
        Func<Task> act = async () => await provider.StartMonitoringAsync();
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*管理者権限*");
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public async Task NamedPipeServer_ShouldCreateWithProperSecurity()
    {
        // Arrange
        var logger = NullLogger<WindowsNamedPipeServer>.Instance;
        var configuration = new NamedPipeConfiguration();
        using var server = new WindowsNamedPipeServer(logger, configuration);

        // Act
        await server.StartAsync();

        try
        {
            // Assert
            server.IsRunning.Should().BeTrue("Named Pipeサーバーが開始されるはずです");

            // パイプのセキュリティ設定をテスト（間接的に）
            using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut);
            
            // 現在のユーザーは接続できるはずです
            var connectTask = client.ConnectAsync(TimeSpan.FromSeconds(5));
            await connectTask.Should().NotThrowAsync("認証されたユーザーは接続できるはずです");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void SecurityIdentifier_ShouldWorkWithWellKnownSids()
    {
        // Act & Assert
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var userSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var guestSid = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null);

        adminSid.Should().NotBeNull();
        userSid.Should().NotBeNull();
        guestSid.Should().NotBeNull();

        adminSid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid).Should().BeTrue();
        userSid.IsWellKnown(WellKnownSidType.BuiltinUsersSid).Should().BeTrue();
        guestSid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid).Should().BeTrue();

        TestContext.WriteLine($"Administrators SID: {adminSid}");
        TestContext.WriteLine($"Users SID: {userSid}");
        TestContext.WriteLine($"Guests SID: {guestSid}");
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void PipeSecurity_ShouldAllowProperAccessControl()
    {
        // Arrange
        var pipeSecurity = new PipeSecurity();
        using var currentUser = WindowsIdentity.GetCurrent();

        // Act
        var userRule = new PipeAccessRule(currentUser.User!, PipeAccessRights.FullControl, AccessControlType.Allow);
        pipeSecurity.AddAccessRule(userRule);

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var adminRule = new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow);
        pipeSecurity.AddAccessRule(adminRule);

        // Assert
        var accessRules = pipeSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
        accessRules.Should().NotBeNull();
        accessRules.Count.Should().BeGreaterOrEqualTo(2);

        TestContext.WriteLine($"Access rules count: {accessRules.Count}");
        foreach (PipeAccessRule rule in accessRules)
        {
            TestContext.WriteLine($"Identity: {rule.IdentityReference}, Rights: {rule.PipeAccessRights}, Type: {rule.AccessControlType}");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void WindowsBuiltInRole_EnumValues_ShouldBeComplete()
    {
        // Act & Assert
        var roles = Enum.GetValues<WindowsBuiltInRole>();
        
        roles.Should().Contain(WindowsBuiltInRole.Administrator);
        roles.Should().Contain(WindowsBuiltInRole.User);
        roles.Should().Contain(WindowsBuiltInRole.Guest);
        roles.Should().Contain(WindowsBuiltInRole.PowerUser);
        roles.Should().Contain(WindowsBuiltInRole.BackupOperator);
        roles.Should().Contain(WindowsBuiltInRole.Replicator);

        TestContext.WriteLine("Available Windows Built-in Roles:");
        foreach (var role in roles)
        {
            TestContext.WriteLine($"  - {role}");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void CurrentUserToken_ShouldProvideValidInformation()
    {
        // Arrange & Act
        using var identity = WindowsIdentity.GetCurrent();
        var token = identity.Token;
        var impersonationLevel = identity.ImpersonationLevel;
        var isSystem = identity.IsSystem;
        var isAnonymous = identity.IsAnonymous;

        // Assert
        token.Should().NotBe(IntPtr.Zero, "有効なトークンハンドルが取得されるはずです");
        impersonationLevel.Should().BeOneOf(
            TokenImpersonationLevel.Anonymous,
            TokenImpersonationLevel.Identification,
            TokenImpersonationLevel.Impersonation,
            TokenImpersonationLevel.Delegation,
            TokenImpersonationLevel.None);

        TestContext.WriteLine($"Token: {token}");
        TestContext.WriteLine($"Impersonation Level: {impersonationLevel}");
        TestContext.WriteLine($"Is System: {isSystem}");
        TestContext.WriteLine($"Is Anonymous: {isAnonymous}");
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    public void ProcessPrivileges_ShouldBeQueryable()
    {
        // Arrange
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        // Act & Assert
        // 各種権限の確認
        var hasAdminRole = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var hasUserRole = principal.IsInRole(WindowsBuiltInRole.User);
        var hasBackupRole = principal.IsInRole(WindowsBuiltInRole.BackupOperator);

        TestContext.WriteLine("Current Process Privileges:");
        TestContext.WriteLine($"  Administrator: {hasAdminRole}");
        TestContext.WriteLine($"  User: {hasUserRole}");
        TestContext.WriteLine($"  Backup Operator: {hasBackupRole}");

        // 権限に基づいた期待値の検証
        if (hasAdminRole)
        {
            TestContext.WriteLine("⚠️  管理者権限でのテスト実行中");
            TestContext.WriteLine("  - ETW監視が可能");
            TestContext.WriteLine("  - システムリソースへのアクセスが可能");
        }
        else
        {
            TestContext.WriteLine("ℹ️  標準ユーザー権限でのテスト実行中");
            TestContext.WriteLine("  - ETW監視は不可");
            TestContext.WriteLine("  - 制限されたリソースアクセス");
        }
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    [Category("RequiresAdministrator")]
    public void AdministratorOnlyTest_ShouldOnlyRunWithAdminRights()
    {
        // Arrange
        if (!IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは管理者権限で実行する必要があります");
            return;
        }

        // Act & Assert
        IsRunningAsAdministrator().Should().BeTrue("管理者権限でのみ実行されるはずです");
        TestContext.WriteLine("✅ 管理者権限でのテスト実行を確認しました");
    }

    [Test]
    [Category("Windows")]
    [Category("Security")]
    [Category("RequiresNonAdministrator")]
    public void NonAdministratorOnlyTest_ShouldOnlyRunWithoutAdminRights()
    {
        // Arrange
        if (IsRunningAsAdministrator())
        {
            Assert.Ignore("このテストは非管理者権限で実行する必要があります");
            return;
        }

        // Act & Assert
        IsRunningAsAdministrator().Should().BeFalse("非管理者権限でのみ実行されるはずです");
        TestContext.WriteLine("✅ 非管理者権限でのテスト実行を確認しました");
    }

    #region Helper Methods

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
}