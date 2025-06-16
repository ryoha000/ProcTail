namespace ProcTail.Core.Models;

/// <summary>
/// ProcTailメイン設定
/// </summary>
public class ProcTailSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "ProcTail";

    /// <summary>
    /// イベント設定
    /// </summary>
    public EventSettings EventSettings { get; set; } = new();

    /// <summary>
    /// Named Pipe設定
    /// </summary>
    public PipeSettings PipeSettings { get; set; } = new();

    /// <summary>
    /// セキュリティ設定
    /// </summary>
    public SecuritySettings SecuritySettings { get; set; } = new();

    /// <summary>
    /// ログ設定
    /// </summary>
    public LoggingSettings LoggingSettings { get; set; } = new();
}

/// <summary>
/// イベント関連設定
/// </summary>
public class EventSettings
{
    /// <summary>
    /// タグごとの最大イベント記録数
    /// </summary>
    public int MaxEventsPerTag { get; set; } = 10000;

    /// <summary>
    /// 有効なイベントタイプ
    /// </summary>
    public List<string> EnabledEventTypes { get; set; } = new() { "FileIO", "Process" };

    /// <summary>
    /// イベントバッファタイムアウト
    /// </summary>
    public TimeSpan EventBufferTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 有効なETWプロバイダー
    /// </summary>
    public List<string> EnabledProviders { get; set; } = new()
    {
        "Microsoft-Windows-Kernel-FileIO",
        "Microsoft-Windows-Kernel-Process"
    };

    /// <summary>
    /// 有効なイベント名
    /// </summary>
    public List<string> EnabledEventNames { get; set; } = new()
    {
        "FileIo/Create",
        "FileIo/Write", 
        "FileIo/Delete",
        "FileIo/Rename",
        "FileIo/SetInfo",
        "Process/Start",
        "Process/End"
    };
}

/// <summary>
/// Named Pipe設定
/// </summary>
public class PipeSettings
{
    /// <summary>
    /// パイプ名
    /// </summary>
    public string PipeName { get; set; } = "ProcTailIPC";

    /// <summary>
    /// 最大同時接続数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10;

    /// <summary>
    /// 接続タイムアウト
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// バッファサイズ
    /// </summary>
    public int BufferSize { get; set; } = 4096;
}

/// <summary>
/// セキュリティ設定
/// </summary>
public class SecuritySettings
{
    /// <summary>
    /// 許可されたユーザーグループ
    /// </summary>
    public string AllowedUsers { get; set; } = "LocalAuthenticatedUsers";

    /// <summary>
    /// 管理者権限が必要か
    /// </summary>
    public bool RequireAdministrator { get; set; } = true;

    /// <summary>
    /// プロセス検証を有効にするか
    /// </summary>
    public bool EnableProcessValidation { get; set; } = true;
}

/// <summary>
/// ログ設定
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// ログレベル
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// ファイルログを有効にするか
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// イベントログを有効にするか
    /// </summary>
    public bool EnableEventLogging { get; set; } = true;

    /// <summary>
    /// ログファイルパス
    /// </summary>
    public string LogFilePath { get; set; } = "logs/proctail-.log";

    /// <summary>
    /// ログローテーション日数
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 7;
}

/// <summary>
/// 監視対象エントリ
/// </summary>
public record WatchTarget(
    int ProcessId,
    string TagName,
    DateTime RegisteredAt,
    bool IsChildProcess = false,
    int? ParentProcessId = null
);

/// <summary>
/// ヘルス状態
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 正常
    /// </summary>
    Healthy,

    /// <summary>
    /// 劣化（一部機能に問題）
    /// </summary>
    Degraded,

    /// <summary>
    /// 異常
    /// </summary>
    Unhealthy
}

/// <summary>
/// ヘルスチェック結果
/// </summary>
public record HealthCheckResult(
    HealthStatus Status,
    string Description,
    IReadOnlyDictionary<string, object>? Details = null
);

/// <summary>
/// ストレージ統計情報
/// </summary>
public record StorageStatistics(
    int TotalTags,
    int TotalEvents,
    IReadOnlyDictionary<string, int> EventCountByTag,
    long EstimatedMemoryUsage
);