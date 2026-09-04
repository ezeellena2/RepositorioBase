using System.Reflection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public class IdentityAccessContractShapeTests
{
    private static readonly Assembly DomainAssembly = typeof(Common.BaseEntity).Assembly;

    [TestCase("CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Organizations.OrganizationProfile")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Memberships.TenantMembership")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Auditing.AuditEvent")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.Role")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.RoleId")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.Permission")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.RolePermission")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Authorization.MembershipRole")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationId")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationStatus")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationRole")]
    public void RequiredAggregateTypeExists(string fullyQualifiedName)
    {
        DomainAssembly.GetType(fullyQualifiedName).ShouldNotBeNull();
    }

    [Test]
    public void TenantStatusExposesPendingConfirmation()
    {
        var tenantStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Tenants.TenantStatus");

        tenantStatus.ShouldNotBeNull();
        Enum.GetNames(tenantStatus!).ShouldContain("PendingConfirmation");
    }

    [Test]
    public void MembershipStatusExposesPendingConfirmation()
    {
        var membershipStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Memberships.MembershipStatus");

        membershipStatus.ShouldNotBeNull();
        Enum.GetNames(membershipStatus!).ShouldContain("PendingConfirmation");
    }

    [TestCase("Pending")]
    [TestCase("Accepted")]
    [TestCase("Cancelled")]
    public void InvitationStatusExposesItsLifecycleState(string memberName)
    {
        var invitationStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationStatus");

        invitationStatus.ShouldNotBeNull();
        Enum.GetNames(invitationStatus!).ShouldContain(memberName);
    }

    /// <summary>Expiry is derived from <c>ExpiresAt</c>, so a stored state nothing ever transitions into would rot.</summary>
    [Test]
    public void InvitationStatusHasNoStateWithoutATransition()
    {
        var invitationStatus = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationStatus");

        invitationStatus.ShouldNotBeNull();
        Enum.GetNames(invitationStatus!).ShouldBe(["Pending", "Accepted", "Cancelled"], ignoreOrder: true);
    }

    [TestCase("TenantId")]
    [TestCase("NormalizedEmail")]
    [TestCase("TokenHash")]
    [TestCase("ExpiresAt")]
    [TestCase("Status")]
    [TestCase("AcceptedByIdentityId")]
    [TestCase("Roles")]
    public void InvitationExposesItsRequiredMember(string memberName)
    {
        var invitation = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation");

        invitation.ShouldNotBeNull();
        invitation!.GetProperty(memberName).ShouldNotBeNull();
    }

    /// <summary>
    /// The usable token never reaches the aggregate: only its versioned hash is modelled, so no member can carry
    /// it into persistence, audit or a log (IA-REQ-015/029).
    /// <para>
    /// An allow-list over the shapes secret material realistically takes — text and raw bytes — rather than a
    /// deny-list of names, which only catches the members someone thought to forbid.
    /// </para>
    /// </summary>
    [Test]
    public void InvitationExposesNoTextualOrBinaryMemberBeyondItsRecipient()
    {
        var invitation = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation");

        invitation.ShouldNotBeNull();
        invitation!.GetProperties()
            .Where(property => property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[]))
            .Select(property => property.Name)
            .ShouldBe(["NormalizedEmail"], "the recipient is the only free text an invitation holds.");
    }

    /// <summary>
    /// The token hash is a type, not a string. That is what makes storing the token instead of its hash a
    /// compile error rather than something a format check has to notice after the fact.
    /// </summary>
    [Test]
    public void InvitationHoldsItsTokenHashAsATypeRatherThanText()
    {
        var invitation = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation");

        invitation.ShouldNotBeNull();
        invitation!.GetProperty("TokenHash").ShouldNotBeNull()
            .PropertyType.FullName.ShouldBe("CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash");
    }

    [Test]
    public void InvitationStateIsOnlyChangedThroughItsOwnBehaviour()
    {
        var invitation = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.Invitation");

        invitation.ShouldNotBeNull();
        invitation!.GetProperties().ShouldNotContain(property => property.SetMethod != null && property.SetMethod.IsPublic);
    }

    [TestCase("TenantId")]
    [TestCase("InvitationId")]
    [TestCase("RoleId")]
    public void InvitationRoleCarriesItsTenantAlongsideBothIdentifiers(string memberName)
    {
        var invitationRole = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationRole");

        invitationRole.ShouldNotBeNull();
        invitationRole!.GetProperty(memberName).ShouldNotBeNull();
    }

    [TestCase("New")]
    [TestCase("From")]
    public void InvitationIdFollowsTheStronglyTypedIdentifierConvention(string factoryName)
    {
        var invitationId = DomainAssembly.GetType("CleanArchitecture.Domain.IdentityAccess.Invitations.InvitationId");

        invitationId.ShouldNotBeNull();
        invitationId!.GetMethod(factoryName).ShouldNotBeNull();
        invitationId.GetProperty("Value").ShouldNotBeNull();
        invitationId.GetProperty("IsEmpty").ShouldNotBeNull();
        invitationId.GetConstructors().ShouldBeEmpty("an identifier is only reachable through New or From");
    }
}
