namespace CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;

public static class ConfirmationToken
{
    private const int TokenBytes = 32;
    private const int EncodedLength = 44;

    public static bool IsCanonical(string? token)
    {
        if (token is null || token.Length != EncodedLength) return false;
        Span<byte> bytes = stackalloc byte[TokenBytes];
        return Convert.TryFromBase64String(token, bytes, out var written) &&
               written == TokenBytes &&
               string.Equals(token, Convert.ToBase64String(bytes), StringComparison.Ordinal);
    }
}
