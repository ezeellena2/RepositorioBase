namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

public enum CuitRule
{
    Satisfied,
    Empty,
    Characters,
    Length,
    CheckDigit
}

public readonly record struct NormalizedCuit
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    private NormalizedCuit(string value) => Value = value;

    public string Value { get; }

    public static NormalizedCuit From(string value)
    {
        var rule = Evaluate(value, out var digits);
        if (rule != CuitRule.Satisfied)
        {
            throw new ArgumentException($"The CUIT breaks the {rule} rule.", nameof(value));
        }

        return new NormalizedCuit(digits);
    }

    /// <summary>
    /// Rehydrates the normalized representation written before check-digit validation became an input rule.
    /// Stored rows still have to satisfy the column's eleven-ASCII-digit representation.
    /// </summary>
    public static NormalizedCuit FromStored(string value) =>
        value is { Length: 11 } && value.All(char.IsAsciiDigit)
            ? new NormalizedCuit(value)
            : throw new InvalidOperationException("A stored CUIT is not eleven digits.");

    /// <summary>Evaluates only the submitted value, so public registration can validate it before any lookup.</summary>
    public static CuitRule Evaluate(string? value, out string digits)
    {
        digits = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return CuitRule.Empty;

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
                return CuitRule.Characters;
            }
        }

        if (normalizedCharacters.Count != 11) return CuitRule.Length;

        var sum = 0;
        for (var index = 0; index < Weights.Length; index++)
        {
            sum += (normalizedCharacters[index] - '0') * Weights[index];
        }

        var expected = (11 - sum % 11) % 11;
        if (expected == 10 || expected != normalizedCharacters[10] - '0') return CuitRule.CheckDigit;

        digits = new string(normalizedCharacters.ToArray());
        return CuitRule.Satisfied;
    }

    public override string ToString() => Value;
}
