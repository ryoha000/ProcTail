using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcTail.Core.Interfaces;
using ProcTail.Testing.Common.Mocks.Etw;
using ProcTail.Testing.Common.Mocks.Ipc;

namespace ProcTail.Testing.Common.Helpers;

/// <summary>
/// モックサービスファクトリー
/// </summary>
public static class MockServiceFactory
{
    /// <summary>
    /// テスト用のサービスコレクションを作成
    /// </summary>
    /// <param name="configureEtw">ETW設定のカスタマイズ</param>
    /// <param name="configurePipe">パイプ設定のカスタマイズ</param>
    /// <returns>設定済みサービスコレクション</returns>
    public static IServiceCollection CreateTestServices(
        Action<MockEtwConfiguration>? configureEtw = null,
        Action<MockNamedPipeConfiguration>? configurePipe = null)
    {
        var services = new ServiceCollection();

        // ログ設定
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // ETW設定
        var etwConfig = MockEtwConfiguration.Default;
        configureEtw?.Invoke(etwConfig);
        services.AddSingleton(etwConfig);
        services.AddSingleton<IEtwConfiguration>(etwConfig);

        // パイプ設定
        var pipeConfig = MockNamedPipeConfiguration.Default;
        configurePipe?.Invoke(pipeConfig);
        services.AddSingleton(pipeConfig);

        // モックサービス登録
        services.AddSingleton<IEtwEventProvider, MockEtwEventProvider>();
        services.AddSingleton<INamedPipeServer, MockNamedPipeServer>();

        return services;
    }

    /// <summary>
    /// テスト用のサービスプロバイダーを作成
    /// </summary>
    /// <param name="configureEtw">ETW設定のカスタマイズ</param>
    /// <param name="configurePipe">パイプ設定のカスタマイズ</param>
    /// <returns>設定済みサービスプロバイダー</returns>
    public static IServiceProvider CreateTestServiceProvider(
        Action<MockEtwConfiguration>? configureEtw = null,
        Action<MockNamedPipeConfiguration>? configurePipe = null)
    {
        return CreateTestServices(configureEtw, configurePipe).BuildServiceProvider();
    }

    /// <summary>
    /// 高頻度イベント用のETWプロバイダーを作成
    /// </summary>
    /// <returns>高頻度イベント用ETWプロバイダー</returns>
    public static MockEtwEventProvider CreateHighFrequencyEtwProvider()
    {
        var config = new MockEtwConfiguration
        {
            EventGenerationInterval = TimeSpan.FromMilliseconds(10),
            FileEventProbability = 0.8,
            ProcessEventProbability = 0.15,
            GenericEventProbability = 0.05,
            EnableRealisticTimings = false,
            SimulatedProcessIds = new[] { 1111, 2222, 3333, 4444, 5555 }
        };

        return new MockEtwEventProvider(config);
    }

    /// <summary>
    /// 低頻度イベント用のETWプロバイダーを作成
    /// </summary>
    /// <returns>低頻度イベント用ETWプロバイダー</returns>
    public static MockEtwEventProvider CreateLowFrequencyEtwProvider()
    {
        var config = new MockEtwConfiguration
        {
            EventGenerationInterval = TimeSpan.FromSeconds(1),
            FileEventProbability = 0.4,
            ProcessEventProbability = 0.4,
            GenericEventProbability = 0.2,
            EnableRealisticTimings = true,
            SimulatedProcessIds = new[] { 9999 }
        };

        return new MockEtwEventProvider(config);
    }

    /// <summary>
    /// 高速レスポンス用のパイプサーバーを作成
    /// </summary>
    /// <returns>高速レスポンス用パイプサーバー</returns>
    public static MockNamedPipeServer CreateFastPipeServer()
    {
        var config = new MockNamedPipeConfiguration
        {
            PipeName = "fast-test-pipe",
            ResponseDelay = TimeSpan.Zero,
            SimulatedEventCount = 50,
            DefaultWatchTargets = new List<string> { @"C:\fast-test" }
        };

        return new MockNamedPipeServer(config);
    }

    /// <summary>
    /// 遅延レスポンス用のパイプサーバーを作成
    /// </summary>
    /// <returns>遅延レスポンス用パイプサーバー</returns>
    public static MockNamedPipeServer CreateSlowPipeServer()
    {
        var config = new MockNamedPipeConfiguration
        {
            PipeName = "slow-test-pipe",
            ResponseDelay = TimeSpan.FromMilliseconds(100),
            SimulatedEventCount = 1000,
            DefaultWatchTargets = new List<string> { @"C:\slow-test", @"C:\heavy-load" }
        };

        return new MockNamedPipeServer(config);
    }

    /// <summary>
    /// 統合テスト用の完全なモック環境を作成
    /// </summary>
    /// <returns>統合テスト用サービスプロバイダー</returns>
    public static IServiceProvider CreateIntegrationTestEnvironment()
    {
        return CreateTestServices(
            etwConfig =>
            {
                etwConfig.EventGenerationInterval = TimeSpan.FromMilliseconds(50);
                etwConfig.FileEventProbability = 0.6;
                etwConfig.ProcessEventProbability = 0.3;
                etwConfig.GenericEventProbability = 0.1;
                etwConfig.EnableRealisticTimings = true;
                etwConfig.SimulatedProcessIds = new[] { 1001, 1002, 1003 };
            },
            pipeConfig =>
            {
                pipeConfig.PipeName = "integration-test-pipe";
                pipeConfig.ResponseDelay = TimeSpan.FromMilliseconds(5);
                pipeConfig.SimulatedEventCount = 200;
                pipeConfig.DefaultWatchTargets = new List<string> { @"C:\integration-test" };
            }
        ).BuildServiceProvider();
    }

    /// <summary>
    /// カスタム設定でモック環境を作成
    /// </summary>
    /// <param name="eventInterval">イベント生成間隔</param>
    /// <param name="processIds">シミュレーション用プロセスID</param>
    /// <param name="pipeName">パイプ名</param>
    /// <param name="responseDelay">レスポンス遅延</param>
    /// <returns>カスタム設定サービスプロバイダー</returns>
    public static IServiceProvider CreateCustomMockEnvironment(
        TimeSpan eventInterval,
        int[] processIds,
        string pipeName,
        TimeSpan responseDelay)
    {
        return CreateTestServices(
            etwConfig =>
            {
                etwConfig.EventGenerationInterval = eventInterval;
                etwConfig.SimulatedProcessIds = processIds;
            },
            pipeConfig =>
            {
                pipeConfig.PipeName = pipeName;
                pipeConfig.ResponseDelay = responseDelay;
            }
        ).BuildServiceProvider();
    }
}