using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using MediatR;
using NUnit.Framework;
using Shouldly;
using CleanArchitecture.Application.IdentityAccess.Platform;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// The Platform operations surface, checked by shape before any of it behaves (Task 15 Step 1).
/// <para>
/// Most of what is asserted here is absence. Platform is the one place in the system with authority over other
/// tenants, so the capabilities it must never grow — impersonation, deletion, a client-chosen tenant, a global
/// flag — are worth failing a build over rather than a review (IA-REQ-046).
/// </para>
/// </summary>
public sealed class PlatformApplicationShapeTests
{
    private const string Root = "CleanArchitecture.Application.IdentityAccess.Platform";

    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeAttribute).Assembly;

    private static Type Require(string name) =>
        ApplicationAssembly.GetType($"{Root}.{name}").ShouldNotBeNull(name);

    [TestCase("Bootstrap.BootstrapPlatformOwnerCommand")]
    [TestCase("Bootstrap.IPlatformBootstrapOptions")]
    [TestCase("Bootstrap.RecoverPendingPlatformOwnerInvitationCommand")]
    [TestCase("Bootstrap.RecoverPendingPlatformOwnerInvitationValidator")]
    [TestCase("Bootstrap.IPlatformBootstrapRecoveryRateLimiter")]
    [TestCase("Administrators.InvitePlatformAdministratorCommand")]
    [TestCase("Administrators.RevokePlatformAdministratorCommand")]
    [TestCase("Organizations.SuspendOrganizationTenantCommand")]
    [TestCase("Organizations.ReactivateOrganizationTenantCommand")]
    [TestCase("Queries.ListPlatformOrganizationsQuery")]
    [TestCase("Queries.ListPlatformIdentitiesQuery")]
    [TestCase("Queries.ListPlatformAdministratorsQuery")]
    [TestCase("Queries.ListPlatformAuditQuery")]
    [TestCase("Queries.PlatformDirectoryQuery")]
    [TestCase("Queries.PlatformAdministratorProjection")]
    [TestCase("IPlatformOperationalProjectionReader")]
    public void The_platform_type_exists(string name) => Require(name);

    /// <summary>
    /// Every operational request resolves an active Platform tenant and an explicit permission through the normal
    /// evaluator (IA-REQ-039). None of them is public, and none of them is tenant-free: reading or changing
    /// anything on Platform is done as a Platform member or not at all.
    /// </summary>
    [TestCase("Administrators.InvitePlatformAdministratorCommand", Permissions.PlatformAdminsManage)]
    [TestCase("Administrators.RevokePlatformAdministratorCommand", Permissions.PlatformAdminsManage)]
    [TestCase("Organizations.SuspendOrganizationTenantCommand", Permissions.PlatformTenantsManage)]
    [TestCase("Organizations.ReactivateOrganizationTenantCommand", Permissions.PlatformTenantsManage)]
    [TestCase("Queries.ListPlatformOrganizationsQuery", Permissions.PlatformOrganizationsRead)]
    [TestCase("Queries.ListPlatformIdentitiesQuery", Permissions.PlatformIdentitiesRead)]
    [TestCase("Queries.ListPlatformAdministratorsQuery", Permissions.PlatformAdminsRead)]
    [TestCase("Queries.ListPlatformAuditQuery", Permissions.PlatformAuditRead)]
    public void An_operational_request_requires_the_platform_tenant_and_its_own_permission(string name, string permission)
    {
        var request = Require(name);
        var authorize = request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();

        ApplicationRequestInventory.IsPublicRequest(request).ShouldBeFalse($"{name} must never be public.");
        authorize.Permission.ShouldBe(permission);
        authorize.RequiresTenant.ShouldBeTrue($"{name} acts as a member of the Platform tenant.");
    }

    /// <summary>
    /// Recovery is the one Platform request nobody can be authenticated for: it runs before the owner exists, and
    /// may run before any ApplicationUser does. It is public by declaration, and it is bodyless — accepting an
    /// email, an identity or a replacement recipient is exactly what IA-REQ-040 forbids.
    /// </summary>
    [Test]
    public void Recovery_is_a_bodyless_public_request_that_accepts_no_recipient()
    {
        var request = Require("Bootstrap.RecoverPendingPlatformOwnerInvitationCommand");

        typeof(IPublicRequest).IsAssignableFrom(request).ShouldBeTrue("recovery runs before any identity exists.");
        request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldBeEmpty();
        typeof(IBaseRequest).IsAssignableFrom(request).ShouldBeTrue();
        request.GetProperties().ShouldBeEmpty("recovery accepts no input at all.");
    }

    /// <summary>
    /// Bootstrap is not reachable over HTTP: it is the ceremony a deployment runs, driven by configuration, and a
    /// request anyone could send would be a way to create the Platform tenant from outside.
    /// </summary>
    [Test]
    public void Bootstrap_is_not_a_request_anyone_can_send()
    {
        var command = Require("Bootstrap.BootstrapPlatformOwnerCommand");

        typeof(IBaseRequest).IsAssignableFrom(command).ShouldBeFalse(
            "bootstrap is a hosted ceremony, not an endpoint anybody can reach.");
    }

    /// <summary>
    /// The one thing configuration may decide is which address receives the first invitation. It is never an
    /// authority: a configured email that changes later must not replace or elevate anybody (IA-REQ-040).
    /// </summary>
    [Test]
    public void Bootstrap_options_carry_only_the_configured_recipient()
    {
        var options = Require("Bootstrap.IPlatformBootstrapOptions");

        options.IsInterface.ShouldBeTrue();
        options.GetProperties().Select(property => property.Name).ShouldBe(["OwnerEmail"]);
        options.GetMethods().Where(method => !method.IsSpecialName).ShouldBeEmpty();
    }

    /// <summary>
    /// The rate limiter keys the pending invitation and the transport source, and nothing the caller supplies:
    /// a limiter an attacker can key differently for each attempt is not a limit.
    /// </summary>
    [Test]
    public void The_recovery_rate_limiter_is_keyed_by_state_not_by_input()
    {
        var limiter = Require("Bootstrap.IPlatformBootstrapRecoveryRateLimiter");

        limiter.IsInterface.ShouldBeTrue();
        var method = limiter.GetMethods().ShouldHaveSingleItem();
        method.GetParameters().Select(parameter => parameter.Name).ShouldNotContain(
            name => name!.Contains("email", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("recipient", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Bounded directories only: a caller may ask for a page, never for everything (IA-REQ-045).</summary>
    [Test]
    public void A_directory_query_carries_only_a_bounded_limit_and_an_opaque_cursor()
    {
        Require("Queries.PlatformDirectoryQuery").GetProperties().Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Cursor", "Limit"]);

        foreach (var name in new[]
                 {
                     "Queries.ListPlatformOrganizationsQuery", "Queries.ListPlatformIdentitiesQuery",
                     "Queries.ListPlatformAdministratorsQuery", "Queries.ListPlatformAuditQuery"
                 })
        {
            Require(name).GetProperties().Select(property => property.Name).ShouldBe(["Query"], $"{name} takes only a page request.");
        }
    }

    /// <summary>
    /// The administrator projection is what IA-REQ-044 allows and nothing more. The normalized email is in the
    /// list because the directory permission is what gates it; CUIT, credentials, tokens and profile payloads are
    /// not in it at all.
    /// </summary>
    [Test]
    public void The_administrator_projection_exposes_only_allowlisted_fields()
    {
        Require("Queries.PlatformAdministratorProjection").GetProperties().Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe([
                "EmailConfirmed", "IdentityId", "IsOwner", "MembershipId", "MembershipStatus",
                "MfaStatus", "NormalizedEmail", "SinceUtc"
            ]);
    }

    /// <summary>
    /// The capabilities Platform must never grow. A request named for any of them would be one; so would a
    /// property letting a caller choose the tenant it acts on, which is what makes cross-tenant context selection
    /// impossible rather than merely unimplemented (IA-REQ-046).
    /// </summary>
    [Test]
    public void The_platform_slice_declares_no_prohibited_capability()
    {
        var forbidden = new[] { "impersonat", "delete", "purge", "destroy", "elevate", "bypass", "superadmin", "assumeidentity" };

        foreach (var type in ApplicationAssembly.GetTypes()
                     .Where(candidate => candidate.Namespace?.StartsWith(Root, StringComparison.Ordinal) == true))
        {
            foreach (var term in forbidden)
            {
                type.Name.Contains(term, StringComparison.OrdinalIgnoreCase).ShouldBeFalse($"{type.Name} names a prohibited capability.");
            }
        }
    }

    /// <summary>
    /// No operational request may name a tenant. The tenant it acts as comes from the validated session, and the
    /// tenant it acts on is a route identifier the handler re-checks — never a field the caller can point
    /// anywhere (IA-REQ-006/046).
    /// </summary>
    [TestCase("Queries.ListPlatformOrganizationsQuery")]
    [TestCase("Queries.ListPlatformIdentitiesQuery")]
    [TestCase("Queries.ListPlatformAdministratorsQuery")]
    [TestCase("Queries.ListPlatformAuditQuery")]
    [TestCase("Administrators.InvitePlatformAdministratorCommand")]
    public void A_platform_request_cannot_choose_the_context_it_runs_in(string name)
    {
        foreach (var property in Require(name).GetProperties())
        {
            property.Name.Contains("tenant", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                $"{name}.{property.Name} would let a caller choose its context.");
        }
    }

    /// <summary>
    /// Every directory handler asks whether this session proved the second factor (IA-REQ-045).
    /// <para>
    /// The pipeline cannot ask it: tenant and permission are both satisfied by a session that presented only a
    /// password, because signing in selects the sole active tenant and the membership already carries the reads.
    /// So the question lives in the handlers, and a fifth directory added without it would be the same hole
    /// reopened. This pins the dependency; the functional tests pin that it is actually consulted.
    /// </para>
    /// </summary>
    [Test]
    public void Every_platform_directory_handler_asks_whether_this_session_proved_the_second_factor()
    {
        var handlers = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Namespace == $"{Root}.Queries")
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType && contract.Name.StartsWith("IRequestHandler", StringComparison.Ordinal)))
            .ToArray();

        handlers.Length.ShouldBe(4, "the four directories are the whole read surface of Platform.");
        foreach (var handler in handlers)
        {
            handler.GetConstructors().ShouldContain(
                constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IPlatformMfaSessionProof)),
                $"{handler.Name} reads a Platform directory without asking for proof of the second factor.");
        }
    }
}
