using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Interceptors;
using CleanArchitecture.Infrastructure.Auditing;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using CleanArchitecture.Application.IdentityAccess.Invitations;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, TenantAuthorizationAuditInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.AddInterceptors(sp.GetServices<DbCommandInterceptor>());
            options.UseNpgsql(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichNpgsqlDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddScoped<IApplicationTransaction, EfApplicationTransaction>();

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();
        builder.Services.AddScoped<PermissionCatalogSynchronizer>();
        builder.Services.AddHostedService<DatabaseMigrationHostedService>();

#if (UseApiOnly)
        builder.Services.AddAuthentication()
            .AddBearerToken(IdentityConstants.BearerScheme);

        builder.Services.AddAuthorizationBuilder();

        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddApiEndpoints();
#else
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.AddAuthorizationBuilder();

        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionCookieEvents.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // The persisted session is the authority for idle and absolute expiry. The ticket is a browser session
            // cookie (no remember-me) that never slides on its own and deliberately outlives the 12-hour absolute
            // lifetime, so expiry is always decided by the persisted row (401 invalid_session), never by the ticket.
            options.ExpireTimeSpan = TimeSpan.FromDays(1);
            options.SlidingExpiration = false;
            // EventsType makes the handler resolve SessionCookieEvents from DI; it validates the persisted session
            // and turns login/access-denied redirects into 401/403 API responses.
            options.EventsType = typeof(SessionCookieEvents);
        });
#endif

        builder.Services.Configure<IdentityOptions>(ConfigureIdentityOptions);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTransient<IIdentityService, IdentityService>();
        builder.Services.AddScoped<IIdentityAccountService, IdentityAccountService>();
        builder.Services.AddScoped<IRegistrationIdempotencyStore, RegistrationIdempotencyStore>();
        builder.Services.AddScoped<IConfirmationSecretStore, ConfirmationSecretStore>();
        builder.Services.AddScoped<IRegistrationInitialRoleProvisioner, RegistrationInitialRoleProvisioner>();
        builder.Services.AddScoped<IOfferableRoleReader, OfferableRoleReader>();
        builder.Services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        builder.Services.AddSingleton<ITokenHasher, VersionedTokenHasher>();
        builder.Services.AddSingleton<IOutboxSecretWriter, OutboxSecretWriter>();
        builder.Services.AddScoped<IValidatedOptionalSession, ValidatedOptionalSession>();
        builder.Services.AddScoped<ICurrentSession, CurrentSession>();
        builder.Services.AddScoped<SessionCookieEvents>();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        builder.Services.AddScoped<IEffectivePermissionReader, EffectivePermissionReader>();
        builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();
        builder.Services.AddScoped<ISecurityDenialAuditWriter, SecurityDenialAuditWriter>();
    }

    /// <summary>
    /// Exact credential policy (IA-REQ-019/020): strong passwords, confirmed email before any session, and a
    /// five-failure 15-minute lockout that <see cref="IdentityAccountService"/> enforces with the injected clock.
    /// </summary>
    private static void ConfigureIdentityOptions(IdentityOptions options)
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;

        options.SignIn.RequireConfirmedEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }
}
