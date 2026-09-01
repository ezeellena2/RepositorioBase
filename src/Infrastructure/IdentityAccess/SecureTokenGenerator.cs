using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));
}
