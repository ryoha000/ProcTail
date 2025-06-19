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
        _logger = logger;
        _procTailService = procTailService;
    }

    /// <summary>
    /// ヘルスチェック実行
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_procTailService.IsRunning)
            {
                return Task.FromResult(HealthCheckResult.Healthy("ProcTail service is running"));
            }
            else
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("ProcTail service is not running"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Health check failed", ex));
        }
    }
}