using ProcTail.Core.Models;

namespace ProcTail.Core.Interfaces;

/// <summary>
/// プロセス情報
/// </summary>
public record ProcessInfo(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    DateTime StartTime,
    int? ParentProcessId
);

/// <summary>
/// プロセス検証の抽象化
/// </summary>
public interface IProcessValidator
{
    /// <summary>
    /// プロセスが存在するかチェック
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>存在する場合true</returns>
    bool ProcessExists(int processId);

    /// <summary>
    /// プロセス情報を取得
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>プロセス情報（存在しない場合null）</returns>
    ProcessInfo? GetProcessInfo(int processId);

    /// <summary>
    /// 子プロセス一覧を取得
    /// </summary>
    /// <param name="parentProcessId">親プロセスID</param>
    /// <returns>子プロセス一覧</returns>
    IReadOnlyList<ProcessInfo> GetChildProcesses(int parentProcessId);
}

/// <summary>
/// システム権限チェックの抽象化
/// </summary>
public interface ISystemPrivilegeChecker
{
    /// <summary>
    /// 管理者権限で実行中かチェック
    /// </summary>
    /// <returns>管理者権限の場合true</returns>
    bool IsRunningAsAdministrator();

    /// <summary>
    /// ETWセッションにアクセス可能かチェック
    /// </summary>
    /// <returns>アクセス可能な場合true</returns>
    bool CanAccessEtwSessions();

    /// <summary>
    /// 権限昇格を試行
    /// </summary>
    /// <returns>昇格成功の場合true</returns>
    Task<bool> TryElevatePrivilegesAsync();
}

/// <summary>
/// システム時刻の抽象化（テスト用）
/// </summary>
public interface ISystemClock
{
    /// <summary>
    /// 現在のUTC時刻
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// 現在のローカル時刻
    /// </summary>
    DateTime Now { get; }
}