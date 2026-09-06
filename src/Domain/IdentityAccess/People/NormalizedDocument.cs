namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// A documentary identity in the only form the system compares: issuing country, kind of document, and the digits
/// with every separator a person might type removed. Two people who typed the same document differently must reach
/// the same value here, because everything downstream — the ciphertext and the keyed fingerprint — is computed from
/// <see cref="Canonical"/>.
/// </summary>
public readonly record struct NormalizedDocument
{
    private NormalizedDocument(IdentityDocumentCountry country, IdentityDocumentKind documentType, string number)
    {
        Country = country;
        DocumentType = documentType;
        Number = number;
    }

    public IdentityDocumentCountry Country { get; }

    public IdentityDocumentKind DocumentType { get; }

    public string Number { get; }

    /// <summary>The tuple the protector encrypts and the fingerprint keys. It is the only member that carries digits.</summary>
    public string Canonical => $"{Country}|{DocumentType}|{Number}";

    public static NormalizedDocument From(IdentityDocumentCountry country, IdentityDocumentKind documentType, string number)
    {
        if (!Enum.IsDefined(country)) throw new ArgumentOutOfRangeException(nameof(country));
        if (!Enum.IsDefined(documentType)) throw new ArgumentOutOfRangeException(nameof(documentType));
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Document numbers cannot be empty.", nameof(number));
        }

        // Separators a person types are tolerated; anything else is refused rather than silently dropped, because
        // dropping it would turn two different strings into one documentary identity.
        if (number.Any(character => !char.IsAsciiDigit(character) && character != '.' && character != '-' && !char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("Document numbers can contain only digits, dots, hyphens, and whitespace.", nameof(number));
        }

        // Leading zeros are removed before the length is checked, so "07123456" and "7123456" are the same person
        // rather than two rows the unique index would happily accept.
        var digits = new string(number.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        if (digits.Length is < 7 or > 8)
        {
            throw new ArgumentException("An Argentine DNI must contain seven or eight digits.", nameof(number));
        }

        return new NormalizedDocument(country, documentType, digits);
    }

    /// <summary>
    /// Deliberately not the value. A record struct prints its members by default, and this one ends up in exception
    /// messages, structured logs and debugger output; the digits must not travel with it (IA-REQ-029).
    /// </summary>
    public override string ToString() =>
        $"{Country}|{DocumentType}|{new string('•', Math.Max(0, Number.Length - 2))}{(Number.Length <= 2 ? Number : Number[^2..])}";
}
