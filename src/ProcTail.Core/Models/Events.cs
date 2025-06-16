using System.Text.Json.Serialization;

namespace ProcTail.Core.Models;

/// <summary>
/// 全イベントデータの基底クラス
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FileEventData), typeDiscriminator: "file")]
[JsonDerivedType(typeof(ProcessStartEventData), typeDiscriminator: "process_start")]
[JsonDerivedType(typeof(ProcessEndEventData), typeDiscriminator: "process_end")]
[JsonDerivedType(typeof(GenericEventData), typeDiscriminator: "generic")]
public abstract record BaseEventData
{
    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 監視対象のタグ名
    /// </summary>
    public required string TagName { get; init; }

    /// <summary>
    /// イベントを発生させたプロセスID
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// イベントを発生させたスレッドID
    /// </summary>
    public required int ThreadId { get; init; }

    /// <summary>
    /// ETWプロバイダー名
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// ETWイベント名
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// ETWアクティビティID
    /// </summary>
    public required Guid ActivityId { get; init; }

    /// <summary>
    /// ETW関連アクティビティID
    /// </summary>
    public required Guid RelatedActivityId { get; init; }

    /// <summary>
    /// 全ペイロード情報の辞書
    /// </summary>
    public required IReadOnlyDictionary<string, object> Payload { get; init; }
}

/// <summary>
/// ファイル操作イベント
/// </summary>
public record FileEventData : BaseEventData
{
    /// <summary>
    /// 操作対象のファイルパス
    /// </summary>
    public required string FilePath { get; init; }
}

/// <summary>
/// プロセス開始イベント
/// </summary>
public record ProcessStartEventData : BaseEventData
{
    /// <summary>
    /// 開始された子プロセスID
    /// </summary>
    public required int ChildProcessId { get; init; }

    /// <summary>
    /// 開始された子プロセス名
    /// </summary>
    public required string ChildProcessName { get; init; }
}

/// <summary>
/// プロセス終了イベント
/// </summary>
public record ProcessEndEventData : BaseEventData
{
    /// <summary>
    /// プロセスの終了コード
    /// </summary>
    public required int ExitCode { get; init; }
}

/// <summary>
/// 汎用イベント（上記以外のイベント）
/// </summary>
public record GenericEventData : BaseEventData;

/// <summary>
/// ETWから受信した生イベントデータ
/// </summary>
public record RawEventData(
    DateTime Timestamp,
    string ProviderName,
    string EventName,
    int ProcessId,
    int ThreadId,
    Guid ActivityId,
    Guid RelatedActivityId,
    IReadOnlyDictionary<string, object> Payload
);

/// <summary>
/// イベント処理結果
/// </summary>
public record ProcessingResult(
    bool Success,
    BaseEventData? EventData = null,
    string? ErrorMessage = null
);