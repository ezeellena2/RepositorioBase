using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Binds antiforgery request tokens to the validated persisted session when one exists.</summary>
public sealed class SessionAntiforgeryAdditionalDataProvider : IAntiforgeryAdditionalDataProvider
{
    public string GetAdditionalData(HttpContext context)
    {
        if (context.Items[SessionCookieEvents.ValidatedSessionKey] is ValidatedSession validated)
        {
            return validated.SessionId.Value.ToString("N");
        }

        return context.Items[SessionCookieEvents.AntiforgerySessionKey] is Guid sessionId
            ? sessionId.ToString("N")
            : string.Empty;
    }

    public bool ValidateAdditionalData(HttpContext context, string additionalData)
    {
        var expected = GetAdditionalData(context);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(additionalData));
    }
}
