namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// What kind of person the stored row is about, derived by the server from the deployment's personal-data mode and
/// never from a request field (IA-REQ-056). It is stamped when the row is created and never recomputed, so a
/// deployment that later changes mode cannot silently reclassify what it already holds.
/// </summary>
public enum DataClassification
{
    Synthetic,
    Real
}
