namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

public readonly record struct NormalizedCuit
{
    private NormalizedCuit(string value) => Value = value;

    public string Value { get; }

    public static NormalizedCuit From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("CUIT cannot be empty.", nameof(value));
        }

        var normalizedCharacters = new List<char>(11);
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                normalizedCharacters.Add(character);
                continue;
            }

            if (character != '-' && !char.IsWhiteSpace(character))
            {
                throw new ArgumentException("CUIT can contain only digits, hyphens, and whitespace.", nameof(value));
            }
        }

        var normalized = new string(normalizedCharacters.ToArray());
        if (normalized.Length != 11)
        {
            throw new ArgumentException("CUIT must contain exactly eleven digits.", nameof(value));
        }

        return new NormalizedCuit(normalized);
    }

    public override string ToString() => Value;
}
