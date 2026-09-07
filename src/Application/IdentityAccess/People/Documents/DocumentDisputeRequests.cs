using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.People.Documents;

/// <summary>An opaque handle. It says a dispute exists and nothing about what it claims.</summary>
public sealed record OpenedDocumentDispute(Guid DisputeId);

/// <summary>
/// The owner's half of IA-REQ-058: saying the recorded document is wrong, and what it should be.
/// <para>
/// It carries no subject — the document is the caller's own, resolved from the validated session — and it never
/// writes a document value. The claimed number is protected on arrival exactly like the recorded one, and from
/// that moment nothing in this system can echo it, log it, audit it or put it in an outbox payload.
/// </para>
/// </summary>
[Authorize(Permissions.IdentityDocumentDispute, false)]
public sealed record OpenDocumentDisputeCommand(string ClaimedCountry, string ClaimedType, string ClaimedNumber, string ReasonCode)
    : IRequest<Result<OpenedDocumentDispute>>, ISensitiveRequest;

/// <summary>
/// The operator's half. It exists only against a stored dispute, and never for the operator's own identity:
/// those two absences are what "no support bypass" means, and they are the reason there is no other route in the
/// system that writes a document value.
/// </summary>
[Authorize(Permissions.PlatformDocumentsResolve, true)]
public sealed record ResolveDocumentDisputeCommand(Guid SubjectIdentityId, Guid DisputeId, string Outcome, string EvidenceReference)
    : IRequest<Result>;
