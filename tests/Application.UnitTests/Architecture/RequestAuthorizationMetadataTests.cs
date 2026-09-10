using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using MediatR;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public class RequestAuthorizationMetadataTests
{
    private const string PermissionPropertyName = "Permission";
    private const string RequiresTenantPropertyName = "RequiresTenant";

    [Test]
    public void ConcreteRequestsHaveExactlyOneAuthorizationClassification()
    {
        foreach (var requestType in GetConcreteRequests())
        {
            var authorizeAttributes = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ToArray();
            var isPublicRequest = ApplicationRequestInventory.IsPublicRequest(requestType);

            (authorizeAttributes.Length == 1).ShouldBe(
                !isPublicRequest,
                $"{requestType.FullName} must declare exactly one AuthorizeAttribute or implement {typeof(IPublicRequest).FullName}.");

            if (isPublicRequest)
            {
                continue;
            }

            AssertAuthorizationMetadata(requestType, authorizeAttributes.Single());
        }
    }

    [Test]
    public void AuthorizeAttributeUsesRequiredImmutablePermissionMetadata()
    {
        var constructor = typeof(AuthorizeAttribute).GetConstructors().ShouldHaveSingleItem();
        var parameters = constructor.GetParameters();

        parameters.Select(parameter => parameter.Name).ShouldBe(["permission", "requiresTenant"]);
        parameters.Select(parameter => parameter.ParameterType).ShouldBe([typeof(string), typeof(bool)]);
        var attributeUsage = typeof(AuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        attributeUsage.ValidOn.ShouldBe(AttributeTargets.Class);
        attributeUsage.Inherited.ShouldBeTrue();
        attributeUsage.AllowMultiple.ShouldBeFalse();

        var attribute = new AuthorizeAttribute("identity.context.read", false);
        typeof(AuthorizeAttribute).GetProperty(nameof(AuthorizeAttribute.Roles))!.PropertyType.ShouldBe(typeof(string));
        typeof(AuthorizeAttribute).GetProperty(nameof(AuthorizeAttribute.Roles))!.CanWrite.ShouldBeTrue();
        typeof(AuthorizeAttribute).GetProperty(nameof(AuthorizeAttribute.Policy))!.PropertyType.ShouldBe(typeof(string));
        typeof(AuthorizeAttribute).GetProperty(nameof(AuthorizeAttribute.Policy))!.CanWrite.ShouldBeTrue();
        attribute.Roles.ShouldBe(string.Empty);
        attribute.Policy.ShouldBe(string.Empty);
    }

    [Test]
    public void ValueTypeRequestsAreIncludedByRequestDiscovery()
    {
        ApplicationRequestInventory.IsConcreteRequest(typeof(ValueTypeRequest)).ShouldBeTrue();
    }

    private static IEnumerable<Type> GetConcreteRequests()
    {
        return ApplicationRequestInventory.GetConcreteRequests(typeof(AuthorizeAttribute).Assembly);
    }

    private static void AssertAuthorizationMetadata(Type requestType, AuthorizeAttribute authorizeAttribute)
    {
        var permissionProperty = typeof(AuthorizeAttribute).GetProperty(PermissionPropertyName);
        var requiresTenantProperty = typeof(AuthorizeAttribute).GetProperty(RequiresTenantPropertyName);

        permissionProperty.ShouldNotBeNull($"{requestType.FullName} requires permission metadata.");
        requiresTenantProperty.ShouldNotBeNull($"{requestType.FullName} requires tenant metadata.");
        permissionProperty!.SetMethod.ShouldBeNull("Permission metadata must be immutable.");
        requiresTenantProperty!.SetMethod.ShouldBeNull("Tenant metadata must be immutable.");

        var permission = permissionProperty.GetValue(authorizeAttribute) as string;
        var requiresTenant = requiresTenantProperty.GetValue(authorizeAttribute);

        string.IsNullOrWhiteSpace(permission).ShouldBeFalse($"{requestType.FullName} requires a nonblank permission.");
        requiresTenant.ShouldBeOfType<bool>($"{requestType.FullName} must explicitly declare whether it requires a tenant.");
    }

    private readonly record struct ValueTypeRequest : IRequest;
}
