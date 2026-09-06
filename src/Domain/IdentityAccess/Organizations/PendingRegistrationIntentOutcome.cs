namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

/// <summary>
/// How a registration intent ended. Every value is terminal: the first spend of the mailed token records one and
/// every later spend returns it, so a replay can never produce a second organization or a second answer.
/// </summary>
public enum PendingRegistrationIntentOutcome
{
    /// <summary>The address was proved and the organization exists.</summary>
    Created,

    /// <summary>The address was proved, but the CUIT or the address itself had been taken since initiation.</summary>
    Conflicted,

    /// <summary>The address already had an account at initiation; nothing was ever mintable for it.</summary>
    Notified,

    /// <summary>The window closed before the token was spent.</summary>
    Expired
}
