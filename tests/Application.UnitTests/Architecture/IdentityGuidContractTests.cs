using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public class IdentityGuidContractTests
{
    [Test]
    public void IdentityAndAuditContractsUseGuidKeys()
    {
        typeof(ApplicationUser).BaseType.ShouldBe(typeof(IdentityUser<Guid>));
        var identityDbContext = GetIdentityDbContextType(typeof(ApplicationDbContext));
        identityDbContext.ShouldNotBeNull();
        identityDbContext!.GetGenericArguments()[1].ShouldBe(typeof(IdentityRole<Guid>));
        identityDbContext.GetGenericArguments()[2].ShouldBe(typeof(Guid));
        typeof(IUser).GetProperty(nameof(IUser.Id))!.PropertyType.ShouldBe(typeof(Guid?));
        typeof(BaseAuditableEntity).GetProperty(nameof(BaseAuditableEntity.CreatedBy))!.PropertyType.ShouldBe(typeof(Guid?));
        typeof(BaseAuditableEntity).GetProperty(nameof(BaseAuditableEntity.LastModifiedBy))!.PropertyType.ShouldBe(typeof(Guid?));
    }

    [Test]
    public void IdentityServiceContractUsesGuidUserIds()
    {
        var identityService = typeof(IIdentityService);

        identityService.GetMethod(nameof(IIdentityService.GetUserNameAsync))!.GetParameters().Single().ParameterType.ShouldBe(typeof(Guid));
        identityService.GetMethod(nameof(IIdentityService.IsInRoleAsync))!.GetParameters().First().ParameterType.ShouldBe(typeof(Guid));
        identityService.GetMethod(nameof(IIdentityService.AuthorizeAsync))!.GetParameters().First().ParameterType.ShouldBe(typeof(Guid));
        identityService.GetMethod(nameof(IIdentityService.DeleteUserAsync))!.GetParameters().Single().ParameterType.ShouldBe(typeof(Guid));
        identityService.GetMethod(nameof(IIdentityService.CreateUserAsync))!.ReturnType.GenericTypeArguments.Single().GenericTypeArguments[1].ShouldBe(typeof(Guid));
    }

    [Test]
    public void PermissionEvaluator_requires_explicit_identity_and_tenant_inputs()
    {
        var method = typeof(IPermissionEvaluator).GetMethod(nameof(IPermissionEvaluator.HasPermissionAsync))!;

        method.GetParameters().Select(parameter => parameter.ParameterType).Take(3).ShouldBe([typeof(Guid), typeof(TenantId), typeof(string)]);
    }

    [Test]
    public void Application_context_does_not_expose_tenant_authorization_bulk_mutation_or_query_surfaces()
    {
        var authorizationTypes = new[] { typeof(Role), typeof(Permission), typeof(RolePermission), typeof(MembershipRole) };

        typeof(IApplicationDbContext).GetProperties()
            .Where(property => property.PropertyType.GetInterfaces().Append(property.PropertyType).Any(type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>) && authorizationTypes.Contains(type.GenericTypeArguments[0])))
            .ShouldBeEmpty("tenant authorization mutations must cross a dedicated invariant boundary rather than an application DbSet or IQueryable surface");
    }

    private static Type? GetIdentityDbContextType(Type type)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IdentityDbContext<,,>))
            {
                return candidate;
            }
        }

        return null;
    }
}
