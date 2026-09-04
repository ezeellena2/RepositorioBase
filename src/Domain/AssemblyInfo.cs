using System.Runtime.CompilerServices;

// VersionedTokenHash.FromPersistedValue is the one entry that accepts a precomputed hash. Persistence has to
// rehydrate stored values and its tests have to pin the rule; no other assembly may reach it.
[assembly: InternalsVisibleTo("CleanArchitecture.Infrastructure")]
[assembly: InternalsVisibleTo("CleanArchitecture.Domain.UnitTests")]
