using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

public sealed class RegistrationSubmission : BaseEntity<Guid>
{
    private RegistrationSubmission() { }

    public string CanonicalKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public RegistrationSubmissionOutcome? Outcome { get; private set; }

    public static RegistrationSubmission Claim(string canonicalKey, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalKey = string.IsNullOrWhiteSpace(canonicalKey) ? throw new ArgumentException("Canonical key cannot be empty.", nameof(canonicalKey)) : canonicalKey,
        CreatedAt = now
    };

    public void Complete(RegistrationSubmissionOutcome outcome, DateTimeOffset now)
    {
        if (CompletedAt is not null) throw new InvalidOperationException("A registration submission cannot be completed twice.");
        Outcome = outcome;
        CompletedAt = now;
    }
}
