using System.Threading.RateLimiting;
using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Transport rate limiting for the login endpoint (IA-REQ-019): two chained fixed-window partitions with zero
/// queue, per client address (global limiter that is a no-op for every other endpoint) and per normalized account
/// (the named policy only <c>POST /api/identity/sessions</c> carries). Rejections go through the shared RFC 9457
/// writer with a stable code and <c>Retry-After</c>. Identity lockout is a separate control owned by the account
/// service; both stay distinct on purpose.
/// </summary>
public static class LoginRateLimiting
{
    public const int ClientPermitLimit = 20;
    public const int AccountPermitLimit = 10;
    public static readonly TimeSpan ClientWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AccountWindow = TimeSpan.FromMinutes(15);

    private const string UnlimitedPartition = "unlimited";
    private const string RateLimitExceededCode = "rate_limit_exceeded";

    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        services.AddOptions<RateLimiterOptions>().Configure<TimeProvider>((options, timeProvider) =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                LoginRateLimitKeyMiddleware.IsLoginEndpoint(context) && context.Items[LoginRateLimitKeyMiddleware.ClientKeyItem] is string clientKey
                    ? RateLimitPartition.Get(clientKey, _ => new TimeProviderFixedWindowRateLimiter(ClientPermitLimit, ClientWindow, timeProvider))
                    : RateLimitPartition.GetNoLimiter(UnlimitedPartition));

            options.AddPolicy(LoginRateLimitPartitioner.PolicyName, context =>
                context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem] is string accountKey
                    ? RateLimitPartition.Get(accountKey, _ => new TimeProviderFixedWindowRateLimiter(AccountPermitLimit, AccountWindow, timeProvider))
                    : RateLimitPartition.GetNoLimiter(UnlimitedPartition));

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // Both partitions are fixed windows that always report the remaining window; the longer window is the
        // conservative fallback should a lease ever arrive without it, because the contract requires Retry-After.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : (int)AccountWindow.TotalSeconds;
        var problems = context.HttpContext.RequestServices.GetRequiredService<CleanArchitecture.Web.Infrastructure.IProblemDetailsService>();
        await problems.WriteAsync(
            context.HttpContext,
            new ApplicationError(RateLimitExceededCode, ApplicationErrorCategory.RateLimited, retryAfterSeconds: retryAfterSeconds),
            cancellationToken);
    }
}
