using System.Text.Json;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>A durable outbox envelope could not be interpreted as its registered identifier-only contract.</summary>
internal sealed class InvalidOutboxPayloadException : Exception
{
    internal InvalidOutboxPayloadException(string message)
        : base(message)
    {
    }

    internal InvalidOutboxPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class OutboxPayload
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static T Deserialize<T>(string payload) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, Options)
                ?? throw new InvalidOutboxPayloadException("The outbox payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOutboxPayloadException("The outbox payload is malformed.", exception);
        }
    }

    internal static Guid RequireId(Guid value)
    {
        if (value == Guid.Empty)
            throw new InvalidOutboxPayloadException("The outbox payload is missing a required identifier.");

        return value;
    }

    internal static TId RequireId<TId>(Guid value, Func<Guid, TId> create)
    {
        RequireId(value);
        try
        {
            return create(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOutboxPayloadException("The outbox payload contains an invalid identifier.", exception);
        }
    }
}
