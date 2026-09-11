namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// Durable, identifier-only contracts for account lifecycle notices. Each semantic template has its own stable
/// type so the outbox never stores rendered prose, template arguments, or a template selector (IA-REQ-029).
/// </summary>
public static class IdentityLifecycleNotice
{
    public const string SelfDeactivatedMessageType = "identity.lifecycle.self.deactivated.notice.requested";
    public const string AdministrativelySuspendedMessageType = "identity.lifecycle.administratively.suspended.notice.requested";
    public const string ReactivatedMessageType = "identity.lifecycle.reactivated.notice.requested";

    public sealed record Envelope(Guid IdentityId);
}
