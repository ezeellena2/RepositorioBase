using System.Diagnostics;

namespace CleanArchitecture.Application.IdentityAccess.Common;

/// <summary>
/// Correlates audit events with the request trace. When no trace is active, a random identifier still keeps the
/// events of one operation linkable to each other.
/// </summary>
public static class AuditCorrelation
{
    public static string Current() => Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
}
