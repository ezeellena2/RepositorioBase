using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;

namespace CleanArchitecture.Infrastructure.Localization;

/// <summary>Provides the configured default language when no interactive HTTP request exists.</summary>
public sealed class ConfiguredRequestLanguage(LocalizationSettings settings) : IRequestLanguage
{
    public string Language => settings.DefaultLanguage;
}
