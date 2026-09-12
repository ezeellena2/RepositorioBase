using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Writes every exception response through the shared RFC 9457 boundary.</summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
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
            BadHttpRequestException or System.Text.Json.JsonException => BindingError(httpContext),
            _ => null
        };

        if (error is null)
        {
            await problemDetails.WriteUnexpectedAsync(httpContext, cancellationToken);
            return true;
        }

        await problemDetails.WriteAsync(httpContext, error, cancellationToken);
        return true;
    }

    private static ApplicationError BindingError(HttpContext httpContext)
    {
        var endpointCode = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint
            ?.Metadata.GetMetadata<ApiBodyBindingFailureMetadata>()?.Code;
        return new ApplicationError(endpointCode ?? "invalid_request", ApplicationErrorCategory.Validation);
    }
}
