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
            if (follow)
            {
                await FollowEventsAsync(tagName, format, context.GetCancellationToken());
            }
            else
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

    /// <summary>
    /// イベントをリアルタイムで監視
    /// </summary>
    /// <param name="tagName">タグ名</param>
    /// <param name="format">出力フォーマット</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    private async Task FollowEventsAsync(string tagName, string format, CancellationToken cancellationToken)
    {
        WriteInfo($"タグ '{tagName}' のイベントを監視中... (Ctrl+C で停止)");
        
        var lastEventCount = 0;
        var pollInterval = TimeSpan.FromSeconds(1);

        // CSV形式の場合、最初にヘッダーを出力
        if (format.ToLowerInvariant() == "csv")
        {
            Console.WriteLine("Timestamp,ProcessId,EventType,Details");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _pipeClient.GetRecordedEventsAsync(tagName, 1000, cancellationToken);
                
                if (response.Success && response.Events.Count > lastEventCount)
                {
                    // 新しいイベントのみを表示
                    var newEvents = response.Events.Skip(lastEventCount).ToList();
                    
                    foreach (var eventData in newEvents)
                    {
                        switch (format.ToLowerInvariant())
                        {
                            case "json":
                                Console.WriteLine(JsonSerializer.Serialize(eventData, new JsonSerializerOptions { WriteIndented = false }));
                                break;
                            case "csv":
                                Console.WriteLine($"{eventData.Timestamp:yyyy-MM-dd HH:mm:ss},{eventData.ProcessId},{eventData.GetType().Name},\"{GetEventDetails(eventData)}\"");
                                break;
                            default:
                                Console.WriteLine($"[{eventData.Timestamp:HH:mm:ss}] PID:{eventData.ProcessId} {eventData.GetType().Name}: {GetEventDetails(eventData)}");
                                break;
                        }
                    }
                    
                    lastEventCount = response.Events.Count;
                }
                
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                WriteError($"イベント監視中にエラーが発生しました: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        
        WriteInfo("イベント監視を停止しました。");
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