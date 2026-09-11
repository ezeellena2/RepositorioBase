namespace CleanArchitecture.Infrastructure.Outbox;

public sealed record IdentityEmail(string Recipient, string Subject, string Body, string Language);
