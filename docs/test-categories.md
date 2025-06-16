# テストカテゴリ分離設計

## 1. テストカテゴリの定義

### 1.1 基本カテゴリ

| カテゴリ名 | 説明 | 実行環境 | 必要権限 |
|-----------|------|----------|----------|
| `Unit` | 純粋なビジネスロジックのテスト | WSL/Windows | なし |
| `Integration` | モック化されたインフラとの統合テスト | WSL/Windows | なし |
| `RequiresWindows` | Windows固有API（モック不可）を使用 | Windows | 一般ユーザー |
| `RequiresAdmin` | 管理者権限が必要なテスト | Windows | 管理者 |
| `Performance` | パフォーマンス測定テスト | Windows | 一般ユーザー |
| `EndToEnd` | 実際のプロセス監視を行う統合テスト | Windows | 管理者 |

### 1.2 組み合わせ使用例

```csharp
[TestFixture]
[Category("Unit")]
public class WatchTargetManagerTests
{
    // WSL/Windowsどちらでも実行可能
}

[TestFixture]
[Category("Integration")]
public class EventFlowIntegrationTests
{
    // モック化されたインフラを使用
}

[TestFixture]
[Category("RequiresWindows")]
public class NamedPipeIntegrationTests
{
    // 実際のNamed Pipesを使用（管理者権限不要）
}

[TestFixture]
[Category("RequiresAdmin")]
[Category("RequiresWindows")]
public class EtwSystemTests
{
    // 実際のETWを使用（管理者権限必要）
}

[TestFixture]
[Category("EndToEnd")]
[Category("RequiresAdmin")]
[Category("RequiresWindows")]
public class FullSystemTests
{
    // 完全な統合テスト
}
```

## 2. 属性とベースクラスの実装

### 2.1 カスタムテスト属性

```csharp
// ProcTail.Testing.Common/Attributes/TestCategoryAttributes.cs
namespace ProcTail.Testing.Common.Attributes
{
    /// <summary>
    /// 管理者権限が必要なテストを示す属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequiresAdminAttribute : CategoryAttribute
    {
        public RequiresAdminAttribute() : base("RequiresAdmin") { }
    }

    /// <summary>
    /// Windows固有機能を使用するテストを示す属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequiresWindowsAttribute : CategoryAttribute
    {
        public RequiresWindowsAttribute() : base("RequiresWindows") { }
    }

    /// <summary>
    /// パフォーマンステストを示す属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class PerformanceTestAttribute : CategoryAttribute
    {
        public PerformanceTestAttribute() : base("Performance") { }
    }

    /// <summary>
    /// エンドツーエンドテストを示す属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class EndToEndTestAttribute : CategoryAttribute
    {
        public EndToEndTestAttribute() : base("EndToEnd") { }
    }

    /// <summary>
    /// ユニットテストを示す属性（デフォルト）
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class UnitTestAttribute : CategoryAttribute
    {
        public UnitTestAttribute() : base("Unit") { }
    }

    /// <summary>
    /// 統合テストを示す属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class IntegrationTestAttribute : CategoryAttribute
    {
        public IntegrationTestAttribute() : base("Integration") { }
    }
}
```

### 2.2 実行前提条件チェック

```csharp
// ProcTail.Testing.Common/Infrastructure/TestEnvironmentChecker.cs
namespace ProcTail.Testing.Common.Infrastructure
{
    public static class TestEnvironmentChecker
    {
        public static void RequireAdministrator()
        {
            if (!IsRunningAsAdministrator())
            {
                Assert.Inconclusive("This test requires administrator privileges. Please run as administrator.");
            }
        }

        public static void RequireWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Inconclusive("This test requires Windows operating system.");
            }
        }

        public static void RequireEtwAccess()
        {
            RequireWindows();
            RequireAdministrator();
            
            // ETWセッション作成テスト
            try
            {
                using var session = new TraceEventSession("TestSession");
                session.Dispose();
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Inconclusive("This test requires ETW access. Please run as administrator.");
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"ETW access test failed: {ex.Message}");
            }
        }

        public static bool IsRunningAsAdministrator()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void SkipOnWSL()
        {
            // WSL環境の検出
            if (Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null ||
                File.Exists("/proc/version") && 
                File.ReadAllText("/proc/version").Contains("Microsoft"))
            {
                Assert.Inconclusive("This test is skipped on WSL environment.");
            }
        }
    }
}
```

### 2.3 ベーステストクラス

