using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly Stopwatch _timer = new();
    private readonly ILogger<TRequest> _logger;

    public PerformanceBehaviour(ILogger<TRequest> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _timer.Start();
        var response = await next(cancellationToken);
        _timer.Stop();

        if (_timer.ElapsedMilliseconds > 500)
        {
            var metadata = SafeRequestLogContext.Create<TRequest>();
            _logger.LogWarning("CleanArchitecture Long Running Request: {RequestName} ({ElapsedMilliseconds} milliseconds); CorrelationId {CorrelationId}",
                metadata.RequestName, _timer.ElapsedMilliseconds, metadata.CorrelationId);
        }

        return response;
    }
}
