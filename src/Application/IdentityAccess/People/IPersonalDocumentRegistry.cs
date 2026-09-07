using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// The two questions a `Personal` claim has to ask before it writes anything, and nothing else.
/// <para>
/// It is a narrow port rather than a DbSet on the application context for the reason the architecture guard states:
/// the fingerprint table must never become a queryable surface. There is no member here that lists documents, that
/// takes a document and answers whose it is, or that resolves a profile the caller does not own.
/// </para>
/// </summary>
public interface IPersonalDocumentRegistry
{
    /// <summary>Whether any unpurged row already holds this documentary identity, under any retained key version.</summary>
    Task<bool> IsRecordedAsync(IReadOnlyList<DocumentFingerprintValue> fingerprints, CancellationToken cancellationToken);

    /// <summary>Whether this identity already owns a `Personal` context.</summary>
    Task<bool> OwnsPersonalContextAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// The same question asked about everybody except one identity, which is what a correction needs: the
    /// subject's own row holds the value being replaced, and a correction that refused itself would be useless
    /// (IA-REQ-058).
    /// <para>
    /// It is a third narrow member rather than a filter the caller applies, for the reason the other two are
    /// narrow: there is still nothing here that lists documents or answers whose a document is.
    /// </para>
    /// </summary>
    Task<bool> IsRecordedByAnotherAsync(
        Guid identityId, IReadOnlyList<DocumentFingerprintValue> fingerprints, CancellationToken cancellationToken);
}
