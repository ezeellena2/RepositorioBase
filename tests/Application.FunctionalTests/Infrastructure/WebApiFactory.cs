using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(
    string connectionString,
    string? environmentName = null,
    bool useTestAuthentication = true,
    bool useStaleApplicationUser = false,
    bool useTestIdentityAccessDoubles = true,
    TimeProvider? timeProvider = null,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureTestServices = null,
    bool keepDeploymentGuard = false) : WebApplicationFactory<Program>
{
    /// <summary>A fixed 32-byte key. This suite never handles a real document, so nothing here needs protecting.</summary>
    private const string TestDocumentFingerprintKey = "dGVzdC1maW5nZXJwcmludC1rZXktMzItYnl0ZXMhISE=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CleanArchitectureDb", connectionString);
        builder.UseSetting("IdentityAccess:Email:PublicOrigin", "https://app.example.test");
        // Fingerprint key material has no default by design, so the host that records documents has to be given
        // some. It is a fixed test key: this suite never handles a real document.
        builder.UseSetting("IdentityAccess:People:DocumentProtection:CurrentKeyVersion", "1");
        builder.UseSetting("IdentityAccess:People:DocumentProtection:FingerprintKeys:1", TestDocumentFingerprintKey);
        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder.UseEnvironment(environmentName);
        }

        // Settings a scenario chooses, applied last so a test can supply what the host reads at build time —
        // an external provider's client, for instance, which decides whether its middleware exists at all.
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            configureTestServices?.Invoke(services);

            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }

            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                services.RemoveAll<IHostedService>();

                // Put back exactly the one a test is asking about. Restoring all of them would start a delivery
                // loop and a migration in every scenario that only wanted to know whether the host comes up.
                if (keepDeploymentGuard)
                {
                    services.AddSingleton<IHostedService, CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle.IdentityDeploymentGuard>();
                }
            }
            if (useTestAuthentication)
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                });
                services.PostConfigure<AuthorizationOptions>(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .RequireClaim(TestAuthenticationHandler.PermissionClaim, TestAuthenticationHandler.PermissionValue)
                        .Build();
                });
            }
            // The shared budget is real everywhere except when a test asks for the outage, which cannot be
            // produced by configuration and is the one behaviour a caller is promised at the edge of the store.
            services.RemoveAll<CleanArchitecture.Application.IdentityAccess.Security.ISharedAttemptBudget>();
            services.AddScoped<CleanArchitecture.Application.IdentityAccess.Security.ISharedAttemptBudget>(provider =>
                new TestSharedAttemptBudget(new CleanArchitecture.Infrastructure.IdentityAccess.Security.PostgreSqlAttemptBudget(
                    provider.GetRequiredService<CleanArchitecture.Infrastructure.Data.ApplicationDbContext>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<CleanArchitecture.Infrastructure.IdentityAccess.Observability.IdentityAccessMetrics>())));

            if (useTestIdentityAccessDoubles)
            {
                services
                    .RemoveAll<IUser>()
                    .AddTransient(provider =>
                    {
                        var mock = new Mock<IUser>();
                        mock.SetupGet(x => x.Roles).Returns(TestApp.GetRoles());
                        mock.SetupGet(x => x.Id).Returns(useStaleApplicationUser ? null : TestApp.GetUserId());
                        return mock.Object;
                    });
                services.RemoveAll<ICurrentTenant>();
                services.AddScoped<ICurrentTenant, TestCurrentTenant>();
                // The real one reads the validated session out of the HttpContext, which a request sent
                // straight through MediatR does not have. Requests that identify the caller by session — the
                // Platform MFA gates — would otherwise be untestable at this level.
                services.RemoveAll<ICurrentSession>();
                services.AddScoped<ICurrentSession, TestCurrentSession>();
                // The bootstrap ceremony reads one configured address. A test chooses it per scenario, which
                // configuration alone cannot do because the host is built once for the whole run.
                services.RemoveAll<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformBootstrapOptions>();
                services.AddSingleton<CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformBootstrapOptions, TestPlatformBootstrapOptions>();
                services.RemoveAll<IValidatedOptionalSession>();
                services.AddScoped<IValidatedOptionalSession>(_ => TestApp.GetValidatedOptionalSession());
                services.RemoveAll<ISecureTokenGenerator>();
                services.AddSingleton<ISecureTokenGenerator, TestRegistrationTokenGenerator>();
                services.RemoveAll<ITokenHasher>();
                services.AddSingleton<ITokenHasher, TestCountingTokenHasher>();
                services.RemoveAll<IPermissionEvaluator>();
                services.AddScoped<IPermissionEvaluator, TestPermissionEvaluator>();
            }
            // Evidence hooks for the login controls: count real password verifications through the hasher the
            // UserManager and the account service resolve, and capture every log entry (Trace and up) for hygiene checks.
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddScoped<IPasswordHasher<ApplicationUser>>(provider =>
                new CountingPasswordHasher(new PasswordHasher<ApplicationUser>(provider.GetRequiredService<IOptions<PasswordHasherOptions>>())));
            services.AddSingleton<ILoggerProvider, TestLogCaptureProvider>();
            services.AddLogging(logging => logging.AddFilter<TestLogCaptureProvider>(null, LogLevel.Trace));
            services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor, TestSaveChangesRaceInterceptor>();
            services.AddScoped<DbCommandInterceptor, ConfirmationSecretLockBarrierInterceptor>();
            services.AddScoped<DbCommandInterceptor, InvitationLockBarrierInterceptor>();
            services.AddScoped<DbCommandInterceptor, SessionLivenessRaceInterceptor>();
            services.AddScoped<DbCommandInterceptor, FailedAccessBarrierInterceptor>();
        });
    }

    private sealed class CountingPasswordHasher(IPasswordHasher<ApplicationUser> inner) : IPasswordHasher<ApplicationUser>
    {
        public string HashPassword(ApplicationUser user, string password) => inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            TestApp.RecordPasswordVerification();
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private sealed class TestLogCaptureProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var entry = new System.Text.StringBuilder().Append('[').Append(logLevel).Append("] ").Append(category).Append(": ").Append(formatter(state, exception));
                if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
                {
                    // Structured values are captured too, so a raw value never formatted into the message is still caught.
                    foreach (var (key, value) in values) entry.Append(' ').Append(key).Append('=').Append(value);
                }

                if (exception is not null) entry.Append(' ').Append(exception);
                TestApp.RecordLog(entry.ToString());
            }
        }
    }

    private sealed class TestRegistrationTokenGenerator : ISecureTokenGenerator
    {
        public string Generate() => TestApp.NextRawToken();
    }

    private sealed class TestCountingTokenHasher : ITokenHasher
    {
        private readonly VersionedTokenHasher _inner = new();

        public string Hash(string token)
        {
            TestApp.RecordConfirmationTokenHash();
            return _inner.Hash(token);
        }

        public CleanArchitecture.Domain.IdentityAccess.Security.VersionedTokenHash Of(string token)
        {
            TestApp.RecordConfirmationTokenHash();
            return _inner.Of(token);
        }

        public bool Verify(string token, string versionedHash) => _inner.Verify(token, versionedHash);
    }

    private sealed class TestCurrentTenant : ICurrentTenant
    {
        public TenantId? TenantId => TestApp.GetTenantId();
    }

    private sealed class TestPlatformBootstrapOptions : CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap.IPlatformBootstrapOptions
    {
        public string? OwnerEmail => TestApp.GetPlatformBootstrapEmail();
    }

    private sealed class TestCurrentSession : ICurrentSession
    {
        public CleanArchitecture.Domain.IdentityAccess.Sessions.UserSessionId? SessionId =>
            TestApp.GetSessionId() is { } id
                ? CleanArchitecture.Domain.IdentityAccess.Sessions.UserSessionId.From(id)
                : null;

        public Guid? IdentityId => TestApp.GetUserId();

        public bool IsInvalid => TestApp.GetUserId() is null || TestApp.GetSessionId() is null;
    }

    private sealed class TestPermissionEvaluator : IPermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(Guid identityId, string permissionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(identityId != Guid.Empty && TestApp.IsApplicationPermissionGranted());

        public Task<bool> HasPermissionAsync(Guid identityId, TenantId tenantId, string permissionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(identityId != Guid.Empty && !tenantId.IsEmpty && TestApp.IsApplicationPermissionGranted());
    }
}
