using System.CommandLine.Invocation;
using System.Text.Json;
using ProcTail.Cli.Services;

namespace ProcTail.Cli.Commands;

/// <summary>
/// イベント取得コマンド
/// </summary>
public class GetEventsCommand : BaseCommand
{
    public GetEventsCommand(IProcTailPipeClient pipeClient) : base(pipeClient) { }

    public override async Task ExecuteAsync(InvocationContext context)
    {
        var tagName = "";
        var count = 50;
        var format = "table";
        var follow = false;
        
        // オプション値を取得
        foreach (var option in context.ParseResult.CommandResult.Command.Options)
        {
            var value = context.ParseResult.GetValueForOption(option);
            switch (option.Name)
            {
                case "tag":
                    tagName = value as string ?? "";
                    break;
                case "count":
                    count = (int?)value ?? 50;
                    break;
                case "format":
                    format = value as string ?? "table";
                    break;
                case "follow":
                    follow = (bool?)value ?? false;
                    break;
            }
        }

        if (!await TestServiceConnectionAsync())
        {
            context.ExitCode = 1;
            return;
        }

        try
        {
            var response = await _pipeClient.GetRecordedEventsAsync(tagName, count, context.GetCancellationToken());

            if (response.Success)
            {
                if (response.Events.Count == 0)
                {
                    WriteInfo($"タグ '{tagName}' のイベントは見つかりませんでした。");
                    return;
                }

                switch (format.ToLowerInvariant())
                {
                    case "json":
                        Console.WriteLine(JsonSerializer.Serialize(response.Events, new JsonSerializerOptions { WriteIndented = true }));
                        break;
                    case "csv":
                        WriteEventsCsv(response.Events);
                        break;
                    default:
                        WriteEventsTable(response.Events);
                        break;
                }
            }
            else
            {
                WriteError($"イベント取得に失敗しました: {response.ErrorMessage}");
                context.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"イベント取得中にエラーが発生しました: {ex.Message}");
            context.ExitCode = 1;
        }
    }

    private static void WriteEventsTable(IList<Core.Models.BaseEventData> events)
    {
        var headers = new[] { "時刻", "プロセスID", "イベント種別", "詳細" };
        var rows = events.Select(e => new[]
        {
            e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            e.ProcessId.ToString(),
            e.GetType().Name,
            GetEventDetails(e)
        }).ToArray();

        WriteTable(headers, rows);
    }

    private static void WriteEventsCsv(IList<Core.Models.BaseEventData> events)
    {
        Console.WriteLine("Timestamp,ProcessId,EventType,Details");
        foreach (var e in events)
        {
            Console.WriteLine($"{e.Timestamp:yyyy-MM-dd HH:mm:ss},{e.ProcessId},{e.GetType().Name},\"{GetEventDetails(e)}\"");
        }
    }

    private static string GetEventDetails(Core.Models.BaseEventData eventData)
    {
        return eventData switch
        {
            Core.Models.FileEventData fileEvent => $"{fileEvent.FilePath} ({fileEvent.EventName})",
            Core.Models.ProcessStartEventData processStart => $"子プロセス: {processStart.ChildProcessName} (PID: {processStart.ChildProcessId})",
            Core.Models.ProcessEndEventData processEnd => $"終了コード: {processEnd.ExitCode}",
            _ => eventData.EventName
        };
    }
}