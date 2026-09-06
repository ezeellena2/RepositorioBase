using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// The deployment's personal-data mode, as a port so the application never reads configuration directly.
/// <para>
/// It is a property of the deployment and never of a request: no caller, no header and no DTO field can reach it,
/// and an absent, blank or unparseable setting is <see cref="DataClassification.Synthetic"/>, so real handling is
/// never reached by omission or by a typo (IA-REQ-056).
/// </para>
/// </summary>
public interface IPersonalDataMode
{
    DataClassification Classification { get; }
}
