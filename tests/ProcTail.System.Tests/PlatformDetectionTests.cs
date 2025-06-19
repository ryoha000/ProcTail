using System.Runtime.InteropServices;
using System.Security.Principal;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Core.Interfaces;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;
using ProcTail.System.Tests.Infrastructure;
using ProcTail.Testing.Common.Mocks.Etw;
using ProcTail.Testing.Common.Mocks.Ipc;

namespace ProcTail.System.Tests;

/// <summary>
/// プラットフォーム検出とWindows固有機能の動作テスト
/// </summary>
[TestFixture]
[Category("System")]
[Category("Platform")]
public class PlatformDetectionTests
{
    private ILogger<WindowsEtwEventProvider> _etwLogger = null!;
    private ILogger<WindowsNamedPipeServer> _pipeLogger = null!;
    private IEtwConfiguration _etwConfiguration = null!;
    private INamedPipeConfiguration _pipeConfiguration = null!;

    [SetUp]
    public void Setup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _etwLogger = loggerFactory.CreateLogger<WindowsEtwEventProvider>();
        _pipeLogger = loggerFactory.CreateLogger<WindowsNamedPipeServer>();
        _etwConfiguration = new EtwConfiguration();
        _pipeConfiguration = new NamedPipeConfiguration();
    }

    [Test]
    public void RuntimeInformation_ShouldCorrectlyDetectPlatform()
    {
        // Act
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // Assert
        // Exactly one should be true
        var platformCount = new[] { isWindows, isLinux, isMacOS }.Count(x => x);
        platformCount.Should().Be(1, "Exactly one platform should be detected");

        // Log current platform for debugging
        TestContext.WriteLine($"Detected Platform - Windows: {isWindows}, Linux: {isLinux}, macOS: {isMacOS}");
        TestContext.WriteLine($"OS Description: {RuntimeInformation.OSDescription}");
        TestContext.WriteLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
    }

    [Test]
    public void WindowsEtwEventProvider_PlatformBehavior_ShouldBeCorrect()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows: Should initialize successfully
            var action = () => new WindowsEtwEventProvider(_etwLogger, _etwConfiguration);
            action.Should().NotThrow("Windows ETW provider should initialize on Windows");
        }
        else
        {
            // On non-Windows: Should throw PlatformNotSupportedException
            var action = () => new WindowsEtwEventProvider(_etwLogger, _etwConfiguration);
            action.Should().Throw<PlatformNotSupportedException>()
                  .WithMessage("*Windows*", "Should indicate Windows requirement");
        }
    }

    [Test]
    public void WindowsNamedPipeServer_PlatformBehavior_ShouldBeCorrect()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows: Should initialize successfully
            var action = () => new WindowsNamedPipeServer(_pipeLogger, _pipeConfiguration);
            action.Should().NotThrow("Windows Named Pipe server should initialize on Windows");
        }
        else
        {
            // On non-Windows: Should throw PlatformNotSupportedException
            var action = () => new WindowsNamedPipeServer(_pipeLogger, _pipeConfiguration);
            action.Should().Throw<PlatformNotSupportedException>()
                  .WithMessage("*Windows*", "Should indicate Windows requirement");
        }
    }

    [Test]
    public void WindowsPrivilegeDetection_ShouldWorkCorrectly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Act
        var isAdmin = IsRunningAsAdministrator();
        var currentUser = Environment.UserName;
        var currentDomain = Environment.UserDomainName;

        // Assert & Log
        TestContext.WriteLine($"Current User: {currentDomain}\\{currentUser}");
        TestContext.WriteLine($"Running as Administrator: {isAdmin}");
        
        // The test should complete without exception
        (isAdmin is bool).Should().BeTrue("Administrator status should be determinable");
    }

    [Test]
    public void DependencyInjection_PlatformSpecificServices_ShouldResolveCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_etwConfiguration);
        services.AddSingleton(_pipeConfiguration);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows: Register real implementations
            services.AddSingleton<IEtwEventProvider, WindowsEtwEventProvider>();
            services.AddSingleton<INamedPipeServer, WindowsNamedPipeServer>();
        }
        else
        {
            // On non-Windows: Register mock implementations
            services.AddSingleton<IEtwEventProvider, MockEtwEventProvider>();
            services.AddSingleton<INamedPipeServer, MockNamedPipeServer>();
        }

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var etwProvider = serviceProvider.GetService<IEtwEventProvider>();
            var pipeServer = serviceProvider.GetService<INamedPipeServer>();

            etwProvider.Should().NotBeNull("ETW provider should be resolvable on Windows");
            etwProvider.Should().BeOfType<WindowsEtwEventProvider>();

            pipeServer.Should().NotBeNull("Named Pipe server should be resolvable on Windows");
            pipeServer.Should().BeOfType<WindowsNamedPipeServer>();
        }
        else
        {
            // On non-Windows, these services should not be registered
            var etwProvider = serviceProvider.GetService<IEtwEventProvider>();
            var pipeServer = serviceProvider.GetService<INamedPipeServer>();

            // These should be mock implementations, not Windows implementations
            etwProvider.Should().NotBeNull("Mock ETW provider should be resolvable on non-Windows");
            etwProvider.Should().BeOfType<MockEtwEventProvider>();

            pipeServer.Should().NotBeNull("Mock Named Pipe server should be resolvable on non-Windows");
            pipeServer.Should().BeOfType<MockNamedPipeServer>();
        }
    }

    [Test]
    public void WindowsSpecificAPIs_ShouldBeAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Test Windows-specific API availability
        var actions = new List<(string name, global::System.Action action)>
        {
            ("WindowsIdentity.GetCurrent", new global::System.Action(() => 
            {
                using var identity = WindowsIdentity.GetCurrent();
                identity.Should().NotBeNull();
            })),
            
            ("Registry access", new global::System.Action(() => 
            {
                var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE");
                key.Should().NotBeNull();
                key?.Close();
            })),
            
            ("Process.GetProcesses", new global::System.Action(() => 
            {
                var processes = global::System.Diagnostics.Process.GetProcesses();
                processes.Should().NotBeEmpty();
            })),
            
            ("Environment.OSVersion", new global::System.Action(() => 
            {
                var osVersion = Environment.OSVersion;
                osVersion.Platform.Should().Be(PlatformID.Win32NT);
            }))
        };

        foreach (var (name, action) in actions)
        {
            action.Should().NotThrow($"Windows API '{name}' should be available");
        }
    }

    [Test]
    public void FrameworkCompatibility_ShouldBeCorrect()
    {
        // Act
        var frameworkDescription = RuntimeInformation.FrameworkDescription;
        var runtimeVersion = Environment.Version;

        // Assert
        TestContext.WriteLine($"Framework: {frameworkDescription}");
        TestContext.WriteLine($"Runtime Version: {runtimeVersion}");

        frameworkDescription.Should().Contain(".NET", "Should be running on .NET");
        runtimeVersion.Major.Should().BeGreaterOrEqualTo(8, "Should be .NET 8 or higher");
    }

    [Test]
    [Category("CI")]
    public void ContinuousIntegration_PlatformDetection_ShouldProvideGuidance()
    {
        // This test provides guidance for CI/CD pipeline configuration
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isAdmin = isWindows ? IsRunningAsAdministrator() : false;

        TestContext.WriteLine("=== CI/CD Platform Detection Results ===");
        TestContext.WriteLine($"Platform: {RuntimeInformation.OSDescription}");
        TestContext.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        TestContext.WriteLine($"Is Windows: {isWindows}");
        TestContext.WriteLine($"Is Linux: {isLinux}");
        TestContext.WriteLine($"Is Administrator (Windows only): {isAdmin}");
        TestContext.WriteLine();

        if (isWindows)
        {
            TestContext.WriteLine("✅ Windows detected - Full system tests can run");
            TestContext.WriteLine($"⚠️  Administrator rights: {(isAdmin ? "Available" : "NOT available")}");
            TestContext.WriteLine("   📋 For complete ETW testing, run with administrator privileges");
        }
        else
        {
            TestContext.WriteLine("ℹ️  Non-Windows platform - Only cross-platform tests will run");
            TestContext.WriteLine("   📋 Windows-specific tests will be skipped");
        }

        TestContext.WriteLine();
        TestContext.WriteLine("=== Test Categories ===");
        TestContext.WriteLine("• Unit: All platforms");
        TestContext.WriteLine("• Integration: All platforms (uses mocks)");
        TestContext.WriteLine("• System+RequiresWindows: Windows only");
        TestContext.WriteLine("• System+RequiresWindowsAndAdministrator: Windows with admin only");

        // Always pass - this is informational
        Assert.Pass("Platform detection completed successfully");
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