```csharp
// ProcTail.Testing.Common/Base/BaseTestFixture.cs
namespace ProcTail.Testing.Common.Base
{
    /// <summary>
    /// 全テストクラスの基底クラス
    /// </summary>
    public abstract class BaseTestFixture
    {
        protected ILogger Logger { get; private set; } = null!;
        protected ITestOutputHelper? TestOutput { get; set; }

        [OneTimeSetUp]
        public virtual void OneTimeSetUp()
        {
            // ログ設定
            Logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                if (TestOutput != null)
                {
                    builder.AddProvider(new XunitLoggerProvider(TestOutput));
                }
            }).CreateLogger(GetType());
        }

        [SetUp]
        public virtual void SetUp()
        {
            Logger.LogInformation("Starting test: {TestName}", TestContext.CurrentContext.Test.Name);
        }

        [TearDown]
        public virtual void TearDown()
        {
            Logger.LogInformation("Completed test: {TestName}", TestContext.CurrentContext.Test.Name);
        }
    }

    /// <summary>
    /// Windows固有テストの基底クラス
    /// </summary>
    [RequiresWindows]
    public abstract class WindowsTestFixture : BaseTestFixture
    {
        [OneTimeSetUp]
        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            TestEnvironmentChecker.RequireWindows();
        }
    }

    /// <summary>
    /// 管理者権限が必要なテストの基底クラス
    /// </summary>
    [RequiresAdmin]
    [RequiresWindows]
    public abstract class AdminTestFixture : WindowsTestFixture
    {
        [OneTimeSetUp]
        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            TestEnvironmentChecker.RequireAdministrator();
        }
    }

    /// <summary>
    /// ETWテスト用の基底クラス
    /// </summary>
    [RequiresAdmin]
    [RequiresWindows]
    public abstract class EtwTestFixture : AdminTestFixture
    {
        [OneTimeSetUp]
        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            TestEnvironmentChecker.RequireEtwAccess();
        }
    }
}
```

## 3. 実際のテストクラス例

### 3.1 ユニットテスト（WSL対応）

```csharp
// ProcTail.Core.Tests/Services/WatchTargetManagerTests.cs
namespace ProcTail.Core.Tests.Services
{
    [TestFixture]
    [UnitTest]
    public class WatchTargetManagerTests : BaseTestFixture
    {
        private IWatchTargetManager _manager = null!;
        private Mock<IProcessValidator> _mockProcessValidator = null!;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _mockProcessValidator = new Mock<IProcessValidator>();
            _manager = new WatchTargetManager(_mockProcessValidator.Object, Logger);
        }

        [Test]
        public async Task AddTarget_ValidPid_ReturnsTrue()
        {
            // Arrange
            _mockProcessValidator.Setup(x => x.ProcessExists(1234)).Returns(true);
            
            // Act
            var result = await _manager.AddTargetAsync(1234, "test-tag");
            
            // Assert
            Assert.IsTrue(result);
        }

        // 他のテストメソッド...
    }
}
```

### 3.2 Windows統合テスト

```csharp
// ProcTail.Integration.Tests/NamedPipeIntegrationTests.cs
namespace ProcTail.Integration.Tests
{
    [TestFixture]
    [IntegrationTest]
    [RequiresWindows]
    public class NamedPipeIntegrationTests : WindowsTestFixture
    {
        private WindowsNamedPipeServer _server = null!;
        private IPipeConfiguration _config = null!;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _config = new PipeConfiguration
            {
                PipeName = $"ProcTailTest_{Guid.NewGuid()}",
                MaxConcurrentConnections = 1
            };
            _server = new WindowsNamedPipeServer(_config, Logger);
        }

        [Test]
        public async Task StartServer_ValidConfiguration_StartsSuccessfully()
        {
            // Windows環境でのみ実行される統合テスト
            await _server.StartAsync();
            Assert.IsTrue(_server.IsRunning);
        }

        [TearDown]
        public override void TearDown()
        {
            _server?.Dispose();
            base.TearDown();
        }
    }
}
```

### 3.3 ETWシステムテスト

```csharp
// ProcTail.System.Tests/EtwSystemTests.cs
namespace ProcTail.System.Tests
{
    [TestFixture]
    [EndToEndTest]
    public class EtwSystemTests : EtwTestFixture
    {
        [Test]
        public async Task EtwProvider_FileCreation_CapturesEvent()
        {
            // 管理者権限 + Windows環境でのみ実行
            var config = new EtwConfiguration
            {
                EnabledProviders = new[] { "Microsoft-Windows-Kernel-FileIO" },
                EnabledEventNames = new[] { "FileIo/Create" }
            };

            using var provider = new WindowsEtwEventProvider(config, Logger);
            var eventCaptured = false;

            provider.EventReceived += (_, _) => eventCaptured = true;
            
            await provider.StartMonitoringAsync();
            
            // 実際にファイルを作成してETWイベントを発生
            var testFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(testFile, "test");
            
            // ETWイベントの到着を待機
            await Task.Delay(1000);
            
            Assert.IsTrue(eventCaptured);
            
            File.Delete(testFile);
        }
    }
}
```

## 4. フィルタ実行例

### 4.1 WSL環境での実行

```bash
# ユニットテストのみ実行
dotnet test --filter "Category=Unit"

# 統合テスト（モック使用）を実行
dotnet test --filter "Category=Integration"

# Windows依存テストを除外
dotnet test --filter "Category!=RequiresWindows&Category!=RequiresAdmin"
```

### 4.2 Windows環境での実行

```powershell
# 全テスト実行
dotnet test

# 管理者権限テストのみ実行
dotnet test --filter "Category=RequiresAdmin"

# パフォーマンステストのみ実行
dotnet test --filter "Category=Performance"

# エンドツーエンドテストのみ実行
dotnet test --filter "Category=EndToEnd"
```

この設計により、開発環境（WSL/Windows）と実行権限に応じて適切なテストセットを実行できます。