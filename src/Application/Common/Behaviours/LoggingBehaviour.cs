using MediatR.Pipeline;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly ILogger _logger;

    public LoggingBehaviour(ILogger<TRequest> logger) => _logger = logger;

    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        var metadata = SafeRequestLogContext.Create<TRequest>();
        _logger.LogInformation("CleanArchitecture Request: {RequestName} {CorrelationId}", metadata.RequestName, metadata.CorrelationId);
        return Task.CompletedTask;
    }
}
