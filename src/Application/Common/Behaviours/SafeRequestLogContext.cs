using System.Diagnostics;

namespace CleanArchitecture.Application.Common.Behaviours;

/// <summary>Provides the only request metadata that application pipeline logging may emit.</summary>
internal readonly record struct SafeRequestLogContext(string RequestName, string CorrelationId)
{
    public static SafeRequestLogContext Create<TRequest>() where TRequest : notnull =>
        new(typeof(TRequest).Name, Activity.Current?.TraceId.ToString() ?? "none");
}
