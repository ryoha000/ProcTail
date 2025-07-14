using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;

namespace ProcTail.Infrastructure.Configuration;

/// <summary>
/// ETW設定の実装
/// </summary>
public class EtwConfiguration : IEtwConfiguration
{
    private readonly ILogger<EtwConfiguration> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// 有効なETWプロバイダー一覧
    /// </summary>
    public IReadOnlyList<string> EnabledProviders { get; private set; }

    /// <summary>
    /// 有効なイベント名一覧
    /// </summary>
    public IReadOnlyList<string> EnabledEventNames { get; private set; }

    /// <summary>
    /// ETWセッションのバッファサイズ（MB）
    /// </summary>
    public int BufferSizeMB { get; private set; }

    /// <summary>
    /// ETWセッションのバッファ数
    /// </summary>
    public int BufferCount { get; private set; }

    /// <summary>
    /// イベントバッファタイムアウト
    /// </summary>
    public TimeSpan EventBufferTimeout { get; private set; }

    /// <summary>
    /// イベントフィルタリング設定
    /// </summary>
    public EtwFilteringOptions FilteringOptions { get; private set; }

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    public EtwPerformanceOptions PerformanceOptions { get; private set; }

    /// <summary>
    /// パラメーターレスコンストラクタ（デフォルト設定用）
    /// </summary>
    public EtwConfiguration()
    {
        // デフォルト設定を設定
        BufferSizeMB = 64;
        BufferCount = 20;
        EventBufferTimeout = TimeSpan.FromMilliseconds(1000);
        
        EnabledProviders = new[]
        {
            "Microsoft-Windows-Kernel-FileIO",
            "Microsoft-Windows-Kernel-Process"
        };
        
        EnabledEventNames = new[]
        {
            "FileIO/Create",
            "FileIO/Write",
            "FileIO/Read",
            "FileIO/Delete",
            "FileIO/Rename",
            "FileIO/SetInfo",
            "FileIO/Close",
            "Process/Start",
            "Process/Stop",
            "Process/End"
        };
        
        // フィルタリングを完全に無効化
        FilteringOptions = new EtwFilteringOptions
        {
            ExcludeSystemProcesses = false,
            MinimumProcessId = 0,
            ExcludedProcessNames = Array.Empty<string>().AsReadOnly(),
            IncludeFileExtensions = Array.Empty<string>().AsReadOnly(),
            ExcludeFilePatterns = Array.Empty<string>().AsReadOnly()
        };
        
        PerformanceOptions = new EtwPerformanceOptions
        {
            EventProcessingBatchSize = 100,
            MaxEventQueueSize = 10000,
            EventProcessingIntervalMs = 10,
            EnableHighFrequencyEvents = false,
            MaxEventsPerSecond = 1000
        };
        
        _logger = null!;
        _configuration = null!;
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public EtwConfiguration(
        ILogger<EtwConfiguration> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        EnabledProviders = Array.Empty<string>();
        EnabledEventNames = Array.Empty<string>();
        FilteringOptions = new EtwFilteringOptions();
        PerformanceOptions = new EtwPerformanceOptions();

        LoadConfiguration();
    }

    /// <summary>
    /// 設定を読み込み（ハードコード化された固定設定を使用）
    /// </summary>
    private void LoadConfiguration()
    {
        try
        {
            // 設定ファイルに関係なく、常に全てのイベントを監視
            EnabledProviders = GetDefaultProviders().AsReadOnly();
            EnabledEventNames = GetDefaultEventNames().AsReadOnly();

            // バッファ設定のみ設定ファイルから読み込み（パフォーマンス調整用）
            var etwSection = _configuration.GetSection("ETW");
            BufferSizeMB = etwSection.GetValue<int>("BufferSizeMB", 64);
            BufferCount = etwSection.GetValue<int>("BufferCount", 20);
            EventBufferTimeout = TimeSpan.FromMilliseconds(etwSection.GetValue<int>("EventBufferTimeoutMs", 1000));

            // フィルタリングを完全に無効化
            FilteringOptions = new EtwFilteringOptions
            {
                ExcludeSystemProcesses = false,
                MinimumProcessId = 0,
                ExcludedProcessNames = Array.Empty<string>().AsReadOnly(),
                IncludeFileExtensions = Array.Empty<string>().AsReadOnly(),
                ExcludeFilePatterns = Array.Empty<string>().AsReadOnly()
            };

            // パフォーマンス設定は設定ファイルから読み込み
            var performanceSection = etwSection.GetSection("Performance");
            PerformanceOptions = new EtwPerformanceOptions
            {
                EventProcessingBatchSize = performanceSection.GetValue<int>("EventProcessingBatchSize", 100),
                MaxEventQueueSize = performanceSection.GetValue<int>("MaxEventQueueSize", 10000),
                EventProcessingIntervalMs = performanceSection.GetValue<int>("EventProcessingIntervalMs", 10),
                EnableHighFrequencyEvents = performanceSection.GetValue<bool>("EnableHighFrequencyEvents", false),
                MaxEventsPerSecond = performanceSection.GetValue<int>("MaxEventsPerSecond", 1000)
            };

            _logger.LogInformation("ETW設定をハードコード化された値で初期化しました - プロバイダー: {ProviderCount}, イベント: {EventCount}, フィルタリング: 無効", 
                EnabledProviders.Count, EnabledEventNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW設定読み込み中にエラーが発生しました。固定設定を使用します。");
            LoadDefaultConfiguration();
        }
    }

    /// <summary>
    /// デフォルト設定を読み込み（フィルタリング無効化）
    /// </summary>
    private void LoadDefaultConfiguration()
    {
        EnabledProviders = GetDefaultProviders().AsReadOnly();
        EnabledEventNames = GetDefaultEventNames().AsReadOnly();
        BufferSizeMB = 64;
        BufferCount = 20;
        EventBufferTimeout = TimeSpan.FromMilliseconds(1000);
        
        // フィルタリングを完全に無効化
        FilteringOptions = new EtwFilteringOptions
        {
            ExcludeSystemProcesses = false,
            MinimumProcessId = 0,
            ExcludedProcessNames = Array.Empty<string>().AsReadOnly(),
            IncludeFileExtensions = Array.Empty<string>().AsReadOnly(),
            ExcludeFilePatterns = Array.Empty<string>().AsReadOnly()
        };

        PerformanceOptions = new EtwPerformanceOptions
        {
            EventProcessingBatchSize = 100,
            MaxEventQueueSize = 10000,
            EventProcessingIntervalMs = 10,
            EnableHighFrequencyEvents = false,
            MaxEventsPerSecond = 1000
        };

        _logger?.LogInformation("デフォルトETW設定を使用します - プロバイダー: {ProviderCount}, イベント: {EventCount}, フィルタリング: 無効", 
            EnabledProviders.Count, EnabledEventNames.Count);
    }

    /// <summary>
    /// デフォルトプロバイダー一覧
    /// </summary>
    private static string[] GetDefaultProviders()
    {
        return new[]
        {
            "Microsoft-Windows-Kernel-FileIO",
            "Microsoft-Windows-Kernel-Process"
        };
    }

    /// <summary>
    /// デフォルトイベント名一覧
    /// </summary>
    private static string[] GetDefaultEventNames()
    {
        return new[]
        {
            "FileIO/Create",
            "FileIO/Write", 
            "FileIO/Read",
            "FileIO/Delete",
            "FileIO/Rename",
            "FileIO/SetInfo",
            "FileIO/Close",
            "Process/Start",
            "Process/Stop",
            "Process/End"
        };
    }

    /// <summary>
    /// デフォルト除外プロセス名
    /// </summary>
    private static string[] GetDefaultExcludedProcessNames()
    {
        return new[]
        {
            "System",
            "Registry",
            "smss.exe",
            "csrss.exe",
            "wininit.exe",
            "winlogon.exe",
            "services.exe",
            "lsass.exe",
            "svchost.exe",
            "dwm.exe"
        };
    }

    /// <summary>
    /// デフォルト対象ファイル拡張子
    /// </summary>
    private static string[] GetDefaultIncludeFileExtensions()
    {
        return new[]
        {
            ".txt", ".log", ".cfg", ".conf", ".ini", ".xml", ".json",
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1",
            ".doc", ".docx", ".xls", ".xlsx", ".pdf",
            ".jpg", ".png", ".gif", ".mp4", ".avi",
            ".zip", ".rar", ".7z"
        };
    }

    /// <summary>
    /// デフォルト除外ファイルパターン
    /// </summary>
    private static string[] GetDefaultExcludeFilePatterns()
    {
        return new[]
        {
            @"C:\Windows\System32\*",
            @"C:\Windows\SysWOW64\*",
            @"C:\Windows\WinSxS\*",
            @"C:\Program Files\Windows Defender\*",
            @"*\Temp\*",
            @"*\$Recycle.Bin\*",
            @"*\.git\*",
            @"*\node_modules\*"
        };
    }
}

/// <summary>
/// ETWフィルタリング設定
/// </summary>
public class EtwFilteringOptions
{
    /// <summary>
    /// システムプロセスを除外するか
    /// </summary>
    public bool ExcludeSystemProcesses { get; init; }

    /// <summary>
    /// 監視対象の最小プロセスID
    /// </summary>
    public int MinimumProcessId { get; init; }

    /// <summary>
    /// 除外するプロセス名一覧
    /// </summary>
    public IReadOnlyList<string> ExcludedProcessNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 対象とするファイル拡張子
    /// </summary>
    public IReadOnlyList<string> IncludeFileExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 除外するファイルパターン
    /// </summary>
    public IReadOnlyList<string> ExcludeFilePatterns { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ETWパフォーマンス設定
/// </summary>
public class EtwPerformanceOptions
{
    /// <summary>
    /// イベント処理バッチサイズ
    /// </summary>
    public int EventProcessingBatchSize { get; init; }

    /// <summary>
    /// 最大イベントキューサイズ
    /// </summary>
    public int MaxEventQueueSize { get; init; }

    /// <summary>
    /// イベント処理間隔（ミリ秒）
    /// </summary>
    public int EventProcessingIntervalMs { get; init; }

    /// <summary>
    /// 高頻度イベントを有効にするか
    /// </summary>
    public bool EnableHighFrequencyEvents { get; init; }

    /// <summary>
    /// 1秒あたりの最大イベント数
    /// </summary>
    public int MaxEventsPerSecond { get; init; }
}