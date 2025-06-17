namespace ProcTail.Core.Models;

/// <summary>
/// 監視対象の詳細情報
/// </summary>
public record WatchTargetInfo(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    DateTime StartTime,
    string TagName);

/// <summary>
/// 全IPC応答の基底クラス
/// </summary>
public abstract record BaseResponse
{
    /// <summary>
    /// 処理成功フラグ
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// エラーメッセージ（失敗時のみ設定）
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

// --- AddWatchTarget ---
/// <summary>
/// 監視対象追加要求
/// </summary>
public record AddWatchTargetRequest(int ProcessId, string TagName);

/// <summary>
/// 監視対象追加応答
/// </summary>
public record AddWatchTargetResponse : BaseResponse;

// --- RemoveWatchTarget ---
/// <summary>
/// 監視対象削除要求
/// </summary>
public record RemoveWatchTargetRequest(string TagName);

/// <summary>
/// 監視対象削除応答
/// </summary>
public record RemoveWatchTargetResponse : BaseResponse;

// --- GetRecordedEvents ---
/// <summary>
/// 記録イベント取得要求
/// </summary>
public record GetRecordedEventsRequest(string TagName);

/// <summary>
/// 記録イベント取得応答
/// </summary>
public record GetRecordedEventsResponse(List<BaseEventData> Events) : BaseResponse
{
    /// <summary>
    /// デフォルトコンストラクタ（失敗時用）
    /// </summary>
    public GetRecordedEventsResponse() : this(new List<BaseEventData>()) { }
}

// --- ClearEvents ---
/// <summary>
/// イベントクリア要求
/// </summary>
public record ClearEventsRequest(string TagName);

/// <summary>
/// イベントクリア応答
/// </summary>
public record ClearEventsResponse : BaseResponse;

// --- GetWatchTargets ---
/// <summary>
/// 監視対象一覧取得要求
/// </summary>
public record GetWatchTargetsRequest;

/// <summary>
/// 監視対象一覧取得応答
/// </summary>
public record GetWatchTargetsResponse(List<WatchTargetInfo> WatchTargets) : BaseResponse
{
    /// <summary>
    /// デフォルトコンストラクタ（失敗時用）
    /// </summary>
    public GetWatchTargetsResponse() : this(new List<WatchTargetInfo>()) { }
}

// --- Shutdown ---
/// <summary>
/// シャットダウン要求
/// </summary>
public record ShutdownRequest;

/// <summary>
/// シャットダウン応答
/// </summary>
public record ShutdownResponse : BaseResponse;

// --- HealthCheck ---
/// <summary>
/// ヘルスチェック要求
/// </summary>
public record HealthCheckRequest;

/// <summary>
/// ヘルスチェック応答
/// </summary>
public record HealthCheckResponse(string Status) : BaseResponse
{
    /// <summary>
    /// デフォルトコンストラクタ（失敗時用）
    /// </summary>
    public HealthCheckResponse() : this("Unhealthy") { }
}