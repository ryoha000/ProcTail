using System.CommandLine.Invocation;
using ProcTail.Cli.Services;

namespace ProcTail.Cli.Commands;

/// <summary>
/// CLIコマンドのベースクラス
/// </summary>
public abstract class BaseCommand
{
    protected readonly IProcTailPipeClient _pipeClient;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    protected BaseCommand(IProcTailPipeClient pipeClient)
    {
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
    }

    /// <summary>
    /// コマンドを実行
    /// </summary>
    public abstract Task ExecuteAsync(InvocationContext context);

    /// <summary>
    /// 成功メッセージを出力
    /// </summary>
    protected static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// エラーメッセージを出力
    /// </summary>
    protected static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// 警告メッセージを出力
    /// </summary>
    protected static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// 情報メッセージを出力
    /// </summary>
    protected static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"ℹ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// サービス接続をテスト
    /// </summary>
    protected async Task<bool> TestServiceConnectionAsync()
    {
        try
        {
            var isConnected = await _pipeClient.TestConnectionAsync();
            if (!isConnected)
            {
                WriteError("ProcTailサービスに接続できませんでした。");
                WriteInfo("サービスが起動していることを確認してください:");
                WriteInfo("  proctail service status");
                WriteInfo("  proctail service start");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            WriteError($"サービス接続テスト中にエラーが発生しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ユーザーに確認を求める
    /// </summary>
    protected static bool PromptConfirmation(string message, bool defaultValue = false)
    {
        var defaultText = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{message} [{defaultText}]: ");
        
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        return input switch
        {
            "y" or "yes" => true,
            "n" or "no" => false,
            "" => defaultValue,
            _ => defaultValue
        };
    }

    /// <summary>
    /// テーブル形式で出力
    /// </summary>
    protected static void WriteTable(string[] headers, string[][] rows)
    {
        if (headers.Length == 0 || rows.Length == 0)
            return;

        // 列幅を計算
        var columnWidths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            columnWidths[i] = headers[i].Length;
            foreach (var row in rows)
            {
                if (i < row.Length && row[i].Length > columnWidths[i])
                {
                    columnWidths[i] = row[i].Length;
                }
            }
        }

        // ヘッダーを出力
        WriteTableRow(headers, columnWidths);
        WriteTableSeparator(columnWidths);

        // データ行を出力
        foreach (var row in rows)
        {
            WriteTableRow(row, columnWidths);
        }
    }

    /// <summary>
    /// テーブル行を出力
    /// </summary>
    private static void WriteTableRow(string[] columns, int[] columnWidths)
    {
        for (int i = 0; i < columns.Length && i < columnWidths.Length; i++)
        {
            var value = i < columns.Length ? columns[i] : "";
            Console.Write($"| {value.PadRight(columnWidths[i])} ");
        }
        Console.WriteLine("|");
    }

    /// <summary>
    /// テーブル区切り線を出力
    /// </summary>
    private static void WriteTableSeparator(int[] columnWidths)
    {
        for (int i = 0; i < columnWidths.Length; i++)
        {
            Console.Write($"|{new string('-', columnWidths[i] + 2)}");
        }
        Console.WriteLine("|");
    }
}