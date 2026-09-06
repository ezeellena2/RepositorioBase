using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.AspNetCore.DataProtection;

namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

/// <summary>
/// Seals a documentary identity with the same Data Protection key ring the outbox envelopes and the Platform TOTP
/// secret use, under its own purpose. One key ring means one rotation story for the deployment; a separate purpose
/// means an envelope from one of them cannot be opened as another (IA-REQ-050).
/// </summary>
public sealed class IdentityDocumentProtector(IDataProtectionProvider dataProtectionProvider) : IIdentityDocumentProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("identity-access.people.identity-document.v1");

    public string Protect(NormalizedDocument document) => _protector.Protect(document.Canonical);

    public NormalizedDocument? Reveal(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return null;

        string canonical;
        try
        {
            canonical = _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // A payload this key cannot open is a state, not a fault: a rotated key ring, a restored database, or an
            // envelope sealed for another purpose. It is never treated as a successful read.
            return null;
        }

        var parts = canonical.Split('|');
        if (parts.Length != 3) return null;
        if (!Enum.TryParse<IdentityDocumentCountry>(parts[0], out var country)) return null;
        if (!Enum.TryParse<IdentityDocumentKind>(parts[1], out var documentType)) return null;

        try
        {
            return NormalizedDocument.From(country, documentType, parts[2]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
