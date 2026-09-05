namespace CleanArchitecture.Web.Infrastructure;

public static class ApiProblemMetadata
{
    public const string ProblemContentType = "application/problem+json";

    public static readonly ApiProblemContract AuthenticationRequired = new(StatusCodes.Status401Unauthorized, "authentication_required");
    public static readonly ApiProblemContract PermissionDenied = new(StatusCodes.Status403Forbidden, "permission_denied");
    public static readonly ApiProblemContract ValidationFailed = new(StatusCodes.Status400BadRequest, "validation_failed");
    public static readonly ApiProblemContract InvalidRequest = new(StatusCodes.Status400BadRequest, "invalid_request");
    public static readonly ApiProblemContract InvalidRegistration = new(StatusCodes.Status400BadRequest, "invalid_registration");
    public static readonly ApiProblemContract AntiforgeryValidationFailed = new(StatusCodes.Status400BadRequest, "antiforgery_validation_failed");
    public static readonly ApiProblemContract InvalidConfirmation = new(StatusCodes.Status400BadRequest, "invalid_confirmation");
    public static readonly ApiProblemContract RouteBodyIdMismatch = new(StatusCodes.Status400BadRequest, "route_body_id_mismatch");
    public static readonly ApiProblemContract NotFound = new(StatusCodes.Status404NotFound, "not_found");
    public static readonly ApiProblemContract TodoItemConcurrencyConflict = new(StatusCodes.Status409Conflict, "todo_item_concurrency_conflict");
    public static readonly ApiProblemContract RegistrationConflict = new(StatusCodes.Status409Conflict, "registration_conflict");
    public static readonly ApiProblemContract SessionConcurrencyConflict = new(StatusCodes.Status409Conflict, "session_concurrency_conflict");
    public static readonly ApiProblemContract InvalidInvitation = new(StatusCodes.Status400BadRequest, "invalid_invitation");
    public static readonly ApiProblemContract InvitationConflict = new(StatusCodes.Status409Conflict, "invitation_conflict");
    public static readonly ApiProblemContract InvalidSession = new(StatusCodes.Status401Unauthorized, "invalid_session");
    public static readonly ApiProblemContract PlatformTenantConcurrencyConflict = new(StatusCodes.Status409Conflict, "platform_tenant_concurrency_conflict");
    public static readonly ApiProblemContract InvalidPlatformOperation = new(StatusCodes.Status400BadRequest, "invalid_platform_operation");
    public static readonly ApiProblemContract RecentMfaRequired = new(StatusCodes.Status401Unauthorized, "recent_mfa_required");
    public static readonly ApiProblemContract InternalServerError = new(StatusCodes.Status500InternalServerError, "internal_server_error");
    public static readonly ApiProblemContract RateLimitExceeded = new(StatusCodes.Status429TooManyRequests, "rate_limit_exceeded", true);

    public static RouteHandlerBuilder WithApiProblemDetails(this RouteHandlerBuilder builder, params ApiProblemContract[] contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        if (contracts.Length == 0)
        {
            throw new ArgumentException("At least one supported problem contract is required.", nameof(contracts));
        }

        var byStatus = contracts.GroupBy(contract => contract.StatusCode).ToArray();
        foreach (var group in byStatus)
        {
            builder.Produces<ApiProblemDetails>(group.Key, ProblemContentType);
        }

        builder.WithMetadata(new ApiProblemContractMetadata(contracts));
        return builder;
    }

    public static RouteHandlerBuilder WithCreatedLocation<TResponse>(this RouteHandlerBuilder builder)
    {
        builder.Produces<TResponse>(StatusCodes.Status201Created);
        builder.WithMetadata(new ApiSuccessContractMetadata(StatusCodes.Status201Created, true));
        return builder;
    }

    public static RouteHandlerBuilder WithBodyBindingFailureCode(this RouteHandlerBuilder builder, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        builder.WithMetadata(new ApiBodyBindingFailureMetadata(code));
        return builder;
    }
}

public sealed record ApiProblemContract(int StatusCode, string Code, bool RequiresRetryAfter = false);

public sealed class ApiProblemContractMetadata
{
    public ApiProblemContractMetadata(IEnumerable<ApiProblemContract> contracts)
    {
        Contracts = contracts.ToArray();
    }

    public IReadOnlyList<ApiProblemContract> Contracts { get; }
}

public sealed record ApiBodyBindingFailureMetadata(string Code);

public sealed record ApiSuccessContractMetadata(int StatusCode, bool RequiresLocationHeader);
