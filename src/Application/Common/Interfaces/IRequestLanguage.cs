namespace CleanArchitecture.Application.Common.Interfaces;

/// <summary>The supported UI language already admitted by the interactive request boundary.</summary>
public interface IRequestLanguage
{
    string Language { get; }
}
