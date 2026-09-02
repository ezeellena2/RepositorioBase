namespace CleanArchitecture.Application.IdentityAccess.Sessions;

/// <summary>
/// Only an explicit session state transition rotates the concurrency token, and each request performs exactly
/// one. Reloading the committed row and re-deciding therefore converges after a competing writer; exhausting
/// these attempts means genuine contention, which the caller receives as a typed conflict rather than a
/// generic failure (IA-REQ-035).
/// </summary>
internal static class SessionWriteRetry
{
    internal const int Attempts = 3;
}
