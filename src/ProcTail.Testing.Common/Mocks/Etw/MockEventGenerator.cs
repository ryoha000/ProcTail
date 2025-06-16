using ProcTail.Core.Models;

namespace ProcTail.Testing.Common.Mocks.Etw;

/// <summary>
/// モックイベント生成器
/// </summary>
public class MockEventGenerator
{
    private readonly MockEtwConfiguration _config;
    private readonly Random _random = new();
    private readonly string[] _sampleFilePaths;
    private readonly string[] _sampleProcessNames;

    /// <summary>
    /// プロセスイベントタイプ
    /// </summary>
    public enum ProcessEventType
    {
        Start,
        End
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="config">モック設定</param>
    public MockEventGenerator(MockEtwConfiguration config)
    {
        _config = config;
        _sampleFilePaths = new[]
        {
            @"C:\Windows\System32\notepad.exe",
            @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE",
            @"C:\Users\TestUser\Documents\test.txt",
            @"C:\temp\logfile.log",
            @"C:\Windows\explorer.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Users\TestUser\AppData\Local\Temp\temp.dat",
            @"C:\Windows\System32\cmd.exe"
        };
        
        _sampleProcessNames = new[]
        {
            "notepad.exe",
            "winword.exe",
            "explorer.exe",
            "chrome.exe",
            "cmd.exe",
            "powershell.exe",
            "devenv.exe",
            "code.exe"
        };
    }

    /// <summary>
    /// イベントを生成すべきかどうかを判定
    /// </summary>
    /// <returns>true: 生成する, false: 生成しない</returns>
    public bool ShouldGenerateEvent()
    {
        return _random.NextDouble() < 0.7; // 70%の確率でイベント生成
    }

    /// <summary>
    /// ランダムなイベントを生成
    /// </summary>
    /// <returns>生成されたイベントデータ</returns>
    public RawEventData GenerateRandomEvent()
    {
        var eventType = DetermineEventType();
        var processId = GetRandomProcessId();

        return eventType switch
        {
            "File" => GenerateFileEvent(processId),
            "ProcessStart" => GenerateProcessEvent(processId, ProcessEventType.Start),
            "ProcessEnd" => GenerateProcessEvent(processId, ProcessEventType.End),
            _ => GenerateGenericEvent(processId)
        };
    }

    /// <summary>
    /// ファイルイベントを生成
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="filePath">ファイルパス（指定されない場合はランダム）</param>
    /// <param name="operation">操作タイプ</param>
    /// <returns>ファイルイベントデータ</returns>
    public RawEventData GenerateFileEvent(int processId, string? filePath = null, string operation = "Create")
    {
        filePath ??= GetRandomFilePath();
        var eventName = $"FileIo/{operation}";
        
        var payload = new Dictionary<string, object>
        {
            { "FileName", filePath },
            { "IrpPtr", GenerateRandomHexPointer() },
            { "FileObject", GenerateRandomHexPointer() },
            { "CreateOptions", _random.Next(0, 0xFFFF) },
            { "CreateAttributes", _random.Next(0, 0xFF) },
            { "ShareAccess", _random.Next(0, 7) },
            { "OpenPath", filePath }
        };

        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-FileIO",
            eventName,
            processId,
            _random.Next(1000, 9999),
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }

