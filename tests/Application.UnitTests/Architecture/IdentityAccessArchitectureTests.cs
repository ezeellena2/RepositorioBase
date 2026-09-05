using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// The structural promises the identity foundation rests on. Each one is cheap to break by accident and
/// expensive to discover later: a layer reaching the wrong way, a permission spelled by hand, a request that
/// nobody classified, or an aggregate quietly coupled to one that was added after it.
/// </summary>
public sealed class IdentityAccessArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Tenant).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Permissions).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.Data.ApplicationDbContext).Assembly;

    private static readonly string[] EarlierDomainSlices =
    [
        "CleanArchitecture.Domain.IdentityAccess.Auditing",
        "CleanArchitecture.Domain.IdentityAccess.Authorization",
        "CleanArchitecture.Domain.IdentityAccess.Identities",
        "CleanArchitecture.Domain.IdentityAccess.Memberships",
        "CleanArchitecture.Domain.IdentityAccess.Organizations",
        "CleanArchitecture.Domain.IdentityAccess.Outbox",
        "CleanArchitecture.Domain.IdentityAccess.Tenants"
    ];

    private static readonly string[] LateDomainSlices =
    [
        "CleanArchitecture.Domain.IdentityAccess.Sessions",
        "CleanArchitecture.Domain.IdentityAccess.Invitations",
        "CleanArchitecture.Domain.IdentityAccess.Platform"
    ];

    [Test]
    public void Each_layer_depends_only_on_the_ones_beneath_it()
    {
        ReferencedProjectAssemblies(DomainAssembly).ShouldBeEmpty(
            "the domain is the innermost layer and must not reference any other project.");
        ReferencedProjectAssemblies(ApplicationAssembly).ShouldBe(["CleanArchitecture.Domain"],
            ignoreOrder: true,
            customMessage: "the application layer may reach the domain and nothing else.");
        ReferencedProjectAssemblies(InfrastructureAssembly).ShouldNotContain("CleanArchitecture.Web",
            "infrastructure is chosen by the web layer, not the other way round.");
    }

    /// <summary>
    /// A permission written as a literal at the point of use is a permission nobody can enumerate: the catalogue
    /// that decides which tenant types may hold it, and the seeder that writes it, would both miss it.
    /// </summary>
    [Test]
    public void Every_identity_request_is_authorized_by_a_permission_the_catalogue_declares()
    {
        var declared = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Where(field => field.Name != nameof(Permissions.ApplicationPermissionClaimType))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        // Anchored, because a namespace that stopped matching would leave this passing over nothing.
        IdentityRequests().Count().ShouldBeGreaterThan(5);
        declared.ShouldContain(Permissions.IdentityContextRead);

        foreach (var request in IdentityRequests())
        {
            if (request.GetCustomAttribute<AuthorizeAttribute>(false) is not { } authorize) continue;
            declared.ShouldContain(
                authorize.Permission,
                $"{request.FullName} authorizes on \"{authorize.Permission}\", which {nameof(Permissions)} does not declare.");
        }
    }

    /// <summary>
    /// Being public is a decision, not a default. A request that declares neither is unreachable through the
    /// pipeline; one that declares both leaves which of the two wins to the order the pipeline happens to run in.
    /// </summary>
    [Test]
    public void Every_identity_request_is_either_public_or_authorized_and_never_both()
    {
        foreach (var request in IdentityRequests())
        {
            var authorized = request.GetCustomAttributes<AuthorizeAttribute>(false).Count();
            var isPublic = ApplicationRequestInventory.IsPublicRequest(request);

            (authorized + (isPublic ? 1 : 0)).ShouldBe(
                1,
                $"{request.FullName} must declare exactly one of {nameof(IPublicRequest)} or {nameof(AuthorizeAttribute)}.");
        }
    }

    /// <summary>
    /// Sessions and invitations were added on top of a model that already worked without them. Letting the older
    /// aggregates hold one would make the earlier behaviour impossible to reason about — or to load — without
    /// them, and would tie the two newest aggregates to each other's lifetime.
    /// </summary>
    [Test]
    public void The_aggregates_added_last_are_not_referenced_by_the_model_that_preceded_them()
    {
        foreach (var type in DomainAssembly.GetTypes().Where(type => EarlierDomainSlices.Contains(type.Namespace)))
        {
            DeclaredMemberTypes(type).ShouldNotContain(
                typeof(UserSession),
                $"{type.FullName} predates {nameof(UserSession)} and must not depend on it.");
            DeclaredMemberTypes(type).ShouldNotContain(
                typeof(Invitation),
                $"{type.FullName} predates {nameof(Invitation)} and must not depend on it.");
        }

        DeclaredMemberTypes(typeof(UserSession)).ShouldNotContain(typeof(Invitation));
        DeclaredMemberTypes(typeof(Invitation)).ShouldNotContain(typeof(UserSession));
    }

    /// <summary>
    /// The Platform aggregates were added last of all, and the same rule holds for them: the model that worked
    /// without Platform must keep working without it, so nothing older may hold one (IA-REQ-039).
    /// </summary>
    [Test]
    public void The_platform_aggregates_are_not_referenced_by_the_model_that_preceded_them()
    {
        var platformTypes = DomainAssembly.GetTypes()
            .Where(type => type.Namespace == "CleanArchitecture.Domain.IdentityAccess.Platform")
            .ToArray();
        platformTypes.ShouldNotBeEmpty("the Platform slice must exist for this to mean anything.");

        foreach (var type in DomainAssembly.GetTypes().Where(candidate =>
                     EarlierDomainSlices.Contains(candidate.Namespace) ||
                     candidate.Namespace is "CleanArchitecture.Domain.IdentityAccess.Sessions"
                         or "CleanArchitecture.Domain.IdentityAccess.Invitations"))
        {
            foreach (var member in DeclaredMemberTypes(type))
            {
                platformTypes.ShouldNotContain(member, $"{type.FullName} predates Platform and must not depend on it.");
            }
        }
    }

    /// <summary>Every slice added after the foundation is named, so a new one cannot be added unnoticed.</summary>
    [Test]
    public void Every_identity_domain_slice_is_either_an_earlier_one_or_a_later_one()
    {
        var namespaces = DomainAssembly.GetTypes()
            .Select(type => type.Namespace)
            .Where(name => name?.StartsWith("CleanArchitecture.Domain.IdentityAccess.", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var name in namespaces)
        {
            var known = EarlierDomainSlices.Contains(name) ||
                        LateDomainSlices.Contains(name) ||
                        name == "CleanArchitecture.Domain.IdentityAccess.Security";
            known.ShouldBeTrue($"{name} is a slice this test does not know about; classify it before adding it.");
        }
    }

    private static IEnumerable<Type> IdentityRequests() =>
        ApplicationRequestInventory
            .GetConcreteRequests(ApplicationAssembly)
            .Where(type => type.Namespace?.StartsWith("CleanArchitecture.Application.IdentityAccess", StringComparison.Ordinal) == true);

    private static string[] ReferencedProjectAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("CleanArchitecture.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static Type[] DeclaredMemberTypes(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => Unwrap(property.PropertyType)),
        .. type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(field => Unwrap(field.FieldType)),
        .. type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => Unwrap(parameter.ParameterType))
    ];

    /// <summary>A collection of the forbidden type is the same dependency wearing a container.</summary>
    private static Type Unwrap(Type type) =>
        type.IsGenericType ? type.GetGenericArguments()[^1] : type.IsArray ? type.GetElementType()! : type;
}
