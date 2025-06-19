using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using System.Collections.Concurrent;

namespace ProcTail.Infrastructure.Configuration;

/// <summary>
/// 設定管理の実装
/// </summary>
public class ConfigurationManager : Core.Interfaces.IConfigurationManager, IConfigurationChangeNotifier, IDisposable
{
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<Type, object> _configurationCache = new();
    private readonly FileSystemWatcher? _configFileWatcher;
    private bool _disposed;

    /// <summary>
    /// 設定変更イベント
    /// </summary>
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ConfigurationManager(
        ILogger<ConfigurationManager> logger,
        ILoggerFactory loggerFactory,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // 設定ファイルの監視を開始
        _configFileWatcher = CreateConfigFileWatcher();
        
        _logger.LogInformation("設定管理システムを初期化しました");
    }

    /// <summary>
    /// 型指定で設定を取得
    /// </summary>
    public T GetConfiguration<T>() where T : class, new()
    {
        var configType = typeof(T);
        
        if (_configurationCache.TryGetValue(configType, out var cachedConfig))
        {
            return (T)cachedConfig;
        }

        var config = CreateConfiguration<T>();
        _configurationCache.TryAdd(configType, config);
        
        _logger.LogDebug("設定を取得しました: {ConfigType}", configType.Name);
        return config;
    }

    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    public void ValidateConfiguration()
    {
        try
        {
            _logger.LogInformation("設定の妥当性を検証しています...");

            var validationResults = new List<string>();

            // ETW設定の検証
            if (ValidateEtwConfiguration(out var etwErrors))
            {
                _logger.LogInformation("ETW設定の検証が完了しました");
            }
            else
            {
                validationResults.AddRange(etwErrors);
            }

            // Named Pipe設定の検証
            if (ValidateNamedPipeConfiguration(out var pipeErrors))
            {
                _logger.LogInformation("Named Pipe設定の検証が完了しました");
            }
            else
            {
                validationResults.AddRange(pipeErrors);
            }

            if (validationResults.Count > 0)
            {
                var errorMessage = $"設定の検証でエラーが発生しました:\n{string.Join("\n", validationResults)}";
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            _logger.LogInformation("全ての設定の検証が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定検証中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 設定の再読み込みを試行
    /// </summary>
    public bool TryReloadConfiguration()
    {
        try
        {
            _logger.LogInformation("設定の再読み込みを実行しています...");

            // キャッシュをクリア
            _configurationCache.Clear();

            // IConfigurationRootの場合は再読み込み
            if (_configuration is IConfigurationRoot configRoot)
            {
                configRoot.Reload();
            }

            _logger.LogInformation("設定の再読み込みが完了しました");
            
            // 設定変更イベントを発火
            OnConfigurationChanged(new ConfigurationChangedEventArgs
            {
                SectionName = "All",
                ConfigurationType = typeof(object)
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定再読み込み中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 設定インスタンスを作成
    /// </summary>
    private T CreateConfiguration<T>() where T : class, new()
    {
        var configType = typeof(T);

        try
        {
            return configType.Name switch
            {
                nameof(EtwConfiguration) => (T)(object)new EtwConfiguration(
                    _loggerFactory.CreateLogger<EtwConfiguration>(),
                    _configuration),
                    
                nameof(NamedPipeConfiguration) => (T)(object)new NamedPipeConfiguration(
                    _loggerFactory.CreateLogger<NamedPipeConfiguration>(),
                    _configuration),
                    
                _ => CreateGenericConfiguration<T>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定作成中にエラーが発生しました: {ConfigType}", configType.Name);
            throw;
        }
    }

    /// <summary>
    /// 汎用設定を作成
    /// </summary>
    private T CreateGenericConfiguration<T>() where T : class, new()
    {
        var configType = typeof(T);
        var sectionName = configType.Name.Replace("Configuration", "");
        var section = _configuration.GetSection(sectionName);
        
        var config = section.Get<T>() ?? new T();
        return config;
    }

    /// <summary>
    /// ETW設定の検証
    /// </summary>
    private bool ValidateEtwConfiguration(out List<string> errors)
    {
        errors = new List<string>();

        try
        {
            var etwConfig = GetConfiguration<EtwConfiguration>();

            if (!etwConfig.EnabledProviders.Any())
            {
                errors.Add("ETW: 有効なプロバイダーが設定されていません");
            }

            if (!etwConfig.EnabledEventNames.Any())
            {
                errors.Add("ETW: 有効なイベント名が設定されていません");
            }

            if (etwConfig.BufferSizeMB <= 0 || etwConfig.BufferSizeMB > 1024)
            {
                errors.Add($"ETW: バッファサイズが無効です ({etwConfig.BufferSizeMB}MB)");
            }

            if (etwConfig.BufferCount <= 0 || etwConfig.BufferCount > 100)
            {
                errors.Add($"ETW: バッファ数が無効です ({etwConfig.BufferCount})");
            }

            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            errors.Add($"ETW設定検証中にエラーが発生しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Named Pipe設定の検証
    /// </summary>
    private bool ValidateNamedPipeConfiguration(out List<string> errors)
    {
        errors = new List<string>();

        try
        {
            var pipeConfig = GetConfiguration<NamedPipeConfiguration>();

            if (string.IsNullOrWhiteSpace(pipeConfig.PipeName))
            {
                errors.Add("NamedPipe: パイプ名が設定されていません");
            }

            if (pipeConfig.MaxConcurrentConnections <= 0 || pipeConfig.MaxConcurrentConnections > 100)
            {
                errors.Add($"NamedPipe: 最大同時接続数が無効です ({pipeConfig.MaxConcurrentConnections})");
            }

            if (pipeConfig.BufferSize <= 0 || pipeConfig.BufferSize > 1024 * 1024)
            {
                errors.Add($"NamedPipe: バッファサイズが無効です ({pipeConfig.BufferSize}バイト)");
            }

            if (pipeConfig.ResponseTimeoutSeconds <= 0 || pipeConfig.ResponseTimeoutSeconds > 300)
            {
                errors.Add($"NamedPipe: 応答タイムアウトが無効です ({pipeConfig.ResponseTimeoutSeconds}秒)");
            }

            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            errors.Add($"NamedPipe設定検証中にエラーが発生しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 設定ファイル監視を作成
    /// </summary>
    private FileSystemWatcher? CreateConfigFileWatcher()
    {
        try
        {
            var configPath = GetConfigurationFilePath();
            
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                _logger.LogWarning("設定ファイルが見つからないため、ファイル監視は無効です");
                return null;
            }

            var directory = Path.GetDirectoryName(configPath)!;
            var fileName = Path.GetFileName(configPath);

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnConfigFileChanged;
            
            _logger.LogInformation("設定ファイル監視を開始しました: {ConfigPath}", configPath);
            return watcher;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "設定ファイル監視の作成に失敗しました");
            return null;
        }
    }

    /// <summary>
    /// 設定ファイルパスを取得
    /// </summary>
    private string? GetConfigurationFilePath()
    {
        if (_configuration is IConfigurationRoot configRoot)
        {
            foreach (var provider in configRoot.Providers)
            {
                if (provider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider jsonProvider)
                {
                    var sourceProperty = jsonProvider.GetType()
                        .GetProperty("Source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (sourceProperty?.GetValue(jsonProvider) is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource source)
                    {
                        return source.Path;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 設定ファイル変更イベントハンドラー
    /// </summary>
    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _logger.LogInformation("設定ファイルの変更を検出しました: {FilePath}", e.FullPath);
            
            // 少し待機してからリロード（ファイルロックを避けるため）
            Task.Delay(500).ContinueWith(_ =>
            {
                if (TryReloadConfiguration())
                {
                    _logger.LogInformation("設定ファイルの変更を反映しました");
                }
                else
                {
                    _logger.LogError("設定ファイルの変更反映に失敗しました");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定ファイル変更処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 設定変更イベントを発火
    /// </summary>
    private void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        try
        {
            ConfigurationChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定変更イベント発火中にエラーが発生しました");
        }
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _configFileWatcher?.Dispose();
            _configurationCache.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConfigurationManager解放中にエラーが発生しました");
        }

        _logger.LogInformation("ConfigurationManagerが解放されました");
    }
}