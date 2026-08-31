using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using MediatR;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public class RequestAuthorizationMetadataTests
{
    private const string PublicRequestInterfaceName = "CleanArchitecture.Application.Common.Security.IPublicRequest";
    private const string PermissionPropertyName = "Permission";
    private const string RequiresTenantPropertyName = "RequiresTenant";

    [Test]
    public void ConcreteRequestsHaveExactlyOneAuthorizationClassification()
    {
        foreach (var requestType in GetConcreteRequests())
        {
            var authorizeAttributes = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ToArray();
            var isPublicRequest = requestType.GetInterfaces().Any(@interface => @interface.FullName == PublicRequestInterfaceName);

            (authorizeAttributes.Length == 1).ShouldBe(
                !isPublicRequest,
                $"{requestType.FullName} must declare exactly one AuthorizeAttribute or implement {PublicRequestInterfaceName}.");

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
        typeof(AuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.AllowMultiple.ShouldBeFalse();
    }

    private static IEnumerable<Type> GetConcreteRequests()
    {
        return typeof(AuthorizeAttribute).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => typeof(IBaseRequest).IsAssignableFrom(type))
            .Where(type => !typeof(INotification).IsAssignableFrom(type));
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
}