    /// <summary>
    /// プロセスイベントを生成
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <param name="eventType">イベントタイプ</param>
    /// <returns>プロセスイベントデータ</returns>
    public RawEventData GenerateProcessEvent(int processId, ProcessEventType eventType)
    {
        var eventName = eventType == ProcessEventType.Start ? "Process/Start" : "Process/End";
        var processName = GetRandomProcessName();
        
        var payload = new Dictionary<string, object>
        {
            { "ProcessName", processName },
            { "ProcessId", processId },
            { "ImageFileName", processName }
        };

        if (eventType == ProcessEventType.Start)
        {
            payload.Add("CommandLine", $"{processName} {GenerateRandomCommandLineArgs()}");
            payload.Add("ParentProcessId", GetRandomProcessId());
            payload.Add("SessionId", _random.Next(0, 10));
        }
        else
        {
            payload.Add("ExitStatus", _random.Next(0, 5) == 0 ? _random.Next(1, 255) : 0);
            payload.Add("HandleCount", _random.Next(50, 1000));
            payload.Add("CommitCharge", _random.Next(1000, 100000));
        }

        return new RawEventData(
            DateTime.UtcNow,
            "Microsoft-Windows-Kernel-Process",
            eventName,
            processId,
            _random.Next(1000, 9999),
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }

    /// <summary>
    /// 汎用イベントを生成
    /// </summary>
    /// <param name="processId">プロセスID</param>
    /// <returns>汎用イベントデータ</returns>
    public RawEventData GenerateGenericEvent(int processId)
    {
        var providers = new[] { "Microsoft-Windows-Kernel-Registry", "Microsoft-Windows-Kernel-Network", "Custom-Test-Provider" };
        var events = new[] { "Registry/SetValue", "Network/Connect", "Custom/TestEvent" };
        
        var providerName = providers[_random.Next(providers.Length)];
        var eventName = events[_random.Next(events.Length)];
        
        var payload = new Dictionary<string, object>
        {
            { "EventId", _random.Next(1, 1000) },
            { "Level", _random.Next(1, 5) },
            { "Keywords", GenerateRandomHexPointer() },
            { "Task", _random.Next(1, 100) },
            { "Opcode", _random.Next(1, 20) }
        };

        // イベント固有のペイロード追加
        switch (eventName)
        {
            case "Registry/SetValue":
                payload.Add("RegistryKeyName", @"HKEY_LOCAL_MACHINE\SOFTWARE\Test");
                payload.Add("ValueName", "TestValue");
                payload.Add("ValueType", "REG_DWORD");
                payload.Add("ValueData", _random.Next(0, 0xFFFFFF));
                break;
            case "Network/Connect":
                payload.Add("SourceAddress", GenerateRandomIPAddress());
                payload.Add("DestinationAddress", GenerateRandomIPAddress());
                payload.Add("SourcePort", _random.Next(1024, 65535));
                payload.Add("DestinationPort", _random.Next(80, 8080));
                break;
            case "Custom/TestEvent":
                payload.Add("CustomData", "Test data for simulation");
                payload.Add("Sequence", _random.Next(1, 10000));
                break;
        }

        return new RawEventData(
            DateTime.UtcNow,
            providerName,
            eventName,
            processId,
            _random.Next(1000, 9999),
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload
        );
    }

    /// <summary>
    /// 次の遅延時間を取得
    /// </summary>
    /// <returns>遅延時間</returns>
    public TimeSpan GetNextDelay()
    {
        if (!_config.EnableRealisticTimings)
        {
            return _config.EventGenerationInterval;
        }

        // リアルなタイミングをシミュレート（指数分布風）
        var baseDelay = _config.EventGenerationInterval.TotalMilliseconds;
        var randomFactor = -Math.Log(1.0 - _random.NextDouble()) * 0.5; // 指数分布
        var delay = (int)(baseDelay * randomFactor);
        
        return TimeSpan.FromMilliseconds(Math.Max(10, Math.Min(delay, baseDelay * 5)));
    }

    private string DetermineEventType()
    {
        var rand = _random.NextDouble();
        
        if (rand < _config.FileEventProbability)
            return "File";
        
        if (rand < _config.FileEventProbability + _config.ProcessEventProbability)
            return _random.NextDouble() < 0.6 ? "ProcessStart" : "ProcessEnd";
        
        return "Generic";
    }

    private int GetRandomProcessId()
    {
        return _config.SimulatedProcessIds[_random.Next(_config.SimulatedProcessIds.Count)];
    }

    private string GetRandomFilePath()
    {
        return _sampleFilePaths[_random.Next(_sampleFilePaths.Length)];
    }

    private string GetRandomProcessName()
    {
        return _sampleProcessNames[_random.Next(_sampleProcessNames.Length)];
    }

    private string GenerateRandomCommandLineArgs()
    {
        var args = new[] { "--help", "-v", "/silent", "/log", "--config app.config", "--debug" };
        return args[_random.Next(args.Length)];
    }

    private string GenerateRandomHexPointer()
    {
        return $"0x{_random.Next(0x1000, 0x7FFFFFFF):X8}";
    }

    private string GenerateRandomIPAddress()
    {
        return $"{_random.Next(1, 255)}.{_random.Next(0, 255)}.{_random.Next(0, 255)}.{_random.Next(1, 255)}";
    }
}