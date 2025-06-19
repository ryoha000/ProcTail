using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;

namespace ProcTail.Infrastructure.Configuration;

/// <summary>
/// Named Pipe設定の実装
/// </summary>
public class NamedPipeConfiguration : INamedPipeConfiguration
{
    private readonly ILogger<NamedPipeConfiguration> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// パイプ名
    /// </summary>
    public string PipeName { get; private set; } = null!;

    /// <summary>
    /// 最大同時接続数
    /// </summary>
    public int MaxConcurrentConnections { get; private set; }

    /// <summary>
    /// バッファサイズ（バイト）
    /// </summary>
    public int BufferSize { get; private set; }

    /// <summary>
    /// 応答タイムアウト（秒）
    /// </summary>
    public int ResponseTimeoutSeconds { get; private set; }

    /// <summary>
    /// 接続タイムアウト（秒）
    /// </summary>
    public int ConnectionTimeoutSeconds { get; private set; }

    /// <summary>
    /// セキュリティ設定
    /// </summary>
    public NamedPipeSecurityOptions SecurityOptions { get; private set; } = null!;

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    public NamedPipePerformanceOptions PerformanceOptions { get; private set; } = null!;

    /// <summary>
    /// パラメーターレスコンストラクタ（デフォルト設定用）
    /// </summary>
    public NamedPipeConfiguration()
    {
        _logger = null!;
        _configuration = null!;
        LoadDefaultConfiguration();
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public NamedPipeConfiguration(
        ILogger<NamedPipeConfiguration> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // デフォルト設定を先に読み込む
        LoadDefaultConfiguration();
        
        // その後、設定ファイルから読み込み（上書き）
        LoadConfiguration();
    }

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    private void LoadConfiguration()
    {
        try
        {
            var pipeSection = _configuration.GetSection("NamedPipe");

            // 基本設定
            PipeName = pipeSection.GetValue<string>("PipeName") ?? GetDefaultPipeName();
            MaxConcurrentConnections = pipeSection.GetValue<int>("MaxConcurrentConnections", 10);
            BufferSize = pipeSection.GetValue<int>("BufferSize", 4096);
            ResponseTimeoutSeconds = pipeSection.GetValue<int>("ResponseTimeoutSeconds", 30);
            ConnectionTimeoutSeconds = pipeSection.GetValue<int>("ConnectionTimeoutSeconds", 10);

            // セキュリティ設定
            var securitySection = pipeSection.GetSection("Security");
            SecurityOptions = new NamedPipeSecurityOptions
            {
                AllowAdministratorsOnly = securitySection.GetValue<bool>("AllowAdministratorsOnly", true),
                AllowCurrentUserOnly = securitySection.GetValue<bool>("AllowCurrentUserOnly", false),
                RequireAuthentication = securitySection.GetValue<bool>("RequireAuthentication", true),
                AllowedUsers = (securitySection.GetSection("AllowedUsers").Get<string[]>() ?? 
                    Array.Empty<string>()).AsReadOnly(),
                DeniedUsers = (securitySection.GetSection("DeniedUsers").Get<string[]>() ?? 
                    Array.Empty<string>()).AsReadOnly()
            };

            // パフォーマンス設定
            var performanceSection = pipeSection.GetSection("Performance");
            PerformanceOptions = new NamedPipePerformanceOptions
            {
                UseAsynchronousIO = performanceSection.GetValue<bool>("UseAsynchronousIO", true),
                MaxMessageSize = performanceSection.GetValue<int>("MaxMessageSize", 1024 * 1024), // 1MB
                ConnectionPoolSize = performanceSection.GetValue<int>("ConnectionPoolSize", 5),
                EnableLogging = performanceSection.GetValue<bool>("EnableLogging", true),
                LogLevel = Enum.Parse<LogLevel>(
                    performanceSection.GetValue<string>("LogLevel") ?? "Information", true)
            };

            _logger.LogInformation("Named Pipe設定を読み込みました - パイプ名: {PipeName}, 最大接続数: {MaxConnections}", 
                PipeName, MaxConcurrentConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Named Pipe設定読み込み中にエラーが発生しました。デフォルト設定を使用します。");
            LoadDefaultConfiguration();
        }
    }

    /// <summary>
    /// デフォルト設定を読み込み
    /// </summary>
    private void LoadDefaultConfiguration()
    {
        PipeName = GetDefaultPipeName();
        MaxConcurrentConnections = 10;
        BufferSize = 4096;
        ResponseTimeoutSeconds = 30;
        ConnectionTimeoutSeconds = 10;

        SecurityOptions = new NamedPipeSecurityOptions
        {
            AllowAdministratorsOnly = true,
            AllowCurrentUserOnly = false,
            RequireAuthentication = true,
            AllowedUsers = Array.Empty<string>().AsReadOnly(),
            DeniedUsers = Array.Empty<string>().AsReadOnly()
        };

        PerformanceOptions = new NamedPipePerformanceOptions
        {
            UseAsynchronousIO = true,
            MaxMessageSize = 1024 * 1024,
            ConnectionPoolSize = 5,
            EnableLogging = true,
            LogLevel = LogLevel.Information
        };
    }

    /// <summary>
    /// デフォルトパイプ名を取得
    /// </summary>
    private static string GetDefaultPipeName()
    {
        return "ProcTail";
    }
}

/// <summary>
/// Named Pipeセキュリティ設定
/// </summary>
public class NamedPipeSecurityOptions
{
    /// <summary>
    /// 管理者のみアクセス許可
    /// </summary>
    public bool AllowAdministratorsOnly { get; init; }

    /// <summary>
    /// 現在のユーザーのみアクセス許可
    /// </summary>
    public bool AllowCurrentUserOnly { get; init; }

    /// <summary>
    /// 認証を要求するか
    /// </summary>
    public bool RequireAuthentication { get; init; }

    /// <summary>
    /// 許可するユーザー一覧
    /// </summary>
    public IReadOnlyList<string> AllowedUsers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 拒否するユーザー一覧
    /// </summary>
    public IReadOnlyList<string> DeniedUsers { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Named Pipeパフォーマンス設定
/// </summary>
public class NamedPipePerformanceOptions
{
    /// <summary>
    /// 非同期I/Oを使用するか
    /// </summary>
    public bool UseAsynchronousIO { get; init; }

    /// <summary>
    /// 最大メッセージサイズ（バイト）
    /// </summary>
    public int MaxMessageSize { get; init; }

    /// <summary>
    /// 接続プールサイズ
    /// </summary>
    public int ConnectionPoolSize { get; init; }

    /// <summary>
    /// ログを有効にするか
    /// </summary>
    public bool EnableLogging { get; init; }

    /// <summary>
    /// ログレベル
    /// </summary>
    public LogLevel LogLevel { get; init; }
}