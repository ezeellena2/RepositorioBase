namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// Whether the caller has proved the second factor recently enough to change something on Platform
/// (IA-REQ-041/043). It is a port so that the freshness window is one configured value rather than a constant
/// each handler restates, and so a handler cannot accidentally answer the question for itself.
/// </summary>
public interface IRecentMfaVerifier
{
    Task<bool> HasRecentStepUpAsync(CancellationToken cancellationToken);
}
