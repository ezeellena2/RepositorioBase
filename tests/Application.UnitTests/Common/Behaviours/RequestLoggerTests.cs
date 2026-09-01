using CleanArchitecture.Application.Common.Behaviours;
using CleanArchitecture.Application.TodoItems.Commands.CreateTodoItem;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace CleanArchitecture.Application.UnitTests.Common.Behaviours;

public class RequestLoggerTests
{
    private Mock<ILogger<CreateTodoItemCommand>> _logger = null!;

    [SetUp]
    public void Setup() => _logger = new Mock<ILogger<CreateTodoItemCommand>>();

    [Test]
    public async Task Logs_the_request_type_without_resolving_a_user_name()
    {
        var requestLogger = new LoggingBehaviour<CreateTodoItemCommand>(_logger.Object);

        await requestLogger.Process(new CreateTodoItemCommand { ListId = 1, Title = "title" }, CancellationToken.None);

        _logger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
