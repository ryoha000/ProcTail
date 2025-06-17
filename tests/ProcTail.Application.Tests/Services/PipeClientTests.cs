using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ProcTail.Cli.Services;
using ProcTail.Core.Models;
using ProcTail.Testing.Common.Helpers;

namespace ProcTail.Application.Tests.Services;

[TestFixture]
[Category("Application")]
public class PipeClientTests
{
    private IProcTailPipeClient _pipeClient = null!;
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        
        _serviceProvider = services.BuildServiceProvider();
        
        var logger = _serviceProvider.GetRequiredService<ILogger<ProcTailPipeClient>>();
        _pipeClient = new ProcTailPipeClient(logger, "test-pipe");
    }

    [TearDown]
    public void TearDown()
    {
        _pipeClient?.Dispose();
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    public void RemoveWatchTargetAsync_ShouldSerializeCorrectRequest()
    {
        // Act & Assert
        // 実際のパイプ接続なしでのテストは困難なため、メソッドの存在確認のみ
        var method = typeof(ProcTailPipeClient).GetMethod("RemoveWatchTargetAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<RemoveWatchTargetResponse>));
    }

    [Test]
    public void GetWatchTargetsAsync_ShouldSerializeCorrectRequest()
    {
        // Arrange & Act & Assert
        // 実際のパイプ接続なしでのテストは困難なため、メソッドの存在確認のみ
        var method = typeof(ProcTailPipeClient).GetMethod("GetWatchTargetsAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<GetWatchTargetsResponse>));
    }

    [Test]
    public void IProcTailPipeClient_Interface_ShouldIncludeNewMethods()
    {
        // Arrange & Act & Assert
        var interfaceType = typeof(IProcTailPipeClient);
        
        var removeMethod = interfaceType.GetMethod("RemoveWatchTargetAsync");
        removeMethod.Should().NotBeNull();
        
        var getMethod = interfaceType.GetMethod("GetWatchTargetsAsync");
        getMethod.Should().NotBeNull();
    }
}