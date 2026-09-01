using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
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
