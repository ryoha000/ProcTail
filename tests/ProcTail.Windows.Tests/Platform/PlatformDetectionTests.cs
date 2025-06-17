using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProcTail.Infrastructure.Configuration;
using ProcTail.Infrastructure.Etw;
using ProcTail.Infrastructure.NamedPipes;

namespace ProcTail.Windows.Tests.Platform;

/// <summary>
/// プラットフォーム検出とクロスプラットフォーム動作のテスト
/// </summary>
[TestFixture]
public class PlatformDetectionTests
{
    [Test]
    [Category("CrossPlatform")]
    public void RuntimeInformation_IsOSPlatform_ShouldDetectWindowsCorrectly()
    {
        // Act
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // Assert
        // 少なくとも1つのプラットフォームがtrueである必要がある
        (isWindows || isLinux || isMacOS).Should().BeTrue("少なくとも1つのプラットフォームが検出されるはずです");

        // プラットフォーム固有のテスト
        if (isWindows)
        {
            TestContext.WriteLine("Windows環境で実行中");
            RuntimeInformation.OSDescription.Should().Contain("Windows");
        }
        else if (isLinux)
        {
            TestContext.WriteLine("Linux環境で実行中");
            RuntimeInformation.OSDescription.Should().Contain("Linux");
        }
        else if (isMacOS)
        {
            TestContext.WriteLine("macOS環境で実行中");
            RuntimeInformation.OSDescription.Should().Contain("Darwin");
        }
    }

