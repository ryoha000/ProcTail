using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcTail.Application.Services;

namespace ProcTail.Host.Workers;

/// <summary>
/// ProcTailのメインワーカーサービス
/// </summary>
public class ProcTailWorker : BackgroundService
{
    private readonly ILogger<ProcTailWorker> _logger;
    private readonly ProcTailService _procTailService;
    private readonly IOptionsMonitor<ProcTailOptions> _options;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcTailWorker(
        ILogger<ProcTailWorker> logger,
        ProcTailService procTailService,
        IOptionsMonitor<ProcTailOptions> options)
    {
        _logger = logger;
        _procTailService = procTailService;
        _options = options;
    }

    /// <summary>
    /// ワーカー実行処理
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcTail Worker starting...");

        try
        {
            // ProcTailサービス開始
            await _procTailService.StartAsync(stoppingToken);
            _logger.LogInformation("ProcTail Service started successfully");

            // サービス停止まで待機
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.CurrentValue.HealthCheckInterval, stoppingToken);
                
                // 簡単なヘルスチェック
                if (!_procTailService.IsRunning)
                {
                    _logger.LogWarning("ProcTail Service is not running, attempting restart...");
                    await _procTailService.StartAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ProcTail Worker stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcTail Worker encountered an error");
            throw;
        }
        finally
        {
            try
            {
                await _procTailService.StopAsync(CancellationToken.None);
                _logger.LogInformation("ProcTail Service stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping ProcTail Service");
            }
        }
    }
}