using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Web.Endpoints;

public sealed class Identity : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity";

    internal const string AuthenticationCookieName = CleanArchitecture.Infrastructure.Identity.SessionCookieEvents.CookieName;
    internal const string AntiforgeryCookieName = "__Host-XSRF-TOKEN";

    public static void Map(RouteGroupBuilder group)
    {
        global::CleanArchitecture.Web.IdentityEndpoints.SessionEndpoints.Map(group);
        global::CleanArchitecture.Web.IdentityEndpoints.ContextEndpoints.Map(group);
        global::CleanArchitecture.Web.IdentityEndpoints.PersonalEndpoints.Map(group);
        global::CleanArchitecture.Web.IdentityEndpoints.PasswordEndpoints.Map(group);
        global::CleanArchitecture.Web.IdentityEndpoints.AccountLifecycleEndpoints.Map(group);
        global::CleanArchitecture.Web.IdentityEndpoints.ExternalLoginEndpoints.Map(group);
        group.MapGet("/antiforgery", GetAntiforgery)
            .Produces<AntiforgeryResponse>()
            .WithApiProblemDetails(ApiProblemMetadata.InternalServerError);
        group.MapPost("/organizations/register", Register)
            .Produces(StatusCodes.Status202Accepted)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidRegistration, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.RegistrationConflict, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidRegistration.Code);
        group.MapPost("/confirm-email", Confirm)
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidConfirmation, ApiProblemMetadata.RegistrationConflict, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidConfirmation.Code);
    }

    private static async Task<Ok<AntiforgeryResponse>> GetAntiforgery(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.Request.Cookies.ContainsKey(AuthenticationCookieName))
        {
            await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        }
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new AntiforgeryResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> Register(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, RegisterOrganizationCommand command)
    {
        var antiForgeryFailure = await ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiForgeryFailure is not null) return antiForgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.StatusCode(StatusCodes.Status202Accepted) : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Confirm(HttpContext context, IAntiforgery antiforgery, ISender sender, ApiProblemDetailsMapper problems, ConfirmEmailCommand command)
    {
        var antiForgeryFailure = await ValidateAntiforgery(context, antiforgery, problems);
        if (antiForgeryFailure is not null) return antiForgeryFailure;
        var result = await sender.Send(command, context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    internal static async Task<IResult?> ValidateAntiforgery(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, bool rejectInvalidOptionalSession = false)
    {
        if (!HasExactSameOrigin(context))
            return AntiforgeryFailure(problems);

        // Public endpoints may still carry the optional session cookie. Authenticate it before
        // validating the session-bound antiforgery token, while leaving no-cookie callers anonymous.
        if (context.Request.Cookies.ContainsKey(AuthenticationCookieName))
        {
            await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (rejectInvalidOptionalSession && context.Items.ContainsKey(SessionCookieEvents.InvalidSessionKey))
            {
                return problems.ToHttpResult(new CleanArchitecture.Application.Common.Models.ApplicationError(
                    "invalid_session",
                    CleanArchitecture.Application.Common.Models.ApplicationErrorCategory.Authentication));
            }
        }
        try { await antiforgery.ValidateRequestAsync(context); return null; }
        catch (AntiforgeryValidationException) { return AntiforgeryFailure(problems); }
    }

    /// <summary>Deletes the antiforgery cookie so the client must bootstrap a pair bound to the new authentication state.</summary>
    internal static void DeleteAntiforgeryCookie(HttpContext context) =>
        context.Response.Cookies.Delete(AntiforgeryCookieName, new CookieOptions
        {
            Path = "/",
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        });

    private static IResult AntiforgeryFailure(ApiProblemDetailsMapper problems) =>
        problems.ToHttpResult(new CleanArchitecture.Application.Common.Models.ApplicationError("antiforgery_validation_failed", CleanArchitecture.Application.Common.Models.ApplicationErrorCategory.Validation, "The request could not be validated."));

    private static bool HasExactSameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
            !string.IsNullOrEmpty(originUri.UserInfo) ||
            originUri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(originUri.Query) ||
            !string.IsNullOrEmpty(originUri.Fragment))
        {
            return false;
        }

        var requestPort = context.Request.Host.Port ?? (string.Equals(context.Request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return string.Equals(originUri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
               originUri.Port == requestPort;
    }
}

public sealed record AntiforgeryResponse(string RequestToken);
