using CleanArchitecture.Application.Common.Behaviours;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace CleanArchitecture.Application.UnitTests.Common.Behaviours;

public class RequestLoggerTests
{
    private Mock<ILogger<TestRequest>> _logger = null!;

    [SetUp]
    public void Setup() => _logger = new Mock<ILogger<TestRequest>>();

    [Test]
    public async Task Logs_the_request_type_without_resolving_a_user_name()
    {
        var requestLogger = new LoggingBehaviour<TestRequest>(_logger.Object);

        await requestLogger.Process(new TestRequest(), CancellationToken.None);

        _logger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    public sealed record TestRequest;
}
