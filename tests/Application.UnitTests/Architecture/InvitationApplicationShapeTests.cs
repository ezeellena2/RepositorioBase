using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using MediatR;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public sealed class InvitationApplicationShapeTests
{
    private const string Namespace = "CleanArchitecture.Application.IdentityAccess.Invitations";

    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeAttribute).Assembly;

    [TestCase($"{Namespace}.InviteMember.InviteMemberCommand")]
    [TestCase($"{Namespace}.RegisterInvitedUser.RegisterInvitedUserCommand")]
    [TestCase($"{Namespace}.AcceptInvitation.AcceptInvitationCommand")]
    [TestCase($"{Namespace}.ResendInvitation.ResendInvitationCommand")]
    [TestCase($"{Namespace}.CancelInvitation.CancelInvitationCommand")]
    public void Invitation_request_exists(string typeName) =>
        ApplicationAssembly.GetType(typeName).ShouldNotBeNull(typeName);

    /// <summary>
    /// Issuing, resending and cancelling all act inside one organization, so each carries a tenant-scoped
    /// permission and the pipeline resolves the tenant from the validated session rather than from the route.
    /// </summary>
    [TestCase("InviteMember.InviteMemberCommand", "members.invite")]
    [TestCase("ResendInvitation.ResendInvitationCommand", "members.invite")]
    [TestCase("CancelInvitation.CancelInvitationCommand", "members.manage")]
    public void Tenant_scoped_invitation_requests_declare_their_permission(string typeName, string permission)
    {
        var request = ApplicationAssembly.GetType($"{Namespace}.{typeName}").ShouldNotBeNull(typeName);
        var authorize = request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();

        ApplicationRequestInventory.IsPublicRequest(request).ShouldBeFalse($"{typeName} must never be public.");
        authorize.Permission.ShouldBe(permission);
        authorize.RequiresTenant.ShouldBeTrue($"{typeName} acts inside one organization.");
    }

    /// <summary>
    /// An invitee has no membership yet, so acceptance cannot require a tenant — but it is not public either:
    /// IA-REQ-016 requires an authenticated identity, and only an authorized request produces an audited denial.
    /// </summary>
    [Test]
    public void Accepting_an_invitation_is_authenticated_self_service_without_a_tenant()
    {
        var request = ApplicationAssembly.GetType($"{Namespace}.AcceptInvitation.AcceptInvitationCommand")
            .ShouldNotBeNull("AcceptInvitationCommand");
        var authorize = request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();

        ApplicationRequestInventory.IsPublicRequest(request).ShouldBeFalse("acceptance requires an authenticated identity.");
        authorize.Permission.ShouldBe("identity.invitations.accept");
        authorize.RequiresTenant.ShouldBeFalse("the invitee has no membership in the tenant until acceptance succeeds.");
    }

    /// <summary>
    /// The invitee has no account and therefore no session when they register, so this one route is public by
    /// declaration rather than public by omission.
    /// </summary>
    [Test]
    public void Registering_from_an_invitation_is_explicitly_public()
    {
        var request = ApplicationAssembly.GetType($"{Namespace}.RegisterInvitedUser.RegisterInvitedUserCommand")
            .ShouldNotBeNull("RegisterInvitedUserCommand");

        ApplicationRequestInventory.IsPublicRequest(request).ShouldBeTrue();
        request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldBeEmpty("a public request declares no permission.");
    }

    [TestCase("RegisterInvitedUser.RegisterInvitedUserCommand")]
    [TestCase("AcceptInvitation.AcceptInvitationCommand")]
    public void Token_bearing_invitation_requests_are_classified_as_sensitive(string typeName)
    {
        var request = ApplicationAssembly.GetType($"{Namespace}.{typeName}").ShouldNotBeNull(typeName);

        request.GetInterfaces().Select(contract => contract.Name).ShouldContain(nameof(ISensitiveRequest));
    }

    /// <summary>
    /// The usable token is a credential minted for the recipient. Returning it to the inviter would let anyone
    /// holding <c>members.invite</c> register an account for an address they do not control, so nothing an
    /// invitation request answers with may carry it (IA-REQ-015/018).
    /// <para>
    /// This follows the declared <c>IRequest&lt;TResponse&gt;</c> of every invitation request and walks the whole
    /// response graph, rather than looking for two member names: a credential smuggled out as
    /// <c>Credential</c>, nested inside another record, or typed as raw bytes would all be missed by a name list.
    /// </para>
    /// </summary>
    [Test]
    public void Nothing_an_invitation_request_answers_with_can_carry_the_usable_token()
    {
        var invitationRequests = ApplicationRequestInventory.GetConcreteRequests(ApplicationAssembly)
            .Where(type => type.Namespace?.StartsWith(Namespace, StringComparison.Ordinal) == true)
            .ToArray();

        invitationRequests.Length.ShouldBe(5, "every invitation use case must be discoverable as a request.");
        foreach (var request in invitationRequests)
        {
            foreach (var responseType in ResponseTypesOf(request))
            {
                foreach (var carrier in SecretBearingMembers(responseType, []))
                {
                    Assert.Fail($"{request.FullName} answers with {carrier}, which can carry the recipient's credential back to the caller.");
                }
            }
        }
    }

    private static IEnumerable<Type> ResponseTypesOf(Type request) =>
        request.GetInterfaces()
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .SelectMany(response => response.IsGenericType ? response.GetGenericArguments().Append(response) : [response]);

    /// <summary>Walks a response graph and names any member that could hold secret material.</summary>
    private static IEnumerable<string> SecretBearingMembers(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type) || type.Assembly != ApplicationAssembly)
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsSecretBearing(property.Name))
            {
                yield return $"{type.Name}.{property.Name}";
                continue;
            }

            foreach (var nested in SecretBearingMembers(property.PropertyType, visited))
            {
                yield return $"{type.Name}.{property.Name} -> {nested}";
            }
        }
    }

    private static bool IsSecretBearing(string memberName) =>
        memberName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        memberName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        memberName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        memberName.Contains("credential", StringComparison.OrdinalIgnoreCase);
}
