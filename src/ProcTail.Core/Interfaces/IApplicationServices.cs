using ProcTail.Core.Models;

namespace ProcTail.Core.Interfaces;

/// <summary>
/// サービス状態
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// 停止中
    /// </summary>
    Stopped,

    /// <summary>
    /// 開始中
    /// </summary>
    Starting,

    /// <summary>
    /// 実行中
    /// </summary>
    Running,

    /// <summary>
    /// 停止中
    /// </summary>
    Stopping,

    /// <summary>
    /// エラー状態
    /// </summary>
    Error
}

/// <summary>
/// サービス状態変更イベント引数
/// </summary>
public class ServiceStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更前の状態
    /// </summary>
    public ServiceStatus PreviousStatus { get; init; }

    /// <summary>
    /// 変更後の状態
    /// </summary>
    public ServiceStatus CurrentStatus { get; init; }

    /// <summary>
    /// 変更理由
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// メインサービスの抽象化
/// </summary>
public interface IProcTailService
{
    /// <summary>
    /// サービス開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サービス停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サービス状態
    /// </summary>
    ServiceStatus Status { get; }

    /// <summary>
    /// 状態変更イベント
    /// </summary>
    event EventHandler<ServiceStatusChangedEventArgs> StatusChanged;
}

/// <summary>
/// IPC要求ハンドラーの抽象化
/// </summary>
public interface IIpcRequestHandler
{
    /// <summary>
    /// 要求を処理して応答を返す
    /// </summary>
    /// <param name="requestJson">要求JSON</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>応答JSON</returns>
    Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// サポートされる要求タイプ
    /// </summary>
    IReadOnlyList<Type> SupportedRequestTypes { get; }
}

/// <summary>
/// 特定の要求タイプのハンドラー
/// </summary>
/// <typeparam name="TRequest">要求型</typeparam>
/// <typeparam name="TResponse">応答型</typeparam>
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : class
    where TResponse : BaseResponse
{
    /// <summary>
    /// 要求を処理
    /// </summary>
    /// <param name="request">要求</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>応答</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}