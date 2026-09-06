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
    RateLimited,

    /// <summary>
    /// The request could not be decided because something it depends on is not answering. It is distinct from
    /// <see cref="RateLimited"/> on purpose: a caller who has spent nothing must not be told they tried too often,
    /// and an outage must stay visible to whoever is watching (IA-REQ-057).
    /// </summary>
    Unavailable
}
