using System.Reflection;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// The Platform aggregates, checked by shape before any behaviour exists. Persistence is the first gate the plan
/// puts in front of MFA: an invitation that cannot be stored cannot bind an enrollment to it, so the MFA members
/// are asserted only after the invitation is real (Task 14 Steps 1 and 3).
/// </summary>
public sealed class PlatformMfaShapeTests
{
    private static readonly Assembly DomainAssembly = typeof(Tenant).Assembly;

    private const string PlatformNamespace = "CleanArchitecture.Domain.IdentityAccess.Platform";

    private static Type Require(string name) =>
        DomainAssembly.GetType($"{PlatformNamespace}.{name}").ShouldNotBeNull(name);

    [TestCase("PlatformAdminInvitation")]
    [TestCase("PlatformAdminInvitationId")]
    [TestCase("PlatformAdminInvitationStatus")]
    [TestCase("PlatformAdminInvitationDelivery")]
    public void The_platform_invitation_type_exists(string name) => Require(name);

    /// <summary>
    /// A Platform invitation is not an Organization one (SPEC section 5). Sharing the type would let a Platform
    /// offer be issued, resent or withdrawn by every code path that handles member invitations, and would put
    /// Platform authority behind the `members.*` permissions.
    /// </summary>
    [Test]
    public void The_platform_invitation_is_not_an_organization_invitation()
    {
        var platform = Require("PlatformAdminInvitation");
        var organization = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation")
            .ShouldNotBeNull("Invitation");

        platform.ShouldNotBe(organization);
        organization.IsAssignableFrom(platform).ShouldBeFalse("a Platform invitation must not be an Invitation.");
        platform.IsAssignableFrom(organization).ShouldBeFalse("an Invitation must not be a Platform invitation.");
    }

    /// <summary>
    /// Recipient, token hash, expiry and delivery state, plus the optional bound user. Delivery state is the one
    /// a reader might not expect: bootstrap recovery may only rotate an invitation that expired or whose delivery
    /// permanently failed, so the aggregate has to be able to say which (IA-REQ-040).
    /// </summary>
    [TestCase("TenantId")]
    [TestCase("NormalizedEmail")]
    [TestCase("TokenHash")]
    [TestCase("Status")]
    [TestCase("Delivery")]
    [TestCase("CreatedAt")]
    [TestCase("ExpiresAt")]
    [TestCase("BoundIdentityId")]
    public void The_platform_invitation_carries(string member) =>
        Require("PlatformAdminInvitation").GetProperty(member).ShouldNotBeNull(member);

    /// <summary>
    /// The usable token must be unreachable from the aggregate: only its hash may be modelled (IA-REQ-029).
    /// TokenHash is the one member allowed to name a token, and its type cannot be built from a precomputed
    /// value, so it cannot be holding the token itself.
    /// </summary>
    [Test]
    public void The_platform_invitation_cannot_carry_a_usable_token()
    {
        foreach (var property in Require("PlatformAdminInvitation").GetProperties())
        {
            if (property.Name == "TokenHash") continue;
            property.Name.Contains("token", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"{property.Name} may not name a token; only TokenHash may.");
        }
    }

    [Test]
    public void The_delivery_state_distinguishes_a_permanent_failure()
    {
        var delivery = Require("PlatformAdminInvitationDelivery");
        delivery.IsEnum.ShouldBeTrue();
        Enum.GetNames(delivery).ShouldBe(["Pending", "Delivered", "PermanentlyFailed"], ignoreOrder: true);
    }

    [Test]
    public void The_invitation_status_is_its_own_lifecycle()
    {
        var status = Require("PlatformAdminInvitationStatus");
        status.IsEnum.ShouldBeTrue();
        Enum.GetNames(status).ShouldBe(["Pending", "Accepted", "Cancelled"], ignoreOrder: true);
    }
    [TestCase("PlatformMfaEnrollment")]
    [TestCase("PlatformMfaEnrollmentId")]
    [TestCase("PlatformMfaEnrollmentStatus")]
    [TestCase("PlatformRecoveryCode")]
    public void The_mfa_type_exists(string name) => Require(name);

    /// <summary>
    /// One enrollment per identity, and it is reached by identity rather than by tenant: an invitee may hold a
    /// session with no active Platform tenant at all until the gates complete (IA-REQ-041).
    /// </summary>
    [TestCase("IdentityId")]
    [TestCase("EncryptedSecret")]
    [TestCase("Status")]
    [TestCase("CreatedAt")]
    [TestCase("VerifiedAt")]
    [TestCase("RecoveryAcknowledgedAt")]
    [TestCase("LastVerifiedAt")]
    [TestCase("RecoveryCodes")]
    public void The_enrollment_carries(string member) =>
        Require("PlatformMfaEnrollment").GetProperty(member).ShouldNotBeNull(member);

    /// <summary>
    /// The TOTP secret is encrypted and the recovery codes are hashed, so neither may be modelled in a member a
    /// reader could mistake for the usable value (IA-REQ-041).
    /// </summary>
    [Test]
    public void No_mfa_member_can_hold_a_usable_secret_or_code()
    {
        // EncryptedSecret is ciphertext, CodeHash is a digest, and RecoveryCodes is the navigation to rows that
        // themselves hold only digests. Every other member naming a secret would be one holding a usable value.
        var allowed = new[] { "EncryptedSecret", "CodeHash", "RecoveryCodes" };
        foreach (var type in new[] { Require("PlatformMfaEnrollment"), Require("PlatformRecoveryCode") })
        {
            foreach (var property in type.GetProperties())
            {
                if (allowed.Contains(property.Name, StringComparer.Ordinal)) continue;
                foreach (var forbidden in new[] { "secret", "code", "password", "token" })
                {
                    property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                        $"{type.Name}.{property.Name} may not name a {forbidden}.");
                }
            }
        }
    }

    [Test]
    public void The_enrollment_status_orders_the_gates()
    {
        var status = Require("PlatformMfaEnrollmentStatus");
        status.IsEnum.ShouldBeTrue();
        Enum.GetNames(status).ShouldBe(["Pending", "Verified", "Active"], ignoreOrder: true);
    }

    /// <summary>
    /// A recovery code is single use and stored only as a hash. Nothing here may say which identity it belongs
    /// to except through its enrollment, which is what keeps one enrollment the only way to reach them.
    /// </summary>
    [TestCase("EnrollmentId")]
    [TestCase("CodeHash")]
    [TestCase("ConsumedAt")]
    public void The_recovery_code_carries(string member) =>
        Require("PlatformRecoveryCode").GetProperty(member).ShouldNotBeNull(member);

    /// <summary>
    /// No global administrator member anywhere: authority is an active Platform membership plus an explicit
    /// permission, never a flag (IA-REQ-039/046).
    /// </summary>
    [Test]
    public void Nothing_in_the_platform_slice_declares_a_global_administrator()
    {
        foreach (var type in DomainAssembly.GetTypes().Where(candidate => candidate.Namespace == PlatformNamespace))
        {
            foreach (var member in type.GetProperties().Select(property => property.Name).Concat(type.GetFields().Select(field => field.Name)))
            {
                foreach (var forbidden in new[] { "SuperAdmin", "IsAdministrator", "IsGlobal", "Bypass", "Impersonat" })
                {
                    member.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                        $"{type.Name}.{member} would be authority outside the evaluator.");
                }
            }
        }
    }
}