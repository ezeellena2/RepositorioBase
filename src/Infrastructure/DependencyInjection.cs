using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Interceptors;
using CleanArchitecture.Infrastructure.Auditing;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using CleanArchitecture.Application.IdentityAccess.Invitations;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Outbox;
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
        builder.Services.AddSingleton<IHostedService>(provider => new EmailReadiness(provider, builder.Environment));
        builder.AddIdentityDataProtection();
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, TenantAuthorizationAuditInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, CleanArchitecture.Infrastructure.Data.Interceptors.TenantTimestampInterceptor>();

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
        builder.Services.AddScoped<IInvitationRoleAssigner, InvitationRoleAssigner>();
        builder.Services.AddSingleton<CleanArchitecture.Application.IdentityAccess.Platform.IPlatformMfaVerifier, CleanArchitecture.Infrastructure.Security.PlatformTotpSecretProtector>();
        builder.Services.AddSingleton<CleanArchitecture.Application.IdentityAccess.Platform.IPlatformRecoveryCodeFactory, CleanArchitecture.Infrastructure.Security.PlatformRecoveryCodeHasher>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.IPlatformMembershipActivator, CleanArchitecture.Infrastructure.Platform.PlatformMembershipActivator>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.IRecentMfaVerifier, CleanArchitecture.Infrastructure.Platform.RecentMfaVerifier>();
        builder.Services.AddSingleton<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformBootstrapOptions, CleanArchitecture.Infrastructure.Platform.ConfiguredPlatformBootstrapper>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformSystemRoleProvisioner, CleanArchitecture.Infrastructure.Platform.PlatformSystemRoleProvisioner>();
        builder.Services.AddSingleton<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformBootstrapRecoveryRateLimiter, CleanArchitecture.Infrastructure.Platform.PlatformBootstrapRecoveryRateLimiter>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.IPlatformOperationalProjectionReader, CleanArchitecture.Infrastructure.Platform.PlatformOperationalProjectionReader>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.RecoverPendingPlatformOwnerInvitationValidator>();
        builder.Services.AddScoped<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.BootstrapPlatformOwner>();

        // The dispatcher and its handlers are registered as scoped units, not as a hosted service. The loop
        // that repeats a pass belongs to the worker process; registering it here would start a poller inside
        // the web application and inside every functional test that boots it.
        builder.Services.AddScoped<IOutboxSecretReader, OutboxSecretReader>();
        builder.Services.AddScoped<IOutboxDeliveryHandler, InvitationEmailDeliveryHandler>();
        builder.Services.AddScoped<IOutboxDeliveryHandler, EmailConfirmationDeliveryHandler>();
        builder.Services.AddScoped<IOutboxDeliveryHandler, InvitedConfirmationDeliveryHandler>();
        builder.Services.AddScoped<IOutboxDeliveryHandler, SignInNoticeDeliveryHandler>();
        builder.Services.AddScoped<OutboxDispatcher>();
        builder.Services.AddOptions<IdentityEmailOptions>().BindConfiguration(IdentityEmailOptions.SectionName);
        builder.Services.AddKeyedSingleton<HttpClient>("identity-email", (_, _) => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ActivityHeadersPropagator = null
        }) { Timeout = TimeSpan.FromSeconds(20) });
        builder.Services.AddScoped<IIdentityEmailSender>(provider => new IdentityEmailAdapter(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityEmailOptions>>(),
            provider.GetRequiredKeyedService<HttpClient>("identity-email")));
        builder.Services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        builder.Services.AddSingleton<ITokenHasher, VersionedTokenHasher>();
        builder.Services.AddSingleton<IOutboxSecretWriter, OutboxSecretWriter>();
        builder.Services.AddScoped<IValidatedOptionalSession, ValidatedOptionalSession>();
        builder.Services.AddScoped<ICurrentSession, CurrentSession>();
        builder.Services.AddScoped<SessionCookieEvents>();
        builder.Services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        builder.Services.AddScoped<IEffectivePermissionReader, EffectivePermissionReader>();
        builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();
        builder.Services.AddScoped<ISecurityDenialAuditWriter, SecurityDenialAuditWriter>();
    }

    private sealed class EmailReadiness(IServiceProvider services, IHostEnvironment environment) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (DatabaseMigrationExecutionPolicy.IsOpenApiDocumentGeneration(services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>()?.GetType().FullName))
                return Task.CompletedTask;
            var email = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityEmailOptions>>().Value;
            if ((environment.IsProduction() && !email.Enabled) || ((environment.IsProduction() || email.Enabled) && !email.IsValid()))
                throw new InvalidOperationException("Identity email delivery is not configured.");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
