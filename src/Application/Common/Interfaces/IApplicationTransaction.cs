namespace CleanArchitecture.Application.Common.Interfaces;

/// <summary>Runs a use-case boundary in one durable database transaction.</summary>
public interface IApplicationTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
