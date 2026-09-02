using System.Reflection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public sealed class IdentitySessionShapeTests
{
    [TestCase("CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession.CreateSessionCommand", true)]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Sessions.RevokeCurrentSession.RevokeCurrentSessionCommand", false)]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext.GetIdentityContextQuery", false)]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Context.SelectTenant.SelectTenantCommand", false)]
    public void Session_application_contracts_exist_and_declare_their_access_boundary(string typeName, bool isPublic)
    {
        var type = typeof(CleanArchitecture.Application.Common.Security.AuthorizeAttribute).Assembly.GetType(typeName);
        type.ShouldNotBeNull(typeName);

        var isPublicRequest = type.GetInterfaces().Any(@interface => @interface.Name == "IPublicRequest");
        isPublicRequest.ShouldBe(isPublic, typeName);

        if (!isPublic)
        {
            type.GetCustomAttribute<CleanArchitecture.Application.Common.Security.AuthorizeAttribute>().ShouldNotBeNull(typeName);
        }
    }

    [TestCase("CleanArchitecture.Application.IdentityAccess.Sessions.ICurrentSession")]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Authorization.ICurrentTenant")]
    public void Trusted_server_context_contracts_exist(string typeName) =>
        typeof(CleanArchitecture.Application.Common.Security.AuthorizeAttribute).Assembly.GetType(typeName).ShouldNotBeNull(typeName);

    [Test]
    public void Create_session_credentials_are_classified_as_sensitive()
    {
        var type = typeof(CleanArchitecture.Application.Common.Security.AuthorizeAttribute).Assembly
            .GetType("CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession.CreateSessionCommand");

        type.ShouldNotBeNull();
        type!.GetInterfaces().Any(@interface => @interface.Name == "ISensitiveRequest").ShouldBeTrue();
    }

    [TestCase("CleanArchitecture.Domain.IdentityAccess.Sessions.UserSession")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Sessions.UserSessionId")]
    public void Session_domain_contract_types_exist(string typeName) =>
        typeof(CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant).Assembly.GetType(typeName).ShouldNotBeNull(typeName);
}
