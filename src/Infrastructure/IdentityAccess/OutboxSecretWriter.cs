using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using Microsoft.AspNetCore.DataProtection;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class OutboxSecretWriter(IDataProtectionProvider dataProtectionProvider) : IOutboxSecretWriter
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("identity-access.registration.outbox-secret.v1");

    public string Encrypt(string token) => _protector.Protect(token);
}
