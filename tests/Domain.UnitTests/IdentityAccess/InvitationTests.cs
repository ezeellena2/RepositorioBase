using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly VersionedTokenHash Hash = VersionedTokenHash.Of("invitation-test-token");
    private static readonly VersionedTokenHash RotatedHash = VersionedTokenHash.Of("invitation-test-rotated-token");

    [Test]
    public void Issue_binds_the_invitation_to_its_organization_tenant_and_starts_pending()
    {
        var tenant = Organization();
        var role = Role.Create(tenant, "member");

        var invitation = Invitation.Issue(tenant, "ana@example.test", [role], Hash, Now, Now.AddDays(7));

        invitation.Id.IsEmpty.ShouldBeFalse();
        invitation.TenantId.ShouldBe(tenant.Id);
        invitation.NormalizedEmail.ShouldBe("ana@example.test");
        invitation.TokenHash.ShouldBe(Hash);
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.CreatedAt.ShouldBe(Now);
        invitation.ExpiresAt.ShouldBe(Now.AddDays(7));
        invitation.AcceptedAt.ShouldBeNull();
        invitation.AcceptedByIdentityId.ShouldBeNull();
        invitation.CancelledAt.ShouldBeNull();
        invitation.Roles.Select(offered => offered.RoleId).ShouldBe([role.Id]);
        invitation.Roles.Select(offered => offered.TenantId).ShouldAllBe(tenantId => tenantId == tenant.Id);
        invitation.Roles.Select(offered => offered.InvitationId).ShouldAllBe(id => id == invitation.Id);
    }

    [TestCase("ana@example.test")]
    [TestCase("ANA@EXAMPLE.TEST")]
    [TestCase("  Ana@Example.Test  ")]
    [TestCase("\tana@EXAMPLE.test\n")]
    public void Issue_normalizes_the_recipient_deterministically(string submitted)
    {
        var tenant = Organization();

        var invitation = Invitation.Issue(tenant, submitted, [Role.Create(tenant, "member")], Hash, Now, Now.AddDays(7));

        invitation.NormalizedEmail.ShouldBe("ana@example.test");
    }

    [Test]
    public void Issue_rejects_a_tenant_that_is_not_an_organization()
    {
        var personal = Tenant.CreatePersonal(TenantSlug.From($"personal-{Guid.NewGuid():N}"));
        var role = Role.Create(personal, "member");

        Should.Throw<InvalidOperationException>(() => Invitation.Issue(personal, "ana@example.test", [role], Hash, Now, Now.AddDays(7)));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("ana-at-example.test")]
    [TestCase("İnfo@example.test", TestName = "a recipient whose canonical form the database could not verify")]
    [TestCase("ana surname@example.test", TestName = "a recipient carrying non-breaking whitespace")]
    public void Issue_rejects_a_recipient_without_a_plausible_address(string submitted)
    {
        var tenant = Organization();

        Should.Throw<ArgumentException>(() => Invitation.Issue(tenant, submitted, [Role.Create(tenant, "member")], Hash, Now, Now.AddDays(7)));
    }

    /// <summary>
    /// The same address written composed and decomposed must reach one canonical form, or each spelling would
    /// take its own pending slot. NFC is the declared policy and PostgreSQL verifies the same rule.
    /// </summary>
    [Test]
    public void Issue_composes_the_recipient_so_one_address_has_one_canonical_form()
    {
        var tenant = Organization();
        const string composed = "josé@example.test";
        const string decomposed = "josé@example.test";
        composed.ShouldNotBe(decomposed, "the two spellings really are different strings before normalization.");

        var first = Invitation.Issue(tenant, composed, [Role.Create(tenant, "member")], Hash, Now, Now.AddDays(7));
        var second = Invitation.Issue(tenant, decomposed, [Role.Create(tenant, "member")], RotatedHash, Now, Now.AddDays(7));

        first.NormalizedEmail.ShouldBe(second.NormalizedEmail);
        first.NormalizedEmail.ShouldBe(composed);
        first.NormalizedEmail.IsNormalized(System.Text.NormalizationForm.FormC).ShouldBeTrue();
    }

    [Test]
    public void Issue_requires_at_least_one_role()
    {
        var tenant = Organization();

        Should.Throw<ArgumentException>(() => Invitation.Issue(tenant, "ana@example.test", [], Hash, Now, Now.AddDays(7)));
    }

    [Test]
    public void Issue_rejects_a_role_from_another_tenant()
    {
        var tenant = Organization();
        var other = Organization();
        var foreignRole = Role.Create(other, "member");

        Should.Throw<InvalidOperationException>(() => Invitation.Issue(tenant, "ana@example.test", [Role.Create(tenant, "member"), foreignRole], Hash, Now, Now.AddDays(7)));
    }

    [Test]
    public void Issue_offers_every_distinct_role_exactly_once()
    {
        var tenant = Organization();
        var role = Role.Create(tenant, "member");
        var second = Role.Create(tenant, "auditor");

        var invitation = Invitation.Issue(tenant, "ana@example.test", [role, second, role], Hash, Now, Now.AddDays(7));

        invitation.Roles.Select(offered => offered.RoleId).ShouldBe([role.Id, second.Id], ignoreOrder: true);
    }


    [Test]
    public void Issue_rejects_an_expiry_that_does_not_outlive_its_creation()
    {
        var tenant = Organization();

        Should.Throw<ArgumentOutOfRangeException>(() => Invitation.Issue(tenant, "ana@example.test", [Role.Create(tenant, "member")], Hash, Now, Now));
    }

    [Test]
    public void An_invitation_is_pending_only_before_it_expires()
    {
        var invitation = Pending(out _);

        invitation.IsPendingAt(Now).ShouldBeTrue();
        invitation.IsPendingAt(Now.AddDays(7).AddTicks(-1)).ShouldBeTrue();
        invitation.IsPendingAt(Now.AddDays(7)).ShouldBeFalse();
    }

    [Test]
    public void Accept_records_the_accepting_identity_once()
    {
        var invitation = Pending(out var tenant);
        var identityId = Guid.NewGuid();

        invitation.Accept(tenant, identityId, Now.AddDays(1));

        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedByIdentityId.ShouldBe(identityId);
        invitation.AcceptedAt.ShouldBe(Now.AddDays(1));
        invitation.CancelledAt.ShouldBeNull();
        invitation.IsPendingAt(Now.AddDays(1)).ShouldBeFalse();
    }

    [Test]
    public void Accept_by_the_same_identity_again_is_idempotent()
    {
        var invitation = Pending(out var tenant);
        var identityId = Guid.NewGuid();
        invitation.Accept(tenant, identityId, Now.AddDays(1));

        invitation.Accept(tenant, identityId, Now.AddDays(2));

        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedByIdentityId.ShouldBe(identityId);
        invitation.AcceptedAt.ShouldBe(Now.AddDays(1), "a replay must not move the acceptance instant");
    }

    /// <summary>
    /// The terminal short-circuit runs before the liveness guard, exactly as <c>UserSession.Revoke</c> does.
    /// A client retrying against a short-lived invitation must not be told its own completed acceptance expired.
    /// </summary>
    [Test]
    public void Accept_by_the_same_identity_stays_idempotent_after_the_invitation_would_have_expired()
    {
        var invitation = Pending(out var tenant);
        var identityId = Guid.NewGuid();
        invitation.Accept(tenant, identityId, Now.AddDays(1));

        invitation.Accept(tenant, identityId, Now.AddDays(30));

        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedAt.ShouldBe(Now.AddDays(1));
    }

    [Test]
    public void Accept_by_a_different_identity_fails_without_changing_the_invitation()
    {
        var invitation = Pending(out var tenant);
        var accepted = Guid.NewGuid();
        invitation.Accept(tenant, accepted, Now.AddDays(1));

        Should.Throw<InvalidOperationException>(() => invitation.Accept(tenant, Guid.NewGuid(), Now.AddDays(2)));
        invitation.AcceptedByIdentityId.ShouldBe(accepted);
    }

    [Test]
    public void Accept_rejects_an_empty_identity()
    {
        var invitation = Pending(out var tenant);

        Should.Throw<ArgumentException>(() => invitation.Accept(tenant, Guid.Empty, Now.AddDays(1)));
    }

    /// <summary>An invitation cannot be settled before it was issued; the database restates the same bound.</summary>
    [Test]
    public void A_terminal_event_cannot_precede_the_invitation_it_settles()
    {
        var invitation = Pending(out var tenant);

        Should.Throw<ArgumentOutOfRangeException>(() => invitation.Accept(tenant, Guid.NewGuid(), Now.AddTicks(-1)));
        Should.Throw<ArgumentOutOfRangeException>(() => invitation.Cancel(tenant, Now.AddTicks(-1)));
        Should.Throw<ArgumentOutOfRangeException>(() => invitation.Reissue(tenant, RotatedHash, Now.AddTicks(-1), Now.AddDays(8)));
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    [Test]
    public void An_expired_invitation_cannot_be_accepted()
    {
        var invitation = Pending(out var tenant);

        var failure = Should.Throw<InvalidOperationException>(() => invitation.Accept(tenant, Guid.NewGuid(), Now.AddDays(7)));
        failure.Message.ShouldBe("Expired invitations cannot be accepted.");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.AcceptedByIdentityId.ShouldBeNull();
    }

    /// <summary>
    /// The message matters, not only the type: a cancelled invitation is not an expired one, and the guard that
    /// keeps them apart is invisible to a test that asserts the exception type alone.
    /// </summary>
    [Test]
    public void A_cancelled_invitation_cannot_be_accepted()
    {
        var invitation = Pending(out var tenant);
        invitation.Cancel(tenant, Now.AddDays(1));

        var failure = Should.Throw<InvalidOperationException>(() => invitation.Accept(tenant, Guid.NewGuid(), Now.AddDays(2)));
        failure.Message.ShouldBe("Only pending invitations can be accepted.");
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
    }

    /// <summary>
    /// Normalization emits one canonical form and refuses anything it cannot canonicalize in a way PostgreSQL
    /// can verify for itself. U+0130 is the concrete case: .NET's invariant mapping leaves it, PostgreSQL folds
    /// it, so accepting it would either bypass the database check or make a legitimate address fail it.
    /// </summary>
    [Test]
    public void Normalization_emits_a_canonical_form_and_refuses_what_it_cannot_canonicalize()
    {
        var tenant = Organization();

        var invitation = Invitation.Issue(tenant, "  ANA@Example.Test  ", [Role.Create(tenant, "member")], Hash, Now, Now.AddDays(7));

        invitation.NormalizedEmail.ShouldBe("ana@example.test");
        invitation.NormalizedEmail.Any(char.IsUpper).ShouldBeFalse("PostgreSQL lower() would change any that remained");
        invitation.NormalizedEmail.Any(char.IsWhiteSpace).ShouldBeFalse("btrim only removes spaces, so none may survive normalization");
        Should.Throw<ArgumentException>(() => Invitation.Issue(tenant, "İnfo@example.test", [Role.Create(tenant, "member")], RotatedHash, Now, Now.AddDays(7)));
    }

    [Test]
    public void Cancel_terminates_a_pending_invitation()
    {
        var invitation = Pending(out var tenant);

        invitation.Cancel(tenant, Now.AddDays(1));

        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
        invitation.CancelledAt.ShouldBe(Now.AddDays(1));
        invitation.AcceptedByIdentityId.ShouldBeNull();
        invitation.AcceptedAt.ShouldBeNull();
        invitation.IsPendingAt(Now.AddDays(1)).ShouldBeFalse();
    }

    [Test]
    public void Cancel_is_idempotent_and_keeps_the_first_instant()
    {
        var invitation = Pending(out var tenant);
        invitation.Cancel(tenant, Now.AddDays(1));

        invitation.Cancel(tenant, Now.AddDays(2));

        invitation.CancelledAt.ShouldBe(Now.AddDays(1));
    }

    [Test]
    public void An_accepted_invitation_cannot_be_cancelled()
    {
        var invitation = Pending(out var tenant);
        invitation.Accept(tenant, Guid.NewGuid(), Now.AddDays(1));

        Should.Throw<InvalidOperationException>(() => invitation.Cancel(tenant, Now.AddDays(2)));
        invitation.Status.ShouldBe(InvitationStatus.Accepted);
    }

    [Test]
    public void Reissue_rotates_the_token_hash_and_the_expiry_atomically()
    {
        var invitation = Pending(out var tenant);

        invitation.Reissue(tenant, RotatedHash, Now.AddDays(1), Now.AddDays(8));

        invitation.TokenHash.ShouldBe(RotatedHash, "the previous token no longer resolves to this invitation");
        invitation.ExpiresAt.ShouldBe(Now.AddDays(8));
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.IsPendingAt(Now.AddDays(7)).ShouldBeTrue("the reissued window replaces the lapsed one");
    }

    [Test]
    public void Reissue_revives_a_lapsed_invitation_in_place_rather_than_creating_a_second_one()
    {
        var invitation = Pending(out var tenant);
        invitation.IsPendingAt(Now.AddDays(20)).ShouldBeFalse();

        invitation.Reissue(tenant, RotatedHash, Now.AddDays(20), Now.AddDays(27));

        invitation.IsPendingAt(Now.AddDays(20)).ShouldBeTrue();
    }

    /// <summary>
    /// Reissuing to the same hash would extend the window while leaving the previous token usable, which is the
    /// opposite of what a reissue is for. A test that always supplies a fresh hash cannot see this.
    /// </summary>
    [Test]
    public void Reissue_must_actually_rotate_the_token()
    {
        var invitation = Pending(out var tenant);

        var failure = Should.Throw<ArgumentException>(() => invitation.Reissue(tenant, Hash, Now.AddDays(1), Now.AddDays(8)));

        failure.Message.ShouldContain("rotate");
        invitation.TokenHash.ShouldBe(Hash);
        invitation.ExpiresAt.ShouldBe(Now.AddDays(7), "a refused reissue must not extend the window either");
    }


    [Test]
    public void Reissue_rejects_an_expiry_that_does_not_outlive_the_reissue()
    {
        var invitation = Pending(out var tenant);

        Should.Throw<ArgumentOutOfRangeException>(() => invitation.Reissue(tenant, RotatedHash, Now.AddDays(1), Now.AddDays(1)));
        invitation.TokenHash.ShouldBe(Hash);
    }

    [Test]
    public void An_accepted_invitation_cannot_be_reissued()
    {
        var invitation = Pending(out var tenant);
        invitation.Accept(tenant, Guid.NewGuid(), Now.AddDays(1));

        Should.Throw<InvalidOperationException>(() => invitation.Reissue(tenant, RotatedHash, Now.AddDays(2), Now.AddDays(9)));
        invitation.TokenHash.ShouldBe(Hash);
    }

    [Test]
    public void A_cancelled_invitation_cannot_be_reissued()
    {
        var invitation = Pending(out var tenant);
        invitation.Cancel(tenant, Now.AddDays(1));

        Should.Throw<InvalidOperationException>(() => invitation.Reissue(tenant, RotatedHash, Now.AddDays(2), Now.AddDays(9)));
        invitation.Status.ShouldBe(InvitationStatus.Cancelled);
    }

    [Test]
    public void An_invitation_only_changes_within_its_own_tenant()
    {
        var invitation = Pending(out _);
        var other = Organization();

        Should.Throw<InvalidOperationException>(() => invitation.Accept(other, Guid.NewGuid(), Now.AddDays(1)));
        Should.Throw<InvalidOperationException>(() => invitation.Cancel(other, Now.AddDays(1)));
        Should.Throw<InvalidOperationException>(() => invitation.Reissue(other, RotatedHash, Now.AddDays(1), Now.AddDays(8)));
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    /// <summary>
    /// Deciding whether a signed-in caller is the recipient is the same canonicalization question the aggregate
    /// already answers when it is issued, so it belongs to the aggregate too. Asking the caller to canonicalize
    /// first would put a second copy of the rule outside the type that owns it, and the two would drift.
    /// </summary>
    [TestCase("ana@example.test")]
    [TestCase("  ANA@Example.Test  ")]
    public void An_invitation_recognises_its_recipient_however_the_address_is_spelled(string spelling)
    {
        var invitation = Pending(out _);

        invitation.IsAddressedTo(spelling).ShouldBeTrue();
    }

    [TestCase("bruno@example.test")]
    [TestCase("ana@other.test")]
    [TestCase("ana")]
    [TestCase("")]
    [TestCase(null)]
    public void An_invitation_does_not_recognise_anyone_else(string? spelling)
    {
        var invitation = Pending(out _);

        invitation.IsAddressedTo(spelling).ShouldBeFalse("an address that is not the recipient's is never the recipient's, however malformed");
    }

    private static Tenant Organization() => Tenant.CreateOrganization(TenantSlug.From($"invitation-{Guid.NewGuid():N}"));

    private static Invitation Pending(out Tenant tenant)
    {
        tenant = Organization();
        return Invitation.Issue(tenant, "ana@example.test", [Role.Create(tenant, "member")], Hash, Now, Now.AddDays(7));
    }
}
