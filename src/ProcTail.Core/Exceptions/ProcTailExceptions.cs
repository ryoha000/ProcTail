namespace ProcTail.Core.Exceptions;

/// <summary>
/// ProcTail例外の基底クラス
/// </summary>
public abstract class ProcTailException : Exception
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    protected ProcTailException(string message) : base(message) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    protected ProcTailException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// ドメインルール違反例外
/// </summary>
public class DomainException : ProcTailException
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public DomainException(string message) : base(message) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// インフラストラクチャ例外
/// </summary>
public class InfrastructureException : ProcTailException
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public InfrastructureException(string message) : base(message) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public InfrastructureException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 設定関連例外
/// </summary>
public class ConfigurationException : ProcTailException
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public ConfigurationException(string message) : base(message) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 権限関連例外
/// </summary>
public class PrivilegeException : ProcTailException
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public PrivilegeException(string message) : base(message) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public PrivilegeException(string message, Exception innerException) : base(message, innerException) { }
}