namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// Whether the session making this request has proved the Platform second factor at all (IA-REQ-045).
/// <para>
/// It is a separate question from <see cref="IRecentMfaVerifier"/>, and both are asked. Reading an operational
/// directory requires that this session completed the factor once; changing something additionally requires that
/// it did so recently. Password sign-in selects a sole active tenant on its own, so without this a session that
/// only presented a password reached the Platform directories — which is the hole it closes.
/// </para>
/// </summary>
public interface IPlatformMfaSessionProof
{
    Task<bool> HasProvedFactorAsync(CancellationToken cancellationToken);
}
