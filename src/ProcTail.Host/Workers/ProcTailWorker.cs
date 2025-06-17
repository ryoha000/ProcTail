using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcTail.Application.Services;

namespace ProcTail.Host.Workers;

/// <summary>
/// ProcTailメインワーカーサービス
/// </summary>
public class ProcTailWorker : BackgroundService
{
    private readonly ILogger<ProcTailWorker> _logger;
    private readonly ProcTailService _procTailService;
    private readonly ProcTailOptions _options;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcTailWorker(
        ILogger<ProcTailWorker> logger,
        ProcTailService procTailService,
        IOptions<ProcTailOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _procTailService = procTailService ?? throw new ArgumentNullException(nameof(procTailService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// ワーカー開始
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("ProcTailワーカーサービスを開始しています...");
            
            // データディレクトリを作成
            EnsureDataDirectory();
            
            // ProcTailサービスを開始
            await _procTailService.StartAsync(cancellationToken);
            
            _logger.LogInformation("ProcTailワーカーサービスが正常に開始されました");
            
            await base.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailワーカーサービス開始中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// ワーカー停止
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("ProcTailワーカーサービスを停止しています...");
            
            // ProcTailサービスを停止
            await _procTailService.StopAsync(cancellationToken);
            
            _logger.LogInformation("ProcTailワーカーサービスが正常に停止されました");
            
            await base.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailワーカーサービス停止中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// バックグラウンド実行
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcTailワーカーサービス実行ループを開始しました");

        try
        {
            // メトリクス収集タスク
            var metricsTask = _options.EnableMetrics ? 
                CollectMetricsAsync(stoppingToken) : 
                Task.CompletedTask;

            // ヘルスチェックタスク
            var healthCheckTask = PerformHealthChecksAsync(stoppingToken);

            // クリーンアップタスク
            var cleanupTask = PerformMaintenanceAsync(stoppingToken);

            // 全てのタスクを並行実行
            await Task.WhenAll(metricsTask, healthCheckTask, cleanupTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ProcTailワーカーサービス実行ループが停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailワーカーサービス実行ループでエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// メトリクス収集
    /// </summary>
    private async Task CollectMetricsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.MetricsInterval, cancellationToken);

                if (_procTailService.IsRunning)
                {
                    var statistics = await _procTailService.GetStatisticsAsync(cancellationToken);
                    
                    _logger.LogDebug("メトリクス - 総タグ数: {TotalTags}, 総イベント数: {TotalEvents}, " +
                        "推定メモリ使用量: {MemoryUsageMB}MB",
                        statistics.TotalTags,
                        statistics.TotalEvents,
                        statistics.EstimatedMemoryUsage / 1024 / 1024);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "メトリクス収集中にエラーが発生しました");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    /// <summary>
    /// ヘルスチェック実行
    /// </summary>
    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HealthCheckInterval, cancellationToken);

                // サービス状態チェック
                var isHealthy = _procTailService.IsRunning;
                
                if (!isHealthy)
                {
                    _logger.LogWarning("ProcTailサービスが停止状態です");
                }

                _logger.LogDebug("ヘルスチェック完了 - 状態: {Status}", isHealthy ? "正常" : "異常");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ヘルスチェック中にエラーが発生しました");
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
        }
    }

    /// <summary>
    /// メンテナンス処理
    /// </summary>
    private async Task PerformMaintenanceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 1日に1回メンテナンス処理を実行
                await Task.Delay(TimeSpan.FromHours(24), cancellationToken);

                _logger.LogInformation("定期メンテナンスを開始します");

                // 古いログファイルのクリーンアップ
                await CleanupOldLogFilesAsync();

                // 古いイベントのクリーンアップ（実装が必要な場合）
                await CleanupOldEventsAsync();

                _logger.LogInformation("定期メンテナンスが完了しました");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "メンテナンス処理中にエラーが発生しました");
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
            }
        }
    }

    /// <summary>
    /// データディレクトリの確保
    /// </summary>
    private void EnsureDataDirectory()
    {
        try
        {
            if (!Directory.Exists(_options.DataDirectory))
            {
                Directory.CreateDirectory(_options.DataDirectory);
                _logger.LogInformation("データディレクトリを作成しました: {DataDirectory}", _options.DataDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "データディレクトリ作成中にエラーが発生しました: {DataDirectory}", _options.DataDirectory);
            throw;
        }
    }

    /// <summary>
    /// 古いログファイルのクリーンアップ
    /// </summary>
    private async Task CleanupOldLogFilesAsync()
    {
        try
        {
            var logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            
            if (!Directory.Exists(logsDirectory))
                return;

            var cutoffDate = DateTime.Now.AddDays(-_options.EventRetentionDays);
            var logFiles = Directory.GetFiles(logsDirectory, "*.log")
                .Where(file => File.GetCreationTime(file) < cutoffDate)
                .ToList();

            foreach (var logFile in logFiles)
            {
                try
                {
                    File.Delete(logFile);
                    _logger.LogDebug("古いログファイルを削除しました: {LogFile}", logFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ログファイル削除に失敗しました: {LogFile}", logFile);
                }
            }

            if (logFiles.Count > 0)
            {
                _logger.LogInformation("古いログファイルを {Count} 個削除しました", logFiles.Count);
            }

            await Task.Delay(100); // 非同期メソッドとして形式を保つ
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ログファイルクリーンアップ中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 古いイベントのクリーンアップ
    /// </summary>
    private async Task CleanupOldEventsAsync()
    {
        try
        {
            // 将来的に永続化されたイベントのクリーンアップを実装する場合
            // 現在はメモリ内のイベントのみなので、特別な処理は不要
            
            _logger.LogDebug("イベントクリーンアップを実行しました（現在はメモリ内のみ）");
            
            await Task.Delay(100); // 非同期メソッドとして形式を保つ
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベントクリーンアップ中にエラーが発生しました");
        }
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public override void Dispose()
    {
        try
        {
            _procTailService?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTailWorkerリソース解放中にエラーが発生しました");
        }

        base.Dispose();
    }
}