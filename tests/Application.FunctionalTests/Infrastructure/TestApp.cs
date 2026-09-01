using CleanArchitecture.Domain.Constants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Application.IdentityAccess.Common;
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

    public static void SetCurrentTenant(TenantId? tenantId) => _tenantId = tenantId;

    public static void SetHttpAuthorizationGranted(bool granted) => _httpAuthorizationGranted = granted;

    public static void SetApplicationPermissionGranted(bool granted) => _applicationPermissionGranted = granted;

    public static void ForceTodoItemConcurrencyConflict() => _forceTodoItemConcurrencyConflict = true;

    public static void ForceUnexpectedFailure() => _forceUnexpectedFailure = true;

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
}
