using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Web.Infrastructure;

public interface IProblemDetailsService
{
    Task WriteAsync(HttpContext httpContext, ApplicationError error, CancellationToken cancellationToken = default);

    Task WriteUnexpectedAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}

public sealed class ApiProblemDetailsMapper : IProblemDetailsService
{
    public ApiProblemDetails Create(HttpContext httpContext, ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = GetStatusCode(error.Category);

        return new ApiProblemDetails
        {
            Status = status,
            Type = "about:blank",
            Title = GetTitle(status),
            Detail = error.Detail,
            Instance = httpContext.Request.Path,
            Code = error.Code,
            TraceId = GetTraceId(httpContext),
            Errors = error.Category == ApplicationErrorCategory.Validation && error.ValidationErrors.Count > 0
                ? error.ValidationErrors
                : null
        };
    }

    public async Task WriteAsync(HttpContext httpContext, ApplicationError error, CancellationToken cancellationToken = default)
    {
        var problem = Create(httpContext, error);
        httpContext.Response.StatusCode = problem.Status!.Value;
        httpContext.Response.ContentType = "application/problem+json";
        if (error.RetryAfterSeconds is { } retryAfterSeconds)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        await System.Text.Json.JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problem,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
            cancellationToken);
    }

    public async Task WriteUnexpectedAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var problem = new ApiProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = "about:blank",
            Title = "Internal Server Error",
            Instance = httpContext.Request.Path,
            Code = "internal_server_error",
            TraceId = GetTraceId(httpContext)
        };

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problem,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
            cancellationToken);
    }

    public IResult ToHttpResult(ApplicationError error) => new ProblemDetailsResult(this, error);

    public static int GetStatusCode(ApplicationErrorCategory category) => category switch
    {
        ApplicationErrorCategory.Validation => StatusCodes.Status400BadRequest,
        ApplicationErrorCategory.Authentication => StatusCodes.Status401Unauthorized,
        ApplicationErrorCategory.Authorization => StatusCodes.Status403Forbidden,
        ApplicationErrorCategory.NotFound => StatusCodes.Status404NotFound,
        ApplicationErrorCategory.Conflict => StatusCodes.Status409Conflict,
        ApplicationErrorCategory.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown expected error category.")
    };

    private static string GetTraceId(HttpContext httpContext) =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    private static string GetTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error"
    };

    private sealed class ProblemDetailsResult(ApiProblemDetailsMapper mapper, ApplicationError error) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => mapper.WriteAsync(httpContext, error, httpContext.RequestAborted);
    }
}
