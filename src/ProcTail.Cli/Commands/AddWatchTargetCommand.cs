using System.CommandLine.Invocation;
using ProcTail.Cli.Services;

namespace ProcTail.Cli.Commands;

/// <summary>
/// 監視対象追加コマンド
/// </summary>
public class AddWatchTargetCommand : BaseCommand
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    public AddWatchTargetCommand(IProcTailPipeClient pipeClient) : base(pipeClient)
    {
    }

    /// <summary>
    /// コマンドを実行
    /// </summary>
    public override async Task ExecuteAsync(InvocationContext context)
    {
        var processId = 0;
        var processName = "";
        var tagName = "";
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            switch (option.Name)
            {
                case "process-id":
                case "pid":
                    processId = (int?)value ?? 0;
                    break;
                case "process-name":
                case "name":
                    processName = value as string;
                    break;
                case "tag":
                    tagName = value as string ?? "";
                    break;
            }
        }

        // プロセスIDまたはプロセス名のいずれかが必要
        if (processId == 0 && string.IsNullOrEmpty(processName))
        {
            WriteError("プロセスIDまたはプロセス名を指定してください。");
            WriteInfo("使用例:");
            WriteInfo("  proctail add --pid 1234 --tag my-app");
            WriteInfo("  proctail add --name notepad.exe --tag notepad");
            context.ExitCode = 1;
            return;
        }

        // サービス接続をテスト
        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        try
        {
            // プロセス名が指定された場合、プロセスIDを検索
            if (processId == 0 && !string.IsNullOrEmpty(processName))
            {
                processId = await FindProcessIdByNameAsync(processName);
                if (processId == 0)
                {
                    WriteError($"プロセス '{processName}' が見つかりませんでした。");
                    context.ExitCode = 1;
                    return;
                }
                WriteInfo($"プロセス '{processName}' のPID {processId} を監視対象に追加します。");
            }

            // 監視対象を追加
            var response = await _pipeClient.AddWatchTargetAsync(processId, tagName, context.GetCancellationToken());

            if (response.Success)
            {
                WriteSuccess($"監視対象を追加しました (PID: {processId}, タグ: {tagName})");
            }
            else
            {
                WriteError($"監視対象の追加に失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"監視対象追加中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }

    /// <summary>
    /// プロセス名からプロセスIDを検索
    /// </summary>
    private static async Task<int> FindProcessIdByNameAsync(string processName)
    {
        await Task.Delay(1); // 非同期メソッドとして形式を保つ

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(processName));

            if (processes.Length == 0)
                return 0;

            if (processes.Length == 1)
                return processes[0].Id;

            // 複数のプロセスが見つかった場合、ユーザーに選択を求める
            Console.WriteLine($"複数の '{processName}' プロセスが見つかりました:");
            for (int i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                try
                {
                    Console.WriteLine($"  {i + 1}. PID {process.Id} - {process.ProcessName} ({process.MainModule?.FileName ?? "不明"})");
                }
                catch
                {
                    Console.WriteLine($"  {i + 1}. PID {process.Id} - {process.ProcessName} (詳細情報を取得できませんでした)");
                }
            }

            Console.Write("選択してください (1-{0}): ", processes.Length);
            var input = Console.ReadLine();

            if (int.TryParse(input, out var selection) && selection >= 1 && selection <= processes.Length)
            {
                return processes[selection - 1].Id;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"プロセス検索中にエラーが発生しました: {ex.Message}");
            return 0;
        }
    }
}