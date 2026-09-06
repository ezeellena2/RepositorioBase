using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

/// <summary>
/// Reads <c>IdentityAccess:PersonalData:Mode</c> once. Anything that is not exactly <c>Real</c> is
/// <c>Synthetic</c> — including a blank value, a missing key and a misspelling — because the failure that matters
/// here is a deployment handling real people while believing it is not (IA-REQ-056).
/// </summary>
public sealed class PersonalDataMode(IConfiguration configuration) : IPersonalDataMode
{
    public DataClassification Classification { get; } =
        string.Equals(configuration["IdentityAccess:PersonalData:Mode"]?.Trim(), "Real", StringComparison.OrdinalIgnoreCase)
            ? DataClassification.Real
            : DataClassification.Synthetic;
}
