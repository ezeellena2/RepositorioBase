using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class IdentityAccessMappingTests
{
    [Test]
    public void Cuit_converters_materialize_legacy_stored_digits_without_reapplying_input_validation()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var entityType in new[] { typeof(OrganizationProfile), typeof(PendingRegistrationIntent) })
        {
            var converter = model.FindEntityType(entityType)!.FindProperty("Cuit")!.GetValueConverter();

            var materialized = (NormalizedCuit)converter!.ConvertFromProvider("30123456789")!;

            materialized.Value.ShouldBe("30123456789");
        }
    }

    [Test]
    public void Model_maps_core_identity_access_entities_with_uuid_keys()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var model = context.GetService<IDesignTimeModel>().Model;

        var user = model.FindEntityType(typeof(ApplicationUser));
        var tenant = model.FindEntityType("CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant");
        var organizationProfile = model.FindEntityType("CleanArchitecture.Domain.IdentityAccess.Organizations.OrganizationProfile");
        var membership = model.FindEntityType("CleanArchitecture.Domain.IdentityAccess.Memberships.TenantMembership");
        var auditEvent = model.FindEntityType("CleanArchitecture.Domain.IdentityAccess.Auditing.AuditEvent");

        user.ShouldNotBeNull();
        tenant.ShouldNotBeNull();
        organizationProfile.ShouldNotBeNull();
        membership.ShouldNotBeNull();
        auditEvent.ShouldNotBeNull();
        user!.FindProperty("Id")!.GetColumnType().ShouldBe("uuid");
        user.GetCheckConstraints().ShouldContain(constraint => constraint.Name == "CK_AspNetUsers_Id_NotEmpty");
        user.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["NormalizedEmail"])).IsUnique.ShouldBeTrue();
        tenant!.GetTableName().ShouldBe("Tenants");
        tenant.FindProperty("Id")!.GetColumnType().ShouldBe("uuid");
        tenant.FindProperty("Slug")!.GetValueConverter().ShouldNotBeNull();
        tenant.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        tenant.FindProperty("Version")!.GetColumnName().ShouldBe("xmin");
        tenant.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["Slug"])).IsUnique.ShouldBeTrue();
        organizationProfile!.FindProperty("Cuit")!.GetValueConverter().ShouldNotBeNull();
        organizationProfile.GetIndexes().Single(index => index.Properties.Single().Name == "Cuit").IsUnique.ShouldBeTrue();
        membership!.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "IdentityId"])).IsUnique.ShouldBeTrue();
        membership.GetKeys().ShouldContain(key => !key.IsPrimaryKey() && key.Properties.Count == 2 && key.Properties[0].Name == "TenantId" && key.Properties[1].Name == "Id");
        membership.FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
        membership.GetForeignKeys().Single(key => key.Properties.Single().Name == "IdentityId").DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
        auditEvent!.FindProperty("Metadata")!.GetColumnType().ShouldBe("jsonb");
        auditEvent.GetCheckConstraints().ShouldContain(constraint => constraint.Name == "CK_AuditEvents_Ids_NotEmpty" && constraint.Sql.Contains("ActorId", StringComparison.Ordinal));
        auditEvent.FindProperty("Metadata")!.GetValueComparer().ShouldNotBeNull();
        auditEvent.GetForeignKeys().Single(key => key.Properties.Single().Name == "ActorId").DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
        model.FindEntityType("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>")!.GetCheckConstraints().ShouldContain(constraint => constraint.Name == "CK_AspNetRoles_Id_NotEmpty");
    }
}
