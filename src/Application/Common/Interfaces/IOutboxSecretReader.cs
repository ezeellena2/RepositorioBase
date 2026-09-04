namespace CleanArchitecture.Application.Common.Interfaces;

/// <summary>
/// Decrypts the envelope of one leased message. It returns the token to the caller's memory and nothing else:
/// there is no listing member and no store surface, because a reader that could enumerate envelopes would be a
/// way to harvest tokens rather than to deliver one (IA-REQ-018).
/// </summary>
public interface IOutboxSecretReader
{
    Task<string?> ReadAsync(Guid outboxMessageId, CancellationToken cancellationToken);
}
