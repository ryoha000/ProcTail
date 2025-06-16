namespace ProcTail.Core.Interfaces;

/// <summary>
/// 設定管理の抽象化
/// </summary>
public interface IConfigurationManager
{
    /// <summary>
    /// 型指定で設定を取得
    /// </summary>
    /// <typeparam name="T">設定型</typeparam>
    /// <returns>設定インスタンス</returns>
    T GetConfiguration<T>() where T : class, new();

    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    void ValidateConfiguration();

    /// <summary>
    /// 設定の再読み込みを試行
    /// </summary>
    /// <returns>再読み込み成功の場合true</returns>
    bool TryReloadConfiguration();
}

/// <summary>
/// 設定変更通知イベント引数
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更されたセクション名
    /// </summary>
    public string SectionName { get; init; } = string.Empty;

    /// <summary>
    /// 設定の型
    /// </summary>
    public Type ConfigurationType { get; init; } = typeof(object);
}

/// <summary>
/// 設定変更通知
/// </summary>
public interface IConfigurationChangeNotifier
{
    /// <summary>
    /// 設定変更イベント
    /// </summary>
    event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
}