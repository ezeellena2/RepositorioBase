namespace CleanArchitecture.Application.Common.Models;

/// <summary>
/// The finite set of expected application failure categories that may cross an
/// application boundary. Unexpected failures remain exceptions.
/// </summary>
public enum ApplicationErrorCategory
{
    Validation,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    RateLimited
}
