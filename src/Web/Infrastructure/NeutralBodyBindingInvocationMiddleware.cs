using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>
/// Opens the narrow execution window in which RequestDelegateFactory can reject a neutral endpoint's body.
/// The endpoint filter installed by <see cref="ApiProblemMetadata.WithNeutralBodyBindingFailure"/> closes that
/// window after successful binding and before application code runs.
/// </summary>
public sealed class NeutralBodyBindingInvocationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problems)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ApiNeutralBodyBindingFailureMetadata>() is null)
        {
            await next(context);
            return;
        }

        // The budgets have already been spent, but a body outside the transport inspection bound is never a
        // neutral parse failure. TestServer does not enforce Kestrel's body-size feature, so refuse it explicitly
        // here as the same existing invalid_request contract on every server.
        if (context.Items.ContainsKey(Identity.LoginRateLimitKeyMiddleware.OversizedBodyItem))
        {
            await problems.WriteAsync(
                context,
                new ApplicationError(ApiProblemMetadata.InvalidRequest.Code, ApplicationErrorCategory.Validation),
                context.RequestAborted);
            return;
        }

        context.Items[ApiNeutralBodyBindingExecutionState.HttpContextItemKey] =
            new ApiNeutralBodyBindingExecutionState();
        await next(context);
    }
}

public static class NeutralBodyBindingInvocationMiddlewareExtensions
{
    public static IApplicationBuilder UseNeutralBodyBindingInvocation(this IApplicationBuilder app) =>
        app.UseMiddleware<NeutralBodyBindingInvocationMiddleware>();
}
