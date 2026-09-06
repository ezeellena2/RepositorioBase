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
    // A Platform invitee holds no membership until the MFA gates complete, so none of the gates can be
    // tenant-scoped either. The permission grants nothing beyond the chance to prove a factor.
    [TestCase(typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.BeginPlatformMfaEnrollmentCommand), "platform.mfa.enroll")]
    [TestCase(typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.VerifyPlatformMfaEnrollmentCommand), "platform.mfa.enroll")]
    [TestCase(typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.AcknowledgePlatformRecoveryCodesCommand), "platform.mfa.enroll")]
    [TestCase(typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.StepUpPlatformMfaCommand), "platform.mfa.enroll")]
    public void Session_context_requests_are_authorized_without_tenant_requirement(Type requestType, string permission)
    {
        var authorizeAttribute = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ShouldHaveSingleItem();
        ApplicationRequestInventory.IsPublicRequest(requestType).ShouldBeFalse();
        GetRequiredMetadata<string>(authorizeAttribute, "Permission").ShouldBe(permission);
        GetRequiredMetadata<bool>(authorizeAttribute, "RequiresTenant").ShouldBeFalse();
    }

    /// <summary>
    /// The exhaustive inventory. Every request the application answers is named here on purpose, so a new one
    /// cannot appear without someone deciding, in this file, how it is authorized.
    /// </summary>
    [Test]
    public void RequestInventoryContainsExactlyTheDeclaredRequests()
    {
        var actualRequestTypes = GetConcreteRequestTypes(typeof(AuthorizeAttribute).Assembly);

        string?[] expectedRequestTypes =
        [
            typeof(ConfirmEmailCommand).FullName,
            typeof(RegisterOrganizationCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate.ReauthenticateCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions.ListOwnSessionsQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions.RevokeOwnSessionCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Sessions.ManageSessions.RevokeOtherSessionsCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext.CreatePersonalContextCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.People.Profile.GetPersonalProfileQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.People.Profile.UpdatePersonalProfileCommand).FullName,
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
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Invitations.RegisterPlatformInviteeCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Invitations.ConfirmPlatformInviteeCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.BeginPlatformMfaEnrollmentCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.VerifyPlatformMfaEnrollmentCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.AcknowledgePlatformRecoveryCodesCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Mfa.StepUpPlatformMfaCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.RecoverPendingPlatformOwnerInvitationCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Administrators.InvitePlatformAdministratorCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Administrators.RevokePlatformAdministratorCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Organizations.SuspendOrganizationTenantCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Organizations.ReactivateOrganizationTenantCommand).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Queries.ListPlatformOrganizationsQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Queries.ListPlatformIdentitiesQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Queries.ListPlatformAdministratorsQuery).FullName,
            typeof(CleanArchitecture.Application.IdentityAccess.Platform.Queries.ListPlatformAuditQuery).FullName,
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
