using Microsoft.Extensions.Logging;
using ProcTail.Core.Interfaces;
using ProcTail.Infrastructure.Configuration;

namespace ProcTail.System.Tests.Infrastructure;

/// <summary>
/// テスト用の設定実装
/// </summary>
public class TestNamedPipeConfiguration : INamedPipeConfiguration
{
    public string PipeName { get; set; } = "TestProcTailIPC";
    public int MaxConcurrentConnections { get; set; } = 5;
    public int BufferSize { get; set; } = 4096;
    public int ResponseTimeoutSeconds { get; set; } = 30;
    public int ConnectionTimeoutSeconds { get; set; } = 10;
    public NamedPipeSecurityOptions SecurityOptions { get; set; } = new();
    public NamedPipePerformanceOptions PerformanceOptions { get; set; } = new();
}

/// <summary>
/// テスト用のETW設定実装
/// </summary>
public class TestEtwConfiguration : IEtwConfiguration
{
    public IReadOnlyList<string> EnabledProviders { get; set; } = new List<string>
    {
        "Microsoft-Windows-FileInfoMinifilter", 
        "Microsoft-Windows-Kernel-Process"
    }.AsReadOnly();
    
    public IReadOnlyList<string> EnabledEventNames { get; set; } = new List<string>
    {
        "FileIo/Create", "FileIo/Write", "FileIo/Delete", "FileIo/Rename",
        "Process/Start", "Process/End"
    }.AsReadOnly();
    
    public TimeSpan EventBufferTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public int BufferSizeMB { get; set; } = 64;
    public int BufferCount { get; set; } = 32;
}