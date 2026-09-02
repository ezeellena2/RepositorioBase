using CleanArchitecture.Domain.Constants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public static class TestApp
{
    private static Guid? _userId;
    private static Guid? _sessionId;
    private static List<string>? _roles;
    private static TenantId? _tenantId;
    private static bool _httpAuthorizationGranted;
    private static bool _applicationPermissionGranted;
    private static bool _forceTodoItemConcurrencyConflict;
    private static bool _forceUnexpectedFailure;
    private static bool _forceRegistrationRollbackAfterPersistedEffects;
    private static bool _forceConfirmationRollbackAfterPersistedEffects;
    private static bool _forceSessionValidationConcurrentRevoke;
    private static bool _forceSessionRevokePersistenceFailure;
    private static SessionWriteStage? _concurrentSessionTouchStage;
    private static bool _concurrentSessionClear;
    private static SessionWriteStage? _concurrentSessionRevokeStage;
    private static bool _concurrentFailedAccess;
    private static Guid? _optionalSessionIdentityId;
    private static string? _optionalSessionEmail;
    private static bool _optionalSessionIsInvalid;
    private const string RegistrationRawToken = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=";
    private static int _confirmationTokenHashInvocationCount;
    private static TaskCompletionSource? _confirmationSecretLockBarrier;
    private static int _confirmationSecretLockBarrierArrivals;
    private static int _passwordVerificationCount;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _capturedLogs = new();

    public static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        return await mediator.Send(request);
    }

    public static async Task SendAsync(IBaseRequest request)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        await mediator.Send(request);
    }

    public static Guid? GetUserId() => _userId;

    public static Guid? GetSessionId() => _sessionId;

    public static List<string>? GetRoles() => _roles;

    public static TenantId? GetTenantId() => _tenantId;

    public static bool IsHttpAuthorizationGranted() => _httpAuthorizationGranted;

    public static bool IsApplicationPermissionGranted() => _applicationPermissionGranted;

    public static bool ConsumeForcedTodoItemConcurrencyConflict() => Interlocked.Exchange(ref _forceTodoItemConcurrencyConflict, false);

    public static bool ConsumeForcedUnexpectedFailure() => Interlocked.Exchange(ref _forceUnexpectedFailure, false);

    public static bool ConsumeForcedRegistrationRollbackAfterPersistedEffects() => Interlocked.Exchange(ref _forceRegistrationRollbackAfterPersistedEffects, false);

    public static bool ConsumeForcedConfirmationRollbackAfterPersistedEffects() => Interlocked.Exchange(ref _forceConfirmationRollbackAfterPersistedEffects, false);

    public static bool ConsumeSessionValidationConcurrentRevoke() => Interlocked.Exchange(ref _forceSessionValidationConcurrentRevoke, false);

    public static bool ConsumeSessionRevokePersistenceFailure() => Interlocked.Exchange(ref _forceSessionRevokePersistenceFailure, false);

    public static bool HasPendingConcurrentSessionTouch => _concurrentSessionTouchStage is not null;

    public static bool HasPendingConcurrentSessionClear => _concurrentSessionClear;

    public static bool ConsumeConcurrentSessionClear() => Interlocked.Exchange(ref _concurrentSessionClear, false);

    public static bool HasPendingConcurrentSessionRevoke => _concurrentSessionRevokeStage is not null;

    public static bool HasPendingConcurrentFailedAccess => _concurrentFailedAccess;

    public static bool ConsumeConcurrentFailedAccess() => Interlocked.Exchange(ref _concurrentFailedAccess, false);

    public static bool ConsumeConcurrentSessionRevoke(SessionWriteStage stage)
    {
        if (_concurrentSessionRevokeStage != stage) return false;
        _concurrentSessionRevokeStage = null;
        return true;
    }

    public static bool ConsumeConcurrentSessionTouch(SessionWriteStage stage)
    {
        if (_concurrentSessionTouchStage != stage) return false;
        _concurrentSessionTouchStage = null;
        return true;
    }

    public static IValidatedOptionalSession GetValidatedOptionalSession() =>
        new TestValidatedOptionalSession(_optionalSessionIdentityId, _optionalSessionEmail, _optionalSessionIsInvalid);

    public static string GetRegistrationRawToken() => RegistrationRawToken;

    public static int ConfirmationTokenHashInvocationCount => Volatile.Read(ref _confirmationTokenHashInvocationCount);

    public static bool ConfirmationSecretLockBarrierWasObserved => Volatile.Read(ref _confirmationSecretLockBarrierArrivals) == 2;

    public static void RecordConfirmationTokenHash() => Interlocked.Increment(ref _confirmationTokenHashInvocationCount);

    public static void ResetConfirmationTokenHashInvocationCount() => Interlocked.Exchange(ref _confirmationTokenHashInvocationCount, 0);

    public static int PasswordVerificationCount => Volatile.Read(ref _passwordVerificationCount);

    public static void RecordPasswordVerification() => Interlocked.Increment(ref _passwordVerificationCount);

    public static void ResetPasswordVerificationCount() => Interlocked.Exchange(ref _passwordVerificationCount, 0);

    public static string[] CapturedLogs => _capturedLogs.ToArray();

    public static void RecordLog(string entry) => _capturedLogs.Enqueue(entry);

    public static void ResetCapturedLogs() => _capturedLogs.Clear();

    public static void EnableConfirmationSecretLockBarrier()
    {
        _confirmationSecretLockBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _confirmationSecretLockBarrierArrivals, 0);
    }

    public static async Task WaitForConfirmationSecretLockBarrierAsync(CancellationToken cancellationToken)
    {
        var barrier = Volatile.Read(ref _confirmationSecretLockBarrier);
        if (barrier is null) return;
        if (Interlocked.Increment(ref _confirmationSecretLockBarrierArrivals) == 2) barrier.TrySetResult();
        await barrier.Task.WaitAsync(cancellationToken);
    }

    public static void SetCurrentTenant(TenantId? tenantId) => _tenantId = tenantId;

    public static void SetHttpAuthorizationGranted(bool granted) => _httpAuthorizationGranted = granted;

    public static void SetApplicationPermissionGranted(bool granted) => _applicationPermissionGranted = granted;

    public static void EnableSessionValidationConcurrentRevoke() => _forceSessionValidationConcurrentRevoke = true;

    public static void EnableConcurrentSessionTouch(SessionWriteStage stage) => _concurrentSessionTouchStage = stage;

    public static void EnableConcurrentSessionClear() => _concurrentSessionClear = true;

    public static void EnableConcurrentSessionRevoke(SessionWriteStage stage) => _concurrentSessionRevokeStage = stage;

    public static void EnableConcurrentFailedAccess() => _concurrentFailedAccess = true;

    public static void ForceSessionRevokePersistenceFailure() => _forceSessionRevokePersistenceFailure = true;

    public static void ForceTodoItemConcurrencyConflict() => _forceTodoItemConcurrencyConflict = true;

    public static void ForceUnexpectedFailure() => _forceUnexpectedFailure = true;

    public static void ForceRegistrationRollbackAfterPersistedEffects() => _forceRegistrationRollbackAfterPersistedEffects = true;

    public static void ForceConfirmationRollbackAfterPersistedEffects() => _forceConfirmationRollbackAfterPersistedEffects = true;

    public static void SetValidatedOptionalSession(Guid? identityId, string? email, bool isInvalid = false)
    {
        _optionalSessionIdentityId = identityId;
        _optionalSessionEmail = email;
        _optionalSessionIsInvalid = isInvalid;
    }

    public static async Task<Guid> RunAsDefaultUserAsync()
    {
        return await RunAsUserAsync("test@local", "Testing1234!", []);
    }

    public static async Task<Guid> RunAsAdministratorAsync()
    {
        return await RunAsUserAsync("administrator@local", "Administrator1234!", [Roles.Administrator]);
    }

    public static async Task<Guid> RunAsUserAsync(string userName, string password, string[] roles)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = userName, Email = userName };

        var result = await userManager.CreateAsync(user, password);

        if (roles.Length > 0)
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            foreach (var role in roles)
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }

            await userManager.AddToRolesAsync(user, roles);
        }

        if (result.Succeeded)
        {
            _userId = user.Id;
            _sessionId = Guid.NewGuid();
            _roles = [..roles];
            _httpAuthorizationGranted = true;
            _applicationPermissionGranted = true;
            return _userId.Value;
        }

        throw new Exception($"Unable to create test identity. Error code: {result.ToApplicationResult(IdentityAccessErrors.UserCreationFailed()).Error?.Code ?? "unknown"}.");
    }

    public static async Task ResetState()
    {
        if (FunctionalTestSetup.DbResetter is not null)
        {
            await FunctionalTestSetup.DbResetter.ResetAsync();
        }

        _userId = null;
        _sessionId = null;
        _roles = null;
        _tenantId = null;
        _httpAuthorizationGranted = false;
        _applicationPermissionGranted = false;
        _forceTodoItemConcurrencyConflict = false;
        _forceUnexpectedFailure = false;
        _forceRegistrationRollbackAfterPersistedEffects = false;
        _forceConfirmationRollbackAfterPersistedEffects = false;
        _forceSessionValidationConcurrentRevoke = false;
        _forceSessionRevokePersistenceFailure = false;
        _concurrentSessionTouchStage = null;
        _concurrentSessionClear = false;
        _concurrentSessionRevokeStage = null;
        _concurrentFailedAccess = false;
        _optionalSessionIdentityId = null;
        _optionalSessionEmail = null;
        _optionalSessionIsInvalid = false;
        _confirmationSecretLockBarrier = null;
        Interlocked.Exchange(ref _confirmationSecretLockBarrierArrivals, 0);
        ResetConfirmationTokenHashInvocationCount();
        ResetPasswordVerificationCount();
        ResetCapturedLogs();
    }

    public static async Task<TEntity?> FindAsync<TEntity>(params object[] keyValues)
        where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.FindAsync<TEntity>(keyValues);
    }

    public static async Task AddAsync<TEntity>(TEntity entity)
        where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Add(entity);

        await context.SaveChangesAsync();
    }

    public static async Task<int> CountAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Set<TEntity>().CountAsync();
    }

    public static async Task<List<TEntity>> ListAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Set<TEntity>().AsNoTracking().ToListAsync();
    }

    public static async Task SetConfirmationLifecycleAsync(TenantStatus tenantStatus, string membershipStatus)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync();
        var membership = await context.TenantMemberships.SingleAsync();

        await context.Database.ExecuteSqlAsync($"UPDATE \"Tenants\" SET \"Status\" = {tenantStatus.ToString()} WHERE \"Id\" = {tenant.Id.Value}");
        await context.Database.ExecuteSqlAsync($"UPDATE \"TenantMemberships\" SET \"Status\" = {membershipStatus} WHERE \"Id\" = {membership.Id.Value}");
    }

    public static async Task ExpireConfirmationSecretAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secret = await context.OutboxSecrets.SingleAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE outbox_secrets SET \"ExpiresAt\" = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\" = {secret.Id}");
    }

    public static async Task SetConfirmationSecretHashAsync(string versionedHash)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secret = await context.OutboxSecrets.SingleAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE outbox_secrets SET \"VersionedHash\" = {versionedHash} WHERE \"Id\" = {secret.Id}");
    }

    public static async Task MarkConfirmationSecretDeliveredAsync(string reason, DateTimeOffset deliveredAt, string providerReceipt)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secret = await context.OutboxSecrets.SingleAsync();
        secret.MarkDelivered(reason, deliveredAt, providerReceipt);
        await context.SaveChangesAsync();
    }
}

internal sealed class TestValidatedOptionalSession(Guid? identityId, string? email, bool isInvalid) : IValidatedOptionalSession
{
    public Guid? IdentityId { get; } = identityId;
    public string? Email { get; } = email;
    public bool IsInvalid { get; } = isInvalid;
}

/// <summary>The persistence step of a session request that a test wants a competing writer to race against.</summary>
public enum SessionWriteStage { Validation, Revocation, TenantSelection, TenantClearing }
