using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;

    public UnhandledExceptionBehaviour(ILogger<TRequest> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (Exception ex)
        {
            var metadata = SafeRequestLogContext.Create<TRequest>();
            _logger.LogError("CleanArchitecture Request: Unhandled exception for {RequestName}; ErrorType {ErrorType}; CorrelationId {CorrelationId}",
                metadata.RequestName, ex.GetType().Name, metadata.CorrelationId);
            throw;
        }
    }
}