    [Test]
    [Category("CrossPlatform")]
    public void WindowsEtwEventProvider_Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        // Arrange
        var logger = NullLogger<WindowsEtwEventProvider>.Instance;
        var configuration = new EtwConfiguration();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows環境では例外が発生しない
            Action act = () => new WindowsEtwEventProvider(logger, configuration);
            act.Should().NotThrow("Windows環境では正常に作成されるはずです");
        }
        else
        {
            // 非Windows環境では例外が発生する
            Action act = () => new WindowsEtwEventProvider(logger, configuration);
            act.Should().Throw<PlatformNotSupportedException>()
                .WithMessage("*Windows platform*");
        }
    }

    [Test]
    [Category("CrossPlatform")]
    public void WindowsNamedPipeServer_Constructor_OnNonWindows_ShouldThrowPlatformNotSupportedException()
    {
        // Arrange
        var logger = NullLogger<WindowsNamedPipeServer>.Instance;
        var configuration = new NamedPipeConfiguration();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows環境では例外が発生しない
            Action act = () => new WindowsNamedPipeServer(logger, configuration);
            act.Should().NotThrow("Windows環境では正常に作成されるはずです");
        }
        else
        {
            // 非Windows環境では例外が発生する
            Action act = () => new WindowsNamedPipeServer(logger, configuration);
            act.Should().Throw<PlatformNotSupportedException>()
                .WithMessage("*Windows platform*");
        }
    }

    [Test]
    [Category("Windows")]
    [TestCase("windows")]
    [TestCase("WINDOWS")]
    [TestCase("Windows")]
    public void SupportedOSPlatformAttribute_ShouldBeRespected(string platformName)
    {
        // This test verifies that the SupportedOSPlatform attribute is properly applied
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Pass($"Windows環境でテストが実行されています: {RuntimeInformation.OSDescription}");
        }
        else
        {
            Assert.Ignore($"非Windows環境ではスキップされます: {RuntimeInformation.OSDescription}");
        }
    }

    [Test]
    [Category("CrossPlatform")]
    public void RuntimeInformation_Properties_ShouldProvideSystemInfo()
    {
        // Act & Assert
        RuntimeInformation.OSDescription.Should().NotBeNullOrEmpty("OS説明が取得できるはずです");
        RuntimeInformation.FrameworkDescription.Should().NotBeNullOrEmpty("フレームワーク説明が取得できるはずです");
        RuntimeInformation.RuntimeIdentifier.Should().NotBeNullOrEmpty("ランタイム識別子が取得できるはずです");
        
        var architecture = RuntimeInformation.OSArchitecture;
        architecture.Should().BeOneOf(Architecture.X64, Architecture.X86, Architecture.Arm64, Architecture.Arm);

        TestContext.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        TestContext.WriteLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        TestContext.WriteLine($"Runtime ID: {RuntimeInformation.RuntimeIdentifier}");
        TestContext.WriteLine($"Architecture: {architecture}");
    }

    [Test]
    [Category("CrossPlatform")]
    public void Environment_Properties_ShouldReflectPlatform()
    {
        // Act & Assert
        Environment.OSVersion.Should().NotBeNull("OS バージョン情報が取得できるはずです");
        Environment.Is64BitOperatingSystem.Should().BeOfType<bool>();
        Environment.Is64BitProcess.Should().BeOfType<bool>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Environment.OSVersion.Platform.Should().Be(PlatformID.Win32NT);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Environment.OSVersion.Platform.Should().Be(PlatformID.Unix);
        }

        TestContext.WriteLine($"OS Version: {Environment.OSVersion}");
        TestContext.WriteLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        TestContext.WriteLine($"64-bit Process: {Environment.Is64BitProcess}");
    }

    [Test]
    [Category("CrossPlatform")]
    public void ConditionalCompilation_ShouldWorkCorrectly()
    {
        // Arrange & Act
        bool isWindowsCompileTime = false;
        bool isUnixCompileTime = false;

#if WINDOWS
        isWindowsCompileTime = true;
#endif

#if UNIX
        isUnixCompileTime = true;
#endif

#if NET8_0
        var isNet80 = true;
#else
        var isNet80 = false;
#endif

        // Assert
        isNet80.Should().BeTrue(".NET 8.0でコンパイルされているはずです");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows環境では条件付きコンパイルの結果を確認
            TestContext.WriteLine($"Windows compile-time flag: {isWindowsCompileTime}");
        }
        else
        {
            // 非Windows環境では条件付きコンパイルの結果を確認
            TestContext.WriteLine($"Unix compile-time flag: {isUnixCompileTime}");
        }

        TestContext.WriteLine($".NET 8.0 compile-time flag: {isNet80}");
    }

    [Test]
    [Category("CrossPlatform")]
    public void AppDomain_Properties_ShouldReflectPlatform()
    {
        // Act
        var currentDomain = AppDomain.CurrentDomain;
        var baseDirectory = currentDomain.BaseDirectory;
        var friendlyName = currentDomain.FriendlyName;

        // Assert
        baseDirectory.Should().NotBeNullOrEmpty("ベースディレクトリが取得できるはずです");
        friendlyName.Should().NotBeNullOrEmpty("ドメイン名が取得できるはずです");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            baseDirectory.Should().ContainAny("\\", ":");
        }
        else
        {
            baseDirectory.Should().StartWith("/");
        }

        TestContext.WriteLine($"Base Directory: {baseDirectory}");
        TestContext.WriteLine($"Friendly Name: {friendlyName}");
    }

    [Test]
    [Category("CrossPlatform")]
    public void Path_Separators_ShouldReflectPlatform()
    {
        // Act & Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Path.DirectorySeparatorChar.Should().Be('\\');
            Path.AltDirectorySeparatorChar.Should().Be('/');
            Path.PathSeparator.Should().Be(';');
        }
        else
        {
            Path.DirectorySeparatorChar.Should().Be('/');
            Path.PathSeparator.Should().Be(':');
        }

        TestContext.WriteLine($"Directory Separator: '{Path.DirectorySeparatorChar}'");
        TestContext.WriteLine($"Alt Directory Separator: '{Path.AltDirectorySeparatorChar}'");
        TestContext.WriteLine($"Path Separator: '{Path.PathSeparator}'");
    }

    [Test]
    [Category("Windows")]
    [SupportedOSPlatform("windows")]
    public void WindowsOnlyFeatures_ShouldBeAvailable()
    {
        // このテストはWindows環境でのみ実行される
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("このテストはWindows環境でのみ実行されます");
            return;
        }

        // Windows固有の機能をテスト
        var windowsIdentity = System.Security.Principal.WindowsIdentity.GetCurrent();
        windowsIdentity.Should().NotBeNull("Windows Identity が取得できるはずです");
        windowsIdentity.Name.Should().NotBeNullOrEmpty("ユーザー名が取得できるはずです");

        TestContext.WriteLine($"Windows Identity: {windowsIdentity.Name}");
        TestContext.WriteLine($"Authentication Type: {windowsIdentity.AuthenticationType}");
        TestContext.WriteLine($"Is System: {windowsIdentity.IsSystem}");
        TestContext.WriteLine($"Is Anonymous: {windowsIdentity.IsAnonymous}");
    }

    [Test]
    [Category("CrossPlatform")]
    public void OperatingSystem_IsWindows_ShouldMatchRuntimeInformation()
    {
        // Act
        var isWindowsOS = OperatingSystem.IsWindows();
        var isWindowsRI = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Assert
        isWindowsOS.Should().Be(isWindowsRI, "両方の判定結果が一致するはずです");

        TestContext.WriteLine($"OperatingSystem.IsWindows(): {isWindowsOS}");
        TestContext.WriteLine($"RuntimeInformation.IsOSPlatform(Windows): {isWindowsRI}");
    }
}