namespace CleanArchitecture.Application.IdentityAccess.Platform.Retention;

/// <summary>
/// The row lock a purge and a hold both take before they decide anything about a subject (IA-REQ-056, C7).
/// <para>
/// C7 names the coordination directly: the winner of a hold racing a purge is "whichever takes `SELECT … FOR
/// UPDATE` on the subject's retention-eligible rows first". This is that statement, as a port, because the two
/// sides live in different layers — the executor is Infrastructure and the hold route is an Application handler —
/// and neither may reach into the other.
/// </para>
/// <para>
/// It is a lock on rows rather than an advisory key on purpose. An advisory lock would serialize the two sides
/// just as well, but it would be a second invented protocol guarding the same rows: anything that later purges
/// or holds without knowing about the convention would slip past it, where a row lock is taken by the rows
/// themselves.
/// </para>
/// <para>
/// It waits rather than failing fast. A hold that gave up because a purge was mid-pass would be a hold the
/// operator believes they placed; a purge that gave up because a hold was mid-flight simply runs again in fifteen
/// minutes. Waiting is what makes the loser's outcome the contract's outcome rather than an error.
/// </para>
/// </summary>
public interface IRetentionSubjectLock
{
    /// <summary>
    /// Locks the retention-eligible rows of these subjects until the ambient transaction ends, and returns once
    /// every competing transaction on them has committed or rolled back.
    /// </summary>
    Task LockAsync(IReadOnlyCollection<Guid> subjectIdentityIds, CancellationToken cancellationToken);
}
