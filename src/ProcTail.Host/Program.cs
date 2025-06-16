using Microsoft.Extensions.Hosting;

namespace ProcTail.Host;

/// <summary>
/// ProcTail ワーカーサービスのエントリーポイント
/// </summary>
public class Program
{
    /// <summary>
    /// メインエントリーポイント
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>非同期タスク</returns>
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    /// <summary>
    /// ホストビルダーを作成
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <returns>ホストビルダー</returns>
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices((context, services) =>
            {
                // TODO: DI設定をここに追加
            });
}