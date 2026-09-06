using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

public sealed class PersonalProfileStamps(ApplicationDbContext context) : IPersonalProfileStamps
{
    public async Task<PersonalProfileStamp?> ReadAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var stamp = await context.PersonProfiles
            .AsNoTracking()
            .Where(profile => profile.IdentityId == identityId)
            .Select(profile => new
            {
                Version = EF.Property<uint>(profile, "Version"),
                UpdatedAt = EF.Property<DateTimeOffset>(profile, OperationalTimestampInterceptor.UpdatedAt)
            })
            .SingleOrDefaultAsync(cancellationToken);

        // The version reaches the client as an opaque string. It is PostgreSQL's own xmin, so a client cannot
        // compute the next one and a stale edit cannot be made to look current.
        return stamp is null ? null : new PersonalProfileStamp(stamp.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), stamp.UpdatedAt);
    }
}
