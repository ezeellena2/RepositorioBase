using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Maps internal expected failures at the HTTP boundary without serializing Result.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result, HttpContext httpContext, ApiProblemDetailsMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? Results.NoContent()
            : mapper.ToHttpResult(result.Error!);
    }

    /// <summary>A bodyless <c>202 Accepted</c> on success; a failure goes through the single RFC 9457 writer.</summary>
    public static IResult ToAcceptedHttpResult(this Result result, HttpContext httpContext, ApiProblemDetailsMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? Results.StatusCode(StatusCodes.Status202Accepted)
            : mapper.ToHttpResult(result.Error!);
    }

    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext, ApiProblemDetailsMapper mapper, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess
            ? onSuccess(result.Value!)
            : mapper.ToHttpResult(result.Error!);
    }
}
