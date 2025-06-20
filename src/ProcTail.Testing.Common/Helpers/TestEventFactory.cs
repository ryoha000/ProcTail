using ProcTail.Core.Models;

namespace ProcTail.Testing.Common.Helpers;

/// <summary>
/// テスト用イベントファクトリー
/// </summary>
public static class TestEventFactory
{
    /// <summary>
    /// テスト用ファイルイベントを作成
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="tagName">タグ名</param>
    /// <param name="processId">プロセスID</param>
    /// <returns>ファイルイベントデータ</returns>
    public static FileEventData CreateFileEvent(
        string filePath = @"C:\test.txt",
        string tagName = "test-tag",
        int processId = 1234)
    {
        return new FileEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = tagName,
            ProcessId = processId,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-FileIO",
            EventName = "FileIO/Create",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = CreateDefaultPayload(),
            FilePath = filePath
        };
    }

    /// <summary>
    /// テスト用プロセス開始イベントを作成
    /// </summary>
    /// <param name="childProcessId">子プロセスID</param>
    /// <param name="childProcessName">子プロセス名</param>
    /// <param name="tagName">タグ名</param>
    /// <param name="processId">プロセスID</param>
    /// <returns>プロセス開始イベントデータ</returns>
    public static ProcessStartEventData CreateProcessStartEvent(
        int childProcessId = 9999,
        string childProcessName = "child.exe",
        string tagName = "test-tag",
        int processId = 1234)
    {
        return new ProcessStartEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = tagName,
            ProcessId = processId,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-Process",
            EventName = "Process/Start",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = CreateProcessPayload(childProcessName),
            ChildProcessId = childProcessId,
            ChildProcessName = childProcessName
        };
    }

    /// <summary>
    /// テスト用プロセス終了イベントを作成
    /// </summary>
    /// <param name="exitCode">終了コード</param>
    /// <param name="tagName">タグ名</param>
    /// <param name="processId">プロセスID</param>
    /// <returns>プロセス終了イベントデータ</returns>
    public static ProcessEndEventData CreateProcessEndEvent(
        int exitCode = 0,
        string tagName = "test-tag",
        int processId = 1234)
    {
        return new ProcessEndEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = tagName,
            ProcessId = processId,
            ThreadId = 5678,
            ProviderName = "Microsoft-Windows-Kernel-Process",
            EventName = "Process/End",
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = CreateDefaultPayload(),
            ExitCode = exitCode
        };
    }

    /// <summary>
    /// テスト用汎用イベントを作成
    /// </summary>
    /// <param name="providerName">プロバイダー名</param>
    /// <param name="eventName">イベント名</param>
    /// <param name="tagName">タグ名</param>
    /// <param name="processId">プロセスID</param>
    /// <returns>汎用イベントデータ</returns>
    public static GenericEventData CreateGenericEvent(
        string providerName = "Test-Provider",
        string eventName = "Test/Event",
        string tagName = "test-tag",
        int processId = 1234)
    {
        return new GenericEventData
        {
            Timestamp = DateTime.UtcNow,
            TagName = tagName,
            ProcessId = processId,
            ThreadId = 5678,
            ProviderName = providerName,
            EventName = eventName,
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            Payload = CreateDefaultPayload()
        };
    }

    /// <summary>
    /// テスト用Raw ETWイベントを作成
    /// </summary>
    /// <param name="providerName">プロバイダー名</param>
    /// <param name="eventName">イベント名</param>
    /// <param name="processId">プロセスID</param>
    /// <param name="payload">ペイロード</param>
    /// <returns>Raw ETWイベントデータ</returns>
    public static RawEventData CreateRawEvent(
        string providerName = "Test-Provider",
        string eventName = "Test/Event",
        int processId = 1234,
        Dictionary<string, object>? payload = null)
    {
        return new RawEventData(
            DateTime.UtcNow,
            providerName,
            eventName,
            processId,
            5678,
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload ?? CreateDefaultPayload()
        );
    }

    /// <summary>
    /// 複数のテストイベントを作成
    /// </summary>
    /// <param name="count">作成数</param>
    /// <param name="tagName">タグ名</param>
    /// <returns>イベントデータリスト</returns>
    public static List<BaseEventData> CreateMultipleEvents(int count = 10, string tagName = "test-tag")
    {
        var events = new List<BaseEventData>();
        var random = new Random();

        for (int i = 0; i < count; i++)
        {
            var eventType = random.Next(3);
            BaseEventData eventData = eventType switch
            {
                0 => CreateFileEvent($@"C:\test\file{i}.txt", tagName, 1000 + i),
                1 => CreateProcessStartEvent(9000 + i, $"process{i}.exe", tagName, 1000 + i),
                _ => CreateGenericEvent("Test-Provider", $"Test/Event{i}", tagName, 1000 + i)
            };

            events.Add(eventData);
        }

        return events;
    }

    /// <summary>
    /// 時系列でソートされたテストイベントを作成
    /// </summary>
    /// <param name="count">作成数</param>
    /// <param name="intervalSeconds">間隔（秒）</param>
    /// <param name="tagName">タグ名</param>
    /// <returns>時系列でソートされたイベントデータリスト</returns>
    public static List<BaseEventData> CreateTimeOrderedEvents(
        int count = 10,
        int intervalSeconds = 1,
        string tagName = "test-tag")
    {
        var events = new List<BaseEventData>();
        var baseTime = DateTime.UtcNow.AddMinutes(-count * intervalSeconds / 60.0);

        for (int i = 0; i < count; i++)
        {
            var fileEvent = new FileEventData
            {
                Timestamp = baseTime.AddSeconds(i * intervalSeconds),
                TagName = tagName,
                ProcessId = 1000 + i,
                ThreadId = 5678,
                ProviderName = "Microsoft-Windows-Kernel-FileIO",
                EventName = "FileIO/Create",
                ActivityId = Guid.NewGuid(),
                RelatedActivityId = Guid.NewGuid(),
                Payload = CreateDefaultPayload(),
                FilePath = $@"C:\test\file{i}.txt"
            };
            events.Add(fileEvent);
        }

        return events;
    }

    private static Dictionary<string, object> CreateDefaultPayload()
    {
        return new Dictionary<string, object>
        {
            { "TestKey", "TestValue" },
            { "Timestamp", DateTime.UtcNow },
            { "Counter", Random.Shared.Next(1, 1000) }
        };
    }

    private static Dictionary<string, object> CreateProcessPayload(string processName)
    {
        return new Dictionary<string, object>
        {
            { "CommandLine", $"{processName} --test" },
            { "WorkingDirectory", @"C:\test" },
            { "Environment", "TEST=1" },
            { "ParentProcessId", Random.Shared.Next(1000, 9999) }
        };
    }
}