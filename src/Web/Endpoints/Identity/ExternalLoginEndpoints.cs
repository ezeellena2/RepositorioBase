using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using CleanArchitecture.Web.Endpoints;
using CleanArchitecture.Web.Infrastructure;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace CleanArchitecture.Web.IdentityEndpoints;

/// <summary>
/// Signing in with a provider, and linking or unlinking one.
/// <para>
/// The three starts are separate routes because they are three different authorization decisions: a sign-in is
/// public, since nobody is signed in yet; a link and a proof are self-service and need the session, the consent
/// and the recent proof. The completion is one route because what it may do is decided by the purpose the
/// callback sealed into the cookie — not by anything the caller sends — and each purpose's own request carries
/// its own authorization, so a `Login` completion cannot be talked into performing a `Link` (IA-REQ-052).
/// </para>
/// <para>
/// The provider's own callback is not here: it is <c>/api/identity/external/google/callback</c>, served by the
/// OpenID Connect handler before routing. It is the single narrow exception to the antiforgery rule, because a
/// cross-site form post can carry neither this application's header nor its `Origin`; what stands in for them is
/// the protocol's one-use state, nonce, PKCE verifier and validated signature, issuer, audience and expiry.
/// </para>
/// </summary>
internal static class ExternalLoginEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/external/{provider}/login/start", StartLogin)
            .RequireLoginAttemptBudgets()
            .Produces<ExternalChallengeResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.InvalidExternalLogin, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.ServiceUnavailable, ApiProblemMetadata.InternalServerError);

        group.MapPost("/external/{provider}/link/start", StartLink)
            .RequireAuthorization()
            .Produces<ExternalChallengeResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.EmailConfirmationRequired, ApiProblemMetadata.InvalidExternalLogin, ApiProblemMetadata.ProviderAlreadyLinked, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidExternalLogin.Code);

        group.MapPost("/external/{provider}/proof/start", StartProof)
            .RequireAuthorization()
            .Produces<ExternalChallengeResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InvalidExternalLogin, ApiProblemMetadata.NotFound, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError)
            .WithBodyBindingFailureCode(ApiProblemMetadata.InvalidExternalLogin.Code);

        // A top-level navigation, so it carries no header and no body to validate. What it does carry is the
        // sealed cookie, and it starts a challenge for that handoff or for nothing at all.
        group.MapGet("/external/{provider}/{leg}/challenge", StartChallenge)
            .ExcludeFromDescription();

        group.MapPost("/external/complete", Complete)
            .RequireLoginAttemptBudgets()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InvalidExternalLogin, ApiProblemMetadata.ExternalLoginConflict, ApiProblemMetadata.ProviderAlreadyLinked, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.ServiceUnavailable, ApiProblemMetadata.InternalServerError);

        group.MapGet("/external", ListLinks)
            .RequireAuthorization()
            .Produces<ExternalLinksResponse>(StatusCodes.Status200OK)
            .WithApiProblemDetails(ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);

        group.MapDelete("/external/{provider}", Unlink)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(ApiProblemMetadata.AntiforgeryValidationFailed, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.InvalidSession, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.RecentProofRequired, ApiProblemMetadata.NotFound, ApiProblemMetadata.LastAuthenticatorRequired, ApiProblemMetadata.RateLimitExceeded, ApiProblemMetadata.InternalServerError);
    }

    private static async Task<IResult> StartLogin(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, string provider)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (ExternalProviders.Canonical(provider) is not { } canonical) return Refused(problems);
        return Handoff(context, problems, await sender.Send(new StartExternalLoginCommand(canonical), context.RequestAborted));
    }

    private static async Task<IResult> StartLink(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, string provider, ExternalLinkRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (ExternalProviders.Canonical(provider) is not { } canonical) return Refused(problems);
        return Handoff(context, problems, await sender.Send(new StartExternalLinkCommand(canonical, body.Consent), context.RequestAborted));
    }

    private static async Task<IResult> StartProof(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, string provider, ExternalProofRequest body)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (ExternalProviders.Canonical(provider) is not { } canonical) return Refused(problems);
        return Handoff(context, problems, await sender.Send(new StartExternalProofCommand(canonical, body.Action), context.RequestAborted));
    }

    private static IResult StartChallenge(HttpContext context, IExternalHandoffContext handoffs, string provider, string leg)
    {
        // Nothing is looked up by what the caller said. The cookie names the handoff; the route values only have
        // to agree with a provider this deployment offers and a leg it knows.
        if (ExternalProviders.Canonical(provider) is not { } canonical
            || !handoffs.IsConfigured(canonical)
            || leg is not ("login" or "link" or "proof")
            || handoffs.Current is not { } handoffId
            || handoffs.CurrentPurpose is not { } purpose)
        {
            return Results.Redirect("/external/return?outcome=refused");
        }

        // The purpose comes from the sealed cookie, never from `leg`. What the challenge asks the provider for
        // differs by purpose, so reading it from a route value would let a caller ask for the weaker one.
        return GoogleOidcConfiguration.Challenge(context, handoffId, purpose);
    }

    /// <summary>
    /// The one completion. Which request it sends is read from the sealed cookie's purpose, so a caller cannot
    /// name the effect it wants; each request then meets its own authorization on the way through.
    /// </summary>
    private static async Task<IResult> Complete(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, IExternalHandoffContext handoffs)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems, rejectInvalidOptionalSession: true);
        if (antiforgeryFailure is not null) return antiforgeryFailure;

        var purpose = handoffs.CurrentPurpose;
        if (purpose is ExternalAuthorizationPurpose.Login)
        {
            var login = await sender.Send(new CompleteExternalLoginCommand(), context.RequestAborted);

            // Spent either way: a refused handoff must not be available for a second attempt.
            ExternalHandoffCookie.Clear(context);
            if (login.IsFailure) return problems.ToHttpResult(login.Error!);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, login.Value!.IdentityId.ToString()),
                new Claim(ClaimTypes.Sid, login.Value.SessionId.ToString())
            };
            await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme)));
            Identity.DeleteAntiforgeryCookie(context);
            return Results.NoContent();
        }

        var result = purpose switch
        {
            ExternalAuthorizationPurpose.Link => await sender.Send(new CompleteExternalLinkCommand(), context.RequestAborted),
            ExternalAuthorizationPurpose.Proof => await sender.Send(new CompleteExternalProofCommand(), context.RequestAborted),
            _ => CleanArchitecture.Application.Common.Models.Result.Failure(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.InvalidExternalLogin())
        };

        ExternalHandoffCookie.Clear(context);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> ListLinks(HttpContext context, ApiProblemDetailsMapper problems, ISender sender)
    {
        var result = await sender.Send(new ListExternalLoginsQuery(), context.RequestAborted);
        return result.IsSuccess
            ? Results.Ok(new ExternalLinksResponse(
                result.Value!.Items.Select(link => new ExternalLinkResponse(link.Handle, link.Provider, link.ProviderEmail, link.LinkedAt)).ToArray(),
                result.Value.Available))
            : problems.ToHttpResult(result.Error!);
    }

    private static async Task<IResult> Unlink(HttpContext context, IAntiforgery antiforgery, ApiProblemDetailsMapper problems, ISender sender, string provider)
    {
        var antiforgeryFailure = await Identity.ValidateAntiforgery(context, antiforgery, problems);
        if (antiforgeryFailure is not null) return antiforgeryFailure;
        if (ExternalProviders.Canonical(provider) is not { } canonical)
        {
            return problems.ToHttpResult(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.ExternalLinkNotFound());
        }

        var result = await sender.Send(new UnlinkExternalLoginCommand(canonical), context.RequestAborted);
        return result.IsSuccess ? Results.NoContent() : problems.ToHttpResult(result.Error!);
    }

    /// <summary>
    /// The identifier goes into the sealed cookie and the body carries only the local URI. Returning it would put
    /// the one thing the callback must not be able to choose into the client's hands.
    /// </summary>
    private static IResult Handoff(HttpContext context, ApiProblemDetailsMapper problems, CleanArchitecture.Application.Common.Models.Result<ExternalAuthorizationHandoff> result)
    {
        if (result.IsFailure) return problems.ToHttpResult(result.Error!);
        ExternalHandoffCookie.Seal(context, result.Value!.HandoffId, result.Value.Purpose);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ExternalChallengeResponse(result.Value.AuthorizationRequestUri));
    }

    private static IResult Refused(ApiProblemDetailsMapper problems) =>
        problems.ToHttpResult(CleanArchitecture.Application.IdentityAccess.Common.IdentityAccessErrors.InvalidExternalLogin());
}

public sealed record ExternalChallengeResponse(string AuthorizationRequestUri);

public sealed record ExternalLinkRequest(bool Consent);

public sealed record ExternalProofRequest(string Action);

public sealed record ExternalLinkResponse(string Handle, string Provider, string ProviderEmail, DateTimeOffset LinkedAt);

public sealed record ExternalLinksResponse(IReadOnlyList<ExternalLinkResponse> Items, IReadOnlyList<string> Available);
