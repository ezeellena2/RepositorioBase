using System.Diagnostics;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Logging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Writes every exception response through the shared RFC 9457 boundary.</summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(httpContext);
        var neutralBodyBinding = endpoint?.Metadata.GetMetadata<ApiNeutralBodyBindingFailureMetadata>();
        var neutralBindingExecution = httpContext.Items.TryGetValue(
            ApiNeutralBodyBindingExecutionState.HttpContextItemKey,
            out var value)
            ? value as ApiNeutralBodyBindingExecutionState
            : null;
        var neutralBindingIsPending = neutralBodyBinding is not null
            && neutralBindingExecution is { BindingCompleted: false };

        if (neutralBodyBinding is not null
            && neutralBindingIsPending
            && !httpContext.Items.ContainsKey(LoginRateLimitKeyMiddleware.OversizedBodyItem)
            && exception is BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType })
        {
            await ClearResponsePreservingInvalidSessionDeletionAsync(httpContext);
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return true;
        }

        if (neutralBodyBinding is not null
            && neutralBindingIsPending
            && !httpContext.Items.ContainsKey(LoginRateLimitKeyMiddleware.OversizedBodyItem)
            && IsNeutralBodyBindingFailure(exception))
        {
            var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
            var problems = httpContext.RequestServices.GetRequiredService<ApiProblemDetailsMapper>();
            var preflightFailure = await global::CleanArchitecture.Web.Endpoints.Identity.ValidateAntiforgery(
                httpContext,
                antiforgery,
                problems,
                endpoint);

            await ClearResponsePreservingInvalidSessionDeletionAsync(httpContext);
            if (preflightFailure is not null)
            {
                await preflightFailure.ExecuteAsync(httpContext);
                return true;
            }

            httpContext.Response.StatusCode = neutralBodyBinding.StatusCode;
            return true;
        }

        if (exception is UnauthorizedAccessException)
        {
            await ApiAuthenticationChallenge.ChallengeAsync(httpContext);
        }

        var error = exception switch
        {
            ValidationException validation => new ApplicationError(
                "validation_failed",
                ApplicationErrorCategory.Validation,
                validationErrors: new Dictionary<string, ValidationErrorDetail[]>(validation.Errors, StringComparer.Ordinal)),
            NotFoundException => new ApplicationError("not_found", ApplicationErrorCategory.NotFound),
            UnauthorizedAccessException => new ApplicationError("authentication_required", ApplicationErrorCategory.Authentication),
            ForbiddenAccessException => new ApplicationError("permission_denied", ApplicationErrorCategory.Authorization),
            BadHttpRequestException when neutralBindingExecution is not { BindingCompleted: true } => BindingError(httpContext),
            System.Text.Json.JsonException when neutralBindingExecution is not { BindingCompleted: true } => BindingError(httpContext),
            _ => null
        };

        if (error is null)
        {
            var endpointName = endpoint?.DisplayName ?? "unmatched";
            var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
            logger.LogSafeFailure(SafeFailure.Describe(exception, endpointName, traceId));
            await problemDetails.WriteUnexpectedAsync(httpContext, cancellationToken);
            return true;
        }

        await problemDetails.WriteAsync(httpContext, error, cancellationToken);
        return true;
    }

    private static ApplicationError BindingError(HttpContext httpContext)
    {
        var endpointCode = Endpoint(httpContext)?.Metadata.GetMetadata<ApiBodyBindingFailureMetadata>()?.Code;
        return new ApplicationError(endpointCode ?? "invalid_request", ApplicationErrorCategory.Validation);
    }

    private static Endpoint? Endpoint(HttpContext httpContext) =>
        httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();

    private static async Task ClearResponsePreservingInvalidSessionDeletionAsync(HttpContext httpContext)
    {
        var rejectedSession = httpContext.Items.ContainsKey(SessionCookieEvents.InvalidSessionKey);
        httpContext.Response.Clear();
        if (rejectedSession)
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    private static bool IsNeutralBodyBindingFailure(Exception exception) =>
        exception is System.Text.Json.JsonException
        or BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest };
}
