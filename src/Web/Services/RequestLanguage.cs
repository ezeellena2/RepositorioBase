using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;
using Microsoft.AspNetCore.Localization;

namespace CleanArchitecture.Web.Services;

/// <summary>
/// Exposes the language already negotiated by ASP.NET Core. It never reads the account: authenticated preference
/// reaches this boundary through the culture cookie written by the SPA (IA-REQ-059/L10N-REQ-004).
/// </summary>
public sealed class RequestLanguage(IHttpContextAccessor accessor, LocalizationSettings settings) : IRequestLanguage
{
    public string Language
    {
        get
        {
            var culture = accessor.HttpContext?.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture;
            if (culture is null)
            {
                return settings.DefaultLanguage;
            }

            return LocalizationRegistry.SupportedLanguages.FirstOrDefault(language =>
                       string.Equals(language, culture.Name, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(language, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                   ?? settings.DefaultLanguage;
        }
    }
}
