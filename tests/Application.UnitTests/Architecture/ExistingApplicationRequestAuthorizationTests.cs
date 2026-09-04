using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;
using CleanArchitecture.Application.IdentityAccess.Sessions.RevokeCurrentSession;
using CleanArchitecture.Application.TodoItems.Commands.CreateTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.DeleteTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.UpdateTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.UpdateTodoItemDetail;
using CleanArchitecture.Application.TodoLists.Commands.CreateTodoList;
using CleanArchitecture.Application.TodoLists.Commands.DeleteTodoList;
using CleanArchitecture.Application.TodoLists.Commands.UpdateTodoList;
using CleanArchitecture.Application.TodoLists.Queries.GetTodos;
using CleanArchitecture.Application.WeatherForecasts.Queries.GetWeatherForecasts;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public class ExistingApplicationRequestAuthorizationTests
{
    [TestCase(typeof(CreateTodoListCommand), "todos.write")]
    [TestCase(typeof(UpdateTodoListCommand), "todos.write")]
    [TestCase(typeof(DeleteTodoListCommand), "todos.write")]
    [TestCase(typeof(GetTodosQuery), "todos.read")]
    [TestCase(typeof(CreateTodoItemCommand), "todos.write")]
    [TestCase(typeof(UpdateTodoItemCommand), "todos.write")]
    [TestCase(typeof(UpdateTodoItemDetailCommand), "todos.write")]
    [TestCase(typeof(DeleteTodoItemCommand), "todos.write")]
    [TestCase(typeof(GetWeatherForecastsQuery), "weather.read")]
    public void ExistingRequestsAreAuthorizedWithoutTenantRequirement(Type requestType, string permission)
    {
        var authorizeAttribute = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();
        ApplicationRequestInventory.IsPublicRequest(requestType)
            .ShouldBeFalse($"{requestType.FullName} must remain authorized, not public.");

        GetRequiredMetadata<string>(authorizeAttribute, "Permission").ShouldBe(permission);
        GetRequiredMetadata<bool>(authorizeAttribute, "RequiresTenant").ShouldBeFalse();
    }

    [TestCase(typeof(RevokeCurrentSessionCommand), "identity.sessions.manage")]
    [TestCase(typeof(GetIdentityContextQuery), "identity.context.read")]
    [TestCase(typeof(SelectTenantCommand), "identity.context.select")]
    [TestCase(typeof(CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation.AcceptInvitationCommand), "identity.invitations.accept")]
    public void Session_context_requests_are_authorized_without_tenant_requirement(Type requestType, string permission)
    {
        var authorizeAttribute = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();
        ApplicationRequestInventory.IsPublicRequest(requestType).ShouldBeFalse();
        GetRequiredMetadata<string>(authorizeAttribute, "Permission").ShouldBe(permission);
        GetRequiredMetadata<bool>(authorizeAttribute, "RequiresTenant").ShouldBeFalse();
    }

    [Test]
    public void ExistingRequestInventoryContainsExactlyTheKnownTwentyRequests()
    {
        var actualRequestTypes = GetConcreteRequestTypes(typeof(AuthorizeAttribute).Assembly);

        string?[] expectedRequestTypes =
        [
            typeof(ConfirmEmailCommand).FullName,
            typeof(RegisterOrganizationCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession.CreateSessionCommand).FullName,
            typeof(RevokeCurrentSessionCommand).FullName,
            typeof(GetIdentityContextQuery).FullName,
            typeof(SelectTenantCommand).FullName,
            typeof(CreateTodoListCommand).FullName,
            typeof(UpdateTodoListCommand).FullName,
            typeof(DeleteTodoListCommand).FullName,
            typeof(GetTodosQuery).FullName,
            typeof(CreateTodoItemCommand).FullName,
            typeof(UpdateTodoItemCommand).FullName,
            typeof(UpdateTodoItemDetailCommand).FullName,
            typeof(DeleteTodoItemCommand).FullName,
            typeof(GetWeatherForecastsQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember.InviteMemberCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser.RegisterInvitedUserCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation.AcceptInvitationCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation.ResendInvitationCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation.CancelInvitationCommand).FullName,
        ];

        actualRequestTypes.ShouldBe(expectedRequestTypes.Order());
    }

    [Test]
    public void InventoryDiscoveryIncludesValueTypeRequests()
    {
        GetConcreteRequestTypes(typeof(ValueTypeRequest).Assembly)
            .ShouldContain(typeof(ValueTypeRequest).FullName);
    }

    private static string?[] GetConcreteRequestTypes(Assembly assembly)
    {
        return ApplicationRequestInventory
            .GetConcreteRequests(assembly)
            .Select(type => type.FullName)
            .Order()
            .ToArray();
    }

    private static T GetRequiredMetadata<T>(AuthorizeAttribute authorizeAttribute, string propertyName)
    {
        var property = typeof(AuthorizeAttribute).GetProperty(propertyName);

        property.ShouldNotBeNull($"AuthorizeAttribute must declare {propertyName} metadata.");

        return property!.GetValue(authorizeAttribute).ShouldBeOfType<T>();
    }

    private readonly record struct ValueTypeRequest : MediatR.IRequest;
}
