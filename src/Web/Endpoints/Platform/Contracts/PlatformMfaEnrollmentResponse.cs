namespace CleanArchitecture.Web.PlatformEndpoints.Contracts;

/// <summary>
/// Everything an enrollment hands its owner, and the only time either value is ever readable: the shared key to
/// put into an authenticator, a provisioning URI for the same key, and the one-time recovery codes. The secret is
/// stored encrypted and the codes only as hashes, so nothing can serve them again (IA-REQ-041).
/// </summary>
public sealed record PlatformMfaEnrollmentResponse(string SharedKey, string ProvisioningUri, IReadOnlyList<string> RecoveryCodes);

/// <summary>The invitation token an MFA gate is being answered for.</summary>
public sealed record PlatformMfaEnrollmentRequest(string Token);

/// <summary>The invitation token plus a code from the enrolled authenticator.</summary>
public sealed record PlatformMfaVerificationRequest(string Token, string Code);

/// <summary>A code from the authenticator of an administrator who already holds Platform authority.</summary>
public sealed record PlatformMfaStepUpRequest(string Code);
