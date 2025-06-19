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
            "FileIO/Delete",
            "FileIO/Rename",
            "FileIO/SetInfo",
            "Process/Start",
            "Process/End"
        };
        
        FilteringOptions = new EtwFilteringOptions
        {
            ExcludeSystemProcesses = true,
            MinimumProcessId = 4,
            ExcludedProcessNames = new[]
            {
                "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe"
            }.AsReadOnly(),
            IncludeFileExtensions = new[]
            {
                ".txt", ".log", ".exe", ".dll"
            }.AsReadOnly(),
            ExcludeFilePatterns = new[]
            {
                "C:\\Windows\\System32\\*",
                "*\\Temp\\*"
            }.AsReadOnly()
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
    /// 設定を読み込み
    /// </summary>
    private void LoadConfiguration()
    {
        try
        {
            var etwSection = _configuration.GetSection("ETW");
            
            // 設定ファイルが存在するかチェック
            if (!etwSection.Exists())
            {
                _logger.LogWarning("ETW設定セクションが見つかりません。デフォルト設定を使用します。");
                LoadDefaultConfiguration();
                return;
            }

            // プロバイダー設定
            var providers = etwSection.GetSection("EnabledProviders").Get<string[]>() ?? GetDefaultProviders();
            EnabledProviders = providers.AsReadOnly();

            // イベント名設定
            var eventNames = etwSection.GetSection("EnabledEventNames").Get<string[]>() ?? GetDefaultEventNames();
            EnabledEventNames = eventNames.AsReadOnly();

            // バッファ設定
            BufferSizeMB = etwSection.GetValue<int>("BufferSizeMB", 64);
            BufferCount = etwSection.GetValue<int>("BufferCount", 20);
            EventBufferTimeout = TimeSpan.FromMilliseconds(etwSection.GetValue<int>("EventBufferTimeoutMs", 1000));

            // フィルタリング設定
            var filteringSection = etwSection.GetSection("Filtering");
            FilteringOptions = new EtwFilteringOptions
            {
                ExcludeSystemProcesses = filteringSection.GetValue<bool>("ExcludeSystemProcesses", true),
                MinimumProcessId = filteringSection.GetValue<int>("MinimumProcessId", 4),
                ExcludedProcessNames = (filteringSection.GetSection("ExcludedProcessNames").Get<string[]>() ?? 
                    GetDefaultExcludedProcessNames()).AsReadOnly(),
                IncludeFileExtensions = (filteringSection.GetSection("IncludeFileExtensions").Get<string[]>() ?? 
                    GetDefaultIncludeFileExtensions()).AsReadOnly(),
                ExcludeFilePatterns = (filteringSection.GetSection("ExcludeFilePatterns").Get<string[]>() ?? 
                    GetDefaultExcludeFilePatterns()).AsReadOnly()
            };

            // パフォーマンス設定
            var performanceSection = etwSection.GetSection("Performance");
            PerformanceOptions = new EtwPerformanceOptions
            {
                EventProcessingBatchSize = performanceSection.GetValue<int>("EventProcessingBatchSize", 100),
                MaxEventQueueSize = performanceSection.GetValue<int>("MaxEventQueueSize", 10000),
                EventProcessingIntervalMs = performanceSection.GetValue<int>("EventProcessingIntervalMs", 10),
                EnableHighFrequencyEvents = performanceSection.GetValue<bool>("EnableHighFrequencyEvents", false),
                MaxEventsPerSecond = performanceSection.GetValue<int>("MaxEventsPerSecond", 1000)
            };

            _logger.LogInformation("ETW設定を読み込みました - プロバイダー: {ProviderCount}, イベント: {EventCount}", 
                EnabledProviders.Count, EnabledEventNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW設定読み込み中にエラーが発生しました。デフォルト設定を使用します。");
            LoadDefaultConfiguration();
        }
    }

    /// <summary>
    /// デフォルト設定を読み込み
    /// </summary>
    private void LoadDefaultConfiguration()
    {
        EnabledProviders = GetDefaultProviders().AsReadOnly();
        EnabledEventNames = GetDefaultEventNames().AsReadOnly();
        BufferSizeMB = 64;
        BufferCount = 20;
        EventBufferTimeout = TimeSpan.FromMilliseconds(1000);
        
        FilteringOptions = new EtwFilteringOptions
        {
            ExcludeSystemProcesses = true,
            MinimumProcessId = 4,
            ExcludedProcessNames = GetDefaultExcludedProcessNames().AsReadOnly(),
            IncludeFileExtensions = GetDefaultIncludeFileExtensions().AsReadOnly(),
            ExcludeFilePatterns = GetDefaultExcludeFilePatterns().AsReadOnly()
        };

        PerformanceOptions = new EtwPerformanceOptions
        {
            EventProcessingBatchSize = 100,
            MaxEventQueueSize = 10000,
            EventProcessingIntervalMs = 10,
            EnableHighFrequencyEvents = false,
            MaxEventsPerSecond = 1000
        };

        _logger?.LogInformation("デフォルトETW設定を使用します - プロバイダー: {ProviderCount}, イベント: {EventCount}, ファイル監視: 有効", 
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
            "Process/Stop"
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