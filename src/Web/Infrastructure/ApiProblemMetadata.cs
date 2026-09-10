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

    /// <summary>
    /// The shared abuse-control store is unreachable, so the attempt was refused without being counted. It is a
    /// distinct contract from <see cref="RateLimitExceeded"/> because a caller who spent nothing must not be told
    /// they tried too often (amendment A5).
    /// </summary>
    public static readonly ApiProblemContract ServiceUnavailable = new(StatusCodes.Status503ServiceUnavailable, "service_unavailable", true);

    public static readonly ApiProblemContract PersonalRegistrationConflict = new(StatusCodes.Status409Conflict, "personal_registration_conflict");

    public static readonly ApiProblemContract PersonalProfileNotFound = new(StatusCodes.Status404NotFound, "personal_profile_not_found");

    public static readonly ApiProblemContract PersonalProfileConcurrencyConflict = new(StatusCodes.Status409Conflict, "personal_profile_concurrency_conflict");

    public static readonly ApiProblemContract ProfileFieldNotEditable = new(StatusCodes.Status400BadRequest, "profile_field_not_editable");

    public static readonly ApiProblemContract SessionNotFound = new(StatusCodes.Status404NotFound, "session_not_found");

    /// <summary>
    /// The action needs a proof this session does not hold. It is `401` rather than `409` because retrying the
    /// same proof cannot succeed: the caller has to prove again (IA-REQ-051).
    /// </summary>
    public static readonly ApiProblemContract CredentialSuperseded = new(StatusCodes.Status401Unauthorized, "credential_superseded");
    public static readonly ApiProblemContract RecentProofRequired = new(StatusCodes.Status401Unauthorized, "recent_proof_required");

    public static readonly ApiProblemContract InvalidCredentialProof = new(StatusCodes.Status400BadRequest, "invalid_credential_proof");

    public static readonly ApiProblemContract EmailConfirmationRequired = new(StatusCodes.Status403Forbidden, "email_confirmation_required");

    public static readonly ApiProblemContract InvalidCredentialToken = new(StatusCodes.Status400BadRequest, "invalid_credential_token");

    /// <summary>Every way a provider round trip can fail, under one code (IA-REQ-052).</summary>
    public static readonly ApiProblemContract InvalidExternalLogin = new(StatusCodes.Status400BadRequest, "invalid_external_login");

    public static readonly ApiProblemContract ExternalLoginConflict = new(StatusCodes.Status409Conflict, "external_login_conflict");

    public static readonly ApiProblemContract ProviderAlreadyLinked = new(StatusCodes.Status409Conflict, "provider_already_linked");

    public static readonly ApiProblemContract LastAuthenticatorRequired = new(StatusCodes.Status409Conflict, "last_authenticator_required");

    public static readonly ApiProblemContract InvalidRoleOperation = new(StatusCodes.Status400BadRequest, "invalid_role_operation");

    public static readonly ApiProblemContract InvalidMembershipOperation = new(StatusCodes.Status400BadRequest, "invalid_membership_operation");

    public static readonly ApiProblemContract RoleConcurrencyConflict = new(StatusCodes.Status409Conflict, "role_concurrency_conflict");

    public static readonly ApiProblemContract MembershipConcurrencyConflict = new(StatusCodes.Status409Conflict, "membership_concurrency_conflict");

    /// <summary>The floor C5 refuses to commit through: one identity holding both halves of administration.</summary>
    public static readonly ApiProblemContract LastAdministratorRequired = new(StatusCodes.Status409Conflict, "last_administrator_required");

    public static readonly ApiProblemContract OwnerRequired = new(StatusCodes.Status403Forbidden, "owner_required");

    /// <summary>Every way the public reactivation route can refuse, worded as one (IA-REQ-054).</summary>
    public static readonly ApiProblemContract InvalidReactivation = new(StatusCodes.Status400BadRequest, "invalid_reactivation");

    public static readonly ApiProblemContract PlatformLastOwner = new(StatusCodes.Status409Conflict, "platform_last_owner");

    public static readonly ApiProblemContract IdentityConcurrencyConflict = new(StatusCodes.Status409Conflict, "identity_concurrency_conflict");

    public static readonly ApiProblemContract PlatformMfaConcurrencyConflict = new(StatusCodes.Status409Conflict, "platform_mfa_concurrency_conflict");

    public static readonly ApiProblemContract RetentionHoldConflict = new(StatusCodes.Status409Conflict, "retention_hold_conflict");

    public static readonly ApiProblemContract RetentionHoldSubjectPurged = new(StatusCodes.Status409Conflict, "retention_hold_subject_purged");

    public static readonly ApiProblemContract InvalidDocumentDispute = new(StatusCodes.Status400BadRequest, "invalid_document_dispute");

    public static readonly ApiProblemContract DocumentDisputeConflict = new(StatusCodes.Status409Conflict, "document_dispute_conflict");

    public static readonly ApiProblemContract SelfResolutionRefused = new(StatusCodes.Status403Forbidden, "self_resolution_refused");

    public static readonly ApiProblemContract DocumentAlreadyRecorded = new(StatusCodes.Status409Conflict, "document_already_recorded");

    /// <summary>`Closed` and nothing else: a tombstone has no way back (IA-REQ-054, amendment A4).</summary>
    public static readonly ApiProblemContract IdentityReactivationUnavailable = new(StatusCodes.Status403Forbidden, "identity_reactivation_unavailable");


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
