using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ProcTail.Application.Services;

namespace ProcTail.Host.Workers;

/// <summary>
/// ProcTailサービスのヘルスチェック
/// </summary>
public class ProcTailHealthCheck : IHealthCheck
{
    private readonly ILogger<ProcTailHealthCheck> _logger;
    private readonly ProcTailService _procTailService;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcTailHealthCheck(
        ILogger<ProcTailHealthCheck> logger,
        ProcTailService procTailService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _procTailService = procTailService ?? throw new ArgumentNullException(nameof(procTailService));
    }

    /// <summary>
    /// ヘルスチェック実行
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthData = new Dictionary<string, object>();

            // サービス実行状態チェック
            var isRunning = _procTailService.IsRunning;
            healthData.Add("IsRunning", isRunning);

            if (!isRunning)
            {
                return HealthCheckResult.Unhealthy("ProcTailサービスが停止しています", data: healthData);
            }

            // 統計情報取得
            var statistics = await _procTailService.GetStatisticsAsync(cancellationToken);
            healthData.Add("TotalTags", statistics.TotalTags);
            healthData.Add("TotalEvents", statistics.TotalEvents);
            healthData.Add("EstimatedMemoryUsageMB", statistics.EstimatedMemoryUsage / 1024 / 1024);

            // メモリ使用量チェック
            var memoryUsageMB = statistics.EstimatedMemoryUsage / 1024 / 1024;
            if (memoryUsageMB > 1024) // 1GB以上で警告
            {
                _logger.LogWarning("メモリ使用量が高くなっています: {MemoryUsageMB}MB", memoryUsageMB);
                return HealthCheckResult.Degraded($"メモリ使用量が高くなっています: {memoryUsageMB}MB", data: healthData);
            }

            // イベント数チェック
            if (statistics.TotalEvents > 100000) // 10万イベント以上で警告
            {
                _logger.LogWarning("イベント数が多くなっています: {TotalEvents}", statistics.TotalEvents);
                return HealthCheckResult.Degraded($"イベント数が多くなっています: {statistics.TotalEvents}", data: healthData);
            }

            _logger.LogDebug("ヘルスチェック正常 - 実行中: {IsRunning}, イベント数: {TotalEvents}, メモリ: {MemoryMB}MB",
                isRunning, statistics.TotalEvents, memoryUsageMB);

            return HealthCheckResult.Healthy("ProcTailサービスは正常に動作しています", data: healthData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ヘルスチェック実行中にエラーが発生しました");
            
            return HealthCheckResult.Unhealthy("ヘルスチェック実行中にエラーが発生しました", 
                ex, 
                new Dictionary<string, object> { { "Error", ex.Message } });
        }
    }
}